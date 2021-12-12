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
    public class MscorlibRelinker : TypeRelinker
    {
        public event ResolveCoreLibHandler? Resolve;

        public static void PostProcessMscorLib(string? outputFolder, string? resourcesFolder, params string[] inputs)
        {
            PostProcessMscorLib(outputFolder, resourcesFolder, null, inputs);
        }

        public static void PostProcessMscorLib(string? outputFolder, string? resourcesFolder, MscorlibRelinker? task, params string[] inputs)
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
                mm.Log($"[OTAPI] Processing corelibs to be net4: {fileName}");

                var extractor = new ResourceExtractor();
                var embeddedResourcesDir = extractor.Extract(input);

                (mm.AssemblyResolver as DefaultAssemblyResolver)!.AddSearchDirectory(embeddedResourcesDir);

                mm.Read();

                mm.AddTask(task ?? new MscorlibRelinker());

                mm.MapDependencies();
                mm.AutoPatch();

                mm.Write();
            }
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

            if (match is not null)
            {
                // this is only needed for ilspy to pick up .net5 libs on osx
                var filename = Path.GetFileName(match.FilePath);
                if (!File.Exists(filename))
                    File.Copy(match.FilePath!, filename!);

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
                        && m.Assembly.Name.Name != "netstandard"
                        && m.Assembly.Name.Name != "System.Private.CoreLib"
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

        AssemblyNameReference ResolveAssembly(TypeReference type)
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

                    throw new MissingMemberException();
                }
                else throw new NotImplementedException();
            }

            if (res.Name == "netstandard" || res.Name == "System.Private.CoreLib")
                throw new NotSupportedException();

            return res;
        }

        public override bool RelinkType<TRef>(ref TRef type)
        {
            if (type.Scope.Name == "netstandard"
                   || type.Scope.Name == "System.Private.CoreLib"
               )
            {
                var asm = ResolveAssembly(type);

                var existing = type.Module.AssemblyReferences.SingleOrDefault(x => x.Name == asm.Name);
                if (existing != null)
                {
                    type.Scope = existing;
                }
                else
                {
                    type.Scope = asm;
                    type.Module.AssemblyReferences.Add(asm);
                }
                return false;
            }
            return false;
        }
    }
}
