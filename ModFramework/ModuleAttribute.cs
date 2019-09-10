using System;

namespace Mod.Framework
{
	/// <summary>
	/// Allows you to configure your <see cref="Module"/> using attributes
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
	public class ModuleAttribute : Attribute
	{
		public string Name { get; set; }

		public string[] Authors { get; set; }

		public int Order { get; set; }
		
		public Type[] Dependencies { get; set; }

		public ModuleAttribute(string name = null, string author = null, int order = 10, params Type[] dependsOn)
		{
			this.Name = name;
			this.Authors = new[] { author };
			this.Order = order;
			this.Dependencies = dependsOn;
		}

		public ModuleAttribute(string name = null, string[] authors = null, int order = 10, params Type[] dependsOn)
		{
			this.Name = name;
			this.Authors = authors;
			this.Order = order;
			this.Dependencies = dependsOn;
		}
	}
}
