using Mod.Framework.Emitters;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mod.Framework.Extensions
{
	public static class ReplacementExtension
	{
		public static PropertyDefinition ChangeToProperty(this FieldDefinition field)
		{
			var emitter = new PropertyEmitter(field.Name, field.FieldType, field.DeclaringType,
				getterAttributes: MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName,
				setterAttributes: MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName
			);

			field.Name = $"<{field.Name}>k__BackingField";
			field.IsPublic = false;
			field.IsPrivate = true;

			field.CustomAttributes.Add(new CustomAttribute(
				field.DeclaringType.Module.Import(
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

		public static void ReplaceWith(this TypeDefinition currentType, TypeDefinition replacement)
		{
			#region Methods
			currentType.Module.ForEachInstruction((method, instruction) =>
			{
				var operandMethod = instruction.Operand as MethodDefinition;

				var selfDeclared = method.DeclaringType.FullName == currentType.FullName;
				var ctorGetter = selfDeclared && method.IsConstructor && instruction.Previous != null && instruction.Previous.OpCode == OpCodes.Ldarg_1;
				var selfMethod = selfDeclared && !method.IsConstructor && !method.IsStatic && instruction.Previous != null && instruction.Previous.OpCode == OpCodes.Ldarg_1;

				if (operandMethod != null && (selfMethod || ctorGetter || method.IsStatic || method.DeclaringType.FullName != currentType.FullName))
				{
					if (operandMethod.IsConstructor)
						return;

					if (operandMethod.DeclaringType.FullName == currentType.FullName && !operandMethod.IsStatic)
					{
						var methods = replacement.Methods.Where(mth =>
							mth.Name == operandMethod.Name
							&& mth.Parameters.Count == operandMethod.Parameters.Count
						);

						if (methods.Count() == 0)
							throw new Exception($"Method `{operandMethod.Name}` is not found on {replacement.FullName}");
						else if (methods.Count() > 1)
							throw new Exception($"Too many methods named `{operandMethod.Name}` found in {replacement.FullName}");

						instruction.Operand = currentType.Module.Import(methods.Single());
					}
				}
			});
			#endregion

			#region Local variables
			currentType.Module.ForEachInstruction((method, instruction) =>
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
			});
			#endregion

			#region Method returns
			currentType.Module.ForEachMethod(method =>
			{
				if (method.ReturnType.FullName == currentType.FullName)
				{
					method.ReturnType = replacement;
				}
			});
			#endregion

			#region Method parameters
			currentType.Module.ForEachMethod(method =>
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
			});
			#endregion

			#region Fields
			currentType.Module.ForEachType(type =>
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
			});
			#endregion
		}
	}
}
