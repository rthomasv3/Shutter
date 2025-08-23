using System;
using System.IO;
using System.Runtime.InteropServices;
using AllPlatformScreenshots.Abstractions;
using AllPlatformScreenshots.Enums;
using AllPlatformScreenshots.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace AllPlatformScreenshots;

internal class MacOSScreenshotService : IPlatformScreenshotService
{
    #region Native Methods

    [DllImport("libSystem.dylib")]
    private static extern IntPtr dlopen(string path, int mode);

    [DllImport("libSystem.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    [DllImport("libSystem.dylib")]
    private static extern int dlclose(IntPtr handle);

    [DllImport("libSystem.dylib")]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport("libSystem.dylib")]
    private static extern long CFDataGetLength(IntPtr data);

    [DllImport("libSystem.dylib")]
    private static extern void CFRelease(IntPtr cf);

    private const int RTLD_LAZY = 1;
    private const string CORE_GRAPHICS = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    // CoreGraphics function delegates
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGDisplayCreateImageDelegate(uint displayId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGDisplayCreateImageRectDelegate(uint displayId, CGRect rect);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CGImageReleaseDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGImageGetDataProviderDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGDataProviderCopyDataDelegate(IntPtr provider);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint CGImageGetWidthDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint CGImageGetHeightDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint CGImageGetBytesPerRowDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint CGImageGetBitsPerPixelDelegate(IntPtr image);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public CGRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    #endregion

    public byte[] TakeScreenshot(ScreenshotOptions options)
    {
        // Route based on target
        return options.Target switch
        {
            CaptureTarget.FullScreen => CaptureFullScreen(options),
            CaptureTarget.Display => CaptureDisplay(options),
            CaptureTarget.Region => CaptureRegion(options),
            CaptureTarget.Window => CaptureFullScreen(options), // Fallback - window not supported
            _ => throw new ArgumentException($"Unsupported capture target: {options.Target}")
        };
    }

    private byte[] CaptureFullScreen(ScreenshotOptions options)
    {
        // Display ID 0 means main display on macOS
        return CaptureDisplayCore(0, null, options);
    }

    private byte[] CaptureDisplay(ScreenshotOptions options)
    {
        if (!options.DisplayIndex.HasValue)
            throw new ArgumentException("DisplayIndex is required for Display capture");

        // macOS display IDs typically start at 1, but we'll use the index as-is
        // In a full implementation, you'd enumerate displays with CGGetActiveDisplayList
        uint displayId = (uint)options.DisplayIndex.Value;

        return CaptureDisplayCore(displayId, null, options);
    }

    private byte[] CaptureRegion(ScreenshotOptions options)
    {
        if (!options.Region.HasValue)
            throw new ArgumentException("Region is required for Region capture");

        Models.Rectangle region = options.Region.Value;

        // Convert to CGRect (macOS uses floating point coordinates)
        CGRect cgRect = new CGRect(region.X, region.Y, region.Width, region.Height);

        // Capture from main display (0) with the specified region
        return CaptureDisplayCore(0, cgRect, options);
    }

    private byte[] CaptureDisplayCore(uint displayId, CGRect? rect, ScreenshotOptions options)
    {
        IntPtr cgHandle = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        IntPtr dataProvider = IntPtr.Zero;
        IntPtr data = IntPtr.Zero;

        try
        {
            // Load CoreGraphics
            cgHandle = dlopen(CORE_GRAPHICS, RTLD_LAZY);
            if (cgHandle == IntPtr.Zero)
                throw new ScreenshotException("Failed to load CoreGraphics framework.", "macOS");

            // Resolve CoreGraphics functions
            CGImageReleaseDelegate cgImageRelease = GetDelegate<CGImageReleaseDelegate>(cgHandle, "CGImageRelease");
            CGImageGetDataProviderDelegate cgImageGetDataProvider = GetDelegate<CGImageGetDataProviderDelegate>(cgHandle, "CGImageGetDataProvider");
            CGDataProviderCopyDataDelegate cgDataProviderCopyData = GetDelegate<CGDataProviderCopyDataDelegate>(cgHandle, "CGDataProviderCopyData");
            CGImageGetWidthDelegate cgImageGetWidth = GetDelegate<CGImageGetWidthDelegate>(cgHandle, "CGImageGetWidth");
            CGImageGetHeightDelegate cgImageGetHeight = GetDelegate<CGImageGetHeightDelegate>(cgHandle, "CGImageGetHeight");
            CGImageGetBytesPerRowDelegate cgImageGetBytesPerRow = GetDelegate<CGImageGetBytesPerRowDelegate>(cgHandle, "CGImageGetBytesPerRow");
            CGImageGetBitsPerPixelDelegate cgImageGetBitsPerPixel = GetDelegate<CGImageGetBitsPerPixelDelegate>(cgHandle, "CGImageGetBitsPerPixel");

            // Capture image based on whether we have a rect
            if (rect.HasValue)
            {
                // Try to use CGDisplayCreateImageRect for region capture
                try
                {
                    CGDisplayCreateImageRectDelegate cgDisplayCreateImageRect = GetDelegate<CGDisplayCreateImageRectDelegate>(
                        cgHandle, "CGDisplayCreateImageRect");
                    image = cgDisplayCreateImageRect(displayId, rect.Value);
                }
                catch
                {
                    // If CGDisplayCreateImageRect is not available (older macOS),
                    // fall back to capturing full display and cropping
                    CGDisplayCreateImageDelegate cgDisplayCreateImage = GetDelegate<CGDisplayCreateImageDelegate>(
                        cgHandle, "CGDisplayCreateImage");
                    image = cgDisplayCreateImage(displayId);
                    // Note: Would need to implement cropping here
                }
            }
            else
            {
                // Full display capture
                CGDisplayCreateImageDelegate cgDisplayCreateImage = GetDelegate<CGDisplayCreateImageDelegate>(
                    cgHandle, "CGDisplayCreateImage");
                image = cgDisplayCreateImage(displayId);
            }

            if (image == IntPtr.Zero)
                throw new ScreenshotException($"Failed to capture screenshot for display {displayId}.", "macOS");

            // Get image dimensions and format
            int width = (int)cgImageGetWidth(image);
            int height = (int)cgImageGetHeight(image);
            int bytesPerRow = (int)cgImageGetBytesPerRow(image);
            int bitsPerPixel = (int)cgImageGetBitsPerPixel(image);

            // Validate format (expect 32-bit BGRA)
            if (bitsPerPixel != 32)
                throw new ScreenshotException($"Unsupported bits per pixel: {bitsPerPixel}. Expected 32 (BGRA).", "macOS");

            // Get pixel data
            dataProvider = cgImageGetDataProvider(image);
            if (dataProvider == IntPtr.Zero)
                throw new ScreenshotException("Failed to get data provider.", "macOS");

            data = cgDataProviderCopyData(dataProvider);
            if (data == IntPtr.Zero)
                throw new ScreenshotException("Failed to copy pixel data.", "macOS");

            // Convert BGRA to RGBA
            byte[] rgbaData = ConvertBgraToRgba(data, width, height, bytesPerRow);

            // Encode based on requested format
            return EncodeImage(rgbaData, width, height, options);
        }
        catch (DllNotFoundException ex)
        {
            throw new ScreenshotException("CoreGraphics framework not found.", "macOS", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new ScreenshotException("Required CoreGraphics function not found.", "macOS", ex);
        }
        finally
        {
            if (data != IntPtr.Zero) CFRelease(data);
            if (dataProvider != IntPtr.Zero) CFRelease(dataProvider);
            if (image != IntPtr.Zero) CFRelease(image);
            if (cgHandle != IntPtr.Zero) dlclose(cgHandle);
        }
    }

    private byte[] EncodeImage(byte[] rgbaData, int width, int height, ScreenshotOptions options)
    {
        using Image<Rgba32> image = Image<Rgba32>.LoadPixelData<Rgba32>(rgbaData, width, height);
        using MemoryStream ms = new();

        switch (options.Format)
        {
            case ImageFormat.Png:
                image.Save(ms, new PngEncoder());
                break;

            case ImageFormat.Jpeg:
                image.Save(ms, new JpegEncoder { Quality = options.JpegQuality });
                break;

            default:
                throw new ArgumentException($"Unsupported image format: {options.Format}");
        }

        return ms.ToArray();
    }

    private unsafe byte[] ConvertBgraToRgba(IntPtr data, int width, int height, int bytesPerRow)
    {
        byte* src = (byte*)CFDataGetBytePtr(data);
        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            byte* rowSrc = src + (y * bytesPerRow);
            int dstOffset = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                int srcOffset = x * 4;
                int dstPixel = dstOffset + (x * 4);
                rgba[dstPixel + 0] = rowSrc[srcOffset + 2]; // Red (from BGR)
                rgba[dstPixel + 1] = rowSrc[srcOffset + 1]; // Green
                rgba[dstPixel + 2] = rowSrc[srcOffset + 0]; // Blue
                rgba[dstPixel + 3] = rowSrc[srcOffset + 3]; // Alpha
            }
        }

        return rgba;
    }

    private static T GetDelegate<T>(IntPtr handle, string symbol) where T : Delegate
    {
        IntPtr ptr = dlsym(handle, symbol);
        if (ptr == IntPtr.Zero)
            throw new ScreenshotException($"Failed to resolve symbol: {symbol}.", "macOS");
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }
}
