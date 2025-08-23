using System;
using System.Runtime.InteropServices;

namespace AllPlatformScreenshots;

internal static class PlatformDetector
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static bool IsWayland()
    {
        return IsLinux && Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.ToLower() == "wayland";
    }

    public static bool IsX11()
    {
        return IsLinux && Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.ToLower() == "x11";
    }
}
