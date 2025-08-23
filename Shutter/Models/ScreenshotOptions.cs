using System;
using Shutter.Enums;

namespace Shutter.Models;

/// <summary>
/// Configuration options for taking screenshots.
/// </summary>
public class ScreenshotOptions
{
    /// <summary>
    /// Gets or sets the capture target type.
    /// Default is <see cref="CaptureTarget.FullScreen"/>.
    /// </summary>
    /// <remarks>
    /// Platform support:
    /// - FullScreen: All platforms
    /// - Window: Windows, X11
    /// - Display: Windows, macOS, X11
    /// - Region: Windows, macOS, X11
    /// </remarks>
    public CaptureTarget Target { get; set; } = CaptureTarget.FullScreen;

    /// <summary>
    /// Gets or sets the fallback behavior when a requested feature is not available on the current platform.
    /// Default is <see cref="FallbackBehavior.Default"/>.
    /// </summary>
    public FallbackBehavior Fallback { get; set; } = FallbackBehavior.Default;

    /// <summary>
    /// Gets or sets the window handle for window capture.
    /// Required when <see cref="Target"/> is <see cref="CaptureTarget.Window"/>.
    /// </summary>
    /// <remarks>
    /// On Windows, this should be an HWND.
    /// On X11, this should be a Window ID.
    /// Not supported on macOS or Wayland.
    /// </remarks>
    public IntPtr? WindowHandle { get; set; }

    /// <summary>
    /// Gets or sets the display/monitor index for display capture.
    /// Required when <see cref="Target"/> is <see cref="CaptureTarget.Display"/>.
    /// </summary>
    /// <remarks>
    /// Display indices are zero-based. The primary display is typically index 0.
    /// Not supported on Wayland.
    /// </remarks>
    public int? DisplayIndex { get; set; }

    /// <summary>
    /// Gets or sets whether to show an interactive selection UI.
    /// Only supported on Wayland. Default is false.
    /// </summary>
    /// <remarks>
    /// When true on Wayland, the user will be presented with a selection interface
    /// to choose what to capture. On other platforms, this option is ignored.
    /// </remarks>
    public bool Interactive { get; set; }

    /// <summary>
    /// Gets or sets the rectangular region to capture.
    /// Required when <see cref="Target"/> is <see cref="CaptureTarget.Region"/>.
    /// </summary>
    /// <remarks>
    /// Coordinates are in screen pixels relative to the primary display's top-left corner.
    /// Not supported on Wayland.
    /// </remarks>
    public Rectangle? Region { get; set; }

    /// <summary>
    /// Gets or sets whether to include window borders in window captures.
    /// Only applies to Windows when capturing windows. Default is false.
    /// </summary>
    /// <remarks>
    /// This option affects the window title bar and borders.
    /// Only supported on Windows; ignored on other platforms.
    /// </remarks>
    public bool IncludeBorder { get; set; }

    /// <summary>
    /// Gets or sets whether to include window shadows in window captures.
    /// Only applies to Windows and macOS when capturing windows. Default is false.
    /// </summary>
    /// <remarks>
    /// On Windows, this affects the drop shadow around windows.
    /// On macOS, this would affect window shadows if window capture were supported.
    /// Ignored on Linux platforms.
    /// </remarks>
    public bool IncludeShadow { get; set; }

    /// <summary>
    /// Gets or sets the timeout duration for screenshot operations.
    /// Default is 10 seconds.
    /// </summary>
    /// <remarks>
    /// Primarily used for Wayland's portal API when waiting for user interaction.
    /// On other platforms, this timeout may be ignored as operations complete quickly.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the output image format.
    /// Default is <see cref="ImageFormat.Png"/>.
    /// </summary>
    /// <remarks>
    /// PNG provides lossless compression and is suitable for screenshots with text and UI elements.
    /// JPEG provides smaller file sizes but with lossy compression.
    /// </remarks>
    public ImageFormat Format { get; set; } = ImageFormat.Png;

    /// <summary>
    /// Gets or sets the JPEG quality level (1-100).
    /// Only used when <see cref="Format"/> is <see cref="ImageFormat.Jpeg"/>. Default is 90.
    /// </summary>
    /// <remarks>
    /// Higher values produce better quality images but larger file sizes.
    /// Typical values: 60-70 for small size, 80-90 for good quality, 95-100 for best quality.
    /// </remarks>
    public int JpegQuality { get; set; } = 90;
}
