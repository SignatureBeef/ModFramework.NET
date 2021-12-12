using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace ModFramework.Relinker
{
    [MonoMod.MonoModIgnore]
    public abstract class TypeRelinker : RelinkTask
    {
        private HashSet<TypeReference> NoChangeCache = new HashSet<TypeReference>();
        private Dictionary<TypeReference, TypeReference> ChangeCache = new Dictionary<TypeReference, TypeReference>();

        public override void Registered()
        {
            base.Registered();
            OnInit();
        }

        protected override void Cleanup()
        {
            base.Cleanup();
            ChangeCache.Clear();
            NoChangeCache.Clear();
        }

        protected virtual void OnInit()
        {
            if (Modder is null) throw new ArgumentNullException(nameof(Modder));
            FixAttributes(Modder.Module.Assembly.CustomAttributes);
            FixAttributes(Modder.Module.Assembly.MainModule.CustomAttributes);

            foreach (var sd in Modder.Module.Assembly.SecurityDeclarations)
            {
                foreach (var sa in sd.SecurityAttributes)
                {
                    CheckType(sa.AttributeType, nt => sa.AttributeType = nt);

                    foreach (var prop in sa.Properties)
                        CheckType(prop.Argument.Type);

                    foreach (var fld in sa.Fields)
                        CheckType(fld.Argument.Type);
                }
            }
        }

        bool CheckType<TRef>(TRef type, Action<TRef>? update = null)
            where TRef : TypeReference
        {
            if (NoChangeCache.Contains(type)) return false;

            if (ChangeCache.TryGetValue(type, out TypeReference? tref))
            {
                if (update is not null)
                    update((TRef)tref);
                return true;
            }

            bool changed = false;
            var original = type;

            //if (type.IsNested)
            //{
            //    changed |= CheckType(type.DeclaringType, nt => type.DeclaringType = nt);
            //}
            if (type is TypeSpecification ts)
            {
                CheckType(ts.ElementType);
            }
            else if (type is GenericParameter gp)
            {
                FixAttributes(gp.CustomAttributes);

                foreach (var prm in gp.GenericParameters)
                {
                    CheckType(prm);
                }
            }
            else if (type is GenericInstanceType genericInstanceType)
            {
                if (genericInstanceType.HasGenericArguments)
                    for (var i = 0; i < genericInstanceType.GenericArguments.Count; i++)
                    {
                        changed |= CheckType(
                            genericInstanceType.GenericArguments[i],
                            nr => genericInstanceType.GenericArguments[i] = nr
                        );
                    }
            }
            else if (type is ArrayType arrayType)
            {
                changed |= CheckType(arrayType.ElementType, ntype => type = (TRef)(object)new ArrayType(ntype, arrayType.Rank));
            }
            else if (type is ByReferenceType byRefType)
            {
                changed |= CheckType(byRefType.ElementType, ntype => type = (TRef)(object)new ByReferenceType(ntype));
            }
            else
            {
                // TODO determine if this is needed anymore. causes recursion issues but i dont see evidence its needed anymore
                //if (type.HasGenericParameters)
                //    for (int i = 0; i < type.GenericParameters.Count; i++)
                //    {
                //        changed |= CheckType(
                //            type.GenericParameters[i],
                //            nr => type.GenericParameters[i] = nr
                //        );
                //    }

                changed |= RelinkType(ref type);
            }

            if (changed)
                ChangeCache[original] = type;
            else
                NoChangeCache.Add(type);

            if (changed && update is not null)
                update(type);

            return changed;
        }

        public abstract bool RelinkType<TRef>(ref TRef typeReference) where TRef : TypeReference;

        public void Relink(Instruction instr)
        {
            if (instr.Operand is MethodReference mref)
            {
                if (mref is GenericInstanceMethod gim)
                    CheckType(gim.ElementMethod.DeclaringType, nt => gim.ElementMethod.DeclaringType = nt);
                else
                    CheckType(mref.DeclaringType, nt => mref.DeclaringType = nt);

                CheckType(mref.ReturnType, nt => mref.ReturnType = nt);

                foreach (var prm in mref.Parameters)
                {
                    CheckType(prm.ParameterType, nt => prm.ParameterType = nt);
                    FixAttributes(prm.CustomAttributes);
                }
            }
            else if (instr.Operand is FieldReference fref)
            {
                CheckType(fref.DeclaringType, nt => fref.DeclaringType = nt);
                CheckType(fref.FieldType, nt => fref.FieldType = nt);
            }
            else if (instr.Operand is TypeSpecification ts)
            {
                CheckType(ts.ElementType);
            }
            else if (instr.Operand is TypeReference tr)
            {
                CheckType(tr, nt => instr.Operand = nt);
            }
            else if (instr.Operand is VariableDefinition vd)
            {
                CheckType(vd.VariableType, nt => vd.VariableType = nt);
            }
            else if (instr.Operand is ParameterDefinition pd)
            {
                CheckType(pd.ParameterType, nt => pd.ParameterType = nt);
            }
            else if (instr.Operand is Instruction[] instructions)
            {
                foreach (var ins in instructions)
                    Relink(ins);
            }
            else if (!(
                instr.Operand is null
                || instr.Operand is Instruction
                || instr.Operand is Int16
                || instr.Operand is Int32
                || instr.Operand is Int64
                || instr.Operand is UInt16
                || instr.Operand is UInt32
                || instr.Operand is UInt64
                || instr.Operand is string
                || instr.Operand is byte
                || instr.Operand is sbyte
                || instr.Operand is Single
                || instr.Operand is Double
            ))
            {
                throw new NotSupportedException();
            }
        }

        public override void Relink(MethodBody body, Instruction instr)
        {
            base.Relink(body, instr);

            Relink(instr);
        }

        public override void Relink(TypeDefinition type)
        {
            base.Relink(type);

            if (type.BaseType != null)
                CheckType(type.BaseType, nt => type.BaseType = nt);

            foreach(var intf in type.Interfaces)
            {
                CheckType(intf.InterfaceType, nt => intf.InterfaceType = nt);
            }
        }

        public override void Relink(EventDefinition typeEvent)
        {
            base.Relink(typeEvent);
            CheckType(typeEvent.EventType, nt => typeEvent.EventType = nt);
        }

        public override void Relink(FieldDefinition field)
        {
            base.Relink(field);
            CheckType(field.FieldType, nt => field.FieldType = nt);
            FixAttributes(field.CustomAttributes);
        }

        void FixAttributes(Collection<CustomAttribute> attributes)
        {
            foreach (var attr in attributes)
            {
                CheckType(attr.AttributeType);

                foreach (var ca in attr.ConstructorArguments)
                    CheckType(ca.Type);

                foreach (var fld in attr.Fields)
                {
                    CheckType(fld.Argument.Type);
                }

                foreach (var prop in attr.Properties)
                    CheckType(prop.Argument.Type);
            }
        }

        public override void Relink(MethodDefinition method)
        {
            base.Relink(method);

            CheckType(method.DeclaringType, nt => method.DeclaringType = nt);
            CheckType(method.ReturnType, nt => method.ReturnType = nt);

            foreach (var prm in method.Parameters)
            {

                CheckType(prm.ParameterType, nt => prm.ParameterType = nt);
                FixAttributes(prm.CustomAttributes);
            }

            FixAttributes(method.CustomAttributes);
        }

        public override void Relink(MethodDefinition method, ParameterDefinition parameter)
        {
            base.Relink(method, parameter);
            CheckType(parameter.ParameterType, nt => parameter.ParameterType = nt);
        }

        public override void Relink(MethodDefinition method, VariableDefinition variable)
        {
            base.Relink(method, variable);
            CheckType(variable.VariableType, nt => variable.VariableType = nt);
        }

        public override void Relink(PropertyDefinition property)
        {
            base.Relink(property);
            CheckType(property.PropertyType, nt => property.PropertyType = nt);
        }
    }
}

