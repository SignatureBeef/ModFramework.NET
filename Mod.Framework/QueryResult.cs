using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	/// <summary>
	/// Holds meta data results from a <see cref="Query"/> instance
	/// </summary>
	public class QueryResult : List<MetaData>
	{
		public Query Query { get; internal set; }

		/// <summary>
		/// Casts the ONLY result to the specified type
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public T As<T>() => (T)this.Single().Instance;
	}
}
