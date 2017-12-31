using Mono.Cecil;
using System.Collections.Generic;

namespace Mod.Framework
{
	public static class CloningExtensions
	{
		/// <summary>
		/// Clones the signatures of a method into a new empty method.
		/// This is used to replace native methods.
		/// </summary>
		/// <param name="method">The method to clone</param>
		/// <returns>The new cloned method</returns>
		public static MethodDefinition Clone(this MethodDefinition method)
		{
			var clone = new MethodDefinition(method.Name, method.Attributes, method.ReturnType)
			{
				ImplAttributes = method.ImplAttributes
			};

			foreach (var param in method.Parameters)
			{
				var parameter = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType);
				//{
				//	HasConstant = param.HasConstant,
				//	HasDefault = param.HasDefault,
				//	Constant = param.Constant
				//};

				if (param.HasConstant)
				{
					parameter.HasConstant = true;
					parameter.Constant = param.Constant;
				}
				if (param.HasDefault)
				{
					parameter.HasDefault = true;
				}

				clone.Parameters.Add(parameter);
			}

			foreach (var method_ref in method.Overrides)
			{
				clone.Overrides.Add(method_ref);
			}

			foreach (var param in method.GenericParameters)
			{
				clone.GenericParameters.Add(param);
			}

			foreach (var attribute in method.CustomAttributes)
			{
				clone.CustomAttributes.Add(attribute);
			}

			foreach (var security_declaration in method.SecurityDeclarations)
			{
				clone.SecurityDeclarations.Add(security_declaration);
			}

			//clone.PInvokeInfo = method.PInvokeInfo;

			return clone;
		}

		/// <summary>
		/// Clones the signatures of a method into a new empty method.
		/// This is used to replace native methods.
		/// </summary>
		/// <param name="methods">The methods to clone</param>
		/// <returns>The new cloned methods</returns>
		public static IEnumerable<MethodDefinition> Clone(this IEnumerable<MethodDefinition> methods)
		{
			foreach (var method in methods)
			{
				yield return method.Clone();
			}
		}
	}
}
