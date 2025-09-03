using System;
using Shutter.Models;

namespace Shutter.Abstractions;

/// <summary>
/// An interface used to define screenshot service methods.
/// </summary>
public interface IShutterService
{
    /// <summary>
    /// Takes a fullscreen screenshot.
    /// </summary>
    /// <returns>PNG image as byte array.</returns>
    byte[] TakeScreenshot();

    /// <summary>
    /// Takes a screenshot based on the provided options.
    /// </summary>
    /// <param name="options">Screenshot options. If null, captures fullscreen.</param>
    /// <returns>PNG image as byte array</returns>
    /// <remarks>
    /// Platform support:
    /// - FullScreen: Windows, macOS, X11, Wayland
    /// - Window: Windows, macOS (with permissions), X11
    /// - Region: Windows, macOS, X11
    /// - Interactive: Wayland only
    /// - Display selection: All platforms
    /// </remarks>
    /// <exception cref="PlatformNotSupportedException">When requested feature isn't available and FallbackBehavior is ThrowException</exception>
    byte[] TakeScreenshot(ScreenshotOptions options);

    /// <summary>
    /// Gets the screenshot capabilities available on the current platform.
    /// </summary>
    /// <returns>A <see cref="ScreenshotCapabilities"/> object describing which features are supported.</returns>
    /// <remarks>
    /// Use this method to determine which screenshot features are available at runtime,
    /// allowing you to adapt your logic based on platform capabilities.
    /// This can help avoid <see cref="PlatformNotSupportedException"/> by checking
    /// capabilities before attempting operations.
    /// </remarks>
    ScreenshotCapabilities GetCapabilities();
}
