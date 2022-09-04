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
using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Linq;
using static ModFramework.ModContext;

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
    }

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
        protected CoreLibRelinker(ModFwModder modder) : base(modder)
        {
        }

        public IFrameworkResolver FrameworkResolver { get; set; } = new DefaultFrameworkResolver();

        public event ResolveCoreLibHandler? Resolve;

        public bool ThrowResolveFailure { get; set; }

        private EApplyResult OnApplying(ModType modType, ModFwModder? modder)
        {
            if (modder is not null)
            {
                if (modType == ModType.Shutdown)
                {
                    modder.ModContext.OnApply -= OnApplying;
                }
                else if (modType == ModType.PreWrite)
                {
                    for (var i = modder.Module.AssemblyReferences.Count - 1; i >= 0; i--)
                    {
                        var aref = modder.Module.AssemblyReferences[i];
                        if (aref.Name == "mscorlib"
                            || aref.Name == "System.Private.CoreLib"
                        )
                        {
                            modder.Module.AssemblyReferences.RemoveAt(i);
                        }
                    }
                }
            }

            return EApplyResult.Continue;
        }

        void PatchTargetFramework()
        {
            if (Modder is null) throw new ArgumentNullException(nameof(Modder));
            var tfa = Modder.Module.Assembly.GetTargetFrameworkAttribute();

            // remove existing, if any
            if (tfa != null)
            {
                Modder.Module.Assembly.CustomAttributes.Remove(tfa);
            }

            // add a new one
            var ctor = Modder.Module.ImportReference(Modder.FindType(typeof(System.Runtime.Versioning.TargetFrameworkAttribute).FullName).Resolve().FindMethod("System.Void .ctor(System.String)"));
            Modder.Module.Assembly.CustomAttributes.Add(new(ctor)
            {
                ConstructorArguments =
                {
                    new (Modder.Module.TypeSystem.String, ".NETCoreApp,Version=v6.0")
                },
                Properties =
                {
                    new ("FrameworkDisplayName", new (Modder.Module.TypeSystem.String, ""))
                }
            });
        }

        public override void Registered()
        {
            // prepare target framework. cannot use the shim libraries otherwise you end up with System.Private.CoreLib refs
            var fw = FrameworkResolver.FindFramework();
            Modder.AssemblyResolver.AddSearchDirectory(fw); // allow monomod to resolve System.Runtime

            //SystemTypes = GetSystemType(fw);

            base.Registered();

            Modder.ModContext.OnApply += OnApplying;
        }

        public override void PreWrite()
        {
            PatchTargetFramework();
            base.PreWrite();
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

        AssemblyNameReference? ResolveAssembly(TypeReference type)
        {
            var res = Resolve?.Invoke(type);
            if (res is null)
            {
                if (type.Scope is AssemblyNameReference anr)
                {
                    var dependencyMatch = ResolveDependency(type);
                    if (dependencyMatch is not null)
                        return dependencyMatch;

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
