using System.Windows.Media;

namespace DynamicIslandDesktop.Models
{
    /// <summary>Info lagu yang sedang diputar. Thumbnail sudah di-Freeze sehingga aman lintas thread.</summary>
    public record MediaInfo(string Title, string Artist, ImageSource? Thumbnail);
}