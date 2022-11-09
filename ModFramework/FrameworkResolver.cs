using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ModFramework;

public interface IFrameworkResolver
{
    string FindFramework();
}

public class DefaultFrameworkResolver : IFrameworkResolver
{
    public string FindFramework()
    {
        var package = InstallPackage("Microsoft.NETCore.App.Ref").Single();
        return Directory.GetDirectories(Path.Combine(package, "ref")).Single();
    }

    public static IEnumerable<string> InstallPackage(string name)
    {
        List<string> paths = new();
        var basedirectory = "dependencies";
        Directory.CreateDirectory(basedirectory);

        var task = Task.Run(async () => await ResolvePackageAsync(name));
        task.Wait();
        var res = task.Result;
        if (res is not null)
            foreach (var package in res)
            {
                var zippath = Path.Combine("dependencies", package.PackageName + ".nupkg");
                var extractpath = Path.Combine("dependencies", package.PackageName);

                if (!File.Exists(zippath))
                {
                    if (package.Stream is not null)
                    {
                        package.Stream.Position = 0;
                        File.WriteAllBytes(zippath, package.Stream.ToArray());

                        try
                        {
                            if (Directory.Exists(extractpath)) Directory.Delete(extractpath, true);
                            ZipFile.ExtractToDirectory(zippath, extractpath);

                            paths.Add(extractpath);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(ex);
                        }
                    }
                }
                else
                {
                    paths.Add(extractpath);
                }
            }

        return paths;
    }

    public class PackageSource
    {
        public string PackageName { get; set; }
        public MemoryStream? Stream { get; set; }

        public PackageSource(string packageName, MemoryStream? stream)
        {
            PackageName = packageName;
            Stream = stream;
        }
    }
    static Dictionary<string, IEnumerable<PackageSource>?> _resolvePackageCache = new();
    static SourceCacheContext _nugetCache = new();
    public static async Task<IEnumerable<PackageSource>?> ResolvePackageAsync(string packageName, string? packageVersion = null, bool includePreReleases = false)
    {
        var key = packageName + (packageVersion ?? "latest");
        if (_resolvePackageCache.TryGetValue(key, out IEnumerable<PackageSource>? stream))
        {
            if (stream is not null)
                foreach (var srm in stream)
                    if (srm.Stream is not null)
                        srm.Stream.Position = 0;
            return stream;
        }

        List<PackageSource> streams = new();

        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

        var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

        var versions = (
            await resource.GetAllVersionsAsync(
                packageName,
                _nugetCache,
                NullLogger.Instance,
                CancellationToken.None
            )
        )
            .Where(x => !x.IsPrerelease || x.IsPrerelease == includePreReleases)
            .OrderByDescending(x => x.Version);

        NuGetVersion? version;
        packageVersion ??= Environment.Version.ToString();
        if (packageVersion is null)
            version = versions.FirstOrDefault();
        else version = versions.FindBestMatch(VersionRange.Parse(packageVersion), version => version);

        if (version is null)
        {
            _resolvePackageCache[key] = null;
            return null;
        }

        MemoryStream packageStream = new();
        await resource.CopyNupkgToStreamAsync(
            packageName,
            version,
            packageStream,
            _nugetCache,
            NullLogger.Instance,
            CancellationToken.None).ConfigureAwait(false);

        _resolvePackageCache[key] = streams;

        if (packageStream.Length > 0)
            streams.Add(new(packageName, packageStream));

        var dependencies = await resource.GetDependencyInfoAsync(packageName, version, _nugetCache,
            NullLogger.Instance,
            CancellationToken.None);

        var deps = dependencies.DependencyGroups.Where(x => x.TargetFramework.Framework == ".NETStandard");
        foreach (var dependency in deps)
        {
            foreach (var package in dependency.Packages)
            {
                if (_resolvePackageCache.TryGetValue(package.Id, out IEnumerable<PackageSource>? existing))
                    continue;

                MemoryStream depStream = new();
                await resource.CopyNupkgToStreamAsync(
                    package.Id,
                    package.VersionRange.MaxVersion ?? package.VersionRange.MinVersion,
                    depStream,
                    _nugetCache,
                    NullLogger.Instance,
                    CancellationToken.None);

                if (depStream.Length > 0)
                    streams.Add(new(package.Id, depStream));
            }
        }

        return streams;
    }

}
