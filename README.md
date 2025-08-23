# Shutter

A cross-platform screenshot library for .NET that supports Windows, macOS, and Linux (X11/Wayland).

## Features

- Cross-platform support with native API calls
- Multiple capture targets: full screen, window, display, or region
- PNG and JPEG output formats
- No external dependencies beyond ImageSharp for image encoding
- Dependency injection friendly
- Configurable fallback behavior for unsupported features

## Installation

```bash
dotnet add package Shutter
```

## Quick Start

```csharp
using Shutter;

// Capture entire screen
ScreenshotService screenshot = new();
byte[] imageData = screenshot.TakeScreenshot();
File.WriteAllBytes("screenshot.png", imageData);
```

## Advanced Usage

```csharp
using Shutter;
using Shutter.Models;
using Shutter.Enums;

ScreenshotService screenshot = new();

// Capture a specific window
ScreenshotOptions options = new()
{
    Target = CaptureTarget.Window,
    WindowHandle = process.MainWindowHandle,
    IncludeBorder = true,
    IncludeShadow = false,
    Format = ImageFormat.Jpeg,
    JpegQuality = 85
};

byte[] imageData = screenshot.TakeScreenshot(options);
```

## Platform Support

| Feature | Windows | macOS | Linux/X11 | Linux/Wayland |
|---------|---------|-------|-----------|---------------|
| Full Screen | ✅ | ✅ | ✅ | ✅ |
| Window Capture | ✅ | ❌ | ✅ | ❌ |
| Display Selection | ✅ | ✅ | ✅ | ❌ |
| Region Capture | ✅ | ✅ | ✅ | ❌ |
| Interactive Mode | ❌ | ❌ | ❌ | ✅ |
| Border Control | ✅ | ❌ | ❌ | ❌ |
| Shadow Control | ✅ | ❌ | ❌ | ❌ |

## Examples

### Capture Specific Display

```csharp
ScreenshotOptions options = new()
{
    Target = CaptureTarget.Display,
    DisplayIndex = 1  // Zero-based index
};
byte[] imageData = screenshot.TakeScreenshot(options);
```

### Capture Screen Region

```csharp
ScreenshotOptions options = new()
{
    Target = CaptureTarget.Region,
    Region = new Rectangle { X = 100, Y = 100, Width = 800, Height = 600 }
};
byte[] imageData = screenshot.TakeScreenshot(options);
```

### Wayland Interactive Mode

```csharp
ScreenshotOptions options = new()
{
    Interactive = true,
    Timeout = TimeSpan.FromSeconds(30)
};
byte[] imageData = screenshot.TakeScreenshot(options);
```

## Fallback Behavior

Configure how the library handles unsupported features:

```csharp
ScreenshotOptions options = new()
{
    Target = CaptureTarget.Window,
    WindowHandle = handle,
    Fallback = FallbackBehavior.ThrowException  // Throws if feature unsupported
};
```

Available fallback behaviors:
- `ThrowException` - Throws `PlatformNotSupportedException` for unsupported features
- `Default` - Falls back to full screen capture
- `BestEffort` - Attempts the closest available alternative

## Dependency Injection

```csharp
services.AddSingleton<IScreenshotService, ScreenshotService>();
```

## Requirements

- .NET 8.0 or higher
- Windows: Windows 7 or higher
- macOS: macOS 10.12 or higher (uses CoreGraphics framework)
- Linux: X11 or Wayland session with appropriate libraries
  - X11: `libX11` installed
  - Wayland: DBus and XDG Desktop Portal support

## Implementation Details

Shutter uses platform-specific native APIs:

- **Windows**: Win32 GDI and User32 APIs
- **macOS**: CoreGraphics framework (`CGDisplayCreateImage`)
- **Linux/X11**: libX11 (`XGetImage`)
- **Linux/Wayland**: DBus communication with XDG Desktop Portal

## Known Limitations

- Window capture is not supported on macOS (cannot obtain window IDs from .NET Process)
- Wayland only supports full screen capture or interactive selection due to security model
- Interactive mode is only available on Wayland
- Border and shadow control only available on Windows