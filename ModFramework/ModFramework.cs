using Mono.Cecil;
using Ninject;
using Ninject.Extensions.Conventions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Mod.Framework
{
	/// <summary>
	/// The bootstrapper for the Mod.Framework library.
	/// It will prepare Ninject and the registered modules
	/// </summary>
	public class ModFramework : IDisposable
	{
		private StandardKernel _kernel;
		private AssemblyResolver _resolver;
		private ReaderParameters _readerParameters;
		private bool _initialised = false;

		public List<Assembly> Assemblies { get; set; } = new List<Assembly>();
		public List<AssemblyDefinition> CecilAssemblies { get; private set; } = new List<AssemblyDefinition>();
		public IEnumerable<RunnableModule> Modules { get; set; }

		public string[] DefaultModuleGlobs { get; } = new[] {
			@"./Modifications/**.dll",
			@"./Modifications/**/**.dll",
		};

		private struct EmbeddedAssembly
		{
			public AssemblyDefinition Assembly { get; set; }
			public ModuleDefinition Module { get; set; }
			public EmbeddedResource Resource { get; set; }
		}
		private List<EmbeddedAssembly> embeddedAssemblies { get; set; } = new List<EmbeddedAssembly>();

		public ModFramework(params Assembly[] module_assemblies)
		{
			this.Assemblies.Add(Assembly.GetExecutingAssembly());
			this.Assemblies.AddRange(module_assemblies);

			this.Initialise();
		}

		#region Private methods
		private void Initialise()
		{
			if (!_initialised)
			{
				_kernel = new StandardKernel();

				_resolver = new AssemblyResolver(this);
				_readerParameters = new ReaderParameters(ReadingMode.Immediate)
				{
					AssemblyResolver = _resolver
				};

				_kernel.Bind<ModFramework>().ToConstant(this);

				LoadExternalModules();

				_kernel.Bind(c => c.From(this.Assemblies)
					.SelectAllClasses()
					.WithAttribute<ModuleAttribute>()
					.BindBase()
				);

				_initialised = true;
			}
		}

		private void LoadExternalModules()
		{
			this.RegisterAssemblies(this.DefaultModuleGlobs);
		}

		public AssemblyDefinition RegisterCecilAssembly(string location)
		{
			var def = AssemblyDefinition.ReadAssembly(location, _readerParameters);
			CecilAssemblies.Add(def);

			foreach (var module in def.Modules)
			{
				if (module.HasResources)
				{
					foreach (var resource in module.Resources)
					{
						if (resource.ResourceType == ResourceType.Embedded)
						{
							var er = resource as EmbeddedResource;
							var data = er.GetResourceData();

							if (data.Length > 2)
							{
								bool is_pe = data.Take(2).SequenceEqual(new byte[] { 77, 90 }); // MZ
								if (is_pe)
								{
									var ms = new MemoryStream(data);
									var resource_asm = AssemblyDefinition.ReadAssembly(ms, _readerParameters);

									embeddedAssemblies.Add(new EmbeddedAssembly()
									{
										Assembly = resource_asm,
										Module = module,
										Resource = er
									});

									CecilAssemblies.Add(resource_asm);
								}
							}
						}
					}
				}
			}
			return def;
		}

		/// <summary>
		/// Syncs any .NET assemblies, to cecil assemblies
		/// </summary>
		/// <returns></returns>
		private bool UpdateCecilAssemblies()
		{
			bool updated = false;
			if (_readerParameters != null)
			{
				foreach (var assembly in this.Assemblies)
				{
					if (!CecilAssemblies.Any(x => x.FullName == assembly.FullName))
					{
						RegisterCecilAssembly(assembly.Location);
						updated = true;
					}
				}
			}
			return updated;
		}
		#endregion

		#region Public methods
		/// <summary>
		/// Registers the provided assembies for use within the framework
		/// </summary>
		/// <param name="assemblies"></param>
		public void RegisterAssemblies(params Assembly[] assemblies)
		{
			foreach (var assembly in assemblies)
			{
				if (assembly == null || String.IsNullOrEmpty(assembly.Location))
					throw new Exception("Invalid Location for assembly");

				this.Assemblies.Add(assembly);
			}

			this.UpdateCecilAssemblies();
		}

		/// <summary>
		/// Registers the provided assembies for use within the framework
		/// </summary>
		/// <param name="globs"></param>
		public void RegisterAssemblies(params string[] globs)
		{
			foreach (var glob in globs)
			{
				foreach (var file in Glob.Glob.Expand(glob))
				{
					var assembly = Assembly.LoadFile(file.FullName);
					RegisterAssemblies(assembly);
				}
			}

			this.UpdateCecilAssemblies();
		}

		/// <summary>
		/// Runs each registered module in sequence
		/// </summary>
		public void RunModules()
		{
			this.Modules = _kernel.GetAll<RunnableModule>().OrderBy(x => x.Order);
			foreach (RunnableModule module in this.Modules)
			{
				module.Assemblies = module.AssemblyTargets.Count() == 0 ?
					this.CecilAssemblies
					: this.CecilAssemblies.Where(asm => module.AssemblyTargets.Any(t => t == asm.FullName))
				;

				Console.WriteLine($"\t-> Running module: {module.Name}");
				module.Run();
			}
		}
		#endregion

		private void UpdateEmbeddedAssembies()
		{
			// remove the existing resource and replace it with the new one

			foreach (var resource in this.embeddedAssemblies)
			{
				resource.Module.Resources.Remove(resource.Resource);

				using (var ms = new MemoryStream())
				{
					resource.Assembly.Write(ms);
					resource.Module.Resources.Add(new EmbeddedResource(resource.Resource.Name,
						resource.Resource.Attributes,
						ms.ToArray()));
				}
			}
		}

		private void SaveOutput(string outputDirectory = "Output")
		{
			UpdateEmbeddedAssembies();

			Directory.CreateDirectory(outputDirectory);
			foreach (var asm in this.CecilAssemblies)
			{
				var save_to = Path.Combine(outputDirectory, asm.MainModule.Name);
				if (File.Exists(save_to))
					File.Delete(save_to);

				asm.Write(save_to);
				Console.WriteLine($"Saved output file: {save_to}");
			}
		}

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					this.SaveOutput();

					// TODO: dispose managed state (managed objects).
					_kernel?.Dispose();
					_kernel = null;
				}

				// TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
				// TODO: set large fields to null.

				disposedValue = true;
			}
		}

		// TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
		// ~Modder() {
		//   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
		//   Dispose(false);
		// }

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion
	}
}
