using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mod.Framework
{
	/// <summary>
	/// A basic wrapper around a <see cref="Instruction"/> instance.
	/// This is used along side <see cref="Extensions.CecilExtensions.ParseAnonymousInstruction(object[])"/> to 
	/// build a chain of instructions where the real instruction is only available at the time of parsing.
	/// </summary>
	public class InstructionReference
	{
		public Instruction Reference { get; set; }

		public InstructionReference Create(OpCode opcode)
		{
			this.Reference = Instruction.Create(opcode);
			return this;
		}

		public InstructionReference Create(OpCode opcode, string operand)
		{
			this.Reference = Instruction.Create(opcode, operand);
			return this;
		}

		public InstructionReference Create(OpCode opcode, ParameterDefinition operand)
		{
			this.Reference = Instruction.Create(opcode, operand);
			return this;
		}
		public InstructionReference Create(OpCode opcode, MethodDefinition operand)
		{
			this.Reference = Instruction.Create(opcode, operand);
			return this;
		}

		public InstructionReference Create(OpCode opcode, MethodReference operand)
		{
			this.Reference = Instruction.Create(opcode, operand);
			return this;
		}

		public InstructionReference Create(OpCode opcode, VariableDefinition operand)
		{
			this.Reference = Instruction.Create(opcode, operand);
			return this;
		}
	}
}
