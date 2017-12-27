using Mod.Framework.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework.Emitters
{
	/// <summary>
	/// This emitter will emit a call to a field that derives from a hook delegate.
	/// </summary>
	public class HookEmitter : IEmitter<MergableMethod>
	{
		private FieldDefinition _hook_field;
		private IEnumerable<ParameterDefinition> _parameters;
		private bool _is_cancellable;
		private bool _is_by_reference;
		private VariableDefinition _result_variable;

		private List<VariableDefinition> _variables = new List<VariableDefinition>();

		public HookEmitter
		(
			FieldDefinition hook_field,
			MethodDefinition method,
			bool is_cancellable,
			bool is_by_reference,

			VariableDefinition result_variable = null
		)
		{
			this._hook_field = hook_field;
			this._parameters = method.Parameters;
			this._is_cancellable = is_cancellable;
			this._is_by_reference = is_by_reference;
			this._result_variable = result_variable;
		}

		public HookEmitter
		(
			FieldDefinition hook_field,
			IEnumerable<ParameterDefinition> parameters,
			bool is_cancellable,
			bool is_by_reference,

			VariableDefinition result_variable = null
		)
		{
			this._hook_field = hook_field;
			this._parameters = parameters;
			this._is_cancellable = is_cancellable;
			this._is_by_reference = is_by_reference;
			this._result_variable = result_variable;
		}

		List<Instruction> EmitCall()
		{
			var invoke_method = _hook_field.FieldType.Resolve().Method("Invoke");

			VariableDefinition
				local_field_instance = new VariableDefinition(invoke_method.ReturnType)
			;

			InstructionReference
				call_invoke = new InstructionReference(),
				store_result = new InstructionReference()
			;

			bool has_return_type = invoke_method.ReturnType.FullName != "System.Void";

			if (this._is_cancellable)
			{
				_variables.Add(local_field_instance);
				return AnonymousExtensions.ParseAnonymousInstruction(
					new { OpCodes.Ldsfld, _hook_field },
					new { OpCodes.Dup },
					new { OpCodes.Brtrue_S, call_invoke },

					new { OpCodes.Pop },
					new { OpCodes.Ldc_I4_1 },
					new { OpCodes.Br_S, store_result },

					new Func<IEnumerable<object>>(() =>
					{
						IEnumerable<object> collection = null;

						if (this._parameters.Count() > 0)
						{
							var first = this._parameters.First();
							call_invoke = call_invoke.Create(
								_is_by_reference && first.ParameterType.IsValueType ? OpCodes.Ldarga : OpCodes.Ldarg,
								first
							);
							collection = new object[]
							{
								call_invoke,
								this._parameters.Skip(1)
									.Select(x => new {
										OpCode = _is_by_reference && x.ParameterType.IsValueType ? OpCodes.Ldarga : OpCodes.Ldarg,
										Operand = x
									}),
								
								// adds the return result
								new Func<IEnumerable<object>>(() =>
								{
									if(_result_variable !=null)
									{
										return new [] {
											new {
												OpCode = _is_by_reference ? OpCodes.Ldloca : OpCodes.Ldloc,
												_result_variable
											}
										};
									}
									return Enumerable.Empty<object >();
								}).Invoke(),

								new { OpCodes.Callvirt, invoke_method }
							};
						}
						else
						{
							collection = new object[]
							{
								// adds the return result
								new Func<IEnumerable<object>>(() =>
								{
									if(_result_variable !=null)
									{
										return new object [] {
											call_invoke = call_invoke.Create(
												_is_by_reference ? OpCodes.Ldloca : OpCodes.Ldloc,
												_result_variable
											),

											new { OpCodes.Callvirt, invoke_method }
										};
									}
									else
									{
										return new []
										{

											call_invoke = call_invoke.Create(OpCodes.Callvirt, invoke_method)
										};
									}
								}).Invoke()
							};
						}

						return collection;
					}).Invoke(),

					store_result = store_result.Create(OpCodes.Stloc, local_field_instance),
					new
					{
						OpCodes.Ldloc,
						local_field_instance
					}
				).ToList();
			}
			else
			{
				return AnonymousExtensions.ParseAnonymousInstruction(
					new { OpCodes.Ldsfld, _hook_field },
					new { OpCodes.Dup },
					new { OpCodes.Brtrue_S, call_invoke },

					new { OpCodes.Pop },
					new { OpCodes.Br_S, store_result },

					new Func<IEnumerable<object>>(() =>
					{
						IEnumerable<object> collection = null;

						if (this._parameters.Count() > 0)
						{
							var first = this._parameters.First();
							call_invoke = call_invoke.Create(
								_is_by_reference && first.ParameterType.IsValueType ? OpCodes.Ldarga : OpCodes.Ldarg,
								first

							);
							collection = new object[]
							{
								call_invoke,
								this._parameters.Skip(1)
									.Select(x => new {
										OpCode = _is_by_reference && x.ParameterType.IsValueType? OpCodes.Ldarga : OpCodes.Ldarg,
										Operand = x
									}),

								// adds the return result
								new Func<IEnumerable<object>>(() =>
								{
									if(_result_variable !=null)
									{
										return new [] {
											new {
												OpCode = _is_by_reference ? OpCodes.Ldloca : OpCodes.Ldloc,
												_result_variable
											}
										};
									}
									return Enumerable.Empty<object>();
								}).Invoke(),

								//new { OpCodes.Callvirt, invoke_method }


								new Func<IEnumerable<object>>(() =>
								{
									IEnumerable<object> res = new [] {
										new { OpCodes.Callvirt, invoke_method }
									};

									if(has_return_type)
									{
										res = res.Concat(new[]
										{
											new { OpCodes.Pop }
										});
									}

									return res;
								}).Invoke()
							};
						}
						else
						{
							//collection = new object[]
							//{
							//	new Func<IEnumerable<object>>(() =>
							//	{
							//		IEnumerable<object> res;
							if (_result_variable != null)
							{
								collection = new object[] {
									call_invoke = call_invoke.Create(
										_is_by_reference ? OpCodes.Ldloca : OpCodes.Ldloc,
										_result_variable
									),

									new { OpCodes.Callvirt, invoke_method }
								};
							}
							else
							{
								collection = new[]
								{
									call_invoke = call_invoke.Create(OpCodes.Callvirt, invoke_method)
								};
							}

							if (has_return_type)
							{
								collection = collection.Concat(new[]
								{
									new { OpCodes.Pop }
								});
							}

							//		return res;
							//	}).Invoke()
							//};
						}

						return collection;
					}).Invoke(),

					store_result = store_result.Create(
						OpCodes.Nop // invoke_method.ReturnType.FullName == "System.Void" ? OpCodes.Nop : OpCodes.Pop
					)
				).ToList();
			}
		}

		public MergableMethod Emit()
		{
			var instructions = EmitCall();

			return new MergableMethod()
			{
				Instructions = instructions,
				Variables = this._variables
			};
		}
	}
}
