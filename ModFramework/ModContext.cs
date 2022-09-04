using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace ModFramework
{
    public class ModPluginLoader
    {
        public AssemblyLoadContext AssemblyLoader { get; set; } = AssemblyLoadContext.Default;
        private List<Assembly> _assemblies = new();

        public delegate bool OnModFileLoadingHandler(string filePath);
        public event OnModFileLoadingHandler? OnModFileLoading;

        public delegate bool OnModAssemblyLoadingHandler(Assembly assembly);
        public event OnModAssemblyLoadingHandler? OnModAssemblyLoading;

        public bool CanAddFile(string filePath)
            => OnModFileLoading?.Invoke(filePath) != false;

        public bool CanAddAssembly(Assembly assembly)
            => OnModAssemblyLoading?.Invoke(assembly) != false;

        public void AddAssembly(Assembly assembly)
        {
            if (!CanAddAssembly(assembly))
                return;

            _assemblies.Add(assembly);
        }

        public void AddFile(string path)
        {
            if (!CanAddFile(path))
                return;

            var asm = AssemblyLoader.LoadFromAssemblyPath(path);
            AddAssembly(asm);
        }

        public void AddFromFolder(string path, string searchPattern = "*.dll", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            foreach (var file in Directory.EnumerateFiles(path, searchPattern, searchOption))
                AddFile(file);
        }

        public IList<ModificationAttribute> DiscoverModificationAttributes(ModType? modType = null, IEnumerable<Assembly>? assemblies = null)
        {
            var modifications = ModificationAttribute.Discover(assemblies ?? _assemblies);
            var requested = modType is null ? modifications : modifications.Where(x => x.Type == modType);
            return requested.ToList();
        }
    }

    public class ModContext
    {
        public delegate void OnContextCreated(ModContext context);
        public static event OnContextCreated? ContextCreated; // allows consumers to add extras into the pipeline (e.g. OTAPI Client Launcher, whom boots terraria, so it can inject cef)

        public string BaseDirectory { get; set; } = Environment.CurrentDirectory;

        public ModPluginLoader PluginLoader { get; set; }

        public List<object> Parameters { get; } = new();
        public List<string> ReferenceFiles { get; } = new();
        public List<string> ReferenceConstants { get; } = new();

        public string TargetAssemblyName { get; set; }

        public ModContext(string targetAssemblyName)
        {
            PluginLoader = new();
            TargetAssemblyName = targetAssemblyName;
            ContextCreated?.Invoke(this);
        }

        private void Invoke(ModificationAttribute modification, ModType modType, MonoMod.MonoModder? modder, List<object> availableParameters)
        {
            Console.WriteLine($"[ModFw:{modType}] {modification.Description}");
            if (modification?.MethodBase?.DeclaringType is null) throw new Exception($"Failed to determine ctor");

            MethodBase modCtor = modification.MethodBase; //.DeclaringType.GetConstructors().Single();
            var modCtorParams = modCtor.GetParameters();

            // bind arguments
            var args = new object[modCtorParams.Length];
            {
                for (var i = 0; i < modCtorParams.Length; i++)
                {
                    var param = modCtorParams[i];
                    var paramValue = availableParameters.SingleOrDefault(p =>
                        param.ParameterType.IsAssignableFrom(p.GetType())
                    );

                    if (paramValue != null)
                        args[i] = paramValue;
                    else throw new Exception($"No valid for parameter `{param.Name}` in modification {modification.MethodBase.DeclaringType.FullName}");
                }
            }

            if (modification.MethodBase.IsConstructor)
                Activator.CreateInstance(modification.MethodBase.DeclaringType, args, null);
            else modification.MethodBase.Invoke(modification.Instance, args);
        }

        /* mainly to expose an Apply function without the need of MonoMod */
        public void Apply(ModType modType) => Apply(modType, null, null);
        public void Apply(ModType modType, IEnumerable<Assembly> assemblies) => Apply(modType, null, assemblies);

        public delegate EApplyResult OnApplyingHandler(ModType modType, ModFwModder? modder);
        public event OnApplyingHandler? OnApply;

        public enum EApplyResult
        {
            Continue,
            Cancel,
        }

        public void Apply(ModType modType, ModFwModder? modder, IEnumerable<Assembly>? assemblies = null)
        {
            if (OnApply is null || OnApply(modType, modder) != EApplyResult.Cancel)
                _Apply(modType, modder, assemblies);
        }

        private void _Apply(ModType modType, ModFwModder? modder, IEnumerable<Assembly>? assemblies = null)
        {
            if (modder?.LogVerboseEnabled == true) Console.WriteLine($"[ModFw:{modType}] Applying mods...");
            var availableParameters = new List<object>()
            {
                modType,
                this,
            };

            if (modder != null) availableParameters.Add(modder);
            availableParameters.AddRange(Parameters);

            var modifications = PluginLoader.DiscoverModificationAttributes(modType, assemblies);

            var completed = new List<ModificationAttribute>();
            //var tasks = new Dictionary<ModificationAttribute, Task>();
            var counter = new Dictionary<ModificationAttribute, int>();

            void QueueFreeTasks()
            {
                foreach (var attr in modifications.ToArray())
                {
                    var deps = attr.Dependencies ?? Enumerable.Empty<string>();
                    var pending = deps.Where(dep =>
                        !completed.Any(d =>
                            d.MethodBase?.DeclaringType?.Name == dep
                            || (d.UniqueName != null && d.UniqueName == dep)
                        )
                    );

                    if (!pending.Any())
                    {
                        modifications.Remove(attr);
                        //tasks.Add(attr, Invoke(attr, modType, modder, availableParameters));
                        Invoke(attr, modType, modder, availableParameters);
                        completed.Add(attr);
                    }
                    else Console.WriteLine($"[ModFw] Awaiting dependencies for {attr.MethodBase?.DeclaringType?.FullName} ({attr.Description}) needs: {String.Join(",", pending)}");
                }
            }

            while (modifications.Any())
            {
                QueueFreeTasks();

                //Task.WhenAll(tasks.Values).Wait();

                //foreach(var task in tasks)
                //{
                //    completed.Add(task.Key);
                //}
                //tasks.Clear();
            }
        }
    }
}

