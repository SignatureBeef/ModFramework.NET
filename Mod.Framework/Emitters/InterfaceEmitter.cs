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

		private TypeReference _from;
		private string _name;

		public InterfaceEmitter(TypeReference from, string name = null)
		{
			this._from = from;
			this._name = name;
		}

		public TypeDefinition Emit()
		{
			TypeDefinition interface_type = new TypeDefinition(_from.Namespace, this._name ?? ("I" + _from.Name), TypeAttributes.Abstract | TypeAttributes.ClassSemanticMask | TypeAttributes.Public);

			_from.Module.Types.Add(interface_type);

			var arrayType = _from as ArrayType;
			if (arrayType != null)
			{
				// todo: make this section an emitter
				var arrayElementType = arrayType.ElementType.Resolve();
				
				var get_Item = new MethodDefinition("get_Item",
					MethodAttributes.Public |
					MethodAttributes.HideBySig |
					MethodAttributes.SpecialName |
					MethodAttributes.NewSlot |
					MethodAttributes.Abstract |
					MethodAttributes.Virtual,
					arrayElementType
				);
				for (var i = 0; i < arrayType.Dimensions.Count; i++)
				{
					get_Item.Parameters.Add(new ParameterDefinition("i" + i, ParameterAttributes.None, _from.Module.TypeSystem.Int32));
				}

				var set_Item = new MethodDefinition("set_Item",
					MethodAttributes.Public |
					MethodAttributes.HideBySig |
					MethodAttributes.SpecialName |
					MethodAttributes.NewSlot |
					MethodAttributes.Abstract |
					MethodAttributes.Virtual,
					_from.Module.TypeSystem.Void
				);
				for (var i = 0; i < arrayType.Dimensions.Count; i++)
				{
					set_Item.Parameters.Add(new ParameterDefinition("i" + i, ParameterAttributes.None, _from.Module.TypeSystem.Int32));
				}
				set_Item.Parameters.Add(new ParameterDefinition("item", ParameterAttributes.None, arrayElementType));

				var item = new PropertyDefinition("Item", PropertyAttributes.None, arrayElementType);
				for (var i = 0; i < arrayType.Dimensions.Count; i++)
				{
					item.Parameters.Add(new ParameterDefinition("i" + i, ParameterAttributes.None, _from.Module.TypeSystem.Int32));
				}
				item.GetMethod = get_Item;
				item.SetMethod = set_Item;
				// end emitter

				// due to a interface limitation, i generate the init method which is called instead of a constructor
				var initialise = new MethodDefinition("Initialise",
					//Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Final | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.NewSlot,
					MethodAttributes.Public |
					MethodAttributes.HideBySig |
					MethodAttributes.NewSlot |
					MethodAttributes.Abstract |
					MethodAttributes.Virtual,
					_from.Module.TypeSystem.Void
				);
				for (var i = 0; i < arrayType.Dimensions.Count; i++)
				{
					initialise.Parameters.Add(new ParameterDefinition("i" + i, ParameterAttributes.None, _from.Module.TypeSystem.Int32));
				}
				//initialise.Body = null;

				interface_type.Methods.Add(initialise);
				interface_type.Methods.Add(get_Item);
				interface_type.Methods.Add(set_Item);
				interface_type.Properties.Add(item);
				
				// todo: emitter
				// this will instruct InvokeMember to use the Item property when the array is called like this: array[0,0]
				var defaultAttribute = _from.Module.ImportReference(typeof(System.Reflection.DefaultMemberAttribute).GetConstructor(new[] { typeof(string) }));
				var defaultPropertyAttribute = new CustomAttribute(defaultAttribute);
				defaultPropertyAttribute.ConstructorArguments.Add(new CustomAttributeArgument(_from.Module.TypeSystem.String, item.Name));
				interface_type.CustomAttributes.Add(defaultPropertyAttribute);
			}

			var definition = _from as TypeDefinition;
			if (definition != null)
			{
				foreach (var property in definition.Properties.Where(x =>
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

				foreach (var method in definition.Methods.Where(x => x.IsPublic
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
			}

			return interface_type;
		}
	}
}
