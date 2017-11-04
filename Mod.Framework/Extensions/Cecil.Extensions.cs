using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework.Extensions
{
	public static class CecilExtensions
	{
		#region Assembly
		/// <summary>
		/// Gets a type from the assembly using <see cref="TypeReference.FullName"/>
		/// </summary>
		/// <param name="assemblyDefinition"></param>
		/// <param name="name"></param>
		/// <returns></returns>
		public static TypeDefinition Type(this AssemblyDefinition assemblyDefinition, string name)
		{
			return assemblyDefinition.MainModule.Types.Single(x => x.FullName == name);
		}

		/// <summary>
		/// Enumerates all instructions in all methods across each type of the assembly
		/// </summary>
		public static void ForEachInstruction(this AssemblyDefinition assembly, Action<MethodDefinition, Mono.Cecil.Cil.Instruction> callback)
		{
			assembly.ForEachType(type =>
			{
				if (type.HasMethods)
				{
					foreach (var method in type.Methods)
					{
						if (method.HasBody)
						{
							foreach (var ins in method.Body.Instructions.ToArray())
								callback.Invoke(method, ins);
						}
					}
				}
			});
		}

		/// <summary>
		/// Enumerates over each type in the assembly, including nested types
		/// </summary>
		public static void ForEachType(this AssemblyDefinition assembly, Action<TypeDefinition> callback)
		{
			foreach (var module in assembly.Modules)
			{
				foreach (var type in module.Types)
				{
					callback(type);

					//Enumerate nested types
					type.ForEachNestedType(callback);
				}
			}
		}
		#endregion

		#region Module
		/// <summary>
		/// Enumerates all methods in the current module
		/// </summary>
		public static void ForEachMethod(this ModuleDefinition module, Action<MethodDefinition> callback)
		{
			module.ForEachType(type =>
			{
				foreach (var mth in type.Methods)
				{
					callback.Invoke(mth);
				}
			});
		}

		/// <summary>
		/// Enumerates all instructions in all methods across each type of the assembly
		/// </summary>
		public static void ForEachInstruction(this ModuleDefinition module, Action<MethodDefinition, Mono.Cecil.Cil.Instruction> callback)
		{
			module.ForEachMethod(method =>
			{
				if (method.HasBody)
				{
					foreach (var ins in method.Body.Instructions.ToArray())
						callback.Invoke(method, ins);
				}
			});
		}

		/// <summary>
		/// Enumerates over each type in the assembly, including nested types
		/// </summary>
		public static void ForEachType(this ModuleDefinition module, Action<TypeDefinition> callback)
		{
			foreach (var type in module.Types)
			{
				callback(type);

				//Enumerate nested types
				type.ForEachNestedType(callback);
			}
		}
		#endregion

		#region Method
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

		/// <summary>
		/// Replaces all occurrences of the current method in the assembly with the provided method
		/// </summary>
		/// <param name="method"></param>
		/// <param name="replacement"></param>
		public static void ReplaceWith(this MethodDefinition method, MethodReference replacement)
		{
			//Enumerates over each type in the assembly, including nested types
			method.Module.ForEachInstruction((mth, ins) =>
			{
				//Compare each instruction operand value as if it were a method reference. Check to 
				//see if they match the current method definition. If it matches, it can be swapped.
				if (ins.Operand == method)
					ins.Operand = replacement;

				var generic_method = ins.Operand as GenericInstanceMethod;
				if (generic_method != null)
				{
					if (generic_method.ElementMethod == method)
					{
						var replacement_instance = new GenericInstanceMethod(replacement);

						foreach (var item in generic_method.GenericArguments)
							replacement_instance.GenericArguments.Add(item);

						foreach (var item in generic_method.GenericParameters)
							replacement_instance.GenericParameters.Add(item);

						ins.Operand = replacement_instance;
					}
				}
			});
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
		/// Gets a field from the given type
		/// </summary>
		/// <param name="typeDefinition">The type to search</param>
		/// <param name="name">Name of the field</param>
		public static FieldDefinition Field(this TypeDefinition typeDefinition, string name)
		{
			return typeDefinition.Fields.Single(x => x.Name == name);
		}

		/// <summary>
		/// Gets a method from the given type
		/// </summary>
		/// <param name="type">The type to search</param>
		/// <param name="name">Name of the method</param>
		public static MethodDefinition Method(this TypeDefinition type, string name)
		{
			return type.Methods.Single(x => x.Name == name);
		}

		/// <summary>
		/// Enumerates over each method in the given type
		/// </summary>
		/// <param name="type">The type that contains the methods</param>
		/// <param name="callback">The callback to process methods with</param>
		public static void ForEachMethod(this TypeDefinition type, Action<MethodDefinition> callback)
		{
			if (type.HasMethods)
			{
				foreach (var method in type.Methods)
				{
					callback.Invoke(method);
				}
			}
		}

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

			type.Module.ForEachInstruction((method, instruction) =>
			{
				if (methods.Any(x => x == instruction.Operand))
				{
					if (instruction.OpCode != OpCodes.Callvirt)
					{
						instruction.OpCode = OpCodes.Callvirt;
					}
				}
			});
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

		#region ILProcesor
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

		/// <summary>
		/// Parses anonymously typed instructions into Instruction instances that are compatible with Mono.Cecil
		/// </summary>
		/// <param name="anonymous">Parameter list of instructions, or array of instructions</param>
		/// <returns>The parsed instructions</returns>
		public static IEnumerable<Instruction> ParseAnonymousInstruction(params object[] anonymous)
		{
			foreach (var anon in anonymous)
			{
				var expandable = anon as IEnumerable<object>;
				var resolver = anon as Func<IEnumerable<object>>;

				if (resolver != null)
				{
					expandable = resolver();
				}

				if (expandable != null)
				{
					foreach (var item in expandable)
					{
						foreach (var sub in ParseAnonymousInstruction(item))
						{
							yield return sub;
						}
					}
				}
				else yield return InternalAnonymousToInstruction(anon);
			}
		}

		/// <summary>
		/// Adds a list of anonymously typed instructions into the current list
		/// </summary>
		/// <param name="list">The list to add parsed instructions to</param>
		/// <param name="anonymous">The list of anonymous types</param>
		public static void Add(this List<Instruction> list, params object[] anonymous)
		{
			list.AddRange(ParseAnonymousInstruction(anonymous));
		}

		/// <summary>
		/// Converts a anonymous type into an Instruction
		/// </summary>
		/// <param name="anonymous">The anonymous type to be parsed</param>
		/// <returns></returns>
		private static Instruction InternalAnonymousToInstruction(object anonymous)
		{
			var reference = anonymous as InstructionReference;
			if (reference != null)
			{
				return reference.Reference;
			}

			var annonType = anonymous.GetType();
			var properties = annonType.GetProperties();

			//An instruction consists of only 1 opcode, or 1 opcode and 1 operation
			if (properties.Length == 0 || properties.Length > 2)
				throw new NotSupportedException("Anonymous instruction expected 1 or 2 properties");

			//Determine the property that contains the OpCode property
			var propOpcode = properties.SingleOrDefault(x => x.PropertyType == typeof(OpCode));
			if (propOpcode == null)
				throw new NotSupportedException("Anonymous instruction expected 1 opcode property");

			//Get the opcode value
			var opcode = (OpCode)propOpcode.GetMethod.Invoke(anonymous, null);

			//Now determine if we need an operand or not
			Instruction ins = null;
			if (properties.Length == 2)
			{
				//We know we already have the opcode determined, so the second property
				//must be the operand.
				var propOperand = properties.Where(x => x != propOpcode).Single();

				var operand = propOperand.GetMethod.Invoke(anonymous, null);
				var operandType = propOperand.PropertyType;
				reference = operand as InstructionReference;
				if (reference != null)
				{
					operand = reference.Reference;
					operandType = reference.Reference.GetType();
				}

				//Now find the Instruction.Create method that takes the same type that is 
				//specified by the operands type.
				//E.g. Instruction.Create(OpCode, FieldReference)
				var instructionMethod = typeof(Instruction).GetMethods()
					.Where(x => x.Name == "Create")
					.Select(x => new { Method = x, Parameters = x.GetParameters() })
					//.Where(x => x.Parameters.Length == 2 && x.Parameters[1].ParameterType == propOperand.PropertyType)
					.Where(x => x.Parameters.Length == 2 && x.Parameters[1].ParameterType.IsAssignableFrom(operandType))
					.SingleOrDefault();

				if (instructionMethod == null)
					throw new NotSupportedException($"Instruction.Create does not support type {operandType.FullName}");

				//Get the operand value and pass it to the Instruction.Create method to create
				//the instruction.
				ins = (Instruction)instructionMethod.Method.Invoke(anonymous, new[] { opcode, operand });
			}
			else
			{
				//No operand required
				ins = Instruction.Create(opcode);
			}

			return ins;
		}

		/// <summary>
		/// Inserts a list of anonymous instructions after the target instruction
		/// </summary>
		public static List<Instruction> InsertAfter(this Mono.Cecil.Cil.ILProcessor processor, Instruction target, params object[] instructions)
		{
			var created = new List<Instruction>();
			foreach (var anon in instructions.Reverse())
			{
				var ins = ParseAnonymousInstruction(anon);
				processor.InsertAfter(target, ins);

				created.AddRange(ins);
			}

			return created;
		}

		/// <summary>
		/// Inserts a list of anonymous instructions before the target instruction
		/// </summary>
		public static List<Instruction> InsertBefore(this Mono.Cecil.Cil.ILProcessor processor, Instruction target, params object[] instructions)
		{
			var created = new List<Instruction>();
			foreach (var anon in instructions)
			{
				var ins = ParseAnonymousInstruction(anon);
				processor.InsertBefore(target, ins);

				created.AddRange(ins);
			}

			return created;
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

		/// <summary>
		/// Replaces instruction references (ie if, try) to a new instruction target.
		/// This is useful if you are injecting new code before a section of code that is already
		/// the receiver of a try/if block.
		/// </summary>
		/// <param name="current">The original instruction</param>
		/// <param name="newTarget">The new instruction that will receive the transfer</param>
		/// <param name="originalMethod">The original method that is used to search for transfers</param>
		public static void ReplaceTransfer(this Instruction current, Instruction newTarget, MethodDefinition originalMethod)
		{
			//If a method has a body then check the instruction targets & exceptions
			if (originalMethod.HasBody)
			{
				//Replaces instruction references from the old instruction to the new instruction
				foreach (var ins in originalMethod.Body.Instructions.Where(x => x.Operand == current))
					ins.Operand = newTarget;

				//If there are exception handlers, it's possible that they will also need to be switched over
				if (originalMethod.Body.HasExceptionHandlers)
				{
					foreach (var handler in originalMethod.Body.ExceptionHandlers)
					{
						if (handler.FilterStart == current) handler.FilterStart = newTarget;
						if (handler.HandlerEnd == current) handler.HandlerEnd = newTarget;
						if (handler.HandlerStart == current) handler.HandlerStart = newTarget;
						if (handler.TryEnd == current) handler.TryEnd = newTarget;
						if (handler.TryStart == current) handler.TryStart = newTarget;
					}
				}

				//Update the new target to take the old targets place
				newTarget.Offset = current.Offset;
				newTarget.SequencePoint = current.SequencePoint;
				newTarget.Offset++; //TODO: spend some time to figure out why this is incrementing
			}
		}
		#endregion

		#region Field
		/// <summary>
		/// Replaces all occurrences of a field with a property call by simply swapping
		/// the field instructions that load/set.
		/// </summary>
		/// <param name="field"></param>
		/// <param name="property"></param>
		public static void ReplaceWith(this FieldDefinition field, PropertyDefinition property)
		{
			//Enumerate over every instruction in the fields assembly
			field.Module.ForEachInstruction((method, instruction) =>
			{
				//Check if the instruction is a field reference
				//We only want to handle the field we want to replace
				var reference = instruction.Operand as FieldReference;
				if (reference != null && reference.FullName == field.FullName)
				{
					//If the instruction is being loaded, we need to replace it
					//with the property's getter method
					if (instruction.OpCode == OpCodes.Ldfld)
					{
						//A getter is required on the property
						if (property.GetMethod == null)
							throw new MissingMethodException("Property is missing getter");

						//If the property has already been added into the assembly
						//don't swap anything in it's getter
						if (method.FullName == property.GetMethod.FullName)
							return;

						//Swap the instruction to call the propertys getter
						instruction.OpCode = OpCodes.Callvirt;
						instruction.Operand = field.Module.Import(property.GetMethod);
					}
					else if (instruction.OpCode == OpCodes.Stfld)
					{
						//A setter is required on the property
						if (property.SetMethod == null)
							throw new MissingMethodException("Property is missing setter");

						//If the property has already been added into the assembly
						//don't swap anything in it's setter
						if (method.FullName == property.SetMethod.FullName)
							return;

						//Swap the instruction to call the propertys setter
						instruction.OpCode = OpCodes.Callvirt;
						instruction.Operand = field.Module.Import(property.SetMethod);
					}
				}
			});
		}
		#endregion
	}
}
