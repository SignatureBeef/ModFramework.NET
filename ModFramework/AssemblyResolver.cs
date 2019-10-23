using System;
using System.IO;
using System.Linq;
using System.Threading;
using Mono.Cecil;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Mod.Framework
{
	[Flags]
	public enum NuGetDllTypes
	{
		Net20 = 1,
		Net35 = 2,
		Net40 = 4,
		Net45 = 8,
		NetStandard2 = 16,

		Default = NetStandard2 | Net45 // only load netstandard2 or net45 dlls
	}

	public class AssemblyResolver : DefaultAssemblyResolver
	{
		private ModFramework modFramework;

		public NuGetDllTypes DefaultNuGetDlls { get; set; } = NuGetDllTypes.Default;

		public AssemblyResolver(ModFramework modFramework)
		{
			this.modFramework = modFramework;
		}

		public override AssemblyDefinition Resolve(AssemblyNameReference name)
		{
			if (this.modFramework.Modules != null)
			{
				foreach (var module in this.modFramework.Modules)
				{
					var assembly = module.ResolveAssembly(name);
					if (assembly != null) return assembly;
				}
			}

			try
			{
				return base.Resolve(name);
			}
			catch (AssemblyResolutionException)
			{
				var asm = TryNugetResolve(name);
				if (asm != null) return asm;

				throw;
			}
		}

		public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
		{
			if (this.modFramework.Modules != null)
			{
				foreach (var module in this.modFramework.Modules)
				{
					var assembly = module.ResolveAssembly(name, parameters);
					if (assembly != null) return assembly;
				}
			}

			try
			{
				return base.Resolve(name, parameters);
			}
			catch (AssemblyResolutionException)
			{
				var asm = TryNugetResolve(name);
				if (asm != null) return asm;

				throw;
			}
		}

		public virtual AssemblyDefinition TryNugetResolve(AssemblyNameReference name)
		{
			var package = new NuGet.Packaging.Core.PackageIdentity(name.Name, null);
			var settings = Settings.LoadDefaultSettings(root: null);
			var sourceRepositoryProvider = new SourceRepositoryProvider(settings, NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3());
			var nuGetFramework = NuGetFramework.ParseFolder("nuget");
			var logger = NullLogger.Instance;

			using (var cacheContext = new SourceCacheContext())
			{
				foreach (var sourceRepository in sourceRepositoryProvider.GetRepositories())
				{
					var dependencyInfoResource = sourceRepository.GetResourceAsync<DependencyInfoResource>().Result;
					var matches = dependencyInfoResource.ResolvePackages(name.Name, nuGetFramework, cacheContext, logger, CancellationToken.None).Result;

					if (matches.Any())
					{
						var latest = matches
							.OrderByDescending(x => x.Version)
							.First(x => !x.Version.IsPrerelease);
						Console.WriteLine($" *** NuGet: Found assembly: {latest}");

						var downloadResource = latest.Source.GetResourceAsync<DownloadResource>(CancellationToken.None).Result;
						var downloadResult = downloadResource.GetDownloadResourceResultAsync(
							latest,
							new PackageDownloadContext(cacheContext),
							SettingsUtility.GetGlobalPackagesFolder(settings),
							NullLogger.Instance, CancellationToken.None).Result;

						var packagePathResolver = new PackagePathResolver(Path.GetFullPath("packages"));
						var packageExtractionContext = new PackageExtractionContext(
							PackageSaveMode.Defaultv3,
							XmlDocFileSaveMode.None,
							null,
							NullLogger.Instance);

						var result = PackageExtractor.ExtractPackageAsync(
							  downloadResult.PackageSource,
							  downloadResult.PackageStream,
							  packagePathResolver,
							  packageExtractionContext,
							  CancellationToken.None).Result;

						var dlls = result.Select(x => new FileInfo(x))
							.Where(fi => fi.Exists && fi.Extension.Equals(".dll", StringComparison.CurrentCultureIgnoreCase));

						FileInfo dll = null;
						if (dll == null && (DefaultNuGetDlls & NuGetDllTypes.NetStandard2) != 0)
						{
							dll = dlls.FirstOrDefault(x => x.Directory.Name.Equals("netstandard2.0", StringComparison.CurrentCultureIgnoreCase));
						}
						if (dll == null && (DefaultNuGetDlls & NuGetDllTypes.Net45) != 0)
						{
							dll = dlls.FirstOrDefault(x => x.Directory.Name.Equals("net45", StringComparison.CurrentCultureIgnoreCase));
						}

						if (dll != null)
						{
							Console.WriteLine($" *** NuGet: Loading assembly: {dll.FullName}");
							var content = File.ReadAllBytes(dll.FullName);
							return AssemblyDefinition.ReadAssembly(new MemoryStream(content));
						}
					}
				}
			}

			return null;
		}
	}
}
