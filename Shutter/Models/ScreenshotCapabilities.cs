namespace Shutter.Models;

/// <summary>
/// Describes the screenshot capabilities available on the current platform.
/// </summary>
public class ScreenshotCapabilities
{
    /// <summary>
    /// Gets or sets whether the platform supports capturing the entire screen.
    /// </summary>
    /// <remarks>
    /// Full screen capture is supported on all platforms (Windows, macOS, Linux/X11, Linux/Wayland).
    /// </remarks>
    public bool SupportsFullScreen { get; init; }

    /// <summary>
    /// Gets or sets whether the platform supports capturing specific windows.
    /// </summary>
    /// <remarks>
    /// Window capture is only supported on Windows and Linux/X11.
    /// Requires a valid window handle in <see cref="ScreenshotOptions.WindowHandle"/>.
    /// </remarks>
    public bool SupportsWindowCapture { get; init; }

    /// <summary>
    /// Gets or sets whether the platform supports capturing specific displays/monitors.
    /// </summary>
    /// <remarks>
    /// Display selection is supported on Windows, macOS, and Linux/X11.
    /// Not supported on Linux/Wayland due to security restrictions.
    /// </remarks>
    public bool SupportsDisplaySelection { get; init; }

    /// <summary>
    /// Gets or sets whether the platform supports capturing a specific rectangular region.
    /// </summary>
    /// <remarks>
    /// Region capture is supported on Windows, macOS, and Linux/X11.
    /// Not supported on Linux/Wayland due to security restrictions.
    /// </remarks>
    public bool SupportsRegionCapture { get; init; }

    /// <summary>
    /// Gets or sets whether the platform supports interactive selection mode.
    /// </summary>
    /// <remarks>
    /// Interactive mode is only supported on Linux/Wayland, where it presents
    /// a user interface for selecting what to capture. When enabled via
    /// <see cref="ScreenshotOptions.Interactive"/>, the user can choose the capture area.
    /// </remarks>
    public bool SupportsInteractiveMode { get; init; }

    /// <summary>
    /// Gets or sets whether the platform supports including or excluding window borders.
    /// </summary>
    /// <remarks>
    /// Border control is only supported on Windows. When supported, the
    /// <see cref="ScreenshotOptions.IncludeBorder"/> option controls whether
    /// window title bars and borders are included in window captures.
    /// </remarks>
    public bool SupportsBorderControl { get; init; }

    /// <summary>
    /// Gets or sets whether the platform supports including or excluding window shadows.
    /// </summary>
    /// <remarks>
    /// Shadow control is only supported on Windows. When supported, the
    /// <see cref="ScreenshotOptions.IncludeShadow"/> option controls whether
    /// drop shadows around windows are included in window captures.
    /// </remarks>
    public bool SupportsShadowControl { get; init; }

    /// <summary>
    /// Gets or sets whether the platform supports querying the number of available displays.
    /// </summary>
    /// <remarks>
    /// When true, <see cref="DisplayCount"/> will contain the number of displays.
    /// When false, <see cref="DisplayCount"/> will be null.
    /// </remarks>
    public bool SupportsDisplayCount { get; init; }

    /// <summary>
    /// Gets or sets the number of displays/monitors available on the system.
    /// </summary>
    /// <remarks>
    /// Contains the count of available displays when <see cref="SupportsDisplayCount"/> is true.
    /// Will be null when display enumeration is not supported on the platform.
    /// Display indices for <see cref="ScreenshotOptions.DisplayIndex"/> are zero-based,
    /// so valid indices range from 0 to DisplayCount - 1.
    /// </remarks>
    public int? DisplayCount { get; init; }
}
