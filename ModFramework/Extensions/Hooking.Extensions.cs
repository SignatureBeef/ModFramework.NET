using Mod.Framework.Emitters;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	public static class HookingExtensions
	{
		const String DefaultTypeNamespace = "ModFramework";
		const String DefaultHooksTypeName = "ModHooks";
		const String DefaultHandlersTypeName = "ModHandlers";
		const TypeAttributes DefaultPublicTypeAttributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit;
		const TypeAttributes DefaultNestedTypeAttributes = TypeAttributes.NestedPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit;

		#region Hooking
		/// <summary>
		/// Generates a hook that is called before any native code
		/// </summary>
		/// <param name="method">Method to generate the hook in</param>
		/// <param name="options">Configurable hook options</param>
		/// <returns>A <see cref="MergableMethod"/> instance</returns>
		public static MergableMethod GenerateBeginHook
		(
			this MethodDefinition method,

			HookOptions options = HookOptions.Default,
			VariableDefinition result_variable = null
		)
		{
			// emit call at offset 0

			// add or get the types where we are storing out auto generated hooks & their delegates
			var hooks_type = method.DeclaringType.GetHooksType();
			var hooks_delegates_type = method.DeclaringType.GetHooksDelegateType();

			// generate the hook handler delegate
			var hook_delegate_emitter = new HookDelegateEmitter("OnPre", method, options);
			var hook_handler = hook_delegate_emitter.Emit();
			hooks_delegates_type.NestedTypes.Add(hook_handler);

			// generate the api hook external modules can attach to
			var hook_field = new FieldDefinition("Pre" + method.GetSafeName(), FieldAttributes.Public | FieldAttributes.Static, hook_handler);
			hooks_type.Fields.Add(hook_field);

			// generate the call to the delegate
			var hook_emitter = new HookEmitter(hook_field, method,
				(options & HookOptions.Cancellable) != 0,
				(options & HookOptions.ReferenceParameters) != 0,
				result_variable
			);
			var result = hook_emitter.Emit();
			//instructions.MergeInto(method, 0);

			// end of method

			var invoke_method = hook_handler.Resolve().Method("Invoke");
			if (invoke_method.ReturnType.FullName != "System.Void")
			{
				result.Instructions = result.Instructions.Concat(new[]
				{
					Instruction.Create(OpCodes.Brtrue_S, method.Body.Instructions.First()),
					Instruction.Create(OpCodes.Br_S, method.Body.Instructions.Last())
				});
			}

			return result;
		}

		/// <summary>
		/// Generates a hook that is called after native code
		/// </summary>
		/// <param name="method">Method to generate the hook in</param>
		/// <param name="options">Configurable hook options</param>
		/// <returns>A <see cref="MergableMethod"/> instance</returns>
		public static MergableMethod GenerateEndHook
		(
			this MethodDefinition method,

			HookOptions options = HookOptions.Default,
			VariableDefinition result_variable = null
		)
		{
			// emit call at each ret instruction

			// add or get the types where we are storing out auto generated hooks & their delegates
			var hooks_type = method.DeclaringType.GetHooksType();
			var hooks_delegates_type = method.DeclaringType.GetHooksDelegateType();

			// generate the hook handler delegate
			var hook_delegate_emitter = new HookDelegateEmitter("OnPost", method, options & ~(
				HookOptions.ReferenceParameters |
				HookOptions.Cancellable
			));
			var hook_handler = hook_delegate_emitter.Emit();
			hooks_delegates_type.NestedTypes.Add(hook_handler);

			// generate the api hook external modules can attach to
			var hook_field = new FieldDefinition("Post" + method.GetSafeName(), FieldAttributes.Public | FieldAttributes.Static, hook_handler);
			hooks_type.Fields.Add(hook_field);

			// generate the call to the delegate
			var hook_emitter = new HookEmitter(hook_field, method, false, false, result_variable);
			var result = hook_emitter.Emit();

			// end of method

			return result;
		}

		/// <summary>
		/// Generates a hook that is called after native code
		/// </summary>
		/// <param name="method">Method to generate the hook in</param>
		/// <param name="options">Configurable hook options</param>
		/// <returns>A <see cref="MergableMethod"/> instance</returns>
		public static MergableMethod GenerateHook
		(
			this IEnumerable<ParameterDefinition> parameters,

			string name,
			TypeDefinition parentType,
			TypeDefinition returnType, HookOptions flags, ModuleDefinition module,

			HookOptions options = HookOptions.Post,
			VariableDefinition result_variable = null
		)
		{
			// emit call at each ret instruction

			// add or get the types where we are storing out auto generated hooks & their delegates
			var hooks_type = parentType.GetHooksType();
			var hooks_delegates_type = parentType.GetHooksDelegateType();

			// generate the hook handler delegate
			var hook_delegate_emitter = new HookDelegateEmitter("On" + name, parameters, returnType, options, module);
			var hook_handler = hook_delegate_emitter.Emit();
			hooks_delegates_type.NestedTypes.Add(hook_handler);

			// generate the api hook external modules can attach to
			var hook_field = new FieldDefinition(name, FieldAttributes.Public | FieldAttributes.Static, hook_handler);
			hooks_type.Fields.Add(hook_field);

			// generate the call to the delegate
			var hook_emitter = new HookEmitter(hook_field, parameters, false, false, result_variable);
			var result = hook_emitter.Emit();


			// end of method

			return result;
		}

		/// <summary>
		/// Adds configurable hooks into each method of the query
		/// </summary>
		/// <param name="query">The Query</param>
		/// <param name="options">Hook options</param>
		/// <returns>The existing <see cref="QueryResult"/> instance</returns>
		public static QueryResult Hook(this Query query, HookOptions options = HookOptions.Default)
			=> query.Run().Hook(options);

		/// <summary>
		/// Adds configurable hooks into each method of the query
		/// </summary>
		/// <param name="results">Methods to be hooked</param>
		/// <param name="options">Hook options</param>
		/// <returns>The existing <see cref="QueryResult"/> instance</returns>
		public static QueryResult Hook(this QueryResult results, HookOptions options = HookOptions.Default)
		{
			var context = results
				.Select(x => x.Instance as MethodDefinition)
				.Where(
					x => x != null
					&& x.HasBody
					&& !x.DeclaringType.IsInterface
					&& x.GenericParameters.Count == 0
				);

			foreach (var method in context)
			{
				var new_method = method.Clone();

				// rename method to be suffixed with Direct
				method.Name += "Direct";
				method.Name = method.GetSafeName();
				method.DeclaringType.Methods.Add(new_method);
				method.Attributes &= ~MethodAttributes.Virtual;
				method.Attributes &= ~MethodAttributes.SpecialName;
				method.Attributes &= ~MethodAttributes.RTSpecialName;
				method.Overrides.Clear();
				method.CustomAttributes.Clear();
				method.SecurityDeclarations.Clear();
				method.ReplaceWith(new_method);

				var processor = new_method.Body.GetILProcessor();
				var ins_return = Instruction.Create(OpCodes.Ret);
				processor.Append(ins_return);

				var call_emitter = new CallEmitter(method, new_method.Body.Instructions.First());

				var call = call_emitter.Emit();

				VariableDefinition return_variable = null;
				var nop = call.Instructions
					.SingleOrDefault(x => x.OpCode == OpCodes.Pop); // expect one here as the CallEmitter should only handle one. if it changes this needs to change
				if (nop != null)
				{
					return_variable = new VariableDefinition(method.ReturnType);
					new_method.Body.Variables.Add(return_variable);
					nop.Operand = return_variable;
					nop.OpCode = OpCodes.Stloc_S;
				}
				call.MergeInto(new_method, 0);

				if ((options & HookOptions.Pre) != 0)
				{
					var hook = new_method.GenerateBeginHook(options, return_variable);

					if ((options & HookOptions.Cancellable) != 0
						&& (options & HookOptions.AlterResult) == 0
						&& method.ReturnType.FullName != "System.Void")
					{
						// TODO: this functionality will be desired - idea: just generate the "default(T)"
						// what to do if you get here: add HookFlags.AlterResult to your flags or don't try cancelling
						throw new NotSupportedException("Attempt to cancel a non-void method without allowing the callback to alter the result.");
					}

					hook.MergeInto(new_method, 0);
				}

				if ((options & HookOptions.Post) != 0)
				{
					var hook = new_method.GenerateEndHook(options, return_variable);

					//var last_ret = new_method.Body.Instructions.Last(x => x.OpCode == OpCodes.Ret);
					//last_ret.ReplaceTransfer(hook.Instructions.First(), new_method);

					hook.MergeInto(new_method, call.Instructions.Last().Next);
				}

				if (return_variable != null)
				{
					var ins_return_variable = Instruction.Create(OpCodes.Ldloc, return_variable);
					processor.InsertBefore(ins_return, ins_return_variable);
					ins_return.ReplaceTransfer(ins_return_variable, new_method);
				}

				// for all classes (not structs) we need to move the base type constructor over to the new
				// method before any of our hook code is executed.
				if (new_method.IsConstructor && new_method.HasThis && !new_method.DeclaringType.IsValueType)
				{
					var instructions = method.Body.Instructions.TakeWhile(
						x => x.Previous == null
						|| x.Previous.OpCode != OpCodes.Call
						|| (x.Previous.Operand as MethodReference).FullName != method.DeclaringType.BaseType.Resolve().Method(".ctor").FullName
					);

					foreach (var instruction in instructions.Reverse())
					{
						method.Body.Instructions.Remove(instruction);
						new_method.Body.Instructions.Insert(0, instruction);
					}
				}
			}

			return results;
		}
		#endregion

		#region Helpers
		/// <summary>
		/// Adds or gets an existing nested type
		/// </summary>
		/// <param name="parentType">The parent type</param>
		/// <param name="nestedTypeName">The name of the nested type</param>
		/// <param name="attributes">Attributes for the nested type</param>
		/// <returns></returns>
		public static TypeDefinition AddOrGetNestedType
		(
			this TypeDefinition parentType,
			string nestedTypeName,
			TypeAttributes attributes
		)
		{
			var nested_type = parentType.NestedTypes.SingleOrDefault(x => x.Name == nestedTypeName);
			if (nested_type == null)
			{
				nested_type = new TypeDefinition(String.Empty, nestedTypeName, attributes);
				nested_type.BaseType = parentType.Module.TypeSystem.Object;
				parentType.NestedTypes.Add(nested_type);
			}
			return nested_type;
		}
		/// <summary>
		/// Adds or gets an existing nested type
		/// </summary>
		/// <param name="parentType">The parent type</param>
		/// <param name="typeName">The name of the type</param>
		/// <param name="attributes">Attributes for the type</param>
		/// <returns></returns>
		public static TypeDefinition AddOrGetType
		(
			this AssemblyDefinition assembly,
			string typeName,
			TypeAttributes attributes,
			string @namespace = ""
		)
		{
			var nested_type = assembly.MainModule.Types.SingleOrDefault(x => x.Name == typeName);
			if (nested_type == null)
			{
				nested_type = new TypeDefinition(@namespace, typeName, attributes);
				nested_type.BaseType = assembly.MainModule.TypeSystem.Object;
				assembly.MainModule.Types.Add(nested_type);
			}
			return nested_type;
		}

		/// <summary>
		/// Adds the standard hooks type into the given type
		/// </summary>
		/// <param name="type">Parent type</param>
		/// <returns>The requested hook type</returns>
		public static TypeDefinition GetHooksType(this TypeDefinition type)
		{
			var hooks_type = AddOrGetType(type.Module.Assembly, DefaultHooksTypeName, DefaultPublicTypeAttributes, DefaultTypeNamespace);

			return hooks_type.AddOrGetNestedType(type.Name, DefaultNestedTypeAttributes);
		}

		/// <summary>
		/// Adds the standard handlers type into the given type
		/// </summary>
		/// <param name="type">Parent type</param>
		/// <returns>The requested handler type</returns>
		public static TypeDefinition GetHooksDelegateType(this TypeDefinition type)
		{
			var hooks_type = GetHooksType(type);
			return hooks_type.AddOrGetNestedType(DefaultHandlersTypeName, DefaultNestedTypeAttributes);
		}
		#endregion
	}
}
