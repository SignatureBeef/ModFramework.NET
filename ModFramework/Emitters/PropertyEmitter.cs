using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace Mod.Framework.Emitters
{
	/// <summary>
	/// This emitter produces new <see cref="PropertyDefinition"/> instances that you can add to a type.
	/// </summary>
	public class PropertyEmitter : IEmitter<PropertyDefinition>
	{
		const PropertyAttributes DefaultAttributes = PropertyAttributes.None;
		const MethodAttributes DefaultGetterAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
		const MethodAttributes DefaultSetterAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

		private string _name;
		private TypeReference _propertyType;
		private TypeDefinition _declaringType;
		private PropertyAttributes _attributes;
		private MethodAttributes? _getterAttributes;
		private MethodAttributes? _setterAttributes;

		public bool AutoImplementedGetter { get; set; } = true;
		public bool AutoImplementedSetter { get; set; } = true;
		public bool CompilerGenerated { get; set; } = true;

		public PropertyEmitter(string name, TypeReference propertyType,
			TypeDefinition declaringType = null,
			PropertyAttributes attributes = DefaultAttributes,
			MethodAttributes? getterAttributes = DefaultGetterAttributes,
			MethodAttributes? setterAttributes = DefaultSetterAttributes)
		{
			this._name = name;
			this._propertyType = propertyType;
			this._declaringType = declaringType;
			this._attributes = attributes;
			this._getterAttributes = getterAttributes;
			this._setterAttributes = setterAttributes;
		}

		/// <summary>
		/// Generates a property with getter and setter methods.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="propertyType"></param>
		/// <param name="declaringType"></param>
		/// <param name="attributes"></param>
		/// <returns></returns>
		public PropertyDefinition GenerateProperty()
		{
			//Create the initial property definition
			var prop = new PropertyDefinition(_name, _attributes, _propertyType);

			//Set the defaults of the property
			prop.HasThis = true;
			if (_declaringType != null)
			{
				prop.DeclaringType = _declaringType;
				prop.DeclaringType.Properties.Add(prop);
			}

			if (_getterAttributes.HasValue)
			{
				//Generate the getter
				prop.GetMethod = GenerateGetter(prop);
				_declaringType.Methods.Add(prop.GetMethod);
			}

			if (_setterAttributes.HasValue)
			{
				//Generate the setter
				prop.SetMethod = GenerateSetter(prop);
				_declaringType.Methods.Add(prop.SetMethod);
			}
			
			return prop;
		}

		private FieldDefinition GetBackingField(PropertyDefinition property)
		{
			var name = $"<{property.Name}>k__BackingField";
			var field = property.DeclaringType.Fields.SingleOrDefault(x => x.Name == name);
			if (field == null)
			{
				field = new FieldDefinition(name, FieldAttributes.Private, property.PropertyType);

				//This is required or it will be shown when you decompile
				field.CustomAttributes.Add(new CustomAttribute(
					property.Module.ImportReference(
						typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)
							.GetConstructors()
							.Single()
					)
				));
			}

			return field;
		}

		/// <summary>
		/// Generates a property getter method
		/// </summary>
		/// <param name="property"></param>
		/// <param name="attributes"></param>
		/// <returns></returns>
		public MethodDefinition GenerateGetter(PropertyDefinition property, bool instance = true)
		{
			//Create the method definition
			var method = new MethodDefinition("get_" + property.Name, _getterAttributes.Value, property.PropertyType);

			//Reference - this is what we need to essentially replicate
			//IL_0000: ldarg.0
			//IL_0001: ldfld int32 OTAPI.Modification.Tile.Modifications.PropertyReferenceTest::'<myData>k__BackingField'
			//IL_0006: ret

			if (AutoImplementedGetter)
			{
				//Create the il processor so we can alter il
				var il = method.Body.GetILProcessor();

				//Load the current type instance if required
				if (instance)
					il.Append(il.Create(OpCodes.Ldarg_0));
				//Load the backing field
				il.Append(il.Create(OpCodes.Ldfld, GetBackingField(property)));
				//Return the backing fields value
				il.Append(il.Create(OpCodes.Ret));
			}

			//Set basic getter method details 
			if (method.Body != null) method.Body.InitLocals = true;
			method.SemanticsAttributes = MethodSemanticsAttributes.Getter;
			method.IsGetter = true;

			if (CompilerGenerated)
			{
				//Add the CompilerGeneratedAttribute or if you decompile the getter body will be shown
				method.CustomAttributes.Add(new CustomAttribute(
					_declaringType.Module.ImportReference(
						typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)
							.GetConstructors()
							.Single()
					)
				));
			}

			//A-ok cap'n
			return method;
		}

		/// <summary>
		/// Generates a property setter method
		/// </summary>
		/// <param name="property"></param>
		/// <param name="attributes"></param>
		/// <returns></returns>
		public MethodDefinition GenerateSetter(PropertyDefinition property, bool instance = true)
		{
			//Create the method definition
			var method = new MethodDefinition("set_" + property.Name, _setterAttributes.Value, property.Module.TypeSystem.Void);

			//Setters always have a 'value' variable, but it's really just a parameter. We need to add this.
			method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, property.PropertyType));

			//Reference - this is what we need to essentially replicate
			//IL_0000: ldarg.0
			//IL_0001: ldarg.1
			//IL_0002: stfld int32 OTAPI.Modification.Tile.Modifications.PropertyReferenceTest::'<myData>k__BackingField'
			//IL_0007: ret

			if (AutoImplementedSetter)
			{
				//Create the il processor so we can alter il
				var il = method.Body.GetILProcessor();

				//Load the current type instance if required
				if (instance)
					il.Append(il.Create(OpCodes.Ldarg_0));
				//Load the 'value' parameter we added (alternatively, we could do il.Create(OpCodes.Ldarg, <parameter definition>)
				il.Append(il.Create(OpCodes.Ldarg_1));
				//Store the parameters value into the backing field
				il.Append(il.Create(OpCodes.Stfld, GetBackingField(property)));
				//Return from the method as we are done.
				il.Append(il.Create(OpCodes.Ret));
			}

			//Set basic setter method details 
			if (method.Body != null) method.Body.InitLocals = true;
			method.SemanticsAttributes = MethodSemanticsAttributes.Setter;
			method.IsSetter = true;

			if (CompilerGenerated)
			{
				//Add the CompilerGeneratedAttribute or if you decompile the getter body will be shown
				method.CustomAttributes.Add(new CustomAttribute(
					property.DeclaringType.Module.ImportReference(
						typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)
							.GetConstructors()
							.Single()
					)
				));
			}

			//A-ok cap'n
			return method;
		}

		public PropertyDefinition Emit()
		{
			return GenerateProperty();
		}
	}
}
