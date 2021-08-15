namespace ModFramework
{
    /// <summary>
    /// Defines when the mod is ran
    /// </summary>
    public enum ModType
    {
        /// <summary>
        /// Occurs when the binary is about to be read and patched
        /// </summary>
        Read,

        /// <summary>
        /// Occurs before MonoMod starts merging binaries together
        /// </summary>
        PreMerge,

        /// <summary>
        /// Occurs before MonoMod applies patches
        /// </summary>
        PrePatch,

        /// <summary>
        /// Occurs after MonoMod has completed processing all patches
        /// </summary>
        PostPatch,

        /// <summary>
        /// Occurs when the patched binary has started
        /// </summary>
        Runtime,

        /// <summary>
        /// Occurs when the patched binary has been written to either a steam or file path.
        /// </summary>
        Write,

        /// <summary>
        /// Occurs when modules need to clean up and prepare for shutting down/Dispose.
        /// </summary>
        Shutdown,
    }
}
