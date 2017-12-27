using Mod.Framework.Collections;
using Mod.Framework.Emitters;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework.Extensions
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

	public static class ReplacementExtension
	{

		public static PropertyDefinition ChangeToProperty(this FieldDefinition field)
		{
			var emitter = new PropertyEmitter(field.Name, field.FieldType, field.DeclaringType,
				getterAttributes: MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName,
				setterAttributes: MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName
			);

			field.Name = field.GetBackingName();
			field.IsPublic = false;
			field.IsPrivate = true;

			field.CustomAttributes.Add(new CustomAttribute(
				field.DeclaringType.Module.ImportReference(
					typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)
						.GetConstructors()
						.Single()
				)
			));

			return emitter.Emit();
		}

		public static PropertyDefinition AsVirtual(this PropertyDefinition property)
		{
			if (property.GetMethod != null)
				property.GetMethod.IsVirtual = property.GetMethod.IsNewSlot = true;
			if (property.SetMethod != null)
				property.SetMethod.IsVirtual = property.SetMethod.IsNewSlot = true;

			return property;
		}

		public static void ReplaceWith(this TypeReference currentType, TypeDefinition replacement, TypeDefinition constructorReplacement = null)
		{
			#region Methods
			var arrayConstructors = new List<(Instruction instruction, MethodDefinition method)>();

			var moduleInstructions = currentType.Module.SelectAllInstructions();
			var moduleMethods = currentType.Module.SelectAllMethods();
			var moduleTypes = currentType.Module.SelectAllTypes().AsDefinitions();

			//currentType.Module.ForEachInstruction((method, instruction) =>
			foreach (var ins_set in moduleInstructions)
			{
				var operandMethodRef = ins_set.instruction.Operand as MethodReference;
				var operandMethodDef = ins_set.instruction.Operand as MethodDefinition;
				var operandMethodGenericType = operandMethodRef?.DeclaringType as GenericInstanceType;
				var operandMethodArrayType = operandMethodRef?.DeclaringType as ArrayType;

				var selfDeclared = ins_set.method.DeclaringType.FullName == currentType.FullName;
				var ctorGetter = selfDeclared && ins_set.method.IsConstructor && ins_set.instruction.Previous != null && ins_set.instruction.Previous.OpCode == OpCodes.Ldarg_1;
				var selfMethod = selfDeclared && !ins_set.method.IsConstructor && !ins_set.method.IsStatic && ins_set.instruction.Previous != null && ins_set.instruction.Previous.OpCode == OpCodes.Ldarg_1;

				if (operandMethodRef != null && operandMethodRef.Name != ".ctor")
				{
					var arrayType = operandMethodRef.DeclaringType as ArrayType;
					if (arrayType != null && arrayType.FullName == currentType.FullName)
					{
						var methods = replacement.SelectAllMethods().Where(mth =>
							mth.Name == operandMethodRef.Name
							&& mth.Parameters.Count == operandMethodRef.Parameters.Count
						);

						if (methods.Count() == 0)
							methods = replacement.SelectAllMethods().Where(mth =>
								mth.Name == $"{operandMethodRef.Name.ToLower()}_Item"
								&& mth.Parameters.Count == operandMethodRef.Parameters.Count
							);

						if (methods.Count() == 0)
							throw new Exception($"Method `{operandMethodRef.Name}` is not found on {replacement.FullName}");
						else if (methods.Count() > 1)
							throw new Exception($"Too many methods named `{operandMethodRef.Name}` found in {replacement.FullName}");

						ins_set.instruction.Operand = currentType.Module.ImportReference(methods.Single());
					}
				}

				if (operandMethodDef != null && (selfMethod || ctorGetter || ins_set.method.IsStatic || ins_set.method.DeclaringType.FullName != currentType.FullName))
				{
					if (!operandMethodDef.IsConstructor)
					{
						if (operandMethodDef.DeclaringType.FullName == currentType.FullName && !operandMethodDef.IsStatic)
						{
							var methods = replacement.Methods.Where(mth =>
								mth.Name == operandMethodRef.Name
								&& mth.Parameters.Count == operandMethodDef.Parameters.Count
							);

							if (methods.Count() == 0)
								throw new Exception($"Method `{operandMethodRef.Name}` is not found on {replacement.FullName}");
							else if (methods.Count() > 1)
								throw new Exception($"Too many methods named `{operandMethodRef.Name}` found in {replacement.FullName}");

							ins_set.instruction.Operand = currentType.Module.ImportReference(methods.Single());
						}
					}
				}

				// array constructor replacements
				if (ins_set.instruction.OpCode == OpCodes.Newobj && operandMethodRef?.Name == ".ctor")
				{
					// currently only field arrays are supported
					if (ins_set.instruction.Next?.OpCode == OpCodes.Stsfld)
					{
						var arrayType = operandMethodRef.DeclaringType as ArrayType;
						if (arrayType != null && arrayType.FullName == currentType.FullName)
						{
							arrayConstructors.Add((
								ins_set.instruction,
								ins_set.method
							));
						}
					}
				}

				if (operandMethodGenericType != null)
				{
					for (var i = 0; i < operandMethodGenericType.GenericArguments.Count; i++)
					{
						if (operandMethodGenericType.GenericArguments[i].FullName == currentType.FullName)
						{
							operandMethodGenericType.GenericArguments[i] = replacement;
						}
					}
				}
			}
			#endregion

			foreach (var item in arrayConstructors)
			{
				var il = item.method.Body.GetILProcessor();
				var se = new StackCounter(item.method.Body.Instructions);
				se.Eval((instruction, stackCount) =>
				{
					if ((instruction?.Operand as MethodReference)?.DeclaringType.Name == currentType.Name)
					{
						var init = replacement.Methods.SingleOrDefault(x => x.Name == "Initialise") ?? throw new Exception($"Method `Initialise` is not found on {replacement.FullName}");

						if (constructorReplacement == null)
							throw new Exception($"{nameof(constructorReplacement)} was not provided while replacing an Array Type");

						// init before the parameters 
						var firstParameter = instruction;
						var offset = stackCount;
						while (offset-- > 0)
						{
							firstParameter = firstParameter.Previous;
						}

						var field = (FieldDefinition)instruction.Next.Operand;

						var hook = new ParameterDefinition[] {

						}.GenerateHook("CreateCollection", field.DeclaringType, constructorReplacement, HookOptions.Post, field.Module);

						hook.MergeInto(item.method, firstParameter);

						var callvirt = hook.Instructions.Single(x => x.OpCode == OpCodes.Callvirt);
						var pop = callvirt.Next(x => x.OpCode == OpCodes.Pop);

						pop.OpCode = OpCodes.Stsfld;
						pop.Operand = field;

						var br = callvirt.Previous(x => x.OpCode == OpCodes.Br_S);
						il.InsertBefore(br, il.Create(OpCodes.Newobj, constructorReplacement.Method(".ctor")));
						il.InsertBefore(br, il.Create(OpCodes.Stsfld, field));

						// new obj, DefaultCollection
						// stsfld
						//il.InsertBefore(firstParameter, il.Create(OpCodes.Newobj, constructorReplacement.Method(".ctor")));
						//il.InsertBefore(firstParameter, il.Create(instruction.Next.OpCode, field));

						il.InsertBefore(firstParameter, il.Create(OpCodes.Ldsfld, field));

						// remove newobj
						// change stsfld with Init
						instruction.Next.OpCode = OpCodes.Callvirt;
						instruction.Next.Operand = init;

						il.Remove(instruction);
					}
				});

				//instruction.OpCode = OpCodes.Call;
				//instruction.Operand = currentType.Module.ImportReference(item.GetMethod);
			}

			#region Local variables
			foreach (var method in moduleMethods)
			{
				if (method.HasBody && method.Body.HasVariables)
				{
					foreach (var local in method.Body.Variables)
					{
						if (local.VariableType.FullName == currentType.FullName)
						{
							local.VariableType = replacement;
						}
					}
				}
			}
			#endregion

			#region Method returns
			foreach (var method in moduleMethods)
			{
				if (method.ReturnType.FullName == currentType.FullName)
				{
					method.ReturnType = replacement;
				}
			}
			#endregion

			#region Method parameters
			foreach (var method in moduleMethods)
			{
				if (method.HasParameters)
				{
					foreach (var parameter in method.Parameters)
					{
						if (parameter.ParameterType.FullName == currentType.FullName)
						{
							parameter.ParameterType = replacement;
						}
					}
				}
			}
			#endregion

			#region Fields
			foreach (var type in moduleTypes)
			{
				if (type.HasFields)
				{
					foreach (var field in type.Fields)
					{
						if (field.FieldType.FullName == currentType.FullName)
						{
							field.FieldType = replacement;
						}
						else
						{
							var array_type = field.FieldType as ArrayType;
							if (array_type != null && array_type.ElementType.FullName == currentType.FullName)
							{
								field.FieldType = new ArrayType(replacement);
							}
						}
					}
				}
			}
			#endregion

			#region Properties
			foreach (var type in moduleTypes)
			{
				if (type.HasProperties)
				{
					foreach (var proeprty in type.Properties)
					{
						if (proeprty.PropertyType.FullName == currentType.FullName)
						{
							proeprty.PropertyType = replacement;
						}
						else
						{
							var array_type = proeprty.PropertyType as ArrayType;
							if (array_type != null && array_type.ElementType.FullName == currentType.FullName)
							{
								proeprty.PropertyType = new ArrayType(replacement);
							}
						}
					}
				}
			}
			#endregion

			#region Type Generics
			foreach (var type in moduleTypes)
			{
				//if (type.HasGenericParameters)
				{
					var gti = type.BaseType as GenericInstanceType;
					if (gti != null)
					{
						for (var i = 0; i < gti.GenericArguments.Count; i++)
						{
							var arg = gti.GenericArguments[i];
							if (arg.FullName == currentType.FullName)
							{
								gti.GenericArguments[i] = replacement;
							}
						}
					}
				}
			}
			#endregion
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
				//newTarget.SequencePoint = current.SequencePoint;
				newTarget.Offset++; //TODO: spend some time to figure out why this is incrementing
			}
		}

		/// <summary>
		/// Generates a 2D collection based on <see cref="DefaultCollection{TItem}"/>
		/// </summary>
		/// <param name="interfaceType"></param>
		/// <returns></returns>
		private static TypeDefinition GenerateImplementedCollection(TypeDefinition interfaceType)
		{
			var elementType = interfaceType.Property("Item").GetMethod.ReturnType;

			var genericVersion = elementType.Module.ImportReference(typeof(DefaultCollection<>));

			//TODO: clone the default collection to the target assembly
			//		method body cloning needs to be implemented correctly to do this, or perhaps being able to use ILRepack as a library (it's clone functionality is hidden)
			//var importableGenericInstance = genericVersion.CloneTo(interfaceType.Module); 
			//importableGenericInstance.Namespace = interfaceType.Namespace;

			var g = new GenericInstanceType(genericVersion);
			g.GenericArguments.Clear();
			g.GenericArguments.Add(elementType);

			TypeDefinition gi = new TypeDefinition(interfaceType.Namespace, "Default" + interfaceType.Name.TrimStart('I'), TypeAttributes.Public | TypeAttributes.BeforeFieldInit, interfaceType.Module.ImportReference(g));
			gi.Interfaces.Add(new InterfaceImplementation(interfaceType));

			interfaceType.Module.Types.Add(gi);

			var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, interfaceType.Module.TypeSystem.Void);
			var il = ctor.Body.GetILProcessor();

			var ret = il.Create(OpCodes.Ret);
			ctor.Body.Instructions.Add(ret);

			var basector = (MethodReference)gi.BaseType.Resolve().Method(".ctor").MakeHostInstanceGeneric(elementType);

			il.InsertBefore(ret,
				new { OpCodes.Ldarg_0 },
				new { OpCodes.Call, Operand = gi.Module.ImportReference(basector) }
			);

			gi.Methods.Add(ctor);
			
			// when the gnericVersion instance is eventually cloned in, we then need to change the implemented members to use final etc
			// without these any call to an implemented method will result in a "method has no implementation". 
			// currently we aren't cloning, so this needs to be forced in the Mod.Framework via code.

			return gi;
		}

		public static MethodReference MakeHostInstanceGeneric(
								  this MethodReference self,
								  params TypeReference[] args)
		{
			var reference = new MethodReference(
				self.Name,
				self.ReturnType,
				self.DeclaringType.MakeGenericInstanceType(args))
			{
				HasThis = self.HasThis,
				ExplicitThis = self.ExplicitThis,
				CallingConvention = self.CallingConvention
			};

			foreach (var parameter in self.Parameters)
			{
				reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
			}

			foreach (var genericParam in self.GenericParameters)
			{
				reference.GenericParameters.Add(new GenericParameter(genericParam.Name, reference));
			}

			return reference;
		}

		/// <summary>
		/// Replaces the field type with an interface
		/// </summary>
		/// <param name="field"></param>
		/// <param name="interfaceName"></param>
		public static void ReplaceWithInterface(this FieldDefinition field, string interfaceName)
		{
			var arrayType = field.FieldType as ArrayType;
			if (arrayType != null)
			{
				if (arrayType.Dimensions.Count != 2) throw new NotSupportedException();

				var existing_interface = field.FieldType.Module.GetType($"{field.FieldType.Namespace}.{interfaceName}");
				if (existing_interface == null)
				{
					var emitter = new InterfaceEmitter(field.FieldType, interfaceName);
					existing_interface = emitter.Emit();

					// emit default impl collection for constructors
					var defaultCollection = GenerateImplementedCollection(existing_interface);

					//var r = field.Module.ImportReference(typeof(DefaultCollection)).Resolve();

					field.FieldType.ReplaceWith(existing_interface, constructorReplacement: defaultCollection);
				}

				field.FieldType = existing_interface;
			}
			else throw new NotSupportedException();
		}

		/// <summary>
		/// Replaces the property type with an interface
		/// </summary>
		/// <param name="property"></param>
		/// <param name="interfaceName"></param>
		public static void ReplaceWithInterface(this PropertyDefinition property, string interfaceName)
		{
			var arrayType = property.PropertyType as ArrayType;
			if (arrayType != null)
			{
				if (arrayType.Dimensions.Count != 2) throw new NotSupportedException();

				var existing_interface = property.PropertyType.Module.GetType($"{property.PropertyType.Namespace}.{interfaceName}");
				if (existing_interface == null)
				{
					var emitter = new Emitters.InterfaceEmitter(property.PropertyType, interfaceName);
					existing_interface = emitter.Emit();
				}

				property.PropertyType = existing_interface;
			}
			else throw new NotSupportedException();
		}


		/// <summary>
		/// Replaces all occurrences of a field with a property call by simply swapping
		/// the field instructions that load/set.
		/// </summary>
		/// <param name="field"></param>
		/// <param name="property"></param>
		public static void ReplaceWith(this FieldDefinition field, PropertyDefinition property)
		{
			//Enumerate over every instruction in the fields assembly
			var moduleInstructions = field.Module.SelectAllInstructions();
			foreach (var ins_set in moduleInstructions)
			{
				//Check if the instruction is a field reference
				//We only want to handle the field we want to replace
				var reference = ins_set.instruction.Operand as FieldReference;
				//if (reference != null)
				//{
				//	if (ins_set.instruction.OpCode == OpCodes.Ldfld)
				//	{

				//	}
				//	else if (ins_set.instruction.OpCode == OpCodes.Stfld)
				//	{

				//	}
				//}
				if (reference != null && reference.FullName == field.FullName)
				{
					//If the instruction is being loaded, we need to replace it
					//with the property's getter method
					if (ins_set.instruction.OpCode == OpCodes.Ldfld)
					{
						//A getter is required on the property
						if (property.GetMethod == null)
							throw new MissingMethodException("Property is missing getter");

						//If the property has already been added into the assembly
						//don't swap anything in it's getter
						if (ins_set.method.FullName == property.GetMethod.FullName)
							continue;

						//Swap the instruction to call the propertys getter
						ins_set.instruction.OpCode = OpCodes.Callvirt;
						ins_set.instruction.Operand = field.Module.ImportReference(property.GetMethod);
					}
					else if (ins_set.instruction.OpCode == OpCodes.Stfld)
					{
						//A setter is required on the property
						if (property.SetMethod == null)
							throw new MissingMethodException("Property is missing setter");

						//If the property has already been added into the assembly
						//don't swap anything in it's setter
						if (ins_set.method.FullName == property.SetMethod.FullName)
							continue;

						//Swap the instruction to call the propertys setter
						ins_set.instruction.OpCode = OpCodes.Callvirt;
						ins_set.instruction.Operand = field.Module.ImportReference(property.SetMethod);
					}
				}
			}
		}

		/// <summary>
		/// Replaces all occurrences of the current method in the assembly with the provided method
		/// </summary>
		/// <param name="method"></param>
		/// <param name="replacement"></param>
		public static void ReplaceWith(this MethodDefinition method, MethodReference replacement)
		{
			//Enumerates over each type in the assembly, including nested types
			var moduleInstructions = method.Module.SelectAllInstructions();
			foreach (var ins_set in moduleInstructions)
			{
				//Compare each instruction operand value as if it were a method reference. Check to 
				//see if they match the current method definition. If it matches, it can be swapped.
				if (ins_set.instruction.Operand == method)
					ins_set.instruction.Operand = replacement;

				var generic_method = ins_set.instruction.Operand as GenericInstanceMethod;
				if (generic_method != null)
				{
					if (generic_method.ElementMethod == method)
					{
						var replacement_instance = new GenericInstanceMethod(replacement);

						foreach (var item in generic_method.GenericArguments)
							replacement_instance.GenericArguments.Add(item);

						foreach (var item in generic_method.GenericParameters)
							replacement_instance.GenericParameters.Add(item);

						ins_set.instruction.Operand = replacement_instance;
					}
				}
			}
		}
	}
}
