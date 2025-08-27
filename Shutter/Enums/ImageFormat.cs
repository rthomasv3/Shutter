namespace Shutter.Enums;

/// <summary>
/// Enum used to define supported image formats.
/// </summary>
public enum ImageFormat
{
    /// <summary>
    /// Uses <see cref="SixLabors.ImageSharp.Formats.Png.PngEncoder"/> for image encoding.
    /// </summary>
    Png,

    /// <summary>
    /// Uses <see cref="SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder"/> for image encoding.
    /// </summary>
    Jpeg
}
