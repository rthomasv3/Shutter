namespace AllPlatformScreenshots.Enums;

/// <summary>
/// Enum used to define fallback behavior when features are not supported on a given platform.
/// </summary>
public enum FallbackBehavior
{
    /// <summary>
    /// Strict - fail if exact request can't be fulfilled
    /// </summary>
    ThrowException,

    /// <summary>
    /// Fall back to fullscreen
    /// </summary>
    Default,

    /// <summary>
    /// Try to get closest possible (e.g., window → fullscreen)
    /// </summary>
    BestEffort
}
