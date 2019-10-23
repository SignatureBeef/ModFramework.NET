using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework
{
	public static class CloningExtensions
	{
		/// <summary>
		/// Clones the signatures of a method into a new empty method.
		/// This is used to replace native methods.
		/// </summary>
		/// <param name="method">The method to clone</param>
		/// <returns>The new cloned method</returns>
		public static MethodDefinition Clone(this MethodDefinition method)
		{
			var clone = new MethodDefinition(method.Name, method.Attributes, method.ReturnType)
			{
				ImplAttributes = method.ImplAttributes
			};

			foreach (var param in method.Parameters)
			{
				var parameterType = method.Module.ImportReference(param.ParameterType);
				var parameter = new ParameterDefinition(param.Name, param.Attributes, parameterType);
				//{
				//	HasConstant = param.HasConstant,
				//	HasDefault = param.HasDefault,
				//	Constant = param.Constant
				//};

				if (param.HasConstant)
				{
					parameter.HasConstant = true;
					parameter.Constant = param.Constant;
				}
				if (param.HasDefault)
				{
					parameter.HasDefault = true;
				}

				clone.Parameters.Add(parameter);
			}

			foreach (var method_ref in method.Overrides)
			{
				clone.Overrides.Add(method_ref);
			}

			foreach (var param in method.GenericParameters)
			{
				clone.GenericParameters.Add(param);
			}

			foreach (var attribute in method.CustomAttributes)
			{
				clone.CustomAttributes.Add(attribute);
			}

			foreach (var security_declaration in method.SecurityDeclarations)
			{
				clone.SecurityDeclarations.Add(security_declaration);
			}

			//clone.PInvokeInfo = method.PInvokeInfo;

			return clone;
		}

		/// <summary>
		/// Clones the signatures of a method into a new empty method.
		/// This is used to replace native methods.
		/// </summary>
		/// <param name="methods">The methods to clone</param>
		/// <returns>The new cloned methods</returns>
		public static IEnumerable<MethodDefinition> Clone(this IEnumerable<MethodDefinition> methods)
		{
			foreach (var method in methods)
			{
				yield return method.Clone();
			}
		}

		/// <summary>
		/// A very basic type cloner, which brings all fields and methods across to a new module
		/// </summary>
		/// <param name="sourceType"></param>
		/// <param name="destinationModule"></param>
		/// <returns></returns>
		public static TypeDefinition CloneTo (this TypeDefinition sourceType, ModuleDefinition destinationModule, bool add = true)
		{
			// basically the idea is to clone each method and update all fields, instructions etc to 
			// the destination module
			// at the same time each reference needs to be updated to the destination reference.

			TypeDefinition new_type = new TypeDefinition(sourceType.Namespace, sourceType.Name,
				 sourceType.Attributes)
			{
				
			};

			foreach(var ca in sourceType.CustomAttributes)
			{
				var attr = new CustomAttribute(destinationModule.ImportReference(ca.Constructor), ca.GetBlob())
				{
					//AttributeType = ca.AttributeType,
					//ConstructorArguments = ca.ConstructorArguments,
					//Fields = ca.Fields,
					//Properties = ca.Properties
				};
				new_type.CustomAttributes.Add(attr);
			}

			new_type.BaseType = destinationModule.ImportReference(sourceType.BaseType);

			foreach (var nested in sourceType.NestedTypes)
			{
				var new_nested_type = nested.CloneTo(destinationModule, false);
				new_type.NestedTypes.Add(new_nested_type);
			}

			if (add)
			{
				destinationModule.Types.Add(new_type);
			}

			foreach (var field in sourceType.Fields)
			{
				var imported = destinationModule.ImportReference(field.FieldType);
				var f = new FieldDefinition(field.Name, field.Attributes, imported);
				new_type.Fields.Add(f);
			}

			var variableMappings = new Dictionary<VariableReference, VariableDefinition>();
			var parameterMappings = new Dictionary<ParameterReference, ParameterDefinition>();
			var instructionMappings = new Dictionary<Instruction, Instruction>();
			var methodMappings = new Dictionary<MethodDefinition, MethodDefinition>();

			foreach (var method in sourceType.Methods)
			{
				variableMappings.Clear();
				parameterMappings.Clear();
				instructionMappings.Clear();

				var cloned = method.Clone();

				methodMappings.Add(method, cloned);

				new_type.Methods.Add(cloned);

				foreach(var ovr in method.Overrides)
				{
					var returnType = destinationModule.ImportReference(ovr.ReturnType);
					var declaringType = destinationModule.ImportReference(ovr.DeclaringType);
					cloned.Overrides.Add(new MethodReference(ovr.Name, returnType, declaringType));
				}
			}

			foreach(var mapped in methodMappings)
			{
				var method = mapped.Key;
				var cloned = mapped.Value;

				foreach (var prm in cloned.Parameters)
				{
					prm.ParameterType = destinationModule.ImportReference(prm.ParameterType);
				}

				cloned.ReturnType = destinationModule.ImportReference(cloned.ReturnType);

				if (method.HasBody)
				{
					#region Clone the instructions
					foreach (var inst in method.Body.Instructions)
					{
						#region Fixed variable references
						if (inst.OpCode == OpCodes.Stloc_0 && cloned.Body.Variables.Count == 0)
						{
							var other = method.Body.Variables[0];
							var variableType = other.VariableType;
							if (new_type.NestedTypes.Any(x => x.FullName == variableType.FullName))
							{
								variableType = new_type.NestedTypes
									.Single(x => x.FullName == variableType.FullName);
							}
							var imported = destinationModule.ImportReference(variableType);
							var vdef = new VariableDefinition(imported);
							cloned.Body.Variables.Add(vdef);
							variableMappings.Add(other, vdef);
						}
						if (inst.OpCode == OpCodes.Stloc_1 && cloned.Body.Variables.Count == 1)
						{
							var other = method.Body.Variables[1];
							var variableType = other.VariableType;
							if (new_type.NestedTypes.Any(x => x.FullName == variableType.FullName))
							{
								variableType = new_type.NestedTypes
									.Single(x => x.FullName == variableType.FullName);
							}
							var imported = destinationModule.ImportReference(variableType);
							var vdef = new VariableDefinition(imported);
							cloned.Body.Variables.Add(vdef);
							variableMappings.Add(other, vdef);
						}
						if (inst.OpCode == OpCodes.Stloc_2 && cloned.Body.Variables.Count == 2)
						{
							var other = method.Body.Variables[2];
							var variableType = other.VariableType;
							if (new_type.NestedTypes.Any(x => x.FullName == variableType.FullName))
							{
								variableType = new_type.NestedTypes
									.Single(x => x.FullName == variableType.FullName);
							}
							var imported = destinationModule.ImportReference(variableType);
							var vdef = new VariableDefinition(imported);
							cloned.Body.Variables.Add(vdef);
							variableMappings.Add(other, vdef);
						}
						if (inst.OpCode == OpCodes.Stloc_3 && cloned.Body.Variables.Count == 3)
						{
							var other = method.Body.Variables[3];
							var variableType = other.VariableType;
							if (new_type.NestedTypes.Any(x => x.FullName == variableType.FullName))
							{
								variableType = new_type.NestedTypes
									.Single(x => x.FullName == variableType.FullName);
							}
							var imported = destinationModule.ImportReference(variableType);
							var vdef = new VariableDefinition(imported);
							cloned.Body.Variables.Add(vdef);
							variableMappings.Add(other, vdef);
						}
						#endregion

						if (inst.Operand != null)
						{
							var vrb = inst.Operand as VariableReference;
							if (vrb != null)
							{
								VariableDefinition def = null;
								if (!variableMappings.ContainsKey(vrb))
								{
									var variableType = vrb.VariableType;
									if (new_type.NestedTypes.Any(x => x.FullName == variableType.FullName))
									{
										variableType = new_type.NestedTypes
											.Single(x => x.FullName == variableType.FullName);
									}

									var imported = destinationModule.ImportReference(variableType);
									var vdef = new VariableDefinition(imported);
									cloned.Body.Variables.Add(vdef);
									variableMappings.Add(vrb, vdef);
									def = vdef;
								}
								else
								{
									def = variableMappings[vrb];
								}
								var i = Instruction.Create(inst.OpCode, def);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var insRef = inst.Operand as Instruction;
							if (insRef != null)
							{
								var i = Instruction.Create(inst.OpCode, insRef);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var fieldRef = inst.Operand as FieldReference;
							if (fieldRef != null)
							{
								if (fieldRef.DeclaringType.FullName == new_type.FullName)
								{
									fieldRef = new_type.Field(fieldRef.Name);
								}
								else if(new_type.NestedTypes.Any(x => x.FullName == fieldRef.DeclaringType.FullName))
								{
									fieldRef = new_type.NestedTypes
										.Single(x => x.FullName == fieldRef.DeclaringType.FullName)
										.Field(fieldRef.Name);
								}

								var imported = destinationModule.ImportReference(fieldRef);
								var i = Instruction.Create(inst.OpCode, imported);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);

								continue;
							}

							var byteRef = inst.Operand as byte?; // untested
							if (byteRef != null)
							{
								var i = Instruction.Create(inst.OpCode, byteRef.Value);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var doubleRef = inst.Operand as double?; // untested
							if (doubleRef != null)
							{
								var i = Instruction.Create(inst.OpCode, doubleRef.Value);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var callsiteRef = inst.Operand as CallSite; // untested
							if (callsiteRef != null)
							{
								var i = Instruction.Create(inst.OpCode, callsiteRef);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var floatRef = inst.Operand as float?; // untested
							if (floatRef != null)
							{
								var i = Instruction.Create(inst.OpCode, floatRef.Value);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var instructions = inst.Operand as Instruction[]; // untested
							if (instructions != null)
							{
								var i = Instruction.Create(inst.OpCode, instructions);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var intRef = inst.Operand as int?; // untested
							if (intRef != null)
							{
								cloned.Body.Instructions.Add(Instruction.Create(inst.OpCode, intRef.Value));
								var i = Instruction.Create(inst.OpCode, insRef);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var longRef = inst.Operand as long?; // untested
							if (longRef != null)
							{
								var i = Instruction.Create(inst.OpCode, longRef.Value);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var methodRef = inst.Operand as MethodReference;
							if (methodRef != null)
							{
								if (methodRef.DeclaringType.FullName == new_type.FullName)
								{
									methodRef = new_type.Methods.Single(x => x.Name == methodRef.Name && x.Parameters.Count == methodRef.Parameters.Count);
								}
								else if (new_type.NestedTypes.Any(x => x.FullName == methodRef.DeclaringType.FullName))
								{
									methodRef = new_type.NestedTypes
										.Single(x => x.FullName == methodRef.DeclaringType.FullName)
										.Methods.Single(x => x.Name == methodRef.Name && x.Parameters.Count == methodRef.Parameters.Count);
								}

								var imported = destinationModule.ImportReference(methodRef);
								var i = Instruction.Create(inst.OpCode, imported);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var sbyteRef = inst.Operand as sbyte?; // untested
							if (sbyteRef != null)
							{
								var i = Instruction.Create(inst.OpCode, sbyteRef.Value);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var stringRef = inst.Operand as string;
							if (stringRef != null)
							{
								var i = Instruction.Create(inst.OpCode, stringRef);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var paramRef = inst.Operand as ParameterReference;
							if (paramRef != null)
							{
								ParameterDefinition prmDef = null;
								if (!parameterMappings.ContainsKey(paramRef))
								{
									//var d = paramRef.Resolve();
									//var imported = destinationModule.ImportReference(paramRef.ParameterType);
									//prmDef = new ParameterDefinition(paramRef.Name, d.Attributes, imported);
									prmDef = cloned.Parameters.Single(x => x.Name == paramRef.Name);
									parameterMappings.Add(paramRef, prmDef);
								}
								else
								{
									prmDef = parameterMappings[paramRef];
								}

								var i = Instruction.Create(inst.OpCode, prmDef);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}

							var typeRef = inst.Operand as TypeReference;
							if (typeRef != null)
							{
								var imported = destinationModule.ImportReference(typeRef);
								var i = Instruction.Create(inst.OpCode, imported);
								instructionMappings.Add(inst, i);
								cloned.Body.Instructions.Add(i);
								continue;
							}
						}
						else
						{
							var i = Instruction.Create(inst.OpCode);
							instructionMappings.Add(inst, i);
							cloned.Body.Instructions.Add(i);
						}
					}
					#endregion

					#region Update instruction references from the old to new module
					foreach (var inst in cloned.Body.Instructions)
					{
						var single = inst.Operand as Instruction;
						if (single != null)
						{
							inst.Operand = instructionMappings.Single(x => x.Key == single).Value;
						}
						else
						{
							var arr = inst.Operand as Instruction[];
							if (arr != null)
							{
								var list = new List<Instruction>();
								foreach (var ins in arr)
								{
									list.Add(instructionMappings.Single(x => x.Key == ins).Value);
								}
								inst.Operand = list.ToArray();
							}
						}


						destinationModule.EnsureImported(inst);
					}
					#endregion

					#region Clone exception handlers
					foreach (var handler in method.Body.ExceptionHandlers)
					{
						var new_handler = new ExceptionHandler(handler.HandlerType);

						if (handler.FilterStart != null)
						{ new_handler.FilterStart = instructionMappings.Single(x => x.Key == handler.FilterStart).Value; }

						if (handler.HandlerEnd != null)
						{ new_handler.HandlerEnd = instructionMappings.Single(x => x.Key == handler.HandlerEnd).Value; }
						if (handler.HandlerStart != null)
						{ new_handler.HandlerStart = instructionMappings.Single(x => x.Key == handler.HandlerStart).Value; }

						if (handler.TryEnd != null)
						{ new_handler.TryEnd = instructionMappings.Single(x => x.Key == handler.TryEnd).Value; }
						if (handler.TryStart != null)
						{ new_handler.TryStart = instructionMappings.Single(x => x.Key == handler.TryStart).Value; }

						new_handler.CatchType = destinationModule.ImportReference(handler.CatchType); ;

						cloned.Body.ExceptionHandlers.Add(new_handler);
					}
					#endregion
				}
			}

			foreach (var intf in sourceType.Interfaces)
			{
				var inf_type = destinationModule.ImportReference(intf.InterfaceType);
				new_type.Interfaces.Add(new InterfaceImplementation(inf_type));
			}

			return new_type;
		}
	}
}
