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
        public override void Registered()
        {
            base.Registered();
            OnInit();
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
                    FixType(sa.AttributeType);

                    foreach (var prop in sa.Properties)
                        FixType(prop.Argument.Type);

                    foreach (var fld in sa.Fields)
                        FixType(fld.Argument.Type);
                }
            }
        }

        void FixType(TypeReference type)
        {
            if (type.IsNested)
            {
                FixType(type.DeclaringType);
            }
            else if (type is TypeSpecification ts)
            {
                FixType(ts.ElementType);
            }
            else if (type is GenericParameter gp)
            {
                FixAttributes(gp.CustomAttributes);

                foreach (var prm in gp.GenericParameters)
                    FixType(prm);
            }
            else
            {
                RelinkType(type);
            }
        }

        public abstract void RelinkType(TypeReference typeReference);

        public void Relink(Instruction instr)
        {
            if (instr.Operand is MethodReference mref)
            {
                if (mref is GenericInstanceMethod gim)
                    FixType(gim.ElementMethod.DeclaringType);
                else
                    FixType(mref.DeclaringType);

                FixType(mref.ReturnType);

                foreach (var prm in mref.Parameters)
                {
                    FixType(prm.ParameterType);
                    FixAttributes(prm.CustomAttributes);
                }
            }
            else if (instr.Operand is FieldReference fref)
            {
                FixType(fref.DeclaringType);
                FixType(fref.FieldType);
            }
            else if (instr.Operand is TypeSpecification ts)
            {
                FixType(ts.ElementType);
            }
            else if (instr.Operand is TypeReference tr)
            {
                FixType(tr);
            }
            else if (instr.Operand is VariableDefinition vd)
            {
                FixType(vd.VariableType);
            }
            else if (instr.Operand is ParameterDefinition pd)
            {
                FixType(pd.ParameterType);
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
                FixType(type.BaseType);
        }

        public override void Relink(EventDefinition typeEvent)
        {
            base.Relink(typeEvent);
            FixType(typeEvent.EventType);
        }

        public override void Relink(FieldDefinition field)
        {
            base.Relink(field);
            FixType(field.FieldType);
            FixAttributes(field.CustomAttributes);
        }

        void FixAttributes(Collection<CustomAttribute> attributes)
        {
            foreach (var attr in attributes)
            {
                FixType(attr.AttributeType);

                foreach (var ca in attr.ConstructorArguments)
                    FixType(ca.Type);

                foreach (var fld in attr.Fields)
                {
                    FixType(fld.Argument.Type);
                }

                foreach (var prop in attr.Properties)
                    FixType(prop.Argument.Type);
            }
        }

        public override void Relink(MethodDefinition method)
        {
            base.Relink(method);

            foreach (var prm in method.Parameters)
            {

                FixType(prm.ParameterType);
                FixAttributes(prm.CustomAttributes);
            }

            FixAttributes(method.CustomAttributes);
        }

        public override void Relink(MethodDefinition method, ParameterDefinition parameter)
        {
            base.Relink(method, parameter);
            FixType(parameter.ParameterType);
        }

        public override void Relink(MethodDefinition method, VariableDefinition variable)
        {
            base.Relink(method, variable);
            FixType(variable.VariableType);
        }

        public override void Relink(PropertyDefinition property)
        {
            base.Relink(property);
            FixType(property.PropertyType);
        }
    }
}

