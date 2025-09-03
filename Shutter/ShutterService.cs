using System;
using Shutter.Abstractions;
using Shutter.Enums;
using Shutter.Models;

namespace Shutter;

/// <summary>
/// Class that implements the features of a <see cref="IShutterService"/>.
/// </summary>
public class ShutterService : IShutterService
{
    #region Fields

    private readonly IPlatformScreenshotService _platformScreenshotService;
    private ScreenshotCapabilities _cachedCapabilities;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Creates a new instance of the <see cref="ShutterService"/> class.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Thrown when the platform is unsupported.</exception>
    public ShutterService()
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

    /// <inheritdoc />
    public ScreenshotCapabilities GetCapabilities()
    {
        _cachedCapabilities ??= _platformScreenshotService.GetCapabilities();
        return _cachedCapabilities;
    }

    #endregion

    #region Private Methods

    private ScreenshotOptions ValidateAndNormalizeOptions(ScreenshotOptions options)
    {
        ScreenshotCapabilities capabilities = GetCapabilities();

        // Clone options to avoid modifying the original
        ScreenshotOptions validatedOptions = options.Clone();

        // Step 1: Validate required parameters (before capability checks)
        ValidateRequiredParameters(validatedOptions);

        // Step 2: Check and handle capability limitations
        ApplyCapabilityConstraints(validatedOptions, capabilities);

        // Step 3: Validate parameter ranges (after capability checks)
        ValidateParameterRanges(validatedOptions, capabilities);

        return validatedOptions;
    }

    private static void ValidateRequiredParameters(ScreenshotOptions options)
    {
        switch (options.Target)
        {
            case CaptureTarget.Window:
                if (!options.WindowHandle.HasValue || options.WindowHandle.Value == IntPtr.Zero)
                {
                    throw new ArgumentException("WindowHandle is required when Target is Window");
                }
                break;

            case CaptureTarget.Display:
                if (!options.DisplayIndex.HasValue)
                {
                    throw new ArgumentException("DisplayIndex is required when Target is Display");
                }
                break;

            case CaptureTarget.Region:
                if (!options.Region.HasValue)
                {
                    throw new ArgumentException("Region is required when Target is Region");
                }

                Rectangle region = options.Region.Value;
                if (region.Width <= 0 || region.Height <= 0)
                {
                    throw new ArgumentException("Region must have positive width and height");
                }
                break;

            case CaptureTarget.FullScreen:
                // No required parameters
                break;

            default:
                throw new ArgumentException($"Unknown capture target: {options.Target}");
        }
    }

    private void ApplyCapabilityConstraints(ScreenshotOptions options, ScreenshotCapabilities capabilities)
    {
        bool targetSupported = IsCaptureTargetSupported(options.Target, capabilities);

        if (!targetSupported)
        {
            string targetName = options.Target.ToString();
            HandleUnsupportedFeature(options, capabilities, $"{targetName} capture");
        }

        if (options.Interactive && !capabilities.SupportsInteractiveMode)
        {
            HandleUnsupportedModifier(options, capabilities, "Interactive mode",
                () => options.Interactive = false);
        }

        if (options.IncludeBorder && !capabilities.SupportsBorderControl)
        {
            HandleUnsupportedModifier(options, capabilities, "Border control",
                () => options.IncludeBorder = false);
        }

        if (options.IncludeShadow && !capabilities.SupportsShadowControl)
        {
            HandleUnsupportedModifier(options, capabilities, "Shadow control",
                () => options.IncludeShadow = false);
        }
    }

    private static void ValidateParameterRanges(ScreenshotOptions options, ScreenshotCapabilities capabilities)
    {
        if (options.Target == CaptureTarget.Display &&
            options.DisplayIndex.HasValue &&
            capabilities.DisplayCount.HasValue &&
            options.DisplayIndex.Value >= capabilities.DisplayCount.Value)
        {
            throw new ArgumentException(
                $"Display index {options.DisplayIndex.Value} is out of range. " +
                $"Available displays: 0-{capabilities.DisplayCount.Value - 1}");
        }

        if (options.Format == ImageFormat.Jpeg && (options.JpegQuality < 1 || options.JpegQuality > 100))
        {
            throw new ArgumentException("JpegQuality must be between 1 and 100");
        }
    }

    private void HandleUnsupportedFeature(ScreenshotOptions options, ScreenshotCapabilities capabilities, 
        string featureName)
    {
        switch (options.Fallback)
        {
            case FallbackBehavior.ThrowException:
                throw new PlatformNotSupportedException($"{featureName} is not supported on the current platform");

            case FallbackBehavior.Default:
                // Simple fallback - always go to fullscreen
                ResetTargetSpecificParameters(options, CaptureTarget.FullScreen);
                options.Target = CaptureTarget.FullScreen;
                break;

            case FallbackBehavior.BestEffort:
                // Intelligent fallback - try to preserve as much intent as possible
                ApplyBestEffortFallback(options, capabilities);
                break;

            default:
                throw new ArgumentException($"Unknown fallback behavior: {options.Fallback}");
        }
    }

    private void HandleUnsupportedModifier(ScreenshotOptions options, ScreenshotCapabilities capabilities, 
        string modifierName, Action disableModifier)
    {
        switch (options.Fallback)
        {
            case FallbackBehavior.ThrowException:
                throw new PlatformNotSupportedException(
                    $"{modifierName} is not supported on the current platform");

            case FallbackBehavior.Default:
            case FallbackBehavior.BestEffort:
                disableModifier();
                break;

            default:
                throw new ArgumentException($"Unknown fallback behavior: {options.Fallback}");
        }
    }

    private void ApplyBestEffortFallback(ScreenshotOptions options, ScreenshotCapabilities capabilities)
    {
        CaptureTarget[] fallbackChain = options.Target switch
        {
            CaptureTarget.Window => new[] { CaptureTarget.Display, CaptureTarget.FullScreen },
            CaptureTarget.Region => new[] { CaptureTarget.Display, CaptureTarget.FullScreen },
            CaptureTarget.Display => new[] { CaptureTarget.FullScreen },
            _ => new[] { CaptureTarget.FullScreen }
        };

        foreach (CaptureTarget fallbackTarget in fallbackChain)
        {
            if (IsCaptureTargetSupported(fallbackTarget, capabilities))
            {
                options.Target = fallbackTarget;
                ResetTargetSpecificParameters(options, fallbackTarget);
                break;
            }
        }

        if (!IsCaptureTargetSupported(options.Target, capabilities))
        {
            options.Target = CaptureTarget.FullScreen;
            ResetTargetSpecificParameters(options, CaptureTarget.FullScreen);
        }
    }

    private static bool IsCaptureTargetSupported(CaptureTarget target, ScreenshotCapabilities capabilities)
    {
        return target switch
        {
            CaptureTarget.FullScreen => capabilities.SupportsFullScreen,
            CaptureTarget.Window => capabilities.SupportsWindowCapture,
            CaptureTarget.Display => capabilities.SupportsDisplaySelection,
            CaptureTarget.Region => capabilities.SupportsRegionCapture,
            _ => false
        };
    }

    private static void ResetTargetSpecificParameters(ScreenshotOptions options, CaptureTarget newTarget)
    {
        options.WindowHandle = null;
        options.DisplayIndex = null;
        options.Region = null;

        if (newTarget == CaptureTarget.Display)
        {
            options.DisplayIndex = 0;
        }
    }

    #endregion
}
