namespace Mod.Framework.Emitters
{
	/// <summary>
	/// Defines the basic functionality of an emitter
	/// </summary>
	/// <typeparam name="TOutput"></typeparam>
	public interface IEmitter<TOutput>
	{
		TOutput Emit();
	}
}
