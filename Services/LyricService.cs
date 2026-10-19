using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DynamicIslandDesktop.Models;

namespace DynamicIslandDesktop.Services
{
    /// <summary>Mengambil dan mem-parsing lirik tersinkron (LRC) dari LRCLIB. Tidak menyentuh UI.</summary>
    public class LyricsService
    {
        private static readonly HttpClient http = new();
        private static readonly Regex TimeTag = new(@"\[(\d+):(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);

        static LyricsService()
        {
            // LRCLIB meminta User-Agent
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIslandDesktop/1.0");
        }

        public async Task<List<LyricLine>> FetchAsync(string artist, string title, int durationSeconds)
        {
            string? synced = null;

            try
            {
                string a = Uri.EscapeDataString(artist);
                string t = Uri.EscapeDataString(title);

                // Durasi lagu membantu LRCLIB mencocokkan versi yang tepat
                string getUrl = $"https://lrclib.net/api/get?artist_name={a}&track_name={t}";
                if (durationSeconds > 0) getUrl += $"&duration={durationSeconds}";

                var resp = await http.GetAsync(getUrl);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    synced = GetSynced(doc.RootElement);
                }

                // Cadangan: pakai pencarian jika /get tidak menemukan
                if (string.IsNullOrEmpty(synced))
                {
                    var sresp = await http.GetAsync($"https://lrclib.net/api/search?artist_name={a}&track_name={t}");
                    if (sresp.IsSuccessStatusCode)
                    {
                        using var sdoc = JsonDocument.Parse(await sresp.Content.ReadAsStringAsync());
                        foreach (var item in sdoc.RootElement.EnumerateArray())
                        {
                            synced = GetSynced(item);
                            if (!string.IsNullOrEmpty(synced)) break;
                        }
                    }
                }
            }
            catch { }

            return string.IsNullOrEmpty(synced) ? new List<LyricLine>() : ParseLrc(synced);
        }

        /// <summary>Index baris lirik yang aktif pada posisi tertentu (-1 jika belum ada).</summary>
        public static int FindIndex(IReadOnlyList<LyricLine> lines, TimeSpan position)
        {
            int idx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Time <= position) idx = i;
                else break;
            }
            return idx;
        }

        private static string? GetSynced(JsonElement el) =>
            el.TryGetProperty("syncedLyrics", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;

        private static List<LyricLine> ParseLrc(string lrc)
        {
            var list = new List<LyricLine>();

            foreach (var line in lrc.Split('\n'))
            {
                var matches = TimeTag.Matches(line);
                if (matches.Count == 0) continue;

                string text = TimeTag.Replace(line, "").Trim();
                foreach (Match m in matches)
                {
                    double min = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    double sec = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    list.Add(new LyricLine(TimeSpan.FromSeconds(min * 60 + sec), text));
                }
            }

            return list.OrderBy(x => x.Time).ToList();
        }
    }
}