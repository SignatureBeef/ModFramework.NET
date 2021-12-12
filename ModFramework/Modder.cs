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
using MonoMod;
using System;
using System.Collections.Generic;
using System.IO;

namespace ModFramework
{
    [MonoMod.MonoModIgnore]
    public class ModFwModder : MonoMod.MonoModder, IRelinkProvider
    {
        public bool AllowInterreferenceReplacements { get; set; } = true;
        protected List<RelinkTask> TaskList { get; set; } = new List<RelinkTask>();
        public IEnumerable<RelinkTask> Tasks => TaskList;

        public event MethodRewriter? OnRewritingMethod;
        public event MethodBodyRewriter? OnRewritingMethodBody;

        public MarkdownDocumentor? MarkdownDocumentor { get; set; }

        public bool EnableWriteEvents { get; set; }
        public bool Silent { get; set; } = true;

        public new DefaultAssemblyResolver AssemblyResolver
        {
            get => (DefaultAssemblyResolver)base.AssemblyResolver;
            set => base.AssemblyResolver = value;
        }

        public virtual void AddTask(RelinkTask task)
        {
            task.Modder = this;
            task.RelinkProvider = this;
            TaskList.Add(task);
            TaskList.Sort((a, b) => a.Order - b.Order);
            task.Registered();
        }

        public virtual void RunTasks(Action<RelinkTask> callback)
        {
            foreach (var task in TaskList)
                callback(task);
        }

        public override void Log(string text)
        {
            if (!Silent)
                base.Log(text);
        }

        public ModFwModder()
        {
            MethodParser = (MonoModder modder, MethodBody body, Instruction instr, ref int instri) => true;
            MethodRewriter = (MonoModder modder, MethodDefinition method) =>
            {
                if (method.Body?.HasVariables == true)
                    foreach (var variable in method.Body.Variables)
                        RunTasks(t => t.Relink(method, variable));

                if (method.HasParameters)
                    foreach (var parameter in method.Parameters)
                        RunTasks(t => t.Relink(method, parameter));

                OnRewritingMethod?.Invoke(modder, method);
            };
            MethodBodyRewriter = (MonoModder modder, MethodBody body, Instruction instr, int instri) =>
            {
                RunTasks(t => t.Relink(body, instr));
                OnRewritingMethodBody?.Invoke(modder, body, instr, instri);
            };

            AddTask(new EventDelegateRelinker());
        }

        public override void Read()
        {
            base.Read();

            // bit of a hack, but saves having to roll our own and having to try/catch the shit out of it (which actually drags out
            // patching by 5-10 minutes!)
            // this just reuses the Mono.Cecil cache to resolve our main assembly instead of reimporting a new module
            // which lets some patches fail to relink due to an unimported module...which should be valid since
            // its the same assembly, but it's lost during ImportReference calls
            {
                var cache = (Dictionary<string, AssemblyDefinition>)
                        ((DefaultAssemblyResolver)this.AssemblyResolver).GetType()
                        .GetField("cache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .GetValue(this.AssemblyResolver)!;
                cache.Add(this.Module.Assembly.FullName, this.Module.Assembly);
            }

            Modifier.Apply(ModType.Read, this);
        }

        public override void PatchRefs()
        {
            Modifier.Apply(ModType.PreMerge, this);
            base.PatchRefs();
        }

        public override void PatchRefsInType(TypeDefinition type)
        {
            base.PatchRefsInType(type);

            RunTasks(t => t.Relink(type));

            if (type.HasEvents)
                foreach (var typeEvent in type.Events)
                    RunTasks(t => t.Relink(typeEvent));

            if (type.HasFields)
                foreach (var field in type.Fields)
                    RunTasks(t => t.Relink(field));

            if (type.HasProperties)
                foreach (var property in type.Properties)
                    RunTasks(t => t.Relink(property));
        }

        public override void PatchRefsInMethod(MethodDefinition method)
        {
            base.PatchRefsInMethod(method);

            RunTasks(t => t.Relink(method));
        }

        public override void AutoPatch()
        {
            Modifier.Apply(ModType.PrePatch, this);

            base.AutoPatch();

            Modifier.Apply(ModType.PostPatch, this);

            foreach (var relinked in RelinkModuleMap)
            {
                // remove the references
                foreach (var asmref in Module.AssemblyReferences.ToArray())
                    if (asmref.Name.Equals(relinked.Key))
                        Module.AssemblyReferences.Remove(asmref);
            }
        }

        public void RelinkAssembly(string fromAssemblyName, ModuleDefinition? toModule = null)
        {
            this.RelinkModuleMap[fromAssemblyName] = toModule ?? this.Module;
        }

        public override void Write(Stream? output = null, string? outputPath = null)
        {
            base.Write(output, outputPath);

            if (EnableWriteEvents)
                Modifier.Apply(ModType.Write, this);
        }

        public override void Dispose()
        {
            foreach(var task in TaskList)
                task.Dispose();
            TaskList.Clear();

            Modifier.Apply(ModType.Shutdown, this);
            base.Dispose();
        }
    }
}
