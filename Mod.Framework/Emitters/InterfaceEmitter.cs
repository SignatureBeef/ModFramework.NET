using Mod.Framework.Extensions;
using Mono.Cecil;
using System.Linq;

namespace Mod.Framework.Emitters
{
	/// <summary>
	/// Generates an interface based on the given type
	/// </summary>
	public class InterfaceEmitter : IEmitter<TypeDefinition>
	{
		const MethodAttributes DefaultPropertyAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Abstract | MethodAttributes.SpecialName;
		const MethodAttributes DefaultMethodAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Abstract;

		private TypeDefinition _from;

		public InterfaceEmitter(TypeDefinition from)
		{
			this._from = from;
		}

		public TypeDefinition Emit()
		{
			TypeDefinition interface_type = new TypeDefinition(_from.Namespace, "I" + _from.Name, TypeAttributes.Abstract | TypeAttributes.ClassSemanticMask | TypeAttributes.Public);

			_from.Module.Types.Add(interface_type);

			foreach (var property in _from.Properties.Where(x =>
				(x.GetMethod != null && x.GetMethod.IsPublic && !x.GetMethod.IsStatic)
				|| (x.SetMethod != null && x.SetMethod.IsPublic && !x.SetMethod.IsStatic)
			))
			{
				var emitter = new PropertyEmitter(property.Name, property.PropertyType,
					getterAttributes: property.GetMethod != null ? DefaultPropertyAttributes : (MethodAttributes?)null,
					setterAttributes: property.SetMethod != null ? DefaultPropertyAttributes : (MethodAttributes?)null,
					declaringType: interface_type
				)
				{
					AutoImplementedGetter = false,
					AutoImplementedSetter = false,
					CompilerGenerated = false
				};
				emitter.Emit();
			}

			foreach (var method in _from.Methods.Where(x => x.IsPublic
				&& !x.IsConstructor
				&& !x.IsGetter
				&& !x.IsSetter
				&& !x.IsStatic
			))
			{
				var clone = method.Clone();
				clone.Attributes = DefaultMethodAttributes;
				interface_type.Methods.Add(clone);
			}

			return interface_type;
		}
	}
}
