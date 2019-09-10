using Mono.Cecil;
using System;
using System.Collections.Generic;

namespace Mod.Framework.Emitters
{
	/// <summary>
	/// This emitter will produce a delegate that can be attached as a nested type to any module
	/// </summary>
	public class DelegateEmitter : IEmitter<TypeDefinition>
	{
		private ModuleDefinition _module;
		private TypeReference _returnType;
		private string _name;
		private IEnumerable<ParameterDefinition> _parameters;

		private TypeReference t_iasyncresult;
		private TypeReference t_asynccallback;

		const MethodAttributes InvokeAttributes =
				MethodAttributes.Public
				| MethodAttributes.HideBySig
				| MethodAttributes.Virtual
				| MethodAttributes.VtableLayoutMask;

		public DelegateEmitter(string name, TypeReference returnType, IEnumerable<ParameterDefinition> parameters, ModuleDefinition module)
		{
			this._name = name;
			this._returnType = returnType;
			this._parameters = parameters;
			this._module = module;

			this.t_iasyncresult = module.ImportReference(typeof(IAsyncResult));
			this.t_asynccallback = module.ImportReference(typeof(AsyncCallback));
		}

		private void EmitConstructor(TypeDefinition delegateType)
		{
			var constructor = new MethodDefinition(
				".ctor",
				MethodAttributes.Public
				| MethodAttributes.HideBySig
				| MethodAttributes.SpecialName
				| MethodAttributes.RTSpecialName,
				this._module.TypeSystem.Void
			);
			constructor.Parameters.Add(new ParameterDefinition(
				"object",
				ParameterAttributes.None,
				this._module.TypeSystem.Object
			));
			constructor.Parameters.Add(new ParameterDefinition(
				"method",
				ParameterAttributes.None,
				this._module.TypeSystem.IntPtr
			));
			constructor.ImplAttributes = MethodImplAttributes.Runtime;

			delegateType.Methods.Add(constructor);
		}

		private void EmitInvoke(TypeDefinition delegateType)
		{
			var method = new MethodDefinition("Invoke", InvokeAttributes, this._returnType);

			foreach (var parameter in this._parameters)
			{
				method.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
			}

			method.ImplAttributes = MethodImplAttributes.Runtime;
			delegateType.Methods.Add(method);
		}

		private void EmitBeginInvoke(TypeDefinition delegateType)
		{
			var method = new MethodDefinition("BeginInvoke", InvokeAttributes, this._module.TypeSystem.Void);

			foreach (var parameter in this._parameters)
			{
				method.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
			}

			method.Parameters.Add(new ParameterDefinition("callback", ParameterAttributes.None, this.t_asynccallback));
			method.Parameters.Add(new ParameterDefinition("object", ParameterAttributes.None, this._module.TypeSystem.Object));
			method.ImplAttributes = MethodImplAttributes.Runtime;

			delegateType.Methods.Add(method);
		}

		private void EmitEndInvoke(TypeDefinition delegateType)
		{
			var method = new MethodDefinition("EndInvoke", InvokeAttributes, this._returnType);

			method.Parameters.Add(new ParameterDefinition("result", ParameterAttributes.None, this.t_iasyncresult));
			method.ImplAttributes = MethodImplAttributes.Runtime;

			delegateType.Methods.Add(method);
		}

		public TypeDefinition Emit()
		{
			var type = new TypeDefinition(String.Empty, this._name, TypeAttributes.NestedPublic | TypeAttributes.Sealed);

			type.BaseType = this._module.ImportReference(typeof(MulticastDelegate));

			EmitConstructor(type);
			EmitInvoke(type);
			EmitBeginInvoke(type);
			EmitEndInvoke(type);

			return type;
		}
	}
}
