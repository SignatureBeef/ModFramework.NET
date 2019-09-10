using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mod.Framework
{
	/// <summary>
	/// Used along side with <see cref="Query"/> to create meta data filters
	/// </summary>
	public class QueryPattern
	{
		public string AssemblyName { get; set; }
		public string FullName { get; set; }
		public bool StartsWith { get; set; }
		public bool EndsWith { get; set; }

		public IEnumerable<string> Parameters => _parameters;

		private List<string> _parameters;
		public QueryPattern()
		{
			_parameters = new List<String>();
		}

		internal bool Matches(MetaData meta)
		{
			if (this.AssemblyName != null)
			{
				if (meta.AssemblyName != this.AssemblyName)
					return false;
			}

			if (this.StartsWith)
			{
				return meta.FullName.StartsWith(this.FullName);
			}
			else if(this.EndsWith)
			{
				return meta.FullName.EndsWith(this.FullName);
			}
			else
			{
				//return meta.FullName.Equals(this.FullName, StringComparison.InvariantCultureIgnoreCase);
				return meta.FullName == this.FullName;
			}
		}

		public enum State
		{
			ReadingAssembly,
			ReadingFullName,
			ReadingParameters,

			NotSupported
			//ReadingIL
		}

		public static IEnumerable<QueryPattern> ParseFrom(string text)
		{
			StringBuilder string_builder = new StringBuilder();

			foreach (var segment in text
				.Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries)
				.Select(x => x.Trim())
			)
			{
				var pattern = new QueryPattern();
				var state = State.ReadingFullName;

				var characters = segment.ToCharArray();
				for (var x = 0; x < characters.Length; x++)
				{
					var character = characters[x];

					switch (character)
					{
						case '[':
							state = State.ReadingAssembly;
							continue;
						case ']':
							state = State.ReadingFullName;
							continue;
						case '*':
							pattern.StartsWith = x > 0;
							pattern.EndsWith = x == 0;
							continue;
						case '(':
							state = State.ReadingParameters;
							string_builder.Clear();
							continue;
						case ')':
							if (state == State.ReadingParameters)
							{
								var parameter_type = string_builder.ToString();
								string_builder.Clear();

								pattern._parameters.Add(parameter_type);
							}
							state = State.NotSupported;
							continue;
						case ',':
							if (state == State.ReadingParameters)
							{
								// complete
								var parameter_type = string_builder.ToString();
								string_builder.Clear();

								pattern._parameters.Add(parameter_type);
							}
							continue;

						default:
							break;
					}

					switch (state)
					{
						case State.ReadingAssembly:

							if (pattern.AssemblyName == null)
								pattern.AssemblyName = character.ToString();
							else pattern.AssemblyName += character;

							break;
						case State.ReadingFullName:

							if (pattern.FullName == null)
								pattern.FullName = character.ToString();
							else pattern.FullName += character;

							break;
						case State.ReadingParameters:
							string_builder.Append(character);
							break;

						case State.NotSupported:
							throw new Exception("Raw query patterns do not (yet) support IL parsing");
					}
				}

				if (pattern.Parameters.Any())
				{
					pattern.FullName += $"({String.Join(",", pattern.Parameters)})";
				}

				yield return pattern;
			}
		}
	}

}
