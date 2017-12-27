using Microsoft.CSharp;
using Mono.Cecil;
using System;
using System.CodeDom.Compiler;
using System.Linq;
using System.Reflection;

namespace Mod.Framework.Emitters
{
	// TODO: Test CSharpEmitter

	public class CSharpEmitter2<TReturnObject> : IEmitter<TReturnObject>
	{
		public TReturnObject Emit()
		{
			//CSharpDecompiler
			//var d = new CSharpDecompiler();

			return default(TReturnObject);
		}
	}

	/// <summary>
	/// This emitter can accept C# code 
	/// </summary>
	public class CSharpEmitter : CSharpEmitter<Assembly>
	{
		public CSharpEmitter(CompilerParameters compilerParameters, params string[] csharpCode)
		: base(compilerParameters, csharpCode)
		{

		}
	}

	/// <summary>
	/// This emitter can accept C# code 
	/// </summary>
	public class CSharpEmitter<TReturnObject> : IEmitter<TReturnObject>
		where TReturnObject : class
	{
		private static string _classPrefix = typeof(CSharpEmitter).FullName.ToLower();

		private CompilerParameters _compilerParameters;
		private string[] _csharpCode;

		public string[] Usings { get; set; }
		public string[] References { get; set; }

		const int RandomClassNameLength = 20;

		public CSharpEmitter(params string[] csharpCode)
		{
			this._csharpCode = csharpCode;
		}

		public CSharpEmitter(CompilerParameters compilerParameters, params string[] csharpCode)
		{
			this._compilerParameters = compilerParameters ?? new CompilerParameters();
			this._csharpCode = csharpCode;
		}

		TReturnObject GetReturnObjectFromAssembly(Assembly compiledAssembly)
		{
			TReturnObject result = null;
			var resultType = typeof(TReturnObject);

			if (resultType.IsAssignableFrom(typeof(Assembly)))
			{
				result = (TReturnObject)(object)compiledAssembly;
			}
			else if (resultType.IsAssignableFrom(typeof(TypeInfo)))
			{
				result = (TReturnObject)(object)compiledAssembly.DefinedTypes
					.Single();
			}
			else if (resultType.IsAssignableFrom(typeof(PropertyInfo)))
			{
				result = (TReturnObject)(object)compiledAssembly.DefinedTypes
					.Single()
					.GetProperties()
					.Single();
			}
			else if (resultType.IsAssignableFrom(typeof(MethodInfo)))
			{
				result = (TReturnObject)(object)compiledAssembly.DefinedTypes
					.Single()
					.GetMethods()
					.Single();
			}
			else if (resultType.IsAssignableFrom(typeof(FieldInfo)))
			{
				result = (TReturnObject)(object)compiledAssembly.DefinedTypes
					.Single()
					.GetFields()
					.Single();
			}
			else if (resultType.IsAssignableFrom(typeof(EventInfo)))
			{
				result = (TReturnObject)(object)compiledAssembly.DefinedTypes
					.Single()
					.GetEvents()
					.Single();
			}
			else if (resultType.IsAssignableFrom(typeof(ConstructorInfo)))
			{
				result = (TReturnObject)(object)compiledAssembly.DefinedTypes
					.Single()
					.GetConstructors()
					.Single();
			}

			// cecil
			else if (resultType.IsAssignableFrom(typeof(AssemblyDefinition)))
			{
				var property = compiledAssembly.DefinedTypes
					.Single()
					.GetProperties()
					.Single();

				result = (TReturnObject)(object)AssemblyDefinition.ReadAssembly(compiledAssembly.Location);
			}
			else if (resultType.IsAssignableFrom(typeof(PropertyDefinition)))
			{
				var property = compiledAssembly.DefinedTypes
					.Single()
					.GetProperties()
					.Single();

				var assembly = AssemblyDefinition.ReadAssembly(compiledAssembly.Location);

				result = (TReturnObject)(object)assembly.Modules
						.SelectMany(x => x.Types)
						.SelectMany(x => x.Properties)
						.Single(x => x.FullName == property.PropertyType.FullName + " " + property.DeclaringType.FullName + "::" + property.Name + "()");
			}
			else if (resultType.IsAssignableFrom(typeof(FieldDefinition)))
			{
				var field = compiledAssembly.DefinedTypes
					.Single()
					.GetFields()
					.Single();

				var assembly = AssemblyDefinition.ReadAssembly(compiledAssembly.Location);

				result = (TReturnObject)(object)assembly.Modules
						.SelectMany(x => x.Types)
						.SelectMany(x => x.Fields)
						.Single(x => x.FullName == field.FieldType.FullName + " " + field.DeclaringType.FullName + "::" + field.Name + "()");
			}
			else if (resultType.IsAssignableFrom(typeof(MethodDefinition)))
			{
				var method = compiledAssembly.DefinedTypes
					.Single()
					.GetMethods()
					.Where(x => !x.DeclaringType.FullName.StartsWith("System."))
					.Single();

				var assembly = AssemblyDefinition.ReadAssembly(compiledAssembly.Location);

				result = (TReturnObject)(object)assembly.Modules
						.SelectMany(x => x.Types)
						.SelectMany(x => x.Methods)
						.Single(x => x.FullName == method.ReturnType.FullName + " " + method.DeclaringType.FullName + "::" + method.Name + "(" +
							String.Join(",", method.GetParameters().ToArray()
								.Select(p => p.ParameterType.FullName)
							)
						+ ")");

				// this is required as the instructions are only existant when you look in the debugger
				// if you dont, there seems to be zero instructions
				// ...i have no idea why
				(result as MethodDefinition).Body.Instructions.ToArray();
			}

			return result;
		}

		public TReturnObject Emit()
		{
			if (this._compilerParameters == null)
			{
				this._compilerParameters = new CompilerParameters();
			}

			if (References != null)
			{
				this._compilerParameters.ReferencedAssemblies.AddRange(References);
			}

			CSharpCodeProvider provider = new CSharpCodeProvider();
			CompilerResults results = provider.CompileAssemblyFromSource(_compilerParameters, _csharpCode);

			if (results.Errors == null || !results.Errors.HasErrors)
				return GetReturnObjectFromAssembly(results.CompiledAssembly);

			if (results.Errors.OfType<CompilerError>().Any(x => x.ErrorNumber == "CS1518"))
			{
				if (_csharpCode.Length == 1)
				{
					var rand = new Random();

					var name = "";
					while (name.Length < RandomClassNameLength)
					{
						name += rand.Next(0, 9);
					}

					string usings = "";
					if (Usings != null)
					{
						usings = String.Join(" ", Usings.Select(x => $"using {x};"));
					}

					_csharpCode[0] = $@"
						{usings}

						public class {_classPrefix}_{name} {{ {_csharpCode[0]} }}
					";

					results = provider.CompileAssemblyFromSource(_compilerParameters, _csharpCode);
					if (results.Errors == null || !results.Errors.HasErrors)
						return GetReturnObjectFromAssembly(results.CompiledAssembly);
				}
			}

			return null;
		}
	}
}
