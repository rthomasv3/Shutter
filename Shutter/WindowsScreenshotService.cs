using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Shutter.Abstractions;
using Shutter.Enums;
using Shutter.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Shutter;

internal class WindowsScreenshotService : IPlatformScreenshotService
{
    #region Native Structures and Constants

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmih, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, ref RECT rectangle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
    private static extern IntPtr GdiSelectObject(IntPtr hdc, IntPtr hgdiobj);

    private const int SM_CXSCREEN = 0; // Screen width
    private const int SM_CYSCREEN = 1; // Screen height
    private const int SM_CXVIRTUALSCREEN = 78; // Virtual screen width
    private const int SM_CYVIRTUALSCREEN = 79; // Virtual screen height
    private const int SM_XVIRTUALSCREEN = 76; // Virtual screen left
    private const int SM_YVIRTUALSCREEN = 77; // Virtual screen top
    private const int SRCCOPY = 0x00CC0020; // BitBlt copy mode
    private const uint BI_RGB = 0; // No compression
    private const uint DIB_RGB_COLORS = 0; // Color table in RGBs
    private const int HEADER_HEIGHT = 32;
    private const int SHADOW_SIZE = 7;

    #endregion

    #region Public Methods

    public byte[] TakeScreenshot(ScreenshotOptions options)
    {
        byte[] imageData = null;

        switch (options.Target)
        {
            case CaptureTarget.FullScreen:
                imageData = CaptureFullScreen(options);
                break;

            case CaptureTarget.Window:
                imageData = CaptureWindow(options);
                break;

            case CaptureTarget.Display:
                imageData = CaptureDisplay(options);
                break;

            case CaptureTarget.Region:
                imageData = CaptureRegion(options);
                break;

            default:
                throw new ArgumentException($"Unsupported capture target: {options.Target}");
        }

        return imageData;
    }

    #endregion

    #region Private Capture Methods

    private byte[] CaptureFullScreen(ScreenshotOptions options)
    {
        // Capture all monitors (virtual screen)
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // If virtual screen metrics fail, fall back to primary monitor
        if (width == 0 || height == 0)
        {
            x = 0;
            y = 0;
            width = GetSystemMetrics(SM_CXSCREEN);
            height = GetSystemMetrics(SM_CYSCREEN);
        }

        return CaptureScreen(x, y, width, height, options);
    }

    private byte[] CaptureWindow(ScreenshotOptions options)
    {
        if (!options.WindowHandle.HasValue || options.WindowHandle.Value == IntPtr.Zero)
        {
            throw new ArgumentException("WindowHandle is required for Window capture");
        }

        RECT rectangle = new();
        if (!GetWindowRect(options.WindowHandle.Value, ref rectangle))
        {
            throw new ScreenshotException("Failed to get window rectangle", "Windows");
        }

        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            throw new ScreenshotException("Window has invalid dimensions", "Windows");
        }

        // Apply border and shadow adjustments
        int shadowAdjustment = options.IncludeShadow ? 0 : SHADOW_SIZE;
        int headerAdjustment = options.IncludeBorder ? 0 : HEADER_HEIGHT;

        int width = rectangle.Width - (shadowAdjustment * 2);
        int height = rectangle.Height - headerAdjustment - shadowAdjustment;
        int left = rectangle.Left + shadowAdjustment;
        int top = rectangle.Top + headerAdjustment;

        if (width <= 0 || height <= 0)
        {
            // Fall back to full window if adjustments result in invalid dimensions
            width = rectangle.Width;
            height = rectangle.Height;
            left = rectangle.Left;
            top = rectangle.Top;
        }

        return CaptureScreen(left, top, width, height, options);
    }

    private byte[] CaptureDisplay(ScreenshotOptions options)
    {
        if (!options.DisplayIndex.HasValue)
        {
            throw new ArgumentException("DisplayIndex is required for Display capture");
        }

        // Enumerate monitors to find the one at the specified index
        MonitorEnumData enumData = new()
        {
            TargetIndex = options.DisplayIndex.Value,
            CurrentIndex = 0,
            Found = false,
            MonitorRect = new RECT()
        };

        GCHandle handle = GCHandle.Alloc(enumData);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumCallback, GCHandle.ToIntPtr(handle));

            if (!enumData.Found)
            {
                throw new ArgumentException($"Display index {options.DisplayIndex.Value} not found");
            }

            RECT rect = enumData.MonitorRect;
            return CaptureScreen(rect.Left, rect.Top, rect.Width, rect.Height, options);
        }
        finally
        {
            handle.Free();
        }
    }

    private byte[] CaptureRegion(ScreenshotOptions options)
    {
        if (!options.Region.HasValue)
        {
            throw new ArgumentException("Region is required for Region capture");
        }

        Models.Rectangle region = options.Region.Value;

        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentException("Region must have positive width and height");
        }

        return CaptureScreen(region.X, region.Y, region.Width, region.Height, options);
    }

    #endregion

    #region Core Capture Implementation

    private unsafe byte[] CaptureScreen(int srcX, int srcY, int width, int height, ScreenshotOptions options)
    {
        IntPtr screenDC = IntPtr.Zero;
        IntPtr memDC = IntPtr.Zero;
        IntPtr dibSection = IntPtr.Zero;
        IntPtr bitsPtr = IntPtr.Zero;
        byte[] result = null;

        try
        {
            // Get screen DC
            screenDC = GetDC(IntPtr.Zero);
            if (screenDC == IntPtr.Zero)
                throw new ScreenshotException("Failed to get screen DC.", "Windows");

            // Create memory DC
            memDC = CreateCompatibleDC(screenDC);
            if (memDC == IntPtr.Zero)
                throw new ScreenshotException("Failed to create memory DC.", "Windows");

            // Set up BITMAPINFOHEADER for 32-bit BGRA
            BITMAPINFOHEADER bmih = new()
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // Negative for top-down bitmap
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
                biSizeImage = 0,
                biXPelsPerMeter = 0,
                biYPelsPerMeter = 0,
                biClrUsed = 0,
                biClrImportant = 0
            };

            // Create DIB section
            dibSection = CreateDIBSection(IntPtr.Zero, ref bmih, DIB_RGB_COLORS, out bitsPtr, IntPtr.Zero, 0);
            if (dibSection == IntPtr.Zero)
                throw new ScreenshotException("Failed to create DIB section.", "Windows");

            // Select DIB into memory DC
            IntPtr oldBitmap = GdiSelectObject(memDC, dibSection);
            if (oldBitmap == IntPtr.Zero)
                throw new ScreenshotException("Failed to select DIB section.", "Windows");

            // Copy screen pixels
            if (!BitBlt(memDC, 0, 0, width, height, screenDC, srcX, srcY, SRCCOPY))
                throw new ScreenshotException("Failed to capture screen with BitBlt.", "Windows");

            // Convert BGRA to RGBA
            byte[] rgbaData = ConvertBgraToRgba(bitsPtr, width, height, width * 4);

            // Encode to requested format
            using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(rgbaData, width, height);
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

            result = ms.ToArray();
        }
        catch (DllNotFoundException ex)
        {
            throw new ScreenshotException("Required Windows API (user32.dll or gdi32.dll) not found.", "Windows", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new ScreenshotException("Required Windows API function not found.", "Windows", ex);
        }
        finally
        {
            if (dibSection != IntPtr.Zero) DeleteObject(dibSection);
            if (memDC != IntPtr.Zero) DeleteDC(memDC);
            if (screenDC != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDC);
        }

        return result;
    }

    private unsafe byte[] ConvertBgraToRgba(IntPtr bits, int width, int height, int stride)
    {
        byte* src = (byte*)bits;
        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            byte* rowSrc = src + (y * stride);
            int dstOffset = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                int srcOffset = x * 4;
                int dstPixel = dstOffset + (x * 4);
                rgba[dstPixel + 0] = rowSrc[srcOffset + 2]; // Red (from BGR)
                rgba[dstPixel + 1] = rowSrc[srcOffset + 1]; // Green
                rgba[dstPixel + 2] = rowSrc[srcOffset + 0]; // Blue
                rgba[dstPixel + 3] = rowSrc[srcOffset + 3]; // Alpha (usually 255)
            }
        }

        return rgba;
    }

    private static RECT GetWindowRect(IntPtr hwnd)
    {
        RECT rectangle = default;
        int attempts = 0;

        while (attempts++ < 5)
        {
            rectangle = new RECT();
            GetWindowRect(hwnd, ref rectangle);

            if (rectangle.Width > 0 && rectangle.Height > 0)
            {
                break;
            }

            Thread.Sleep(50);
        }

        return rectangle;
    }

    #endregion

    #region Monitor Enumeration

    private class MonitorEnumData
    {
        public int TargetIndex { get; set; }
        public int CurrentIndex { get; set; }
        public bool Found { get; set; }
        public RECT MonitorRect { get; set; }
    }

    private static bool MonitorEnumCallback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
    {
        GCHandle handle = GCHandle.FromIntPtr(dwData);
        MonitorEnumData data = (MonitorEnumData)handle.Target;

        if (data.CurrentIndex == data.TargetIndex)
        {
            MONITORINFO info = new()
            {
                cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
            };

            if (GetMonitorInfo(hMonitor, ref info))
            {
                data.MonitorRect = info.rcMonitor;
                data.Found = true;
            }

            return false; // Stop enumeration
        }

        data.CurrentIndex++;
        return true; // Continue enumeration
    }

    #endregion
}
