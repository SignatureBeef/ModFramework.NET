using System.Collections.Generic;

namespace Mod.Framework
{
	/// <summary>
	/// Holds meta data results from a <see cref="Query"/> instance
	/// </summary>
	public class QueryResult : List<MetaData>
	{
		public Query Query { get; internal set; }
	}
}
