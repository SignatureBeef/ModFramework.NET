namespace Mod.Framework
{
	/// <summary>
	/// Holds expanded meta data.
	/// Typically used along side with <see cref="AssemblyExpander"/> and <see cref="QueryResult"/>
	/// </summary>
	public class MetaData
	{
		public string AssemblyName { get; set; }
		public string FullName { get; set; }
		public object Instance { get; set; }
	}
}
