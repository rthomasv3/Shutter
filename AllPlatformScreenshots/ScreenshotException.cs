using System;

namespace AllPlatformScreenshots;

internal class ScreenshotException : Exception
{
    public ScreenshotException(string message, string platform) : base($"{platform}: {message}") { }
    public ScreenshotException(string message, string platform, Exception inner) : base($"{platform}: {message}", inner) { }
}
