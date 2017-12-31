using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	public static class CecilExtensions
	{
		#region Assembly Extensions
		public static IEnumerable<TypeReference> SelectAllTypes(this AssemblyDefinition assembly)
		{
			return assembly.Modules.SelectMany(am =>
				am.Types.SelectMany(t => t.SelectAllTypes())
			);
		}

		public static IEnumerable<MethodDefinition> SelectAllMethods(this AssemblyDefinition assembly)
		{
			return assembly.SelectAllTypes().SelectMany(t => t.SelectAllMethods());
		}

		public static IEnumerable<PropertyDefinition> SelectAllProperties(this AssemblyDefinition assembly)
		{
			return assembly.SelectAllTypes().SelectMany(t => t.SelectAllProperties());
		}

		public static IEnumerable<(MethodDefinition method, Instruction instruction)> SelectAllInstructions(this AssemblyDefinition assembly)
		{
			return assembly.Modules.SelectMany(m => m.SelectAllInstructions());
		}

		public static IEnumerable<(MethodDefinition method, Collection<Instruction> instructions)> SelectInstructions(this AssemblyDefinition assembly)
		{
			return assembly.Modules.SelectMany(m => m.SelectInstructions());
		}
		#endregion

		#region Module Extensions
		public static IEnumerable<TypeReference> SelectAllTypes(this ModuleDefinition module)
		{
			return module.Types.SelectMany(t => t.SelectAllTypes());
		}

		public static IEnumerable<MethodDefinition> SelectAllMethods(this ModuleDefinition module)
		{
			return module.SelectAllTypes().SelectMany(t => t.SelectAllMethods());
		}

		public static IEnumerable<PropertyDefinition> SelectAllProperties(this ModuleDefinition module)
		{
			return module.SelectAllTypes().SelectMany(t => t.SelectAllProperties());
		}

		public static IEnumerable<(MethodDefinition method, Instruction instruction)> SelectAllInstructions(this ModuleDefinition module)
		{
			return module.SelectAllTypes().AsDefinitions().SelectMany(t => t.SelectAllInstructions());
		}

		public static IEnumerable<(MethodDefinition method, Collection<Instruction> instructions)> SelectInstructions(this ModuleDefinition module)
		{
			return module.SelectAllTypes().AsDefinitions().SelectMany(t => t.SelectInstructions());
		}
		#endregion

		#region Type Extensions
		public static IEnumerable<TypeDefinition> AsDefinitions(this IEnumerable<TypeReference> types)
		{
			foreach (var type in types)
			{
				var resolved = type.Resolve();
				yield return resolved; //(definition: resolved, reference: type);
			}
		}

		public static IEnumerable<TypeReference> SelectAllTypes(this TypeReference type)
		{
			yield return type;

			var definition = type.Resolve();

			if (definition != null)
			{
				foreach (var nestedType in definition.NestedTypes)
					yield return nestedType;

				foreach (var intf in definition.Interfaces)
				{
					var generic = intf.InterfaceType as GenericInstanceType;
					if (generic != null)
					{
						foreach (var argumentType in generic.GenericArguments)
						{
							yield return argumentType;
						}
					}
				}
			}
		}

		public static IEnumerable<PropertyDefinition> SelectAllProperties(this TypeReference type)
		{
			return type.SelectAllTypes()
				.AsDefinitions()
				.Where(t => t?.HasProperties == true)
				.SelectMany(t => t.Properties);
		}

		public static IEnumerable<MethodDefinition> SelectAllMethods(this TypeReference type)
		{
			return type.SelectAllTypes()
				.AsDefinitions()
				.Where(t => t?.HasMethods == true)
				.SelectMany(t => t.Methods);
		}

		public static IEnumerable<(MethodDefinition method, Instruction instruction)> SelectAllInstructions(this TypeDefinition type)
		{
			return type.SelectAllMethods()
				.Where(m => m.Body?.Instructions != null)
				.SelectMany(m => m.Body.Instructions.Select(i => (method: m, instruction: i)));
		}

		public static IEnumerable<(MethodDefinition method, Collection<Instruction> instructions)> SelectInstructions(this TypeDefinition type)
		{
			return type.SelectAllMethods()
				.Select(m => (method: m, instructions: m.Body?.Instructions))
				.Where(s => s.instructions != null);
		}
		#endregion

		#region Method
		/// <summary>
		/// Finds the first match of the given OpCode pattern
		/// </summary>
		/// <param name="method">The method to search in</param>
		/// <param name="opcodes">The pattern of opcodes</param>
		/// <returns>The first and last instruction found by the pattern.</returns>
		public static (Instruction first, Instruction last) FindPattern(this MethodDefinition method, params OpCode[] opcodes)
		{
			Instruction first = null, last = null;
			foreach (var ins in method.Body.Instructions)
			{
				first = null;
				last = null;
				Instruction match = ins;
				foreach (var opcode in opcodes)
				{
					if (opcode == match.OpCode)
					{
						if (first == null) first = match;
						last = match;
						match = match.Next;
					}
					else
					{
						first = null;
						last = null;
						break;
					}
				}

				if (first != null && last != null)
					break;
			}

			return (first, last);
		}

		/// <summary>
		/// Returns a cleaned name that can be used to add a new method into a type
		/// </summary>
		/// <param name="method">Method whose name needs cleaning</param>
		/// <returns></returns>
		public static string GetSafeName(this MethodDefinition method)
		{
			//return method.Name.Replace(".", String.Empty);
			return method.Name.TrimStart('.');
		}

		public static string GetBackingName(this FieldDefinition field)
		{
			return $"<{field.Name}>k__BackingField";
		}

		/// <summary>
		/// Compares to see if two methods parameters are compatible
		/// </summary>
		/// <param name="method"></param>
		/// <param name="compareTo"></param>
		/// <param name="ignoreDeclaringType"></param>
		/// <param name="ignoreParameterNames"></param>
		/// <returns>True when the parameter matches</returns>
		public static bool ParametersMatch(this MethodReference method, MethodReference compareTo, bool ignoreDeclaringType = true, bool ignoreParameterNames = false)
		{
			if (method.Parameters.Count != compareTo.Parameters.Count)
				return false;

			for (var x = 0; x < method.Parameters.Count; x++)
			{
				if (method.Parameters[x].ParameterType.FullName != compareTo.Parameters[x].ParameterType.FullName

					&& (
						ignoreDeclaringType
						&& method.Parameters[x].ParameterType != method.DeclaringType
					)
				)
				{
					return false;
				}

				if (!ignoreParameterNames && method.Parameters[x].Name != compareTo.Parameters[x].Name)
					return false;
			}

			return true;
		}

		/// <summary>
		/// Compares two methods to check if the signatures match
		/// </summary>
		/// <param name="sourceMethod">Source method</param>
		/// <param name="compareTo">The method to be compared to</param>
		/// <param name="ignoreDeclaringType">
		/// Ignores comparing against the declaring type of the method.
		/// This allows can help to enforce instance callbacks are correct.
		/// </param>
		/// <returns>True when the signature matches</returns>
		public static bool SignatureMatches
		(
			this MethodDefinition sourceMethod,
			MethodDefinition compareTo,

			bool ignoreDeclaringType = true
		)
		{
			if (sourceMethod.Name != compareTo.Name)
				return false;
			if (sourceMethod.ReturnType.FullName != compareTo.ReturnType.FullName)
				return false;
			if (sourceMethod.Overrides.Count != compareTo.Overrides.Count)
				return false;
			if (sourceMethod.GenericParameters.Count != compareTo.GenericParameters.Count)
				return false;
			if (!sourceMethod.DeclaringType.IsInterface && sourceMethod.Attributes != compareTo.Attributes)
				return false;

			if (!sourceMethod.ParametersMatch(compareTo, ignoreDeclaringType))
				return false;

			for (var x = 0; x < sourceMethod.Overrides.Count; x++)
			{
				if (sourceMethod.Overrides[x].Name != compareTo.Overrides[x].Name)
					return false;
			}

			for (var x = 0; x < sourceMethod.GenericParameters.Count; x++)
			{
				if (sourceMethod.GenericParameters[x].Name != compareTo.GenericParameters[x].Name)
					return false;
			}

			return true;
		}
		#endregion

		#region Type
		/// <summary>
		/// Ensures all members of the type are publicly accessible
		/// </summary>
		/// <param name="type">The type to be made accessible</param>
		public static void MakePublic(this TypeDefinition type)
		{
			var state = type.IsPublic;
			if (type.IsNestedFamily)
			{
				type.IsNestedFamily = false;
				type.IsNestedPublic = true;
				state = false;
			}
			if (type.IsNestedFamilyAndAssembly)
			{
				type.IsNestedFamilyAndAssembly = false;
				type.IsNestedPublic = true;
				state = false;
			}
			if (type.IsNestedFamilyOrAssembly)
			{
				type.IsNestedFamilyOrAssembly = false;
				type.IsNestedPublic = true;
				state = false;
			}
			if (type.IsNestedPrivate)
			{
				type.IsNestedPrivate = false;
				type.IsNestedPublic = true;
				state = false;
			}

			type.IsPublic = state;

			foreach (var itm in type.Methods)
			{
				itm.IsPublic = true;
				if (itm.IsFamily) itm.IsFamily = false;
				if (itm.IsFamilyAndAssembly) itm.IsFamilyAndAssembly = false;
				if (itm.IsFamilyOrAssembly) itm.IsFamilyOrAssembly = false;
				if (itm.IsPrivate) itm.IsPrivate = false;
			}
			foreach (var itm in type.Fields)
			{
				if (itm.IsFamily) itm.IsFamily = false;
				if (itm.IsFamilyAndAssembly) itm.IsFamilyAndAssembly = false;
				if (itm.IsFamilyOrAssembly) itm.IsFamilyOrAssembly = false;
				if (itm.IsPrivate)
				{
					if (type.Events.Where(x => x.Name == itm.Name).Count() == 0)
						itm.IsPrivate = false;
					else
					{
						continue;
					}
				}

				itm.IsPublic = true;
			}
			foreach (var itm in type.Properties)
			{
				if (null != itm.GetMethod)
				{
					itm.GetMethod.IsPublic = true;
					if (itm.GetMethod.IsFamily) itm.GetMethod.IsFamily = false;
					if (itm.GetMethod.IsFamilyAndAssembly) itm.GetMethod.IsFamilyAndAssembly = false;
					if (itm.GetMethod.IsFamilyOrAssembly) itm.GetMethod.IsFamilyOrAssembly = false;
					if (itm.GetMethod.IsPrivate) itm.GetMethod.IsPrivate = false;
				}
				if (null != itm.SetMethod)
				{
					itm.SetMethod.IsPublic = true;
					if (itm.SetMethod.IsFamily) itm.SetMethod.IsFamily = false;
					if (itm.SetMethod.IsFamilyAndAssembly) itm.SetMethod.IsFamilyAndAssembly = false;
					if (itm.SetMethod.IsFamilyOrAssembly) itm.SetMethod.IsFamilyOrAssembly = false;
					if (itm.SetMethod.IsPrivate) itm.SetMethod.IsPrivate = false;
				}
			}

			foreach (var nt in type.NestedTypes)
				nt.MakePublic();
		}

		/// <summary>
		/// Transforms all of the instance methods into virtual (overridable) methods
		/// </summary>
		/// <param name="type">The type to be made virtual</param>
		public static void MakeVirtual(this TypeDefinition type)
		{
			var methods = type.Methods.Where(m => !m.IsConstructor && !m.IsStatic).ToArray();
			foreach (var method in methods)
			{
				method.IsVirtual = true;
				method.IsNewSlot = true;
			}

			foreach (var ins_set in type.Module.SelectAllInstructions())
			{
				if (methods.Any(x => x == ins_set.instruction.Operand))
				{
					if (ins_set.instruction.OpCode != OpCodes.Callvirt)
					{
						ins_set.instruction.OpCode = OpCodes.Callvirt;
					}
				}
			};
		}

		/// <summary>
		/// Compares two types to check if the type's signatures match. 
		/// This can be useful when replacing types/interfaces
		/// </summary>
		/// <param name="type">Source type</param>
		/// <param name="compareTo">The type to compare to</param>
		/// <returns>True when the signatures match</returns>
		public static bool SignatureMatches(this TypeDefinition type, TypeDefinition compareTo)
		{
			var typeInstanceMethods = type.Methods.Where(m => !m.IsStatic && !m.IsGetter && !m.IsSetter);
			var compareToInstanceMethods = compareTo.Methods.Where(m => !m.IsStatic && !m.IsGetter && !m.IsSetter && (type.IsInterface && !m.IsConstructor));

			var missing = compareToInstanceMethods.Where(m => !typeInstanceMethods.Any(m2 => m2.Name == m.Name));

			if (typeInstanceMethods.Count() != compareToInstanceMethods.Count())
				return false;

			for (var x = 0; x < typeInstanceMethods.Count(); x++)
			{
				var typeMethod = typeInstanceMethods.ElementAt(x);
				var compareToMethod = compareToInstanceMethods.ElementAt(x);

				if (!typeMethod.SignatureMatches(compareToMethod))
					return false;
			}

			return true;
		}

		/// <summary>
		/// Enumerates all nested types in the given type
		/// </summary>
		/// <param name="parent">Type to find nested types in</param>
		/// <param name="callback">The callback to process nested types</param>
		public static void ForEachNestedType(this TypeDefinition parent, Action<TypeDefinition> callback)
		{
			foreach (var type in parent.NestedTypes)
			{
				callback(type);

				type.ForEachNestedType(callback);
			}
		}
		#endregion

		#region ILProcessor
		/// <summary>
		/// Inserts a group of instructions after the target instruction
		/// </summary>
		public static void InsertAfter(this Mono.Cecil.Cil.ILProcessor processor, Instruction target, IEnumerable<Instruction> instructions)
		{
			foreach (var instruction in instructions.Reverse())
			{
				processor.InsertAfter(target, instruction);
			}
		}
		#endregion

		#region Instructions
		/// <summary>
		/// Finds the first instruction after the current instruction that matches the predicate
		/// </summary>
		/// <param name="initial">The current instruction</param>
		/// <param name="predicate">Predicate</param>
		/// <returns>The matching instruction</returns>
		public static Instruction Previous(this Instruction initial, Func<Instruction, Boolean> predicate)
		{
			while (initial.Previous != null)
			{
				if (predicate(initial)) return initial;
				initial = initial.Previous;
			}

			return null;
		}

		/// <summary>
		/// Finds the first instruction before the current instruction that matches the predicate
		/// </summary>
		/// <param name="initial">The current instruction</param>
		/// <param name="predicate">Predicate</param>
		/// <returns>The matching instruction</returns>
		public static Instruction Next(this Instruction initial, Func<Instruction, Boolean> predicate)
		{
			while (initial.Next != null)
			{
				if (predicate(initial.Next)) return initial.Next;
				initial = initial.Next;
			}

			return null;
		}
		#endregion
	}
}