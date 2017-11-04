using Mod.Framework.Extensions;
using Mono.Cecil;
using System.Linq;

namespace Mod.Framework.Emitters
{
	/// <summary>
	/// This emitter will produce a hook delegate that is generated as per the <see cref="HookOptions"/>.
	/// </summary>
	public class HookDelegateEmitter : IEmitter<TypeDefinition>
	{
		private string _prefix;
		private MethodDefinition _method;
		private HookOptions _flags;

		public HookDelegateEmitter(string prefix, MethodDefinition method, HookOptions flags)
		{
			this._prefix = prefix;
			this._method = method;
			this._flags = flags;
		}

		public TypeDefinition Emit()
		{
			var delegate_parameters = _method.Parameters
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
				&& _method.ReturnType != _method.DeclaringType.Module.TypeSystem.Void
			)
			{
				delegate_parameters.Add(new ParameterDefinition(
					"result",
					ParameterAttributes.None,
					(_flags & HookOptions.ReferenceParameters) != 0 ?
						new ByReferenceType(_method.ReturnType) : _method.ReturnType
				));
			}

			TypeReference return_type = (_flags & HookOptions.Cancellable) != 0 ?
				_method.DeclaringType.Module.TypeSystem.Boolean
				: _method.DeclaringType.Module.TypeSystem.Void;

			var delegate_emitter = new DelegateEmitter(
				_prefix + _method.GetSafeName(),
				return_type,
				delegate_parameters,
				_method.DeclaringType.Module
			);

			return delegate_emitter.Emit();
		}
	}
}
