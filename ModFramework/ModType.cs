namespace ModFramework;

/// <summary>
/// Defines when the mod is ran
/// </summary>
public enum ModType
{
    /// <summary>
    /// Occurs when the binary is about to be read and patched
    /// </summary>
    PreRead,

    /// <summary>
    /// Occurs after the binary is read
    /// </summary>
    Read,

    /// <summary>
    /// Occurs before MonoMod starts merging binaries together
    /// </summary>
    PreMerge,

    /// <summary>
    /// Occurs after MonoMod starts merging binaries together
    /// </summary>
    PostMerge,

    /// <summary>
    /// Occurs before MonoMod applies patches
    /// </summary>
    PrePatch,

    /// <summary>
    /// Occurs after MonoMod has completed processing all patches
    /// </summary>
    /// <remarks>Avoid modifying IL in this event as it will be applied after any relinking has occurred</remarks>
    PostPatch,

    /// <summary>
    /// Occurs when the patched binary has started
    /// </summary>
    Runtime,

    /// <summary>
    /// Occurs before the assembly is written to either a steam or file path.
    /// </summary>
    PreWrite,

    /// <summary>
    /// Occurs when the patched binary has been written to either a steam or file path.
    /// </summary>
    Write,

    /// <summary>
    /// Occurs when modules need to clean up and prepare for shutting down/Dispose.
    /// </summary>
    Shutdown,

    /// <summary>
    /// Occurs before MonoMod maps all dependencies
    /// </summary>
    PreMapDependencies,

    /// <summary>
    /// Occurs after MonoMod maps all dependencies
    /// </summary>
    PostMapDependencies,
}
