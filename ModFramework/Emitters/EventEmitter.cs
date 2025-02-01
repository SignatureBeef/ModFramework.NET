/*
Copyright (C) 2024 SignatureBeef

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
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;
using System;
using System.Linq;

namespace ModFramework;

[MonoMod.MonoModIgnore]
public static class EventEmitter
{
    /// <summary>
    /// Creates a new event based upon the source definition
    /// </summary>
    /// <param name="sourceDefinition">The method to base the even upon</param>
    /// <param name="containingType">The type to create the event in</param>
    /// <param name="eventArgsType">The event args to use</param>
    /// <param name="name">Optional desired name for the event</param>
    /// <returns>A tuple of the field and event definitions</returns>
    public static (FieldDefinition fieldDefinition, EventDefinition eventDefinition) CreateEvent(this MethodDefinition sourceDefinition, TypeDefinition containingType, TypeDefinition eventArgsType, MonoModder modder, string? name = null)
    {
        //var eventHandlerType = modder.ResolveTypeReference(typeof(EventHandler<>));
        var eventHandlerType = !sourceDefinition.IsStatic ? HookEmitter.GetOrCreateHookDelegate(modder) : modder.ResolveTypeReference(typeof(EventHandler<>));
        var eventHandlerTypeGeneric = new GenericInstanceType(eventHandlerType)
        {
            GenericArguments = { eventArgsType }
        };
        if (!sourceDefinition.IsStatic)
            eventHandlerTypeGeneric.GenericArguments.Insert(0, sourceDefinition.DeclaringType);

        // Define the event backing field
        var fieldName = name ?? $"{sourceDefinition.Name}Event";
        FieldDefinition eventField = new(
            fieldName,
            FieldAttributes.Private | FieldAttributes.Static,
            eventHandlerTypeGeneric
        );
        containingType.Fields.Add(eventField);

        // Define the event itself
        EventDefinition eventDefinition = new(
            fieldName,
            EventAttributes.None,
            eventField.FieldType
        );
        containingType.Events.Add(eventDefinition);

        // Create the `add` method
        MethodDefinition addMethod = new(
            $"add_{fieldName}",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static | MethodAttributes.SpecialName,
            modder.Module.TypeSystem.Void
        );
        ParameterDefinition parameter = new("value", ParameterAttributes.None, eventField.FieldType);
        addMethod.Parameters.Add(parameter);
        var ilAdd = addMethod.Body.GetILProcessor();

        var compareExchange = modder.Module.ImportReference(modder.ResolveTypeReference(typeof(System.Threading.Interlocked))
            .Resolve()
            .Methods.Single(m => m.Name == "CompareExchange" && m.HasGenericParameters && m.IsStatic));

        GenericInstanceMethod methodInterlockedCompareExchange = new(compareExchange);
        methodInterlockedCompareExchange.GenericArguments.Add(eventField.FieldType);


        VariableDefinition v0 = new(eventField.FieldType);
        VariableDefinition v1 = new(eventField.FieldType);
        VariableDefinition v2 = new(eventField.FieldType);
        addMethod.Body.InitLocals = true;
        addMethod.Body.Variables.Add(v0);
        addMethod.Body.Variables.Add(v1);
        addMethod.Body.Variables.Add(v2);

        ilAdd.Emit(OpCodes.Ldsfld, eventField); // Load static field
        ilAdd.Emit(OpCodes.Stloc_0);           // Store into local v0
        var loopStart = ilAdd.Create(OpCodes.Ldloc_0);
        ilAdd.Append(loopStart);
        ilAdd.Emit(OpCodes.Stloc_1);           // Store into local v1
        ilAdd.Emit(OpCodes.Ldloc_1);           // Load local v1
        ilAdd.Emit(OpCodes.Ldarg_0);           // Load the parameter value

        var delegateType = modder.ResolveTypeReference(typeof(System.Delegate)).Resolve();
        var combine = modder.Module.ImportReference(delegateType
            .Methods.Single(m => m.Name == "Combine" && m.IsStatic && m.Parameters.Count == 2));
        ilAdd.Emit(OpCodes.Call, combine);
        ilAdd.Emit(OpCodes.Castclass, eventField.FieldType);
        ilAdd.Emit(OpCodes.Stloc_2);           // Store into local v2
        ilAdd.Emit(OpCodes.Ldsflda, eventField);
        ilAdd.Emit(OpCodes.Ldloc_2);
        ilAdd.Emit(OpCodes.Ldloc_1);
        ilAdd.Emit(OpCodes.Call, methodInterlockedCompareExchange);
        ilAdd.Emit(OpCodes.Stloc_0);           // Update v0
        ilAdd.Emit(OpCodes.Ldloc_0);
        ilAdd.Emit(OpCodes.Ldloc_1);
        ilAdd.Emit(OpCodes.Bne_Un_S, loopStart); // If not equal, loop back
        ilAdd.Emit(OpCodes.Ret);
        containingType.Methods.Add(addMethod);

        // Create the `remove` method
        MethodDefinition removeMethod = new(
            $"remove_{fieldName}",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static | MethodAttributes.SpecialName,
            modder.Module.TypeSystem.Void
        );
        removeMethod.Parameters.Add(parameter);

        v0 = new(eventField.FieldType);
        v1 = new(eventField.FieldType);
        v2 = new(eventField.FieldType);
        removeMethod.Body.InitLocals = true;
        removeMethod.Body.Variables.Add(v0);
        removeMethod.Body.Variables.Add(v1);
        removeMethod.Body.Variables.Add(v2);

        var ilRemove = removeMethod.Body.GetILProcessor();
        ilRemove.Emit(OpCodes.Ldsfld, eventField); // Load static field
        ilRemove.Emit(OpCodes.Stloc_0);           // Store into local v0
        loopStart = ilRemove.Create(OpCodes.Ldloc_0);
        ilRemove.Append(loopStart);
        ilRemove.Emit(OpCodes.Stloc_1);           // Store into local v1
        ilRemove.Emit(OpCodes.Ldloc_1);           // Load local v1
        ilRemove.Emit(OpCodes.Ldarg_0);           // Load the parameter value
        var remove = modder.Module.ImportReference(delegateType
            .Methods.Single(m => m.Name == "Remove" && m.IsStatic));
        ilRemove.Emit(OpCodes.Call, remove);
        ilRemove.Emit(OpCodes.Castclass, eventField.FieldType);
        ilRemove.Emit(OpCodes.Stloc_2);           // Store into local v2
        ilRemove.Emit(OpCodes.Ldsflda, eventField);
        ilRemove.Emit(OpCodes.Ldloc_2);
        ilRemove.Emit(OpCodes.Ldloc_1);
        ilRemove.Emit(OpCodes.Call, methodInterlockedCompareExchange);
        ilRemove.Emit(OpCodes.Stloc_0);           // Update v0
        ilRemove.Emit(OpCodes.Ldloc_0);
        ilRemove.Emit(OpCodes.Ldloc_1);
        ilRemove.Emit(OpCodes.Bne_Un_S, loopStart); // If not equal, loop back
        ilRemove.Emit(OpCodes.Ret);
        containingType.Methods.Add(removeMethod);

        // add compiler generated attribute
        var ctor = modder.Module.ImportReference(modder.ResolveTypeReference(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))
            .Resolve()
            .Methods.Single(m => m.Name == ".ctor" && m.IsConstructor && m.Parameters.Count == 0));
        addMethod.CustomAttributes.Add(new(ctor));
        removeMethod.CustomAttributes.Add(new(ctor));
        eventField.CustomAttributes.Add(new(ctor));

        // Link the add/remove methods to the event
        eventDefinition.AddMethod = addMethod;
        eventDefinition.RemoveMethod = removeMethod;

        return (eventField, eventDefinition);
    }
}
