using System.Diagnostics;
using System.IO;
using System.Linq;
using Shutter.Enums;
using Shutter.Models;

namespace Shutter.TestConsole;

internal class Program
{
    static void Main(string[] args)
    {
        // simple test
        ShutterService screenshotService = new();
        byte[] pngImageData = screenshotService.TakeScreenshot();
        File.WriteAllBytes("screenshot_test.png", pngImageData);

        // advanced test 
        Process process = Process.GetProcesses().FirstOrDefault(x => x.ProcessName.Contains("Jopli") && x.MainWindowHandle > 0);
        byte[] windowImage = screenshotService.TakeScreenshot(new ScreenshotOptions()
        {
            Target = CaptureTarget.Window,
            WindowHandle = process.MainWindowHandle,
            IncludeBorder = true,
            IncludeShadow = true,
            Format = ImageFormat.Jpeg,
            JpegQuality = 85
        });
        File.WriteAllBytes("screenshot_window_test.png", windowImage);


    }
}
