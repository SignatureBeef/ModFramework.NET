using System;

namespace Mod.Framework
{
	/// <summary>
	/// Decorate your <see cref="Module"/> with these attributes when you only want it
	/// to run when certain assemblies are available.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public class AssemblyTargetAttribute : Attribute
	{
		public string AssemblyName { get; set; }

		public AssemblyTargetAttribute(string assemblyName)
		{
			this.AssemblyName = assemblyName;
		}
	}
}
