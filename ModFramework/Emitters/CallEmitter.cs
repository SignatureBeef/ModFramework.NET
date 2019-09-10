using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;

namespace Mod.Framework.Emitters
{
	/// <summary>
	/// This emitter will output IL for calling a method.
	/// </summary>
	public class CallEmitter : IEmitter<MergableMethod>
	{
		private MethodDefinition _method;
		private Instruction _insert_before;
		public bool _reference_args;

		public CallEmitter(MethodDefinition method, Instruction insert_before)
		{
			this._method = method;
			this._insert_before = insert_before;
		}

		public MergableMethod Emit()
		{
			var instructions = new List<Instruction>();

			// if the callback is an instance method then add in the instance (this)
			if (!_method.IsStatic)
			{
				instructions.AddRange(AnonymousExtensions.ParseAnonymousInstruction(
					new { OpCodes.Ldarg_0 }
				));
			}

			foreach (var parameter in _method.Parameters)
			{
				var opcode = parameter.ParameterType.IsByReference ? OpCodes.Ldarga : OpCodes.Ldarg;

				instructions.AddRange(AnonymousExtensions.ParseAnonymousInstruction(
					new { opcode, parameter }
				));
			}

			instructions.AddRange(AnonymousExtensions.ParseAnonymousInstruction(
				new { OpCodes.Call, _method }
			));

			if (_method.ReturnType.FullName != "System.Void")
			{
				instructions.AddRange(AnonymousExtensions.ParseAnonymousInstruction(
					new { OpCodes.Pop }
				));
			}

			return new MergableMethod()
			{
				Instructions = instructions
			};
		}
	}
}
