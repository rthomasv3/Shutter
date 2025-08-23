using Shutter.Models;

namespace Shutter.Abstractions;

internal interface IPlatformScreenshotService
{
    byte[] TakeScreenshot(ScreenshotOptions options);
}
