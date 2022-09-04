namespace ModFramework;

/// <summary>
/// Defines a generalised order in which the mod is applied
/// </summary>
public enum ModPriority : int
{
    /// <summary>
    /// May run slightly earlier than other mods
    /// </summary>
    Early = -100,

    /// <summary>
    /// Default priority, no preference or requirements
    /// </summary>
    Default = 0,

    /// <summary>
    /// May run later than most mods
    /// </summary>
    Late = 50,

    /// <summary>
    /// May be one of the last mods to be ran
    /// </summary>
    Last = 100,
}
