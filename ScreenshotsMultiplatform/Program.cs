using System.IO;
using AllPlatformScreenshots;

namespace ScreenshotsMultiplatform;

internal class Program
{
    static void Main(string[] args)
    {
        ScreenshotService screenshotService = new();

        byte[] pngImageData = screenshotService.TakeScreenshot();

        File.WriteAllBytes("screenshot_test.png", pngImageData);
    }
}
