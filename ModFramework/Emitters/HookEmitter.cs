/*
Copyright (C) 2024 DeathCradle

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
using System;
using System.Linq;

namespace ModFramework;

[MonoMod.MonoModIgnore]
public static class HookEmitter
{
    /// <summary>
    /// Creates a new type in the assembly to hoist the hook events.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    static TypeDefinition GetOrCreateHookType(TypeDefinition type)
    {
        var hookTypeName = "HookEvents." + type.FullName;
        var hookType = type.Module.Types.SingleOrDefault(x => x.FullName == hookTypeName);
        if (hookType is null)
        {
            hookType = new(
                "HookEvents." + type.Namespace,
                type.Name,
                TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Public | TypeAttributes.BeforeFieldInit
            );
            hookType.BaseType = type.Module.TypeSystem.Object;
            type.Module.Types.Add(hookType);
        }
        return hookType;
    }

    static string GetUniqueName(MethodDefinition method)
    {
        // generate a name, with consideration to overloads
        var name = method.Name;
        if (method.DeclaringType.Methods.Count(x => x.Name == name) > 1 && method.Parameters.Count > 0)
        {
            // overloads are detected, append parameter types to the name
            name += "_" + string.Join("_", method.Parameters.Select(y => y.ParameterType.Name));
        }
        return name;
    }

    const String HookReturnValueName = "HookReturnValue";
    const String ContinueExecutionName = "ContinueExecution";

    static TypeDefinition CreateHookEventArgs(TypeDefinition hookType, MethodDefinition hookDefinition, string? name = null)
    {
        var hookEventName = name ?? (hookDefinition.Name + "EventArgs");
        TypeDefinition hookEvent = new(
             "", //hookType.Namespace,
             hookEventName,
             TypeAttributes.Class | TypeAttributes.BeforeFieldInit | TypeAttributes.NestedPublic | TypeAttributes.Sealed,
             hookType.Module.ImportReference(typeof(EventArgs))
         );
        hookType.NestedTypes.Add(hookEvent);

        var resultType = hookDefinition.Module.TypeSystem.Boolean;
        FieldDefinition resultField = new(ContinueExecutionName, FieldAttributes.Public, resultType);

        // if the method has a return type, add a field for it
        var hasReturnValue = hookDefinition.ReturnType != hookDefinition.Module.TypeSystem.Void;
        if (hasReturnValue)
        {
            FieldDefinition returnField = new(HookReturnValueName, FieldAttributes.Public, hookDefinition.ReturnType);
            hookEvent.Fields.Add(returnField);
        }

        // for each parameter in the method, create a field
        foreach (var param in hookDefinition.Parameters)
        {
            var paramType = param.ParameterType;
            FieldDefinition paramField = new(param.Name, FieldAttributes.Public, paramType);
            hookEvent.Fields.Add(paramField);
        }

        // create ctor, calling base ctor
        MethodDefinition ctor = new (".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, hookDefinition.Module.TypeSystem.Void);
        ctor.Body = new MethodBody(ctor);
        var il = ctor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, hookDefinition.Module.ImportReference(typeof(object).GetConstructors().Single()));
        // Set ContinueExecution to true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, resultField);
        il.Emit(OpCodes.Ret);
        hookEvent.Methods.Add(ctor);

        hookEvent.Fields.Add(resultField);
        return hookEvent;
    }

    static MethodDefinition CreateInvokeMethod(TypeDefinition hookType, FieldDefinition eventField, TypeDefinition hookEventArgsType, string? name = null)
    {
        var methodName = name ?? $"Invoke{eventField.Name.TrimStart('_')}";

        // Define the `Invoke` method
        MethodDefinition invokeMethod = new(
            methodName,
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static,
            hookEventArgsType
        );

        // Add parameters: object sender, hookEventArgsType args
        var senderFieldName = "instanceAsSender";
        while (hookType.Fields.Any(x => x.Name == senderFieldName))
            senderFieldName = "_" + senderFieldName;
        ParameterDefinition senderParam = new(senderFieldName, ParameterAttributes.None, hookType.Module.TypeSystem.Object);
        invokeMethod.Parameters.Add(senderParam);

        //var returnValueField = hookEventArgsType.Fields.SingleOrDefault(x => x.Name == HookReturnValueName);
        //if (returnValueField is not null)
        //{
        //    // add a param type "byref" for the return value
        //    ParameterDefinition returnValueParam = new(HookReturnValueName, ParameterAttributes.Out, returnValueField.FieldType);
        //    invokeMethod.Parameters.Add(returnValueParam);
        //}

        // instead of an event args, intake the parameters so each call doesnt need to new up itself.
        foreach (var field in hookEventArgsType.Fields.Where(x => x.Name != ContinueExecutionName && x.Name != HookReturnValueName))
        {
            ParameterDefinition prm = new(field.Name, ParameterAttributes.None, field.FieldType);
            invokeMethod.Parameters.Add(prm);
        }

        // Create a GenericInstanceType for EventHandler<HookEventArgsType>
        var eventHandlerGenericType = hookType.Module.ImportReference(typeof(EventHandler<>));
        GenericInstanceType genericEventHandlerType = new(eventHandlerGenericType)
        {
            GenericArguments = { hookEventArgsType }
        };

        // Import the "Invoke" method of EventHandler<HookEventArgsType>
        var eventHandlerInvokeMethod = eventHandlerGenericType.Resolve().Methods.First(m => m.Name == "Invoke");
        MethodReference invokeMethodReference = new(
            eventHandlerInvokeMethod.Name,
            hookType.Module.TypeSystem.Void,
            genericEventHandlerType
        )
        {
            HasThis = true
        };

        // Add parameters to the invokeMethodReference
        invokeMethodReference.Parameters.Add(new (hookType.Module.TypeSystem.Object)); // sender
        invokeMethodReference.Parameters.Add(new (eventHandlerInvokeMethod.Parameters[1].ParameterType)); // args  - see EventHandler<>.Invoke, il is !0

        // Generate IL for the Invoke method
        var il = invokeMethod.Body.GetILProcessor();
        var returnLabel = il.Create(OpCodes.Ldloc_0);

        // Create the event args instance from the method parameters
        VariableDefinition vrb = new (hookEventArgsType);
        invokeMethod.Body.Variables.Add(vrb);
        il.Emit(OpCodes.Newobj, hookEventArgsType.Methods.Single(x => x.Name == ".ctor")); // Create a new instance of the event args
        // Set the fields of the event args instance
        foreach (var prm in invokeMethod.Parameters.Skip(1 /*sender*/))
        {
            il.Emit(OpCodes.Dup);                        // Load the event args instance
            il.Emit(OpCodes.Ldarg, prm);                 // Load the parameter
            il.Emit(OpCodes.Stfld, hookEventArgsType.Fields.Single(x => x.Name == prm.Name)); // Set the field
        }
        il.Emit(OpCodes.Stloc_0);                        // Store the event args instance

        // Check if the event is not null
        il.Emit(OpCodes.Ldsfld, eventField);             // Load the static event field
        il.Emit(OpCodes.Brfalse_S, returnLabel);         // If null, skip invocation

        // Invoke the event delegate
        il.Emit(OpCodes.Ldsfld, eventField);             // Load the static event field
        il.Emit(OpCodes.Ldarg_0);                        // Load the sender (first parameter)
        //il.Emit(OpCodes.Ldarg_1);                        // Load the args (second parameter)
        il.Emit(OpCodes.Ldloc_0);                        // Load the event args instance
        il.Emit(OpCodes.Callvirt, invokeMethodReference); // Call the Invoke method on the delegate

        // Return args.Result
        il.Append(returnLabel);
        //il.Emit(OpCodes.Ldfld, hookEventArgsType.Fields.Single(x => x.Name == "ContinueExecution")); // Load the Result field
        // Return the event args variable
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);

        // Add the Invoke method to the type
        hookType.Methods.Add(invokeMethod);

        return invokeMethod;
    }

    /// <summary>
    /// Creates a replacement method for the original method.
    /// </summary>
    /// <param name="original"></param>
    /// <param name="eventInvoke"></param>
    /// <returns></returns>
    static MethodDefinition CreateReplacement(MethodDefinition original, MethodDefinition eventInvoke)
    {
        var eventArgs = eventInvoke.ReturnType.Resolve();
        var hookReturnValueField = eventArgs.Fields.SingleOrDefault(x => x.Name == HookReturnValueName);

        MethodDefinition methodDefinition = new(
            original.Name,
            original.Attributes,
            original.ReturnType
        );

        foreach (var param in original.Parameters)
            methodDefinition.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));

        var il = methodDefinition.Body.GetILProcessor();

        VariableDefinition eventArgsVariable = new(eventInvoke.ReturnType);
        methodDefinition.Body.Variables.Add(eventArgsVariable);

        il.Emit(original.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0);
        for (int i = 0; i < methodDefinition.Parameters.Count; i++)
            il.Emit(OpCodes.Ldarg, methodDefinition.Parameters[i]);
        il.Emit(OpCodes.Call, eventInvoke);

        // store the event args in a local variable
        il.Emit(OpCodes.Stloc, eventArgsVariable);

        // use ContinueExecutionName to determine whether to continue or not
            il.Emit(OpCodes.Ldloc, eventArgsVariable);
        il.Emit(OpCodes.Ldfld, eventInvoke.ReturnType.Resolve().Fields.Single(x => x.Name == ContinueExecutionName));

        // the event invoke is a boolen, if false, return else invoke the original method
        var returnLabel = hookReturnValueField is not null ? il.Create(OpCodes.Ldloc, eventArgsVariable) : il.Create(OpCodes.Ret);

        il.Emit(OpCodes.Brfalse_S, returnLabel);
        
        il.Emit(OpCodes.Ldarg_0);
        // for each event arg field, load it onto the stack to the original method
        foreach (var field in eventArgs.Fields.Where(x => x.Name != ContinueExecutionName && x.Name != HookReturnValueName))
        {
            il.Emit(OpCodes.Ldloc, eventArgsVariable);
            il.Emit(OpCodes.Ldfld, field);
        }
        il.Emit(OpCodes.Call, original.Module.ImportReference(original));
        il.Emit(OpCodes.Ret);

        il.Append(returnLabel);

        if (hookReturnValueField is not null)
        {
            il.Emit(OpCodes.Ldfld, hookReturnValueField);
            il.Emit(OpCodes.Ret);
        }

        return methodDefinition;
    }

    /// <summary>
    /// Creates a hook for a single method.
    /// </summary>
    /// <param name="definition"></param>
    /// <param name="modder"></param>
    public static void CreateHook(this MethodDefinition definition, ModFwModder modder)
    {
        // create hook type
        // create event in hook type
        // rename current method
        // put one in it's place
        // call an event
        // check whether to continue or not, using a simple bool flag

        var uniqueName = GetUniqueName(definition);
        var hookType = GetOrCreateHookType(definition.DeclaringType);

        var hookEventArgs = CreateHookEventArgs(hookType, definition, name: $"{uniqueName}EventArgs");
        var (hookField, _) = definition.CreateEvent(hookType, hookEventArgs, name: uniqueName);
        var newMethod = CreateInvokeMethod(hookType, hookField, hookEventArgs, name: $"Invoke{uniqueName}");

        var replacement = CreateReplacement(definition, newMethod);
        definition.DeclaringType.Methods.Add(replacement);

        // rename the original method
        definition.Name = $"hooked_{definition.Name}";

        // remove any overrides etc
        definition.Attributes &= ~MethodAttributes.Virtual;
        definition.Attributes &= ~MethodAttributes.NewSlot;
    }

    /// <summary>
    /// Creates hooks for an entire type.
    /// </summary>
    /// <param name="definition">The assembly definition</param>
    /// <param name="modder">Modder instance</param>
    /// <param name="methodNames">Optional list of methods to process, if empty all methods will be processed</param>
    public static void CreateHooks(this TypeDefinition definition, ModFwModder modder, params string[] methodNames)
    {
        foreach (var method in definition.Methods.Where(x => x.HasBody &&
            (methodNames.Length == 0 || methodNames.Contains(x.Name)) &&
            // not a constructor
            !x.IsConstructor &&
            // not an event
            !(definition.Events.Any(evt => evt.AddMethod == x || evt.RemoveMethod == x))
        ).ToList())
            method.CreateHook(modder);
    }

    /// <summary>
    /// Creates hooks for an entire module.
    /// </summary>
    /// <param name="definition">The module definition</param>
    /// <param name="modder">Modder instance</param>
    /// <param name="typeNames">Optional list of types to process, if empty all types will be processed</param>
    public static void CreateHooks(this ModuleDefinition definition, ModFwModder modder, params string[] typeNames)
    {
        foreach (var type in definition.Types.ToList())
            if (typeNames.Length == 0 || typeNames.Contains(type.FullName))
                type.CreateHooks(modder);
    }

    /// <summary>
    /// Creates hooks for an entire assembly.
    /// </summary>
    /// <param name="definition">The assembly definition</param>
    /// <param name="modder">Modder instance</param>
    /// <param name="typeNames">Optional list of types to process, if empty all types will be processed</param>
    public static void CreateHooks(this AssemblyDefinition definition, ModFwModder modder, params string[] typeNames)
    {
        foreach (var module in definition.Modules.ToList())
            module.CreateHooks(modder, typeNames);
    }
}
