using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	/// <summary>
	/// Provides the base class for modifications
	/// </summary>
	public abstract class Module : IDisposable
	{
		public string Name => attribute.Name;

		public string[] Authors => attribute.Authors;

		public int Order => attribute.Order;

		/// <summary>
		/// Defines which Assembly Names the modification applies to.
		/// </summary>
		public IEnumerable<String> AssemblyTargets { get; set; }

		public IEnumerable<AssemblyDefinition> Assemblies { get; set; }

		protected ModuleAttribute attribute;

		public Module()
		{
			attribute = (ModuleAttribute)Attribute.GetCustomAttribute(
				this.GetType(),
				typeof(ModuleAttribute)
			);

			if (attribute == null)
				throw new NotSupportedException($"The {nameof(ModuleAttribute)} declaration is missing, please add it to your module.");

			AssemblyTargets = ((AssemblyTargetAttribute[])Attribute.GetCustomAttributes(
				this.GetType(),
				typeof(AssemblyTargetAttribute)
			)).Select(x => x.AssemblyName);
		}

        public virtual AssemblyDefinition ResolveAssembly(AssemblyNameReference name) => null;
        public virtual AssemblyDefinition ResolveAssembly(AssemblyNameReference name, ReaderParameters parameters) => null;

        public virtual void Dispose() { }
	}
}
