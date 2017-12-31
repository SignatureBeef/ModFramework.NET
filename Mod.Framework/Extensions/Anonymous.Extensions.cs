using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	public static class AnonymousExtensions
	{
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
				foreach (var ins in ParseAnonymousInstruction(anon))
				{
					processor.InsertAfter(target, ins);
					created.Add(ins);
				}
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
				foreach (var ins in ParseAnonymousInstruction(anon))
				{
					processor.InsertBefore(target, ins);
					created.Add(ins);
				}
			}

			return created;
		}
	}
}
