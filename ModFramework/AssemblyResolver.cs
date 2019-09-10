using Mono.Cecil;

namespace Mod.Framework
{
    public class AssemblyResolver : DefaultAssemblyResolver
    {
        private ModFramework modFramework;

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
            return base.Resolve(name);
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
            return base.Resolve(name, parameters);
        }
    }
}
