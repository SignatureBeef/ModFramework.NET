using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	/// <summary>
	/// Provides the ability to merge a methods IL into another method
	/// </summary>
	public class MergableMethod
	{
		public IEnumerable<Instruction> Instructions { get; set; }
		public IEnumerable<VariableDefinition> Variables { get; set; }

		public void MergeInto(MethodDefinition method, int instruction_num)
		{
			var before_instruction = method.Body.Instructions[instruction_num];
			MergeInto(method, before_instruction);
		}
		public void MergeInto(MethodDefinition method, Func<Instruction, bool> insert_before)
		{
			var before_instruction = method.Body.Instructions.Single(insert_before);
			MergeInto(method, before_instruction);
		}

		public void MergeInto(MethodDefinition method, Instruction before_instruction)
		{
			if (this.Variables != null)
			{
				foreach (var variable in this.Variables)
				{
					method.Body.Variables.Add(variable);
				}
			}

			var processor = method.Body.GetILProcessor();

			foreach (var instruction in this.Instructions)
				processor.InsertBefore(before_instruction, instruction);
		}
	}
}
