/*
Copyright (C) 2020 DeathCradle

This file is part of Open Terraria API v3 (OTAPI)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ModFramework.Relinker
{
    [MonoMod.MonoModIgnore]
    public class SystemType
    {
        public string? FilePath { get; set; }
        public AssemblyDefinition? Assembly { get; set; }
        public ExportedType? Type { get; set; }

        public AssemblyNameReference? AsNameReference() => Assembly?.AsNameReference();

        public override string ToString() => Type?.ToString() ?? "";

        public static IEnumerable<SystemType> SystemTypes { get; set; } = GetSystemType();

        static SystemType[] GetSystemType()
        {
            var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);

            if (assemblyPath == null) return new SystemType[] { };

            var runtimeAssemblies = Directory.GetFiles(assemblyPath, "*.dll")
                .Select(x =>
                {
                    try
                    {
                        return new
                        {
                            asm = AssemblyDefinition.ReadAssembly(x),
                            path = x,
                        };
                    }
                    catch //(Exception ex)
                    {
                        // discard assemblies that cecil cannot parse. e.g. api-ms**.dll on windows
                        return null;
                    }
                })
                .Where(x => x != null);

            var forwardTypes = runtimeAssemblies.SelectMany(ra =>
                ra!.asm.MainModule.ExportedTypes
                    .Where(x => x.IsForwarder)
                    .Select(x => new SystemType()
                    {
                        Type = x,
                        Assembly = ra.asm,
                        FilePath = ra.path,
                    })
            );

            return forwardTypes.ToArray();
        }
    }

    [MonoMod.MonoModIgnore]
    public static partial class Extensions
    {
        public static AssemblyNameReference AsNameReference(this AssemblyDefinition assembly)
        {
            var name = assembly.Name;
            return new AssemblyNameReference(name.Name, name.Version)
            {
                PublicKey = name.PublicKey,
                PublicKeyToken = name.PublicKeyToken,
                Culture = name.Culture,
                Hash = name.Hash,
                HashAlgorithm = name.HashAlgorithm,
                Attributes = name.Attributes
            };
        }
    }

    [MonoMod.MonoModIgnore]
    public delegate AssemblyNameReference ResolveCoreLibHandler(TypeReference target);

    [MonoMod.MonoModIgnore]
    public class CoreLibRelinker : TypeRelinker
    {
        public event ResolveCoreLibHandler? Resolve;

        private string? _SystemRefsOutputFolder;
        /// <summary>
        /// Optional Directory where System dll's can be output during the patch process.
        /// </summary>
        public string? SystemRefsOutputFolder
        {
            get => _SystemRefsOutputFolder;
            set
            {
                if (!Directory.Exists(value))
                    throw new DirectoryNotFoundException($"[{nameof(SystemRefsOutputFolder)}] cannot be set to non-existent folder: {value}");

                _SystemRefsOutputFolder = value;
            }
        }
        
        public bool ThrowResolveFailure { get; set; }

        // TODO replace these gross things
        public static void PostProcessCoreLib(string? outputFolder, string? resourcesFolder, IEnumerable<string> searchDirectories, params string[] inputs)
        {
            PostProcessCoreLib(outputFolder, resourcesFolder, searchDirectories, null, inputs);
        }

        // TODO replace these gross things
        public static void PostProcessCoreLib(string? outputFolder, string? resourcesFolder, IEnumerable<string>? searchDirectories, CoreLibRelinker? task, params string[] inputs)
        {
            if (String.IsNullOrWhiteSpace(outputFolder))
                outputFolder = Environment.CurrentDirectory;

            foreach (var input in inputs)
            {
                var fileName = Path.GetFileName(input);
                using var mm = new ModFwModder()
                {
                    InputPath = input,
                    OutputPath = Path.Combine(outputFolder, fileName),
                    MissingDependencyThrow = false,
                    //LogVerboseEnabled = true,
                    // PublicEverything = true, // this is done in setup

                    GACPaths = new string[] { } // avoid MonoMod looking up the GAC, which causes an exception on .netcore
                };
                mm.Log($"[OTAPI] Processing corelibs to be net6: {fileName}");

                var extractor = new ResourceExtractor();
                var embeddedResourcesDir = extractor.Extract(input, resourcesFolder);

                mm.AssemblyResolver.AddSearchDirectory(embeddedResourcesDir);

                if (searchDirectories != null)
                    foreach (var folder in searchDirectories)
                        mm.AssemblyResolver.AddSearchDirectory(folder);

                mm.Read();

                mm.AddTask(task ?? new CoreLibRelinker());

                mm.MapDependencies();
                mm.AutoPatch();

                mm.Write();
            }
        }

        void PatchTargetFramework()
        {
            if (Modder is null) throw new ArgumentNullException(nameof(Modder));
            var tfa = Modder.Module.Assembly.GetTargetFrameworkAttribute();
            if (tfa != null)
            {
                tfa.ConstructorArguments[0] = new CustomAttributeArgument(
                    tfa.ConstructorArguments[0].Type,
                    ".NETCoreApp,Version=v6.0"
                );
                var fdm = tfa.Properties.Single();
                tfa.Properties[0] = new CustomAttributeNamedArgument(
                    fdm.Name,
                    new CustomAttributeArgument(fdm.Argument.Type, "")
                );
            }
        }

        protected override void OnInit()
        {
            PatchTargetFramework();
            base.OnInit();
        }

        AssemblyNameReference? ResolveSystemType(TypeReference type)
        {
            var searchType = type.FullName;

            var matches = SystemType.SystemTypes
                .Where(x => x.Type?.FullName == searchType
                    && x.Assembly?.Name?.Name != "mscorlib"
                    && x.Assembly?.Name?.Name != "System.Private.CoreLib"
                )
                // pick the assembly with the highest version.
                // TODO: consider if this will ever need to target other fw's
                .OrderByDescending(x => x.Assembly?.Name?.Version);
            var match = matches.FirstOrDefault();

            if (match is not null && match.FilePath is not null)
            {
                // this is only needed for ilspy to pick up .net5 libs on osx
                if (!String.IsNullOrWhiteSpace(SystemRefsOutputFolder))
                {
                    var filename = Path.GetFileName(match.FilePath);
                    if (!File.Exists(filename))
                        File.Copy(match.FilePath, Path.Combine(SystemRefsOutputFolder, filename));
                }

                return match.AsNameReference();
            }
            return null;
        }

        AssemblyNameReference? ResolveDependency(TypeReference type)
        {
            if (Modder is null) throw new ArgumentNullException(nameof(Modder));
            var depds = Modder.DependencyCache.Values
                .Select(m => new
                {
                    Module = m,
                    Types = m.Types.Where(x => x.FullName == type.FullName
                        && m.Assembly.Name.Name != "mscorlib"
                        && m.Assembly.Name.Name != "System.Private.CoreLib"
                        && x.IsPublic
                    )
                })
                .Where(x => x.Types.Any())
                // pick the assembly with the highest version.
                // TODO: consider if this will ever need to target other fw's
                .OrderByDescending(x => x.Module.Assembly.Name.Version); ;

            var first = depds.FirstOrDefault();
            if (first is not null)
            {
                return first.Module.Assembly.AsNameReference();
            }
            return null;
        }

        AssemblyNameReference? ResolveRedirection(TypeReference type)
        {
            //foreach (var mod in Modder.Mods)
            //{

            //}

            return null;
        }

        AssemblyNameReference? ResolveAssembly(TypeReference type)
        {
            var res = Resolve?.Invoke(type);
            if (res is null)
            {
                if (type.Scope is AssemblyNameReference anr)
                {
                    var redirected = ResolveRedirection(type);
                    if (redirected is not null)
                        return redirected;

                    var dependencyMatch = ResolveDependency(type);
                    if (dependencyMatch is not null)
                        return dependencyMatch;

                    var systemMatch = ResolveSystemType(type);
                    if (systemMatch is not null)
                        return systemMatch;

                    if (ThrowResolveFailure)
                        throw new Exception($"Relink failed. Unable to resolve {type.FullName}");
                }
                else throw new Exception($"{type.Scope.GetType().FullName} is not handled.");
            }

            if (res?.Name == "mscorlib" || res?.Name == "System.Private.CoreLib")
                throw new Exception($"Relink failed, must not be corelib");

            return res;
        }

        public override bool RelinkType<TRef>(ref TRef type)
        {
            if (type.Scope.Name == "mscorlib"
                   || type.Scope.Name == "netstandard"
                   || type.Scope.Name == "System.Private.CoreLib"
               )
            {
                var asm = ResolveAssembly(type);
                if (asm is not null)
                {
                    // transition period (until i get the new roslyn package working wrt nullattrs)
                    // we must find the highest package, because System.Runtime can have v5 + v6
                    var existing = type.Module.AssemblyReferences
                        .OrderByDescending(x => x.Version)
                        .FirstOrDefault(x => x.Name == asm.Name);
                    if (existing != null)
                    {
                        type.Scope = existing;
                    }
                    else
                    {
                        type.Scope = asm;
                        type.Module.AssemblyReferences.Add(asm);
                    }
                    return true;
                }
            }
            return false;
        }
    }
}
