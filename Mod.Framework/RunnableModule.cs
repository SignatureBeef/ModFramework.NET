namespace Mod.Framework
{
	/// <summary>
	/// Provides the base class for modifications that is ran by the framework
	/// </summary>
	public abstract class RunnableModule : Module
	{
		public abstract void Run();
	}
}
