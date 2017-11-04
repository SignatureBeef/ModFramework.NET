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

			if (AssemblyTargets == null || AssemblyTargets.Count() == 0)
				throw new NotSupportedException($"At least one {nameof(AssemblyTargetAttribute)} declaration is required, please add at least one to your module.");
		}

		public virtual void Dispose() { }
	}
}
