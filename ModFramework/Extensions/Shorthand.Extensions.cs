using Mono.Cecil;
using System.Linq;

namespace Mod.Framework
{
	public static class ShorthandExtensions
	{
		/// <summary>
		/// Determines if a type exists
		/// </summary>
		public static bool HasType(this AssemblyDefinition assemblyDefinition, string name)
		{
			return assemblyDefinition.MainModule.Types.Any(x => x.FullName == name);
		}

		/// <summary>
		/// Gets a type from the assembly using <see cref="TypeReference.FullName"/>
		/// </summary>
		/// <param name="assemblyDefinition"></param>
		/// <param name="name"></param>
		/// <returns></returns>
		public static TypeDefinition Type(this AssemblyDefinition assemblyDefinition, string name)
		{
			return assemblyDefinition.MainModule.Types.Single(x => x.FullName == name);
		}

		/// <summary>
		/// Gets a field from the given type
		/// </summary>
		/// <param name="typeDefinition">The type to search</param>
		/// <param name="name">Name of the field</param>
		public static FieldDefinition Field(this TypeDefinition typeDefinition, string name)
		{
			return typeDefinition.Fields.Single(x => x.Name == name);
		}

		/// <summary>
		/// Gets a method from the given type
		/// </summary>
		/// <param name="type">The type to search</param>
		/// <param name="name">Name of the method</param>
		public static MethodDefinition Method(this TypeDefinition type, string name)
		{
			return type.Methods.Single(x => x.Name == name);
		}

		/// <summary>
		/// Gets a property from the given type
		/// </summary>
		/// <param name="type">The type to search</param>
		/// <param name="name">Name of the property</param>
		public static PropertyDefinition Property(this TypeDefinition type, string name)
		{
			return type.Properties.Single(x => x.Name == name);
		}
	}
}
