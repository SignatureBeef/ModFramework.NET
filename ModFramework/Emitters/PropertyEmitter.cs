/*
Copyright (C) 2020 DeathCradle

This file is part of Open Terraria API v3 (OTAPI)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see <http://www.gnu.org/licenses/>.
*/
using ModFramework.Relinker;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace ModFramework;

[MonoMod.MonoModIgnore]
public static class PropertyEmitter
{
    const MethodAttributes DefaultMethodAttributes = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

    public static PropertyDefinition RemapAsProperty(this FieldDefinition field, ModFwModder modder)
    {
        PropertyDefinition property = new(field.Name, PropertyAttributes.None, field.FieldType)
        {
            HasThis = !field.IsStatic,
            GetMethod = field.GenerateGetter(),
            SetMethod = field.GenerateSetter()
        };

        //Add the CompilerGeneratedAttribute or if you decompile the getter body will be shown
        field.CustomAttributes.Add(CreateCompilerGeneratedAttribute(field.DeclaringType.Module));

        field.DeclaringType.Properties.Add(property);

        // add a task to rewrite the field accessors to properties
        modder.AddTask<FieldToPropertyRelinker>(field, property);

        field.Name = $"<{field.Name}>k__BackingField";
        //field.Attributes = FieldAttributes.Private;
        field.IsPrivate = true;

        return property;
    }

    public static void RemapFieldsToProperties(this TypeDefinition type, ModFwModder modder)
    {
        foreach (var field in type.Fields.Where(f => !f.HasConstant && !f.IsPrivate))
        {
            field.RemapAsProperty(modder);
        }
    }

    public static MethodDefinition GenerateGetter(this FieldDefinition field)
    {
        //Create the method definition
        MethodDefinition method = new("get_" + field.Name, DefaultMethodAttributes, field.FieldType)
        {
            HasThis = !field.IsStatic,
        };

        method.IsStatic = field.IsStatic;

        //Create the il processor so we can alter il
        var il = method.Body.GetILProcessor();

        //Load the current type instance if required
        if (!field.IsStatic)
            il.Append(il.Create(OpCodes.Ldarg_0));

        //Load the backing field
        il.Append(il.Create(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field));
        //Return the backing fields value
        il.Append(il.Create(OpCodes.Ret));

        //Set basic getter method details 
        method.Body.InitLocals = true;
        method.SemanticsAttributes = MethodSemanticsAttributes.Getter;
        method.IsGetter = true;

        //Add the CompilerGeneratedAttribute or if you decompile the getter body will be shown
        method.CustomAttributes.Add(CreateCompilerGeneratedAttribute(field.DeclaringType.Module));

        field.DeclaringType.Methods.Add(method);

        return method;
    }

    static CustomAttribute CreateCompilerGeneratedAttribute(ModuleDefinition module)
    {
        var cga = module.GetCoreLibMethod("System.Runtime.CompilerServices", "CompilerGeneratedAttribute", ".ctor");
        cga.HasThis = true;
        return new(cga);
    }

    public static MethodDefinition GenerateSetter(this FieldDefinition field)
    {
        //Create the method definition
        MethodDefinition method = new("set_" + field.Name, DefaultMethodAttributes, field.DeclaringType.Module.TypeSystem.Void)
        {
            HasThis = !field.IsStatic,
        };

        method.IsStatic = field.IsStatic;

        //Setters always have a 'value' variable, but it's really just a parameter. We need to add this.
        method.Parameters.Add(new("value", ParameterAttributes.None, field.FieldType));

        //Create the il processor so we can alter il
        var il = method.Body.GetILProcessor();

        //Load the current type instance if required
        if (!field.IsStatic)
            il.Append(il.Create(OpCodes.Ldarg_0));
        //Load the 'value' parameter we added (alternatively, we could do il.Create(OpCodes.Ldarg, <field definition>)
        il.Append(il.Create(field.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1));
        //Store the parameters value into the backing field
        il.Append(il.Create(field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, field));
        //Return from the method as we are done.
        il.Append(il.Create(OpCodes.Ret));

        //Set basic setter method details 
        method.Body.InitLocals = true;
        method.SemanticsAttributes = MethodSemanticsAttributes.Setter;
        method.IsSetter = true;

        //Add the CompilerGeneratedAttribute or if you decompile the getter body will be shown
        method.CustomAttributes.Add(CreateCompilerGeneratedAttribute(field.DeclaringType.Module));

        field.DeclaringType.Methods.Add(method);

        return method;
    }
}
