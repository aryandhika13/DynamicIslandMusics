using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIslandDesktop.Models;
using DynamicIslandDesktop.Services;

namespace DynamicIslandDesktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const int MaxCompactLines = 3;

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

        // ===== PROPERTI BARU UNTUK PROGRESS BAR & DURASI =====
        [ObservableProperty]
        private double progressPercent;

        [ObservableProperty]
        private string positionText = "0:00";

        [ObservableProperty]
        private string durationText = "0:00";

        public string CompactText =>
            ShowLyrics ? CurrentLyric : (HasMedia ? Title : "Tidak ada media");

        public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768";

        public AppSettings Settings { get; }

        public double LyricScale => Settings.LyricFontSize / AppSettings.DefaultLyricFontSize;
        public double LyricCurrentFontSize => Settings.LyricFontSize;
        public double LyricNextFontSize => 11 * LyricScale;
        public double LyricMaxHeight => 40 * LyricScale;
        public double CompactFontSize => ShowLyrics ? 11 * LyricScale : 11;
        public double CompactLyricMaxHeight => 15 * MaxCompactLines * LyricScale;

        public MainViewModel(MediaSessionService media, LyricsService lyrics, ITicker lyricTicker, AppSettings settings)
        {
            this.media = media;
            this.lyrics = lyrics;
            this.lyricTicker = lyricTicker;
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
            media.PlaybackChanged += playing =>
            {
                IsPlaying = playing;
                UpdateTickerState();
                UpdateTimelineUI();
            };
            media.TimelineChanged += UpdateTimelineUI;

            lyricTicker.Tick += OnTick;
        }

        public Task InitializeAsync() => media.InitializeAsync();

        public void StopTimers() => lyricTicker.Stop();

        private void UpdateTickerState()
        {
            if (HasMedia && (IsPlaying || ShowLyrics))
                lyricTicker.Start();
            else
                lyricTicker.Stop();
        }

        // ================= COMMANDS =================
        [RelayCommand] private Task PlayPauseAsync() => media.TogglePlayPauseAsync();
        [RelayCommand] private Task NextAsync() => media.NextAsync();
        [RelayCommand] private Task PrevAsync() => media.PreviousAsync();

        [RelayCommand]
        private async Task ToggleLyricsAsync()
        {
            ShowLyrics = !ShowLyrics;
            UpdateTickerState();

            if (ShowLyrics)
            {
                lastLyricIndex = -2;
                await LoadLyricsAsync();
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
                ResetTimelineUI();
                UpdateTickerState();
                return;
            }

            rawTitle = info.Title;
            rawArtist = info.Artist;

            HasMedia = true;
            Title = string.IsNullOrEmpty(info.Title) ? "Unknown Track" : info.Title;
            Artist = string.IsNullOrEmpty(info.Artist) ? "Unknown Artist" : info.Artist;
            AlbumArt = info.Thumbnail;

            UpdateTimelineUI();
            UpdateTickerState();

            if (ShowLyrics) await LoadLyricsAsync();
        }

        // ================= PROGRESS BAR & WAKTU =================
        private void ResetTimelineUI()
        {
            ProgressPercent = 0;
            PositionText = "0:00";
            DurationText = "0:00";
        }

        private void UpdateTimelineUI()
        {
            if (!HasMedia)
            {
                ResetTimelineUI();
                return;
            }

            var (pos, duration) = media.GetTimeline();
            if (duration <= TimeSpan.Zero)
            {
                ResetTimelineUI();
                return;
            }

            ProgressPercent = Math.Clamp((pos.TotalSeconds / duration.TotalSeconds) * 100.0, 0, 100);
            PositionText = FormatTime(pos);
            DurationText = FormatTime(duration);
        }

        private static string FormatTime(TimeSpan time) =>
            time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

        // ================= LIRIK =================
        private void SetLyric(string current, string next)
        {
            CurrentLyric = current;
            NextLyric = next;
        }

        private async Task LoadLyricsAsync()
        {
            string key = $"{rawArtist}|{rawTitle}";
            if (key == lyricsKey) return;
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

            if (key != lyricsKey) return;

            lines = result;
            if (lines.Count == 0)
                SetLyric("Lirik tidak ditemukan", "");
        }

        // Dipanggil setiap ticker berdetak: update progress bar + lirik
        private void OnTick()
        {
            UpdateTimelineUI();

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