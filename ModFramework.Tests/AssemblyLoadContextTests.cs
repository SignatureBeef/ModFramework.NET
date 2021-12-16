using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModFramework.Modules.CSharp;
using ModFramework.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace ModFramework.Tests
{
    [TestClass]
    public class AssemblyLoadContextTests
    {
        [TestMethod]
        public void EnsureModAttributeWorks()
        {
            var ctx = new TestDependencyResolver();
            PluginLoader.AssemblyLoader = new TestAssemblyLoader(ctx);
            CSharpLoader.AssemblyContextDefault = ctx;

            Directory.CreateDirectory("modifications");
            CopyMod("ModFramework.Modules.CSharp.dll");
            CopyMod("ModFramework.Modules.ClearScript.dll");
            CopyMod("ModFramework.Modules.Lua.dll");

            PluginLoader.TryLoad();

            Assert.IsNotNull(PluginLoader.Assemblies);

            var mods = ModificationAttribute
                .Discover(PluginLoader.Assemblies)
                .ToArray();

            Assert.IsTrue(mods.Length > 0);
        }

        void CopyMod(string file)
        {
            var dest = Path.Combine("modifications", file);
            if (File.Exists(dest)) File.Delete(dest);
            File.Copy(file, dest); ;
        }
    }

    /// <summary>
    /// This assembly loader binds the <see cref="TestDependencyResolver"/> to the ModFw assembly loaders
    /// </summary>
    class TestAssemblyLoader : ModFramework.Plugins.DefaultAssemblyLoader
    {
        private AssemblyLoadContext AssemblyLoadContext;
        public TestAssemblyLoader(AssemblyLoadContext assemblyLoadContext)
        {
            AssemblyLoadContext = assemblyLoadContext;
        }

        public override Assembly Load(MemoryStream assembly, MemoryStream? symbols = null)
            => AssemblyLoadContext.LoadFromStream(assembly, symbols);
    }

    /// <summary>
    /// This resolver will correctly demonstrate how to share ModFramework types
    /// cross domain.
    /// </summary>
    /// <see cref="https://docs.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext#type-conversion-issues"/>
    public class TestDependencyResolver : AssemblyLoadContext
    {
        private AssemblyDependencyResolver _resolver;

        public TestDependencyResolver() : base(isCollectible: true)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "ModFramework.dll");
            _resolver = new AssemblyDependencyResolver(path);
        }

        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name == typeof(ModificationAttribute).Assembly.GetName().Name)
                return typeof(ModificationAttribute).Assembly;
            if (name.Name == typeof(ModFramework.Modules.ClearScript.Hooks).Assembly.GetName().Name)
                return typeof(ModFramework.Modules.ClearScript.Hooks).Assembly;
            if (name.Name == typeof(ModFramework.Modules.CSharp.Hooks).Assembly.GetName().Name)
                return typeof(ModFramework.Modules.ClearScript.Hooks).Assembly;
            if (name.Name == typeof(ModFramework.Modules.ClearScript.Hooks).Assembly.GetName().Name)
                return typeof(ModFramework.Modules.Lua.Hooks).Assembly;

            System.Diagnostics.Debug.WriteLine($"Looking for dep: {name}");
            string assemblyPath = _resolver.ResolveAssemblyToPath(name);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }
    }
}