using System;

namespace DynamicIslandDesktop.Models
{
    /// <summary>Satu baris lirik yang tersinkron dengan waktu lagu.</summary>
    public record LyricLine(TimeSpan Time, string Text);
}