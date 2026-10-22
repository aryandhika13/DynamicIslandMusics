namespace DynamicIslandDesktop.Models
{
    /// <summary>Info lagu yang sedang diputar. Thumbnail berupa data gambar mentah (byte[]), bebas dari tipe WPF.</summary>
    public record MediaInfo(string Title, string Artist, byte[]? Thumbnail);
}