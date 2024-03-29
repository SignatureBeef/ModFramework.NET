﻿/*
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
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;

namespace ModFramework.Relinker;

[MonoMod.MonoModIgnore]
public abstract class RelinkTask : IDisposable
{
    private bool disposedValue;

    public ModFwModder Modder { get; set; }
    public virtual int Order { get; set; } = 100;

    public virtual void Registered() { }
    public virtual void PreWrite() { }

    public RelinkTask(ModFwModder modder)
    {
        Modder = modder;
    }

    public virtual void Relink(TypeDefinition type) { }
    public virtual void Relink(MethodDefinition method) { }
    public virtual void Relink(MethodDefinition method, VariableDefinition variable) { }
    public virtual void Relink(MethodDefinition method, ParameterDefinition parameter) { }
    public virtual void Relink(FieldDefinition field) { }
    public virtual void Relink(PropertyDefinition property) { }
    public virtual void Relink(MethodBody body, Instruction instr) { }
    public virtual void Relink(EventDefinition typeEvent) { }

    protected virtual void Cleanup() { }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
                Cleanup();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~RelinkTask()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

[MonoMod.MonoModIgnore]
public interface IRelinkProvider
{
    bool AllowInterreferenceReplacements { get; set; }
    void AddTask(RelinkTask task);
    IEnumerable<RelinkTask> Tasks { get; }
}
