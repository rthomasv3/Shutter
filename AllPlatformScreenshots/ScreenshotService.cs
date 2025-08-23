using System;
using AllPlatformScreenshots.Abstractions;
using AllPlatformScreenshots.Enums;
using AllPlatformScreenshots.Models;

namespace AllPlatformScreenshots;

/// <summary>
/// Class that implements the features of a <see cref="IScreenshotService"/>.
/// </summary>
public class ScreenshotService : IScreenshotService
{
    #region Fields

    private readonly IPlatformScreenshotService _platformScreenshotService;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Creates a new instance of the <see cref="ScreenshotService" class.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Thrown when the platform is unsupported.</exception>
    public ScreenshotService()
    {
        if (PlatformDetector.IsWindows)
        {
            _platformScreenshotService = new WindowsScreenshotService();
        }
        else if (PlatformDetector.IsLinux)
        {
            if (PlatformDetector.IsWayland())
            {
                _platformScreenshotService = new WaylandScreenshotService();
            }
            else if (PlatformDetector.IsX11())
            {
                _platformScreenshotService = new X11ScreenshotService();
            }
            else
            {
                throw new PlatformNotSupportedException("Unknown Linux session type. X11 or Wayland required.");
            }
        }
        else if (PlatformDetector.IsMacOS)
        {
            _platformScreenshotService = new MacOSScreenshotService();
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported platform.");
        }
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public byte[] TakeScreenshot()
    {
        return _platformScreenshotService.TakeScreenshot(new ScreenshotOptions());
    }

    /// <inheritdoc />
    public byte[] TakeScreenshot(ScreenshotOptions options)
    {
        ScreenshotOptions effectiveOptions = ValidateAndNormalizeOptions(options);
        return _platformScreenshotService.TakeScreenshot(effectiveOptions);
    }

    #endregion

    #region Private Methods

    private ScreenshotOptions ValidateAndNormalizeOptions(ScreenshotOptions options)
    {
        // Clone options to avoid modifying the original
        ScreenshotOptions validatedOptions = new()
        {
            Target = options.Target,
            Fallback = options.Fallback,
            WindowHandle = options.WindowHandle,
            DisplayIndex = options.DisplayIndex,
            Interactive = options.Interactive,
            Region = options.Region,
            IncludeBorder = options.IncludeBorder,
            IncludeShadow = options.IncludeShadow,
            Timeout = options.Timeout,
            Format = options.Format,
            JpegQuality = options.JpegQuality
        };

        switch (validatedOptions.Target)
        {
            case CaptureTarget.Window:
                ValidateWindowCapture(validatedOptions);
                break;

            case CaptureTarget.Display:
                ValidateDisplayCapture(validatedOptions);
                break;

            case CaptureTarget.Region:
                ValidateRegionCapture(validatedOptions);
                break;

            case CaptureTarget.FullScreen:
                // Always supported, no validation needed
                break;

            default:
                throw new ArgumentException($"Unknown capture target: {validatedOptions.Target}");
        }

        // Validate platform-specific modifiers
        ValidateModifiers(validatedOptions);

        return validatedOptions;
    }

    private void ValidateWindowCapture(ScreenshotOptions options)
    {
        if (!options.WindowHandle.HasValue || options.WindowHandle.Value == IntPtr.Zero)
        {
            throw new ArgumentException("WindowHandle is required when Target is Window");
        }

        bool isSupported = PlatformDetector.IsWindows || PlatformDetector.IsX11();

        if (!isSupported)
        {
            HandleUnsupportedFeature(options, "Window capture", "Windows and X11");
        }
    }

    private void ValidateDisplayCapture(ScreenshotOptions options)
    {
        if (!options.DisplayIndex.HasValue)
        {
            throw new ArgumentException("DisplayIndex is required when Target is Display");
        }

        bool isSupported = PlatformDetector.IsWindows ||
                           PlatformDetector.IsMacOS ||
                           PlatformDetector.IsX11();

        if (!isSupported)
        {
            HandleUnsupportedFeature(options, "Display selection", "Windows, macOS, and X11");
        }

        // Note: We can't validate if the display index is valid here without 
        // platform-specific code, so that check will happen in the platform service
    }

    private void ValidateRegionCapture(ScreenshotOptions options)
    {
        if (!options.Region.HasValue)
        {
            throw new ArgumentException("Region is required when Target is Region");
        }

        Rectangle region = options.Region.Value;

        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentException("Region must have positive width and height");
        }

        bool isSupported = PlatformDetector.IsWindows ||
                           PlatformDetector.IsMacOS ||
                           PlatformDetector.IsX11();

        if (!isSupported)
        {
            HandleUnsupportedFeature(options, "Region capture", "Windows, macOS, and X11");
        }
    }

    private void ValidateModifiers(ScreenshotOptions options)
    {
        if (options.Interactive && !PlatformDetector.IsWayland())
        {
            if (options.Fallback == FallbackBehavior.ThrowException)
            {
                throw new PlatformNotSupportedException("Interactive mode is only supported on Wayland");
            }

            options.Interactive = false;
        }

        bool supportsBorderControl = PlatformDetector.IsWindows || PlatformDetector.IsMacOS;

        if ((options.IncludeBorder || options.IncludeShadow) && !supportsBorderControl)
        {
            if (options.Fallback == FallbackBehavior.ThrowException)
            {
                throw new PlatformNotSupportedException(
                    "Border and shadow control is only supported on Windows and macOS");
            }
            // For other fallback modes, just ignore these options
            // (they won't affect the capture on unsupported platforms)
        }

        // Timeout is primarily for Wayland interactive mode
        // Other platforms will ignore it, so no validation needed
    }

    private void HandleUnsupportedFeature(ScreenshotOptions options, string feature, string supportedPlatforms)
    {
        switch (options.Fallback)
        {
            case FallbackBehavior.ThrowException:
                throw new PlatformNotSupportedException($"{feature} is only supported on {supportedPlatforms}");

            case FallbackBehavior.Default:
            case FallbackBehavior.BestEffort:
                // Fall back to fullscreen
                options.Target = CaptureTarget.FullScreen;
                options.WindowHandle = null;
                options.DisplayIndex = null;
                options.Region = null;
                break;

            default:
                throw new ArgumentException($"Unknown fallback behavior: {options.Fallback}");
        }
    }

    #endregion
}
