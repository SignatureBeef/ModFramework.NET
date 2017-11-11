using Mod.Framework.Extensions;
using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;

namespace Mod.Framework.Emitters
{
	/// <summary>
	/// This emitter will produce a hook delegate that is generated as per the <see cref="HookOptions"/>.
	/// </summary>
	public class HookDelegateEmitter : IEmitter<TypeDefinition>
	{
		private HookOptions _flags;
		private IEnumerable<ParameterDefinition> _parameters;
		private TypeReference _returnType;
		private string _name;
		private ModuleDefinition _module;

		public HookDelegateEmitter(string prefix, MethodDefinition method, HookOptions flags)
		{
			this._name = prefix + method.GetSafeName();
			this._flags = flags;
			this._parameters = method.Parameters;
			this._module = method.Module;
			this._returnType = method.ReturnType;
		}

		public HookDelegateEmitter(string name, IEnumerable<ParameterDefinition> parameters, TypeDefinition returnType, HookOptions flags, ModuleDefinition module)
		{
			this._name = name;
			this._parameters = parameters;
			this._flags = flags;
			this._module = module;
			this._returnType = returnType;
		}

		public TypeDefinition Emit()
		{
			var delegate_parameters = _parameters
				.Select(x => new ParameterDefinition(x.Name, x.Attributes, x.ParameterType))
				.ToList();

			if ((_flags & HookOptions.ReferenceParameters) != 0)
			{
				foreach (var param in delegate_parameters.Where(x => x.ParameterType.IsValueType))
				{
					param.ParameterType = new ByReferenceType(param.ParameterType);
				}
			}

			if (
				(_flags & HookOptions.AlterResult) != 0
				&& this._returnType != _module.TypeSystem.Void
			)
			{
				delegate_parameters.Add(new ParameterDefinition(
					"result",
					ParameterAttributes.None,
					(_flags & HookOptions.ReferenceParameters) != 0 ?
						new ByReferenceType(this._returnType) : this._returnType
				));
			}

			TypeReference return_type = (_flags & HookOptions.Cancellable) != 0 ?
				_module.TypeSystem.Boolean
				: this._returnType;

			var delegate_emitter = new DelegateEmitter(
				this._name,
				return_type,
				delegate_parameters,
				_module
			);

			return delegate_emitter.Emit();
		}
	}
}
