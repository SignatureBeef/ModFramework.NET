//using Mono.Cecil;

//namespace Mod.Framework
//{
//	public static class PatchingExtensions
//	{
//		/// <summary>
//		/// Decompiles the method into C# code that cane be patched and replaced
//		/// </summary>
//		/// <param name="method"></param>
//		/// <returns></returns>
//		public static string Decompile(this MethodDefinition method)
//		{
//			var decompiler = new ICSharpCode.Decompiler.CSharp.CSharpDecompiler(method.Module, new ICSharpCode.Decompiler.DecompilerSettings()
//			{

//			});

//			return decompiler.DecompileAsString(new[]
//			{
//				method
//			});
//		}

//		///// <summary> TODO - this will be handy for non IL lads
//		///// Applies a patch to the method
//		///// </summary>
//		///// <param name="method"></param>
//		///// <param name="patch"></param>
//		//public static void ApplyPatch(this MethodDefinition method, string patch)
//		//{
//		//	var code = method.Decompile();

//		//}
//	}
//}
