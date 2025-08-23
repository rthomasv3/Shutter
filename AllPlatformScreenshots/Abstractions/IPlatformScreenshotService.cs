using AllPlatformScreenshots.Models;

namespace AllPlatformScreenshots.Abstractions;

internal interface IPlatformScreenshotService
{
    byte[] TakeScreenshot(ScreenshotOptions options);
}
