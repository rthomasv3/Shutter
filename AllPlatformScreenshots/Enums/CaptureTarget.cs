namespace AllPlatformScreenshots.Enums;

/// <summary>
/// Enum used to define the screenshot capture target.
/// </summary>
public enum CaptureTarget
{
    /// <summary>
    /// Default fullscreen
    /// </summary>
    FullScreen,

    /// <summary>
    /// Requires WindowHandle
    /// </summary>
    Window,

    /// <summary>
    /// Requires DisplayIndex
    /// </summary>
    Display,

    /// <summary>
    /// Requires Region
    /// </summary>
    Region
}
