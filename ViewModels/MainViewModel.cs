using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIslandDesktop.Models;
using DynamicIslandDesktop.Services;

namespace DynamicIslandDesktop.ViewModels
{
    /// <summary>State yang ditampilkan island + command-nya. Bebas dari tipe WPF dan kontrol UI.</summary>
    public partial class MainViewModel : ObservableObject
    {
        private const int MaxCompactLines = 3;   // maksimal baris lirik di compact

        private readonly MediaSessionService media;
        private readonly LyricsService lyrics;
        private readonly ITicker lyricTicker;

        private List<LyricLine> lines = new();
        private string lyricsKey = "";
        private int lastLyricIndex = -2;
        private string rawTitle = "";
        private string rawArtist = "";

        [ObservableProperty, NotifyPropertyChangedFor(nameof(CompactText))]
        private string title = "Media tidak aktif";

        [ObservableProperty]
        private string artist = "-";

        [ObservableProperty]
        private byte[]? albumArt;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
        private bool isPlaying;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(CompactText))]
        private bool hasMedia;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(CompactText)), NotifyPropertyChangedFor(nameof(CompactFontSize))]
        private bool showLyrics;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(CompactText))]
        private string currentLyric = "Tidak ada media";

        [ObservableProperty]
        private string nextLyric = "";

        /// <summary>Teks di island mode compact: lirik saat mode lirik, selain itu judul.</summary>
        public string CompactText =>
            ShowLyrics ? CurrentLyric : (HasMedia ? Title : "Tidak ada media");

        // E769 = Pause, E768 = Play (Segoe MDL2 Assets)
        public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";

        // ================= UKURAN LIRIK (berskala dari pengaturan) =================
        public AppSettings Settings { get; }

        /// <summary>1.0 = ukuran default. Dipakai View untuk menskalakan ukuran island.</summary>
        public double LyricScale => Settings.LyricFontSize / AppSettings.DefaultLyricFontSize;

        public double LyricCurrentFontSize => Settings.LyricFontSize;
        public double LyricNextFontSize => 11 * LyricScale;
        public double LyricMaxHeight => 40 * LyricScale;

        /// <summary>Font teks di island compact: berskala hanya saat mode lirik.</summary>
        public double CompactFontSize => ShowLyrics ? 11 * LyricScale : 11;

        /// <summary>Tinggi maksimal lirik compact (MaxCompactLines baris, berskala).</summary>
        public double CompactLyricMaxHeight => 15 * MaxCompactLines * LyricScale;

        public MainViewModel(MediaSessionService media, LyricsService lyrics, ITicker lyricTicker, AppSettings settings)
        {
            this.media = media;
            this.lyrics = lyrics;
            this.lyricTicker = lyricTicker;   // pakai "this." karena nama parameter sama dengan field
            Settings = settings;

            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(AppSettings.LyricFontSize)) return;
                OnPropertyChanged(nameof(LyricScale));
                OnPropertyChanged(nameof(LyricCurrentFontSize));
                OnPropertyChanged(nameof(LyricNextFontSize));
                OnPropertyChanged(nameof(LyricMaxHeight));
                OnPropertyChanged(nameof(CompactFontSize));
                OnPropertyChanged(nameof(CompactLyricMaxHeight));
            };

            media.MediaChanged += OnMediaChanged;
            media.PlaybackChanged += playing => IsPlaying = playing;

            lyricTicker.Tick += OnLyricTick;
        }

        public Task InitializeAsync() => media.InitializeAsync();

        public void StopTimers() => lyricTicker.Stop();

        // ================= COMMANDS =================
        [RelayCommand] private Task PlayPauseAsync() => media.TogglePlayPauseAsync();
        [RelayCommand] private Task NextAsync() => media.NextAsync();
        [RelayCommand] private Task PrevAsync() => media.PreviousAsync();

        [RelayCommand]
        private async Task ToggleLyricsAsync()
        {
            ShowLyrics = !ShowLyrics;

            if (ShowLyrics)
            {
                lastLyricIndex = -2;   // paksa refresh teks lirik
                await LoadLyricsAsync();
                lyricTicker.Start();
            }
            else
            {
                lyricTicker.Stop();
            }
        }

        // ================= MEDIA =================
        private async void OnMediaChanged(MediaInfo? info)
        {
            if (info == null)
            {
                HasMedia = false;
                Title = "Media tidak aktif";
                Artist = "-";
                AlbumArt = null;
                rawTitle = "";
                rawArtist = "";
                lyricsKey = "";
                lines = new List<LyricLine>();
                lastLyricIndex = -2;
                SetLyric("Tidak ada media", "");
                return;
            }

            rawTitle = info.Title;
            rawArtist = info.Artist;

            HasMedia = true;
            Title = string.IsNullOrEmpty(info.Title) ? "Unknown Track" : info.Title;
            Artist = string.IsNullOrEmpty(info.Artist) ? "Unknown Artist" : info.Artist;
            AlbumArt = info.Thumbnail;

            if (ShowLyrics) await LoadLyricsAsync();
        }

        // ================= LIRIK =================
        private void SetLyric(string current, string next)
        {
            CurrentLyric = current;
            NextLyric = next;
        }

        private async Task LoadLyricsAsync()
        {
            string key = $"{rawArtist}|{rawTitle}";
            if (key == lyricsKey) return;   // lagu sama, tidak perlu load ulang
            lyricsKey = key;

            lines = new List<LyricLine>();
            lastLyricIndex = -2;

            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                SetLyric("Tidak ada media", "");
                return;
            }

            SetLyric("Memuat lirik...", "");

            var result = await lyrics.FetchAsync(rawArtist, rawTitle, media.GetDurationSeconds());

            // Jika lagu sudah berganti selama proses download, abaikan hasilnya
            if (key != lyricsKey) return;

            lines = result;
            if (lines.Count == 0)
                SetLyric("Lirik tidak ditemukan", "");
        }

        private void OnLyricTick()
        {
            if (!ShowLyrics || lines.Count == 0) return;

            var pos = media.GetPosition();
            if (pos == null) return;

            int idx = LyricsService.FindIndex(lines, pos.Value);
            if (idx == lastLyricIndex) return;
            lastLyricIndex = idx;

            string current = idx >= 0 ? lines[idx].Text : "";
            string next = idx + 1 < lines.Count ? lines[idx + 1].Text : "";
            SetLyric(string.IsNullOrWhiteSpace(current) ? "♪" : current, next);
        }
    }
}