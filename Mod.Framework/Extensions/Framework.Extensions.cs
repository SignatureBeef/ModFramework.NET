namespace Mod.Framework
{
	public static class FrameworkExtensions
	{
		/// <summary>
		/// Queries the specified pattern from the assemblies loaded in the framework
		/// </summary>
		/// <param name="framework"></param>
		/// <param name="pattern"></param>
		/// <returns></returns>
		public static QueryResult Query(this ModFramework framework, string pattern)
		{
			return new Query(pattern, framework.CecilAssemblies).Run();
		}
	}
}
