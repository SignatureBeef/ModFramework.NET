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
using System;
using System.Linq;

namespace ModFramework.Relinker;

public static partial class Extensions
{
    public static void AddTask<T>(this ModFwModder modder, Type externalType)
        where T : HookExternalTypeRelinker
    {
        modder.AddTask<T>(externalType);
    }
}

[MonoMod.MonoModIgnore]
public class HookExternalTypeRelinker<TType>(ModFwModder modder) : HookExternalTypeRelinker(modder, typeof(TType));

[MonoMod.MonoModIgnore]
public class HookExternalTypeRelinker(ModFwModder modder, Type externalType) : RelinkTask(modder)
{
    MethodDefinition GetOrCreateHookMethod(MethodReference method)
    {
        var definition = method.Resolve();
        var uniqueName = HookEmitter.GetUniqueName(definition);
        var hookTypeName = "HookEvents." + definition.DeclaringType.FullName;
        var hookType = Modder.NewTypes.SingleOrDefault(x => x.FullName == hookTypeName) ?? HookEmitter.GetOrCreateHookType(definition.DeclaringType, Modder.Module, add: false);

        if (!Modder.NewTypes.Contains(hookType))
        {
            Modder.NewTypes.Add(hookType);
        }

        var hookEventArgs = HookEmitter.CreateHookEventArgs(hookType, definition, modder, name: $"{uniqueName}EventArgs");

        // create a method to call the original method
        var originalMethodDelegate = HookEmitter.GetOrCreateOriginalMethodDelegate(modder, definition, hookEventArgs);
        hookEventArgs.Fields.Add(new FieldDefinition(HookEmitter.OriginalMethodName, FieldAttributes.Public, originalMethodDelegate));

        var (hookField, _) = definition.CreateEvent(hookType, hookEventArgs, modder, name: uniqueName);
        var newMethod = HookEmitter.CreateInvokeMethod(hookType, hookField, hookEventArgs, modder, definition.IsStatic ? null : definition.DeclaringType, originalMethodDelegate, name: $"Invoke{uniqueName}");
        hookType.Methods.Add(newMethod);

        var replacement = HookEmitter.CreateReplacement(definition, newMethod, name: $"{HookEmitter.HookMethodNamePrefix}{definition.Name}", module: Modder.Module);
        hookType.Methods.Add(replacement);

        return replacement;
    }

    public override void Relink(MethodBody body, Instruction instr)
    {
        if (instr.Operand is MethodReference methodRef)
        {
            var declaringType = methodRef.DeclaringType;
            // todo nested?
            if (declaringType.FullName == externalType.FullName)
            {
                var hookMethod = GetOrCreateHookMethod(methodRef);
                instr.Operand = hookMethod;
            }
        }
    }

    public override void PreWrite()
    {
        base.PreWrite();
    }
}
