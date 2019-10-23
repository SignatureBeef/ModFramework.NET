using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Mod.Framework
{
	public static class ReflectionExtensions
	{
		/// <summary>
		/// Replaces the source reference with the target dll
		/// </summary>
		/// <param name="asm"></param>
		/// <param name="source"></param>
		/// <param name="dll"></param>
		public static void ReplaceReferences(this Mono.Cecil.AssemblyDefinition asm, string source, string dll)
		{
			var coreDir = Directory.GetParent(typeof(Object).GetTypeInfo().Assembly.Location);

			var msc = coreDir.FullName + Path.DirectorySeparatorChar + dll;
			var mscasm = Mono.Cecil.AssemblyDefinition.ReadAssembly(msc);

			asm.ReplaceReferences(source, mscasm.Name);
		}
		/// <summary>
		/// Replaces the source reference with the target
		/// </summary>
		/// <param name="asm"></param>
		/// <param name="source"></param>
		/// <param name="dll"></param>
		public static void ReplaceReferences(this Mono.Cecil.AssemblyDefinition asm, string source, Mono.Cecil.AssemblyNameDefinition target)
		{
			var reference = asm.MainModule.AssemblyReferences
				.Where(x => x.Name.StartsWith(source, StringComparison.CurrentCultureIgnoreCase))
				.ToArray();

			for (var x = 0; x < reference.Length; x++)
			{
				reference[x].Name = target.Name;
				reference[x].PublicKey = target.PublicKey;
				reference[x].PublicKeyToken = target.PublicKeyToken;
				reference[x].Version = target.Version;
			}
		}
	}
}
