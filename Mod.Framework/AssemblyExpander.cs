using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mod.Framework
{
	/// <summary>
	/// This is used to build the <see cref="Query"/> filters.
	/// It allows input of most types of meta data from Mono.Cecil.
	/// The expander will recursively scan the meta data into a flat array in memory, which makes it easier 
	/// for <see cref="QueryResult"/> to make use of Linq
	/// </summary>
	public class AssemblyExpander
	{
		private List<object> _context;
		private List<MetaData> _results = new List<MetaData>();

		private static Dictionary<string, List<MetaData>> _assembly_cache = new Dictionary<string, List<MetaData>>();

		public AssemblyExpander()
		{

		}

		public IEnumerable<MetaData> Results
		{
			get { return _results; }
		}

		public void SetContext(IEnumerable<object> context)
		{
			var context_map = new List<object>();

			foreach (var item in context)
			{
				context_map.Add(item);
			}

			this._context = context_map;
		}

		// TODO: Add IL patterns etc (plus a switch to turn off as this is very intensive)
		//public void Expand(MethodDefinition method, Instruction instruction, bool add = true)
		//{
		//	//if (add) this._context.Add(parameter);
		//	if (add) this._results.Add(new TypeMeta()
		//	{
		//		Instance = instruction,
		//		AssemblyName = method.DeclaringType.Module.Assembly.Name.Name,
		//		//FullName = $"{method.FullName}#{instruction.ToString()}"
		//		FullName = $"{method.DeclaringType.FullName}.{method.Name}{GenerateParameters(method.Parameters)}#{instruction.ToString()}"
		//	});
		//}

		public void Expand(ParameterDefinition parameter, bool add = true)
		{
			if (add) this._results.Add(new MetaData()
			{
				Instance = parameter,
				AssemblyName = parameter.ParameterType.Module.Assembly.Name.Name,
				FullName = parameter.ParameterType.Name
			});
		}

		public void Expand(PropertyDefinition property, bool add = true)
		{
			if (add) this._results.Add(new MetaData()
			{
				Instance = property,
				AssemblyName = property.PropertyType.Module.Assembly.Name.Name,
				FullName = property.DeclaringType.FullName + '.' + property.Name
			});
		}

		string GenerateParameters(IEnumerable<ParameterDefinition> parameters)
		{
			var sb = new StringBuilder();

			sb.Append('(');

			bool commar = false;
			foreach (var param in parameters)
			{
				if (commar) sb.Append(',');
				commar = true;
				sb.Append(param.ParameterType.FullName);
			}

			sb.Append(')');

			return sb.ToString();
		}

		public void Expand(MethodDefinition method, bool add = true)
		{
			if (add) this._results.Add(new MetaData()
			{
				Instance = method,
				AssemblyName = method.DeclaringType.Module.Assembly.Name.Name,
				FullName = $"{method.DeclaringType.FullName}.{method.Name}{GenerateParameters(method.Parameters)}"
			});

			foreach (var parameter in method.Parameters)
			{
				Expand(parameter);
			}

			//if (method.HasBody)
			//{
			//	foreach (var instruction in method.Body.Instructions)
			//	{
			//		Expand(method, instruction);
			//	}
			//}
		}
		public void Expand(TypeDefinition type, bool add = true)
		{
			if (add) this._results.Add(new MetaData()
			{
				Instance = type,
				AssemblyName = type.Module.Assembly.Name.Name,
				FullName = type.FullName
			});

			foreach (var nested in type.NestedTypes)
			{
				Expand(nested);
			}
			foreach (var method in type.Methods)
			{
				Expand(method);
			}
			foreach (var prop in type.Properties)
			{
				Expand(prop);
			}
		}

		public void Expand(ModuleDefinition module, bool add = true)
		{
			if (add) this._results.Add(new MetaData()
			{
				Instance = module,
				AssemblyName = module.Assembly.Name.Name,
				FullName = module.Name
			});

			foreach (var type in module.Types)
			{
				Expand(type);
			}
		}

		public void Expand(AssemblyDefinition assembly, bool add = true)
		{
			List<MetaData> meta = null;
			if (!_assembly_cache.TryGetValue(assembly.FullName, out meta) || meta == null)
			{
				Console.Write($"Expanding assembly {assembly.Name}...");
				var expand_start = DateTime.Now;

				if (add) this._results.Add(new MetaData()
				{
					Instance = assembly,
					AssemblyName = assembly.Name.Name,
					FullName = assembly.FullName
				});

				foreach (var module in assembly.Modules)
				{
					Expand(module);
				}

				System.Console.WriteLine($"found {this._results.Count} item(s). Took {(DateTime.Now - expand_start).TotalMilliseconds}ms");
				_assembly_cache.Add(assembly.FullName, this._results);
			}
			else
			{
				this._results = meta;
			}
		}
		
		public void Expand()
		{
			var initial = this._context.ToArray();
			var self = this.GetType().GetMethods()
				.Where(x => x.Name == "Expand")
				.Select(x => new { Method = x, Parameters = x.GetParameters() })
				.Where(x => x.Parameters.Count() == 2);

			foreach (var item in initial)
			{
				var item_type = item.GetType();
				var expand_match = self
					.SingleOrDefault(x => x.Parameters[0].ParameterType.IsAssignableFrom(item_type))
				;

				if (expand_match != null)
				{
					expand_match.Method.Invoke(this, new[] { item, true });
				}
				else
				{
					throw new InvalidOperationException("No Expand match");
				}
			}
		}
	}
}
