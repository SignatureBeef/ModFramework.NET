using Mod.Framework.Collections;
using Mod.Framework.Emitters;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	/// <summary>
	/// A very-VERY basic stack counter. Some of it's uses are finding the parameters for a method usin.
	/// </summary>
	internal class StackCounter
	{
		private IEnumerable<Instruction> instructions;

		public StackCounter(IEnumerable<Instruction> instructions)
		{
			this.instructions = instructions.ToArray();
		}

		public void Eval(Action<Instruction, Int32> match)
		{
			int stackTotal = 0;
			for (var i = 0; i < this.instructions.Count(); i++)
			{
				var instruction = this.instructions.ElementAt(i);
				var pop = instruction.OpCode.StackBehaviourPop;
				var push = instruction.OpCode.StackBehaviourPush;

				if (push == StackBehaviour.Push0 && pop == StackBehaviour.Pop0)
				{
					continue;
				}
				else if (pop == StackBehaviour.Varpop)
				{
					match(instruction, stackTotal);

					stackTotal = 0;

					if (push != StackBehaviour.Push0)
					{
						stackTotal++;
					}
				}
				else if (push != StackBehaviour.Push0 && pop != StackBehaviour.Pop0)
				{
					continue;
				}
				else if (push == StackBehaviour.Push0)
				{
					stackTotal--;
				}
				else if (pop == StackBehaviour.Pop0)
				{
					stackTotal++;
				}
				else
				{

				}
			}
		}
	}
}
