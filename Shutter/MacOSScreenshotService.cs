using System;
using System.IO;
using System.Runtime.InteropServices;
using Shutter.Abstractions;
using Shutter.Enums;
using Shutter.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Shutter;

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
    private static extern IntPtr dlerror();

    private const int RTLD_LAZY = 1;
    private const string CORE_GRAPHICS = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CORE_FOUNDATION = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // CoreFoundation function delegates (these need to be loaded dynamically)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CFDataGetBytePtrDelegate(IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long CFDataGetLengthDelegate(IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CFReleaseDelegate(IntPtr cf);

    // CoreGraphics function delegates
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint CGMainDisplayIDDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGDisplayCreateImageDelegate(uint displayId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGDisplayCreateImageForRectDelegate(uint displayId, CGRect rect);

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
    private delegate nuint CGImageGetBitsPerComponentDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nuint CGImageGetBitsPerPixelDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint CGImageGetBitmapInfoDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGImageGetColorSpaceDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CGGetOnlineDisplayListDelegate(uint maxDisplays, uint[] displays, ref uint displayCount);

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

    // Bitmap info flags (for determining pixel format)
    private const uint kCGBitmapAlphaInfoMask = 0x1F;
    private const uint kCGBitmapByteOrderMask = 0x7000;
    private const uint kCGBitmapByteOrder32Little = 2 << 12;
    private const uint kCGBitmapByteOrder32Big = 4 << 12;

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
        // Use 0 to indicate main display (will be resolved to actual ID in CaptureDisplayCore)
        return CaptureDisplayCore(0, null, options);
    }

    private byte[] CaptureDisplay(ScreenshotOptions options)
    {
        if (!options.DisplayIndex.HasValue)
            throw new ArgumentException("DisplayIndex is required for Display capture");

        // Get the actual display ID for the given index
        uint displayId = GetDisplayIdFromIndex(options.DisplayIndex.Value);
        return CaptureDisplayCore(displayId, null, options);
    }

    private byte[] CaptureRegion(ScreenshotOptions options)
    {
        if (!options.Region.HasValue)
            throw new ArgumentException("Region is required for Region capture");

        Models.Rectangle region = options.Region.Value;

        // Convert to CGRect (macOS uses floating point coordinates)
        CGRect cgRect = new CGRect(region.X, region.Y, region.Width, region.Height);

        // Capture from main display with the specified region
        return CaptureDisplayCore(0, cgRect, options);
    }

    private uint GetDisplayIdFromIndex(int index)
    {
        IntPtr cgHandle = IntPtr.Zero;

        try
        {
            cgHandle = dlopen(CORE_GRAPHICS, RTLD_LAZY);
            if (cgHandle == IntPtr.Zero)
                throw new ScreenshotException("Failed to load CoreGraphics framework.", "macOS");

            // Try to get the display list
            try
            {
                CGGetOnlineDisplayListDelegate cgGetOnlineDisplayList =
                    GetDelegate<CGGetOnlineDisplayListDelegate>(cgHandle, "CGGetOnlineDisplayList", false);

                if (cgGetOnlineDisplayList != null)
                {
                    uint maxDisplays = 32;
                    uint[] displays = new uint[maxDisplays];
                    uint displayCount = 0;

                    int result = cgGetOnlineDisplayList(maxDisplays, displays, ref displayCount);
                    if (result == 0 && index < displayCount)
                    {
                        return displays[index];
                    }

                    throw new ArgumentException($"Display index {index} not found. Available displays: 0-{displayCount - 1}");
                }
            }
            catch (ScreenshotException)
            {
                // CGGetOnlineDisplayList not available, fall through to simple mapping
            }

            // Fallback: simple index to ID mapping
            // Note: This is a simplification - display IDs don't necessarily map directly to indices
            // but without CGGetOnlineDisplayList, we have limited options
            if (index == 0)
            {
                // Try to get main display ID
                try
                {
                    CGMainDisplayIDDelegate cgMainDisplayID =
                        GetDelegate<CGMainDisplayIDDelegate>(cgHandle, "CGMainDisplayID", false);
                    if (cgMainDisplayID != null)
                        return cgMainDisplayID();
                }
                catch { }
            }

            // Last resort: use index as display ID (may not work correctly)
            return (uint)index;
        }
        finally
        {
            if (cgHandle != IntPtr.Zero)
                dlclose(cgHandle);
        }
    }

    private byte[] CaptureDisplayCore(uint displayId, CGRect? rect, ScreenshotOptions options)
    {
        IntPtr cgHandle = IntPtr.Zero;
        IntPtr cfHandle = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        IntPtr dataProvider = IntPtr.Zero;
        IntPtr data = IntPtr.Zero;

        try
        {
            // Load CoreGraphics
            cgHandle = dlopen(CORE_GRAPHICS, RTLD_LAZY);
            if (cgHandle == IntPtr.Zero)
                throw new ScreenshotException("Failed to load CoreGraphics framework.", "macOS");

            // Load CoreFoundation
            cfHandle = dlopen(CORE_FOUNDATION, RTLD_LAZY);
            if (cfHandle == IntPtr.Zero)
                throw new ScreenshotException("Failed to load CoreFoundation framework.", "macOS");

            // Resolve CoreGraphics functions
            CGImageReleaseDelegate cgImageRelease = GetDelegate<CGImageReleaseDelegate>(cgHandle, "CGImageRelease");
            CGImageGetDataProviderDelegate cgImageGetDataProvider = GetDelegate<CGImageGetDataProviderDelegate>(cgHandle, "CGImageGetDataProvider");
            CGDataProviderCopyDataDelegate cgDataProviderCopyData = GetDelegate<CGDataProviderCopyDataDelegate>(cgHandle, "CGDataProviderCopyData");
            CGImageGetWidthDelegate cgImageGetWidth = GetDelegate<CGImageGetWidthDelegate>(cgHandle, "CGImageGetWidth");
            CGImageGetHeightDelegate cgImageGetHeight = GetDelegate<CGImageGetHeightDelegate>(cgHandle, "CGImageGetHeight");
            CGImageGetBytesPerRowDelegate cgImageGetBytesPerRow = GetDelegate<CGImageGetBytesPerRowDelegate>(cgHandle, "CGImageGetBytesPerRow");
            CGImageGetBitsPerComponentDelegate cgImageGetBitsPerComponent = GetDelegate<CGImageGetBitsPerComponentDelegate>(cgHandle, "CGImageGetBitsPerComponent");
            CGImageGetBitsPerPixelDelegate cgImageGetBitsPerPixel = GetDelegate<CGImageGetBitsPerPixelDelegate>(cgHandle, "CGImageGetBitsPerPixel");

            // Resolve CoreFoundation functions
            CFDataGetBytePtrDelegate cfDataGetBytePtr = GetDelegate<CFDataGetBytePtrDelegate>(cfHandle, "CFDataGetBytePtr");
            CFDataGetLengthDelegate cfDataGetLength = GetDelegate<CFDataGetLengthDelegate>(cfHandle, "CFDataGetLength");
            CFReleaseDelegate cfRelease = GetDelegate<CFReleaseDelegate>(cfHandle, "CFRelease");

            // Optional: try to get bitmap info for better pixel format handling
            CGImageGetBitmapInfoDelegate cgImageGetBitmapInfo = null;
            try
            {
                cgImageGetBitmapInfo = GetDelegate<CGImageGetBitmapInfoDelegate>(cgHandle, "CGImageGetBitmapInfo", false);
            }
            catch { }

            // If displayId is 0, get the main display ID
            if (displayId == 0)
            {
                try
                {
                    CGMainDisplayIDDelegate cgMainDisplayID = GetDelegate<CGMainDisplayIDDelegate>(cgHandle, "CGMainDisplayID");
                    displayId = cgMainDisplayID();
                }
                catch
                {
                    // If CGMainDisplayID is not available, try using a large constant
                    // that typically represents the main display
                    displayId = 0x042728c0; // Common main display ID, but not guaranteed
                }
            }

            // Capture image based on whether we have a rect
            if (rect.HasValue)
            {
                // Try to use CGDisplayCreateImageForRect (correct name!)
                try
                {
                    CGDisplayCreateImageForRectDelegate cgDisplayCreateImageForRect =
                        GetDelegate<CGDisplayCreateImageForRectDelegate>(cgHandle, "CGDisplayCreateImageForRect");
                    image = cgDisplayCreateImageForRect(displayId, rect.Value);
                }
                catch
                {
                    // If CGDisplayCreateImageForRect is not available (older macOS),
                    // fall back to capturing full display and manually crop
                    CGDisplayCreateImageDelegate cgDisplayCreateImage =
                        GetDelegate<CGDisplayCreateImageDelegate>(cgHandle, "CGDisplayCreateImage");
                    image = cgDisplayCreateImage(displayId);

                    // Note: Manual cropping would need to be implemented here
                    // For now, we're returning the full display image
                    // TODO: Implement manual cropping based on rect
                }
            }
            else
            {
                // Full display capture
                CGDisplayCreateImageDelegate cgDisplayCreateImage =
                    GetDelegate<CGDisplayCreateImageDelegate>(cgHandle, "CGDisplayCreateImage");
                image = cgDisplayCreateImage(displayId);
            }

            if (image == IntPtr.Zero)
                throw new ScreenshotException($"Failed to capture screenshot for display {displayId}.", "macOS");

            // Get image dimensions and format
            int width = (int)cgImageGetWidth(image);
            int height = (int)cgImageGetHeight(image);
            int bytesPerRow = (int)cgImageGetBytesPerRow(image);
            int bitsPerPixel = (int)cgImageGetBitsPerPixel(image);
            int bitsPerComponent = (int)cgImageGetBitsPerComponent(image);

            // Get bitmap info if available to determine pixel format
            uint bitmapInfo = 0;
            if (cgImageGetBitmapInfo != null)
            {
                bitmapInfo = cgImageGetBitmapInfo(image);
            }

            // Validate format
            if (bitsPerPixel != 32 && bitsPerPixel != 24)
                throw new ScreenshotException($"Unsupported bits per pixel: {bitsPerPixel}. Expected 24 or 32.", "macOS");

            // Get pixel data
            dataProvider = cgImageGetDataProvider(image);
            if (dataProvider == IntPtr.Zero)
                throw new ScreenshotException("Failed to get data provider.", "macOS");

            data = cgDataProviderCopyData(dataProvider);
            if (data == IntPtr.Zero)
                throw new ScreenshotException("Failed to copy pixel data.", "macOS");

            // Convert to RGBA based on detected format
            byte[] rgbaData = ConvertToRgba(data, width, height, bytesPerRow, bitsPerPixel, bitmapInfo, cfDataGetBytePtr);

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
            // Release CoreFoundation objects using the loaded CFRelease function
            if (cfHandle != IntPtr.Zero)
            {
                try
                {
                    CFReleaseDelegate cfRelease = GetDelegate<CFReleaseDelegate>(cfHandle, "CFRelease", false);
                    if (cfRelease != null)
                    {
                        if (data != IntPtr.Zero) cfRelease(data);
                        if (dataProvider != IntPtr.Zero) cfRelease(dataProvider);
                    }
                }
                catch { }
            }

            if (image != IntPtr.Zero)
            {
                try
                {
                    // Get the release function if we haven't already
                    if (cgHandle != IntPtr.Zero)
                    {
                        CGImageReleaseDelegate cgImageRelease =
                            GetDelegate<CGImageReleaseDelegate>(cgHandle, "CGImageRelease", false);
                        cgImageRelease?.Invoke(image);
                    }
                }
                catch { }
            }

            if (cfHandle != IntPtr.Zero) dlclose(cfHandle);
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

    private unsafe byte[] ConvertToRgba(IntPtr data, int width, int height, int bytesPerRow, int bitsPerPixel, uint bitmapInfo, CFDataGetBytePtrDelegate cfDataGetBytePtr)
    {
        IntPtr srcPtr = cfDataGetBytePtr(data);
        byte* src = (byte*)srcPtr;
        byte[] rgba = new byte[width * height * 4];

        // Determine byte order from bitmap info
        bool isLittleEndian = (bitmapInfo & kCGBitmapByteOrderMask) == kCGBitmapByteOrder32Little;
        bool isBigEndian = (bitmapInfo & kCGBitmapByteOrderMask) == kCGBitmapByteOrder32Big;

        // Default to BGRA for little endian or unspecified, ARGB for big endian
        bool isBgra = !isBigEndian;

        if (bitsPerPixel == 32)
        {
            for (int y = 0; y < height; y++)
            {
                byte* rowSrc = src + (y * bytesPerRow);
                int dstOffset = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    int srcOffset = x * 4;
                    int dstPixel = dstOffset + (x * 4);

                    if (isBgra)
                    {
                        // BGRA -> RGBA
                        rgba[dstPixel + 0] = rowSrc[srcOffset + 2]; // Red (from B_G_R_A)
                        rgba[dstPixel + 1] = rowSrc[srcOffset + 1]; // Green
                        rgba[dstPixel + 2] = rowSrc[srcOffset + 0]; // Blue
                        rgba[dstPixel + 3] = rowSrc[srcOffset + 3]; // Alpha
                    }
                    else
                    {
                        // ARGB -> RGBA
                        rgba[dstPixel + 0] = rowSrc[srcOffset + 1]; // Red (from A_R_G_B)
                        rgba[dstPixel + 1] = rowSrc[srcOffset + 2]; // Green
                        rgba[dstPixel + 2] = rowSrc[srcOffset + 3]; // Blue
                        rgba[dstPixel + 3] = rowSrc[srcOffset + 0]; // Alpha
                    }
                }
            }
        }
        else if (bitsPerPixel == 24)
        {
            for (int y = 0; y < height; y++)
            {
                byte* rowSrc = src + (y * bytesPerRow);
                int dstOffset = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    int srcOffset = x * 3;
                    int dstPixel = dstOffset + (x * 4);

                    if (isBgra)
                    {
                        // BGR -> RGBA
                        rgba[dstPixel + 0] = rowSrc[srcOffset + 2]; // Red
                        rgba[dstPixel + 1] = rowSrc[srcOffset + 1]; // Green
                        rgba[dstPixel + 2] = rowSrc[srcOffset + 0]; // Blue
                    }
                    else
                    {
                        // RGB -> RGBA
                        rgba[dstPixel + 0] = rowSrc[srcOffset + 0]; // Red
                        rgba[dstPixel + 1] = rowSrc[srcOffset + 1]; // Green
                        rgba[dstPixel + 2] = rowSrc[srcOffset + 2]; // Blue
                    }
                    rgba[dstPixel + 3] = 255; // Alpha (opaque)
                }
            }
        }

        return rgba;
    }

    private static T GetDelegate<T>(IntPtr handle, string symbol, bool throwOnError = true) where T : Delegate
    {
        IntPtr ptr = dlsym(handle, symbol);
        if (ptr == IntPtr.Zero)
        {
            if (throwOnError)
            {
                // Get error message
                IntPtr error = dlerror();
                string errorMsg = error != IntPtr.Zero ? Marshal.PtrToStringAnsi(error) : "Symbol not found";
                throw new ScreenshotException($"Failed to resolve symbol '{symbol}': {errorMsg}", "macOS");
            }
            return null;
        }
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }
}
