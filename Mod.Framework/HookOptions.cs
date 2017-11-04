using System;

namespace Mod.Framework
{
	/// <summary>
	/// Hook options define how hooks are hooked, generated and handled at runtime
	/// </summary>
	[Flags]
	public enum HookOptions : byte
	{
		Default = Pre | Post | Cancellable | ReferenceParameters | AlterResult,
		None = 0,

		Pre = 1,
		Post = 2,

		ReferenceParameters = 4,
		AlterResult = 8,

		Cancellable = 16 // only applies to Pre hooks
	}
}
