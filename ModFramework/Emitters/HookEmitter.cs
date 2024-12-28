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
using Microsoft.VisualBasic.FileIO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod;
using System;
using System.Linq;

namespace ModFramework;

[MonoMod.MonoModIgnore]
public static class HookEmitter
{
    /// <summary>
    /// The name to place in front of the preserved hook method
    /// </summary>
    /// <remarks>ModFramework Hook</remarks>
    public const String HookMethodNamePrefix = "mfwh_";

    /// <summary>
    /// The name of the field created in event args to store the return value
    /// </summary>
    const String HookReturnValueName = "HookReturnValue";

    /// <summary>
    /// The name of the field created in event args to determine whether to continue execution
    /// </summary>
    const String ContinueExecutionName = "ContinueExecution";

    /// <summary>
    /// The name of the field created in event args to store the original method delegate
    /// </summary>
    const String OriginalMethodName = "OriginalMethod";

    /// <summary>
    /// Excluded field names from the event args that are to be ignored when loading upon stack, etc, as they are usually handled manually.
    /// </summary>
    static readonly String[] ExcludeNames = [HookReturnValueName, ContinueExecutionName, OriginalMethodName];

    /// <summary>
    /// Creates a new type in the assembly to hoist the hook events.
    /// </summary>
    /// <param name="type">The type that is being hooked, and thuus to be created as a hooked type</param>
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

    /// <summary>
    /// Generates a unique name for a method, with consideration to overloads.
    /// </summary>
    /// <param name="method">The method to generate a name for</param>
    /// <returns>A unique name string</returns>
    static string GetUniqueName(MethodDefinition method)
    {
        // generate a name, with consideration to overloads
        var name = method.Name;
        if (method.DeclaringType.Methods.Count(x => x.Name == name) > 1 && method.Parameters.Count > 0)
        {
            // overloads are detected, append parameter types to the name
            name += "_" + string.Join("_", method.Parameters.Select(y =>
            {
                var name = y.ParameterType.Name;
                if (y.ParameterType.IsByReference)
                    name = $"{y.ParameterType.GetElementType().Name}ByRef";
                return name;
            }));
        }
        return name;
    }

    /// <summary>
    /// Creates an event in the hook type for the method.
    /// </summary>
    /// <param name="hookType">The hook type where the event is created</param>
    /// <param name="hookDefinition">The definition that is used to generate fields/properties in the event args from the methods arguments</param>
    /// <param name="modder">The monomodder instance</param>
    /// <param name="name">Optional desired name</param>
    /// <returns>The deginition of the event args</returns>
    static TypeDefinition CreateHookEventArgs(TypeDefinition hookType, MethodDefinition hookDefinition, MonoModder modder, string? name = null)
    {
        var hookEventName = name ?? (hookDefinition.Name + "EventArgs");
        TypeDefinition hookEvent = new(
             "", //hookType.Namespace,
             hookEventName,
             TypeAttributes.Class | TypeAttributes.BeforeFieldInit | TypeAttributes.NestedPublic | TypeAttributes.Sealed,
             hookType.Module.TypeSystem.Object
         );
        hookType.NestedTypes.Add(hookEvent);

        var resultType = hookDefinition.Module.TypeSystem.Boolean;
        FieldDefinition resultField = new(ContinueExecutionName, FieldAttributes.Public, resultType);

        // if the method has a return type, add a field for it
        // n.b. this may happen before/after relinking so two Void types may not be equal thus FullName is used
        var hasReturnValue = hookDefinition.ReturnType.FullName != hookDefinition.Module.TypeSystem.Void.FullName;
        if (hasReturnValue)
        {
            FieldDefinition returnField = new(HookReturnValueName, FieldAttributes.Public, hookDefinition.ReturnType);
            hookEvent.Fields.Add(returnField);
        }

        // for each parameter in the method, create a field
        foreach (var param in hookDefinition.Parameters)
        {
            var paramType = param.ParameterType.IsByReference ? param.ParameterType.GetElementType() : param.ParameterType;
            FieldDefinition paramField = new(param.Name, FieldAttributes.Public, paramType);
            hookEvent.Fields.Add(paramField);
        }

        // create ctor, calling base ctor
        MethodDefinition ctor = new(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, hookDefinition.Module.TypeSystem.Void);
        var il = ctor.Body.GetILProcessor();

        il.Emit(OpCodes.Ldarg_0);

        var objCtor = hookDefinition.Module.TypeSystem.Object.Resolve().Methods.Single(x => x.Name == ".ctor");
        il.Emit(OpCodes.Call, hookDefinition.Module.ImportReference(objCtor));

        // Set ContinueExecution to true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, resultField);

        il.Emit(OpCodes.Ret);
        hookEvent.Methods.Add(ctor);

        hookEvent.Fields.Add(resultField);

        return hookEvent;
    }

    /// <summary>
    /// Creates an instruction to load a default value onto the stack.
    /// </summary>
    /// <param name="type">The type of default value</param>
    /// <returns>An instruction that can be appended to a method body</returns>
    public static Instruction CreateDefaultValueInstruction(TypeReference type)
    {
        return type.MetadataType switch
        {
            MetadataType.Boolean or MetadataType.Byte or MetadataType.SByte or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 => Instruction.Create(OpCodes.Ldc_I4_0),
            MetadataType.Int64 or MetadataType.UInt64 => Instruction.Create(OpCodes.Ldc_I8, 0L),
            MetadataType.Single => Instruction.Create(OpCodes.Ldc_R4, 0f),
            MetadataType.Double => Instruction.Create(OpCodes.Ldc_R8, 0d),
            MetadataType.ValueType => Instruction.Create(OpCodes.Initobj, type),
            _ => Instruction.Create(OpCodes.Ldnull)
        };
    }

    /// <summary>
    /// Creates an instruction to load an indirect value onto the stack.
    /// </summary>
    /// <param name="type">The type that the instruction is intended for</param>
    /// <returns>An instruction that can be appended to a method body</returns>
    public static Instruction CreateLoadIndirectInstruction(TypeReference type)
    {
        return type.MetadataType switch
        {
            MetadataType.Boolean => Instruction.Create(OpCodes.Ldind_U1),
            MetadataType.Byte or MetadataType.SByte or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 => Instruction.Create(OpCodes.Ldind_I4),
            MetadataType.Int64 or MetadataType.UInt64 => Instruction.Create(OpCodes.Ldind_I8),
            MetadataType.Single => Instruction.Create(OpCodes.Ldind_R4),
            MetadataType.Double => Instruction.Create(OpCodes.Ldind_R8),
            MetadataType.ValueType => Instruction.Create(OpCodes.Ldobj, type),
            _ => Instruction.Create(OpCodes.Ldind_Ref)
        };
    }

    /// <summary>
    /// Creates an instruction to store an indirect value onto the stack.
    /// </summary>
    /// <param name="type">The type that the instruction is intended for</param>
    /// <returns>An instruction that can be appended to a method body</returns>
    public static Instruction CreateStoreIndirectFunction(TypeReference type)
    {
        return type.MetadataType switch
        {
            MetadataType.Boolean => Instruction.Create(OpCodes.Stind_I1),
            MetadataType.Byte or MetadataType.SByte or MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 => Instruction.Create(OpCodes.Stind_I4),
            MetadataType.Int64 or MetadataType.UInt64 => Instruction.Create(OpCodes.Stind_I8),
            MetadataType.Single => Instruction.Create(OpCodes.Stind_R4),
            MetadataType.Double => Instruction.Create(OpCodes.Stind_R8),
            MetadataType.ValueType => Instruction.Create(OpCodes.Stobj, type),
            _ => Instruction.Create(OpCodes.Stind_Ref)
        };
    }

    /// <summary>
    /// For use within the event args as a field that provides access to the original method.
    /// e.g. public delegate ReturnType OriginalMethodDelegate([optional instance], ...params)
    /// </summary>
    /// <param name="modder">The monomodder instance</param>
    /// <returns>The definition of the delegate</returns>
    public static TypeDefinition GetOrCreateOriginalMethodDelegate(MonoModder modder, MethodDefinition originalDefinition, TypeDefinition eventArgs)
    {
        var delegateName = GetUniqueName(originalDefinition) + "Callback";
        var delegateType = modder.Module.Types.SingleOrDefault(x => x.Name == delegateName);
        if (delegateType is not null)
            return delegateType;

        var multicast = modder.ResolveTypeReference<MulticastDelegate>();

        delegateType = new(
            "",
            $"{OriginalMethodName}Delegate",
            TypeAttributes.Sealed | TypeAttributes.NestedPublic
        );
        delegateType.BaseType = multicast;
        eventArgs.NestedTypes.Add(delegateType);

        // create ctor, Invoke, BeginInvoke, EndInvoke (no body)
        MethodDefinition ctor = new(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, delegateType.Module.TypeSystem.Void)
        {
            IsRuntime = true
        };
        ctor.Parameters.Add(new("object", ParameterAttributes.None, delegateType.Module.TypeSystem.Object));
        ctor.Parameters.Add(new("method", ParameterAttributes.None, delegateType.Module.TypeSystem.IntPtr));
        delegateType.Methods.Add(ctor);

        MethodDefinition invoke = new("Invoke", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, delegateType.Module.TypeSystem.Void)
        {
            IsRuntime = true
        };

        foreach (var param in originalDefinition.Parameters)
            invoke.Parameters.Add(new(param.Name, ParameterAttributes.None, param.ParameterType));

        delegateType.Methods.Add(invoke);

        var iAsyncResult = modder.ResolveTypeReference<IAsyncResult>();
        var iAsyncCallback = modder.ResolveTypeReference<AsyncCallback>();

        MethodDefinition beginInvoke = new("BeginInvoke", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, iAsyncResult)
        {
            IsRuntime = true
        };
        foreach (var param in originalDefinition.Parameters)
            beginInvoke.Parameters.Add(new(param.Name, ParameterAttributes.None, param.ParameterType));
        beginInvoke.Parameters.Add(new("callback", ParameterAttributes.None, iAsyncCallback));
        beginInvoke.Parameters.Add(new("object", ParameterAttributes.None, delegateType.Module.TypeSystem.Object));
        delegateType.Methods.Add(beginInvoke);

        MethodDefinition endInvoke = new("EndInvoke", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, delegateType.Module.TypeSystem.Void)
        {
            IsRuntime = true
        };
        endInvoke.Parameters.Add(new("result", ParameterAttributes.None, iAsyncResult));
        delegateType.Methods.Add(endInvoke);

        return delegateType;
    }

    /// <summary>
    /// Creates a hook delegate to be used for events
    /// </summary>
    /// <param name="modder">The monomodder instance</param>
    /// <returns>The deginition of the delegate</returns>
    public static TypeDefinition GetOrCreateHookDelegate(MonoModder modder)
    {
        var hookDelegate = modder.Module.Types.SingleOrDefault(x => x.Name == "HookDelegate");
        if (hookDelegate is not null)
            return hookDelegate;

        var multicast = modder.ResolveTypeReference<MulticastDelegate>();

        hookDelegate = new(
            "HookEvents",
            "HookDelegate",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed
        );
        hookDelegate.BaseType = multicast;
        hookDelegate.GenericParameters.Add(new("TSender", hookDelegate));
        hookDelegate.GenericParameters.Add(new("TArgs", hookDelegate));

        modder.Module.Types.Add(hookDelegate);

        // create ctor, Invoke, BeginInvoke, EndInvoke (no body)
        MethodDefinition ctor = new(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, hookDelegate.Module.TypeSystem.Void)
        {
            IsRuntime = true
        };
        ctor.Parameters.Add(new("object", ParameterAttributes.None, hookDelegate.Module.TypeSystem.Object));
        ctor.Parameters.Add(new("method", ParameterAttributes.None, hookDelegate.Module.TypeSystem.IntPtr));
        hookDelegate.Methods.Add(ctor);

        MethodDefinition invoke = new("Invoke", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, hookDelegate.Module.TypeSystem.Void)
        {
            IsRuntime = true
        };
        invoke.Parameters.Add(new("sender", ParameterAttributes.None, hookDelegate.GenericParameters[0]));
        invoke.Parameters.Add(new("args", ParameterAttributes.None, hookDelegate.GenericParameters[1]));
        hookDelegate.Methods.Add(invoke);

        var iAsyncResult = modder.ResolveTypeReference<IAsyncResult>();
        var iAsyncCallback = modder.ResolveTypeReference<AsyncCallback>();

        MethodDefinition beginInvoke = new("BeginInvoke", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, iAsyncResult)
        {
            IsRuntime = true
        };
        beginInvoke.Parameters.Add(new("sender", ParameterAttributes.None, hookDelegate.GenericParameters[0]));
        beginInvoke.Parameters.Add(new("args", ParameterAttributes.None, hookDelegate.GenericParameters[1]));
        beginInvoke.Parameters.Add(new("callback", ParameterAttributes.None, iAsyncCallback));
        beginInvoke.Parameters.Add(new("object", ParameterAttributes.None, hookDelegate.Module.TypeSystem.Object));
        hookDelegate.Methods.Add(beginInvoke);

        MethodDefinition endInvoke = new("EndInvoke", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, hookDelegate.Module.TypeSystem.Void)
        {
            IsRuntime = true
        };
        endInvoke.Parameters.Add(new("result", ParameterAttributes.None, iAsyncResult));
        hookDelegate.Methods.Add(endInvoke);


        return hookDelegate;
    }

    /// <summary>
    /// Creates an event in the hook type for the method.
    /// </summary>
    /// <param name="hookType">The type to receive the method being created</param>
    /// <param name="eventField">The event to fire</param>
    /// <param name="hookEventArgsType">The event args</param>
    /// <param name="modder">The monomodder instance</param>
    /// <param name="instanceType">Optional/Nullable instance type</param>
    /// <param name="originalMethodDelegate">The delegate for the original method</param>
    /// <param name="name">Optional name to call the method</param>
    /// <returns>The definition of the invoke method that was created</returns>
    static MethodDefinition CreateInvokeMethod(
        TypeDefinition hookType,
        FieldDefinition eventField,
        TypeDefinition hookEventArgsType,
        MonoModder modder,
        TypeReference? instanceType,
        TypeDefinition originalMethodDelegate,
        string? name = null)
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
        while (hookEventArgsType.Fields.Any(x => x.Name == senderFieldName))
            senderFieldName = "_" + senderFieldName;
        ParameterDefinition senderParam = new(senderFieldName, ParameterAttributes.None, instanceType ?? hookType.Module.TypeSystem.Object);
        invokeMethod.Parameters.Add(senderParam);

        // add the original method delegate
        var loweredOriginalMethodName = OriginalMethodName[..1].ToLower() + OriginalMethodName[1..];
        ParameterDefinition originalMethodParameter = new(loweredOriginalMethodName, ParameterAttributes.None, originalMethodDelegate);
        invokeMethod.Parameters.Add(originalMethodParameter);

        // instead of an event args, intake the parameters so each call doesnt need to new up itself.
        foreach (var field in hookEventArgsType.Fields.Where(x => !ExcludeNames.Contains(x.Name)))
        {
            var fieldType = field.FieldType is ByReferenceType byRef ? byRef.ElementType : field.FieldType;
            ParameterDefinition prm = new(field.Name, ParameterAttributes.None, fieldType);
            invokeMethod.Parameters.Add(prm);
        }

        // Create a GenericInstanceType for EventHandler<HookEventArgsType>
        var eventHandlerType = instanceType is not null ? GetOrCreateHookDelegate(modder) : modder.ResolveTypeReference(typeof(EventHandler<>));
        GenericInstanceType genericEventHandlerType = new(eventHandlerType)
        {
            GenericArguments = { hookEventArgsType }
        };

        if (instanceType is not null)
            genericEventHandlerType.GenericArguments.Insert(0, instanceType);

        // Import the "Invoke" method of EventHandler<HookEventArgsType>
        var eventHandlerInvokeMethod = eventHandlerType.Resolve().Methods.First(m => m.Name == "Invoke");
        MethodReference invokeMethodReference = new(
            eventHandlerInvokeMethod.Name,
            hookType.Module.TypeSystem.Void,
            genericEventHandlerType
        )
        {
            HasThis = true
        };

        // Add parameters to the invokeMethodReference
        invokeMethodReference.Parameters.Add(new(instanceType is not null ?
            eventHandlerInvokeMethod.Parameters[0].ParameterType :
            hookType.Module.TypeSystem.Object)); // sender
        invokeMethodReference.Parameters.Add(new(eventHandlerInvokeMethod.Parameters[1].ParameterType)); // args  - see EventHandler<>.Invoke, il is !0

        // Generate IL for the Invoke method
        var il = invokeMethod.Body.GetILProcessor();
        var returnLabel = il.Create(OpCodes.Ldloc_0);

        // Create the event args instance from the method parameters
        VariableDefinition vrb = new(hookEventArgsType);
        invokeMethod.Body.Variables.Add(vrb);
        invokeMethod.Body.InitLocals = true;
        il.Emit(OpCodes.Newobj, hookEventArgsType.Methods.Single(x => x.Name == ".ctor")); // Create a new instance of the event args

        il.Emit(OpCodes.Dup);                               // Load the event args instance
        il.Emit(OpCodes.Ldarg, originalMethodParameter);    // Load the parameter
        il.Emit(OpCodes.Stfld, hookEventArgsType.Fields.Single(x => x.Name == OriginalMethodName)); // Set the field

        // Set the fields of the event args instance
        foreach (var prm in invokeMethod.Parameters.Skip(1 /*sender*/ + 1 /*method*/))
        {
            il.Emit(OpCodes.Dup);                           // Load the event args instance
            il.Emit(OpCodes.Ldarg, prm);                    // Load the parameter
            il.Emit(OpCodes.Stfld, hookEventArgsType.Fields.Single(x => x.Name == prm.Name)); // Set the field
        }
        il.Emit(OpCodes.Stloc_0);                           // Store the event args instance

        // Check if the event is not null
        il.Emit(OpCodes.Ldsfld, eventField);                // Load the static event field
        il.Emit(OpCodes.Brfalse, returnLabel);              // If null, skip invocation

        // Invoke the event delegate
        il.Emit(OpCodes.Ldsfld, eventField);                // Load the static event field
        il.Emit(OpCodes.Ldarg_0);                           // Load the sender
        il.Emit(OpCodes.Ldloc_0);                           // Load the event args instance
        il.Emit(OpCodes.Callvirt, invokeMethodReference);   // Call the Invoke method on the delegate

        // Return args
        il.Append(returnLabel);                             // Return the event args (Ldloc_0 from earlier)
        il.Emit(OpCodes.Ret);

        // Add the Invoke method to the type
        hookType.Methods.Add(invokeMethod);

        return invokeMethod;
    }

    /// <summary>
    /// Creates a replacement method for the original method.
    /// </summary>
    /// <param name="original">The definition of the original method</param>
    /// <param name="eventInvoke">The events invoke method definition</param>
    /// <param name="name">Optional/Nullable name for the created method</param>
    /// <returns>The newly created methods definition</returns>
    static MethodDefinition CreateReplacement(MethodDefinition original, MethodDefinition eventInvoke, string? name = null)
    {
        var eventArgs = eventInvoke.ReturnType.Resolve();
        var hookReturnValueField = eventArgs.Fields.SingleOrDefault(x => x.Name == HookReturnValueName);
        var originalMethodField = eventArgs.Fields.Single(x => x.Name == OriginalMethodName);

        MethodDefinition methodDefinition = new(
            name ?? original.Name,
            original.Attributes,
            original.ReturnType
        );

        foreach (var param in original.Parameters)
            methodDefinition.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));

        var il = methodDefinition.Body.GetILProcessor();

        VariableDefinition eventArgsVariable = new(eventInvoke.ReturnType);
        methodDefinition.Body.Variables.Add(eventArgsVariable);

        // if any out parameters, initialise them with default values
        foreach (var param in methodDefinition.Parameters.Where(x => x.IsOut))
        {
            var type = param.ParameterType.GetElementType();
            il.Emit(OpCodes.Ldarg_S, param);
            var defaultValue = CreateDefaultValueInstruction(type);
            il.Append(defaultValue);
            if (defaultValue.OpCode != OpCodes.Initobj)
                il.Append(CreateStoreIndirectFunction(type));
        }

        // load the sender onto the stack
        il.Emit(original.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0);
        // load the original method onto the stack
        il.Emit(original.IsStatic ? OpCodes.Ldnull : OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, original);
        il.Emit(OpCodes.Newobj, originalMethodField.FieldType.Resolve().Methods.Single(x => x.Name == ".ctor"));

        for (int i = 0; i < methodDefinition.Parameters.Count; i++)
        {
            var isByRef = methodDefinition.Parameters[i].ParameterType.IsByReference;
            var opCode = isByRef ? OpCodes.Ldarg_S : OpCodes.Ldarg;
            il.Emit(opCode, methodDefinition.Parameters[i]);
            if (isByRef)
                il.Append(CreateLoadIndirectInstruction(methodDefinition.Parameters[i].ParameterType.GetElementType()));
        }
        il.Emit(OpCodes.Call, eventInvoke);

        // store the event args in a local variable
        il.Emit(OpCodes.Stloc, eventArgsVariable);

        // if any out parameters, set them from the args
        foreach (var param in methodDefinition.Parameters.Where(x => x.ParameterType.IsByReference))
        {
            var field = eventArgs.Fields.Single(x => x.Name == param.Name);
            il.Emit(OpCodes.Ldarg_S, param);
            il.Emit(OpCodes.Ldloc, eventArgsVariable);
            il.Emit(OpCodes.Ldfld, field);
            il.Append(CreateStoreIndirectFunction(field.FieldType.GetElementType()));
        }

        // use ContinueExecutionName to determine whether to continue or not
        il.Emit(OpCodes.Ldloc, eventArgsVariable);
        il.Emit(OpCodes.Ldfld, eventInvoke.ReturnType.Resolve().Fields.Single(x => x.Name == ContinueExecutionName));

        // the event invoke is a boolen, if false, return else invoke the original method
        var returnLabel = hookReturnValueField is not null ? il.Create(OpCodes.Ldloc, eventArgsVariable) : il.Create(OpCodes.Ret);
        il.Emit(OpCodes.Brfalse, returnLabel);

        if (!original.IsStatic)
            il.Emit(OpCodes.Ldarg_0);
        // for each event arg field, load it onto the stack to the original method
        foreach (var field in eventArgs.Fields.Where(x => !ExcludeNames.Contains(x.Name)))
        {
            var parameter = methodDefinition.Parameters.Single(x => x.Name == field.Name);
            // if byref, pass through original args which are updated by the event
            if (parameter.ParameterType.IsByReference)
            {
                il.Emit(OpCodes.Ldarg_S, parameter);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, eventArgsVariable);
                il.Emit(OpCodes.Ldfld, field);
            }
        }
        il.Emit(OpCodes.Call, original.Module.ImportReference(original));
        if (hookReturnValueField is not null)
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
    /// <param name="definition">The definition of the method to be hooked</param>
    /// <param name="modder">The monomodder instance</param>
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

        var hookEventArgs = CreateHookEventArgs(hookType, definition, modder, name: $"{uniqueName}EventArgs");

        // create a method to call the original method
        var originalMethodDelegate = GetOrCreateOriginalMethodDelegate(modder, definition, hookEventArgs);
        hookEventArgs.Fields.Add(new FieldDefinition(OriginalMethodName, FieldAttributes.Public, originalMethodDelegate));

        var (hookField, _) = definition.CreateEvent(hookType, hookEventArgs, modder, name: uniqueName);
        var newMethod = CreateInvokeMethod(hookType, hookField, hookEventArgs, modder, definition.IsStatic ? null : definition.DeclaringType, originalMethodDelegate, name: $"Invoke{uniqueName}");

        var replacement = CreateReplacement(definition, newMethod, name: $"{HookMethodNamePrefix}{definition.Name}");
        definition.DeclaringType.Methods.Add(replacement);

        // remove any overrides etc
        replacement.Attributes &= ~MethodAttributes.Virtual;
        replacement.Attributes &= ~MethodAttributes.NewSlot;
        replacement.Attributes &= ~MethodAttributes.SpecialName;

        // swap bodies, much easier this way...

        // swap the method references
        foreach (var instr in replacement.Body.Instructions)
        {
            if (instr.Operand is MethodReference mr && mr.Name == definition.Name)
                instr.Operand = replacement;
        }

        // then the bodies
        var temp = definition.Body;
        definition.Body = replacement.Body;
        replacement.Body = temp;
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
            !(definition.Events.Any(evt => evt.AddMethod == x || evt.RemoveMethod == x || evt.InvokeMethod == x)) &&
            // not generic instance (TODO)
            !x.HasGenericParameters
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
