using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Shutter.Abstractions;
using Shutter.Enums;
using Shutter.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Shutter;

internal class X11ScreenshotService : IPlatformScreenshotService
{
    #region Native Methods and Structures

    [DllImport("libX11", EntryPoint = "XOpenDisplay")]
    private static extern IntPtr XOpenDisplay(string display_name);

    [DllImport("libX11", EntryPoint = "XCloseDisplay")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11", EntryPoint = "XDefaultRootWindow")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11", EntryPoint = "XGetImage")]
    private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong plane_mask, int format);

    [DllImport("libX11", EntryPoint = "XDestroyImage")]
    private static extern void XDestroyImage(IntPtr image);

    [DllImport("libX11", EntryPoint = "XGetWindowAttributes")]
    private static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes attributes);

    [DllImport("libX11", EntryPoint = "XScreenCount")]
    private static extern int XScreenCount(IntPtr display);

    [DllImport("libX11", EntryPoint = "XRootWindow")]
    private static extern IntPtr XRootWindow(IntPtr display, int screen_number);

    [DllImport("libX11", EntryPoint = "XDisplayWidth")]
    private static extern int XDisplayWidth(IntPtr display, int screen_number);

    [DllImport("libX11", EntryPoint = "XDisplayHeight")]
    private static extern int XDisplayHeight(IntPtr display, int screen_number);

    [StructLayout(LayoutKind.Sequential)]
    private struct XWindowAttributes
    {
        public int x, y;
        public int width, height;
        public int border_width;
        public int depth;
        public IntPtr visual;
        public IntPtr root;
        public int class_;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public ulong backing_planes;
        public ulong backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed;
        public int map_state;
        public long all_event_masks;
        public long your_event_mask;
        public long do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int width, height;
        public int xoffset;
        public int format;
        public IntPtr data;
        public int byte_order;
        public int bitmap_unit;
        public int bitmap_bit_order;
        public int bitmap_pad;
        public int depth;
        public int bytes_per_line;
        public int bits_per_pixel;
        public ulong red_mask;
        public ulong green_mask;
        public ulong blue_mask;
        // ... other fields
    }

    private const string X11_LIB = "libX11";
    private const int ZPixmap = 2;
    private const ulong AllPlanes = ~0UL;

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
        IntPtr display = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        byte[] result = null;

        try
        {
            // Open display
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
                throw new ScreenshotException("Failed to open X11 display. Ensure libX11 is installed and an X11 session is active.", "X11");

            // Get root window of default screen
            IntPtr root = XDefaultRootWindow(display);

            // Get screen dimensions
            if (XGetWindowAttributes(display, root, out XWindowAttributes attributes) == 0)
                throw new ScreenshotException("Failed to get window attributes for root window.", "X11");

            int width = attributes.width;
            int height = attributes.height;

            // Capture image
            image = XGetImage(display, root, 0, 0, (uint)width, (uint)height, AllPlanes, ZPixmap);
            if (image == IntPtr.Zero)
                throw new ScreenshotException("Failed to capture X11 screenshot.", "X11");

            // Process and encode the image
            result = ProcessAndEncodeImage(image, width, height, options);
        }
        catch (DllNotFoundException ex)
        {
            throw new ScreenshotException($"X11 library ({X11_LIB}) not found. Ensure libX11 is installed.", "X11", ex);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new ScreenshotException($"Required X11 function not found in {X11_LIB}. Ensure a compatible version is installed.", "X11", ex);
        }
        finally
        {
            if (image != IntPtr.Zero)
                XDestroyImage(image);
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }

        return result;
    }

    private byte[] CaptureWindow(ScreenshotOptions options)
    {
        if (!options.WindowHandle.HasValue || options.WindowHandle.Value == IntPtr.Zero)
        {
            throw new ArgumentException("WindowHandle is required for Window capture");
        }

        IntPtr display = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        byte[] result = null;

        try
        {
            // Open display
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
                throw new ScreenshotException("Failed to open X11 display.", "X11");

            IntPtr window = options.WindowHandle.Value;

            // Get window dimensions
            if (XGetWindowAttributes(display, window, out XWindowAttributes attributes) == 0)
                throw new ScreenshotException("Failed to get window attributes.", "X11");

            int width = attributes.width;
            int height = attributes.height;

            if (width <= 0 || height <= 0)
                throw new ScreenshotException("Window has invalid dimensions.", "X11");

            // Capture the window
            image = XGetImage(display, window, 0, 0, (uint)width, (uint)height, AllPlanes, ZPixmap);
            if (image == IntPtr.Zero)
                throw new ScreenshotException("Failed to capture window screenshot.", "X11");

            // Process and encode the image
            result = ProcessAndEncodeImage(image, width, height, options);
        }
        finally
        {
            if (image != IntPtr.Zero)
                XDestroyImage(image);
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }

        return result;
    }

    private byte[] CaptureDisplay(ScreenshotOptions options)
    {
        if (!options.DisplayIndex.HasValue)
        {
            throw new ArgumentException("DisplayIndex is required for Display capture");
        }

        IntPtr display = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        byte[] result = null;

        try
        {
            // Open display
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
                throw new ScreenshotException("Failed to open X11 display.", "X11");

            // Check if the screen index is valid
            int screenCount = XScreenCount(display);
            if (options.DisplayIndex.Value >= screenCount)
            {
                throw new ArgumentException($"Display index {options.DisplayIndex.Value} is out of range. Available screens: 0-{screenCount - 1}");
            }

            // Get root window of specified screen
            IntPtr root = XRootWindow(display, options.DisplayIndex.Value);

            // Get screen dimensions
            int width = XDisplayWidth(display, options.DisplayIndex.Value);
            int height = XDisplayHeight(display, options.DisplayIndex.Value);

            if (width <= 0 || height <= 0)
                throw new ScreenshotException("Display has invalid dimensions.", "X11");

            // Capture the screen
            image = XGetImage(display, root, 0, 0, (uint)width, (uint)height, AllPlanes, ZPixmap);
            if (image == IntPtr.Zero)
                throw new ScreenshotException($"Failed to capture display {options.DisplayIndex.Value}.", "X11");

            // Process and encode the image
            result = ProcessAndEncodeImage(image, width, height, options);
        }
        finally
        {
            if (image != IntPtr.Zero)
                XDestroyImage(image);
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }

        return result;
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

        IntPtr display = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        byte[] result = null;

        try
        {
            // Open display
            display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
                throw new ScreenshotException("Failed to open X11 display.", "X11");

            // Get root window
            IntPtr root = XDefaultRootWindow(display);

            // Capture the specified region
            image = XGetImage(display, root, region.X, region.Y,
                            (uint)region.Width, (uint)region.Height,
                            AllPlanes, ZPixmap);
            if (image == IntPtr.Zero)
                throw new ScreenshotException("Failed to capture region screenshot.", "X11");

            // Process and encode the image
            result = ProcessAndEncodeImage(image, region.Width, region.Height, options);
        }
        finally
        {
            if (image != IntPtr.Zero)
                XDestroyImage(image);
            if (display != IntPtr.Zero)
                XCloseDisplay(display);
        }

        return result;
    }

    #endregion

    #region Image Processing

    private byte[] ProcessAndEncodeImage(IntPtr image, int width, int height, ScreenshotOptions options)
    {
        // Get XImage details
        XImage xImage = Marshal.PtrToStructure<XImage>(image);

        // Validate format
        if (xImage.format != ZPixmap)
            throw new ScreenshotException($"Unsupported XImage format: {xImage.format}. Expected ZPixmap.", "X11");

        // Convert to RGBA byte array
        byte[] rgbaData = ConvertXImageToRgba(xImage, width, height);

        // Encode as requested format
        byte[] result;
        using Image<Rgba32> imageSharp = Image<Rgba32>.LoadPixelData<Rgba32>(rgbaData, width, height);
        using MemoryStream ms = new();

        switch (options.Format)
        {
            case ImageFormat.Png:
                imageSharp.Save(ms, new PngEncoder());
                break;

            case ImageFormat.Jpeg:
                imageSharp.Save(ms, new JpegEncoder { Quality = options.JpegQuality });
                break;

            default:
                throw new ArgumentException($"Unsupported image format: {options.Format}");
        }

        result = ms.ToArray();
        return result;
    }

    private unsafe byte[] ConvertXImageToRgba(XImage xImage, int width, int height)
    {
        byte* src = (byte*)xImage.data;
        byte[] rgba = new byte[width * height * 4]; // Always output RGBA

        int redShift = BitOperations.TrailingZeroCount(xImage.red_mask);
        int greenShift = BitOperations.TrailingZeroCount(xImage.green_mask);
        int blueShift = BitOperations.TrailingZeroCount(xImage.blue_mask);

        // Determine if BGR (blue at lower bit position than red)
        bool isBgr = blueShift < redShift;

        // Handle different bits per pixel
        if (xImage.bits_per_pixel == 32)
        {
            for (int y = 0; y < height; y++)
            {
                byte* rowSrc = src + (y * xImage.bytes_per_line);
                int dstOffset = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    uint pixel = *(uint*)(rowSrc + (x * 4));
                    rgba[dstOffset + (x * 4) + 0] = (byte)((pixel & xImage.red_mask) >> redShift);   // Red
                    rgba[dstOffset + (x * 4) + 1] = (byte)((pixel & xImage.green_mask) >> greenShift); // Green
                    rgba[dstOffset + (x * 4) + 2] = (byte)((pixel & xImage.blue_mask) >> blueShift);  // Blue
                    rgba[dstOffset + (x * 4) + 3] = 255; // Alpha (assume opaque, X11 screenshots typically lack transparency)
                }
            }
        }
        else if (xImage.bits_per_pixel == 24)
        {
            for (int y = 0; y < height; y++)
            {
                byte* rowSrc = src + (y * xImage.bytes_per_line);
                int dstOffset = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    // Read 3 bytes (packed RGB/BGR)
                    byte* pixelPtr = rowSrc + (x * 3);
                    byte b0 = pixelPtr[0];
                    byte b1 = pixelPtr[1];
                    byte b2 = pixelPtr[2];

                    // Assume BGR if blue mask is at lowest bits, else RGB
                    if (isBgr)
                    {
                        rgba[dstOffset + (x * 4) + 0] = b2; // Red
                        rgba[dstOffset + (x * 4) + 1] = b1; // Green
                        rgba[dstOffset + (x * 4) + 2] = b0; // Blue
                    }
                    else
                    {
                        rgba[dstOffset + (x * 4) + 0] = b0; // Red
                        rgba[dstOffset + (x * 4) + 1] = b1; // Green
                        rgba[dstOffset + (x * 4) + 2] = b2; // Blue
                    }
                    rgba[dstOffset + (x * 4) + 3] = 255; // Alpha
                }
            }
        }
        else if (xImage.bits_per_pixel == 16)
        {
            // Assume RGB565 (5 bits red, 6 green, 5 blue)
            for (int y = 0; y < height; y++)
            {
                byte* rowSrc = src + (y * xImage.bytes_per_line);
                int dstOffset = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    ushort pixel = *(ushort*)(rowSrc + (x * 2));
                    byte red = (byte)(((pixel & xImage.red_mask) >> redShift) * 255 / 31);   // 5 bits
                    byte green = (byte)(((pixel & xImage.green_mask) >> greenShift) * 255 / 63); // 6 bits
                    byte blue = (byte)(((pixel & xImage.blue_mask) >> blueShift) * 255 / 31);  // 5 bits

                    rgba[dstOffset + (x * 4) + 0] = red;
                    rgba[dstOffset + (x * 4) + 1] = green;
                    rgba[dstOffset + (x * 4) + 2] = blue;
                    rgba[dstOffset + (x * 4) + 3] = 255; // Alpha
                }
            }
        }
        else
        {
            throw new ScreenshotException($"Unsupported X11 bits per pixel: {xImage.bits_per_pixel}. Expected 16, 24, or 32.", "X11");
        }

        return rgba;
    }

    #endregion
}
