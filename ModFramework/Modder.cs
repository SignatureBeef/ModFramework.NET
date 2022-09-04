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
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RF = System.Reflection;

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

        //public bool EnableWriteEvents { get; set; }
        public bool Silent { get; set; } = true;

        public ModContext ModContext { get; set; }

        public new DefaultAssemblyResolver AssemblyResolver
        {
            get => (DefaultAssemblyResolver)base.AssemblyResolver;
            set => base.AssemblyResolver = value;
        }

        public virtual T AddTask<T>(params object[] param)
            where T : RelinkTask
        {
            IEnumerable<object> args = new[] { this };
            var task = (T?)Activator.CreateInstance(typeof(T),
                RF.BindingFlags.Public | RF.BindingFlags.NonPublic | RF.BindingFlags.Instance,
                null,
                args.Concat(param).ToArray(),
                CultureInfo.CurrentCulture
            );
            if (task is null) throw new Exception($"Failed to create type: {typeof(T).FullName}");
            AddTask(task);
            return task;
        }

        public virtual void AddTask(RelinkTask task)
        {
            task.Modder = this;
            //task.RelinkProvider = this;
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

        public ModFwModder(ModContext context)
        {
            ModContext = context;
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

            AddTask<EventDelegateRelinker>();
        }

        public override void MapDependencies()
        {
            ModContext.Apply(ModType.PreMapDependencies, this);
            base.MapDependencies();
            ModContext.Apply(ModType.PostMapDependencies, this);
        }

        public override void Read()
        {
            ModContext.Apply(ModType.PreRead, this);

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

            ModContext.Apply(ModType.Read, this);
        }

        public override void PatchRefs()
        {
            ModContext.Apply(ModType.PreMerge, this);
            base.PatchRefs();
            ModContext.Apply(ModType.PostMerge, this);
        }

        public override void PatchType(TypeDefinition type)
        {
            base.PatchType(type);
            RelinkType(type);
        }

        public override void PatchRefsInType(TypeDefinition type)
        {
            base.PatchRefsInType(type);
            RelinkType(type);
        }

        public override void PatchEvent(TypeDefinition targetType, EventDefinition srcEvent, HashSet<MethodDefinition>? propMethods = null)
        {
            base.PatchEvent(targetType, srcEvent, propMethods);

            EventDefinition targetEvent = targetType.FindEvent(srcEvent.Name);

            RunTasks(t => t.Relink(targetEvent));
            RunTasks(t => t.Relink(srcEvent));
        }

        private void RelinkType(TypeDefinition type)
        {
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

            if (type.HasMethods)
                foreach (var method in type.Methods)
                    RunTasks(t => t.Relink(method));
        }

        public override void PatchField(TypeDefinition targetType, FieldDefinition field)
        {
            base.PatchField(targetType, field);
            RunTasks(t => t.Relink(field));
        }

        public override void PatchRefsInMethod(MethodDefinition method)
        {
            base.PatchRefsInMethod(method);

            RunTasks(t => t.Relink(method));

            // pending: https://github.com/MonoMod/MonoMod/pull/92
            for (int i = 0; i < method.MethodReturnType.CustomAttributes.Count; i++)
                PatchRefsInCustomAttribute(method.MethodReturnType.CustomAttributes[i] = method.MethodReturnType.CustomAttributes[i].Relink(Relinker, method));
        }

        public override void AutoPatch()
        {
            ModContext.Apply(ModType.PrePatch, this);

            base.AutoPatch();

            ModContext.Apply(ModType.PostPatch, this);

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
            ModContext.Apply(ModType.PreWrite, this);

            RunTasks(t => t.PreWrite());

            base.Write(output, outputPath);

            ModContext.Apply(ModType.Write, this);
        }

        public override void Dispose()
        {
            ModContext.Apply(ModType.Shutdown, this);

            foreach (var task in TaskList)
                task.Dispose();
            TaskList.Clear();

            base.Dispose();
        }
    }
}
