using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	/// <summary>
	/// A pattern based query that matches most meta data types in an assembly
	/// </summary>
	public class Query
	{
		private AssemblyExpander _expander = new AssemblyExpander();
		public string Pattern { get; private set; }

		private QueryResult _result = null;

		public Query(string pattern, IEnumerable<object> context)
		{
			this.Pattern = pattern;

			this._expander.SetContext(context);
		}

		public QueryResult Run()
		{
			if (_result != null) return _result;

			_expander.Expand();

			var patterns = QueryPattern.ParseFrom(this.Pattern);

			var results = new QueryResult()
			{
				Query = this
			};

			foreach (var item in _expander.Results)
			{
				foreach (var pattern in patterns)
				{
					if (pattern.Matches(item))
					{
						results.Add(item);
						break;
					}
				}
			}

			return _result = results;
		}
	}
}
