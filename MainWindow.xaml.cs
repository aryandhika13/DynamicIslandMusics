using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace DynamicIslandDesktop
{
    public partial class MainWindow : Window
    {
        private bool isExpanded = false;
        private bool isRotating = false;
        private bool isPillVisible = false;
        private bool isExiting = false;
        private GlobalSystemMediaTransportControlsSessionManager? sessionManager;
        private GlobalSystemMediaTransportControlsSession? currentSession;

        private static readonly HttpClient http = new HttpClient();
        private readonly DispatcherTimer lyricTimer;
        private bool showLyrics = false;
        private List<(TimeSpan Time, string Text)> lyricLines = new();
        private string lyricsKey = "";
        private int lastLyricIndex = -2;
        private string currentTitle = "";
        private string currentArtist = "";

        private const double CompactWidth = 180;        // compact biasa
        private const double CompactLyricWidth = 320;   // compact saat mode lirik
        private const double ExpandedWidth = 360;
        private bool isIntroPlaying = true;

        private double CollapsedWidth => showLyrics ? CompactLyricWidth : CompactWidth;

        static MainWindow()
        {
            // LRCLIB meminta User-Agent
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIslandDesktop/1.0");
        }

        public MainWindow()
        {
            InitializeComponent();

            lyricTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            lyricTimer.Tick += LyricTimer_Tick;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Posisikan window di tengah atas layar
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = 0;

            PlayIntroAnimation();

            // Inisialisasi Media Transport Controller API
            await InitMediaSessionAsync();
        }

        private async Task InitMediaSessionAsync()
        {
            sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (sessionManager != null)
            {
                // Event ketika aplikasi pemutar media berubah (misal dari Spotify pindah ke Chrome/YouTube)
                sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;

                // Update session pertama kali
                UpdateCurrentSession(sessionManager.GetCurrentSession());
            }
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateCurrentSession(sender.GetCurrentSession());
            });
        }

        private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? session)
        {
            if (currentSession != null)
            {
                currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
            }

            currentSession = session;

            if (currentSession != null)
            {
                currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;

                _ = UpdateMediaInfoAsync();
                UpdatePlaybackInfo();
            }
            else
            {
                SetLyric("Tidak ada media", "");
                TitleText.Text = "Media tidak aktif";
                ArtistText.Text = "-";
                CompactAlbumArt.Source = null;
                ExpandedAlbumArt.Source = null;
                currentTitle = "";
                currentArtist = "";
                lyricsKey = "";
                lyricLines.Clear();
                LyricCurrent.Text = "Tidak ada media";
                LyricNext.Text = "";
                StopRotation();
            }
        }

        private async void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await UpdateMediaInfoAsync();
            });
        }

        private void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                UpdatePlaybackInfo();
            });
        }

        // ================= ANIMASI PEMBUKA =================
        private void PlayIntroAnimation()
        {
            // 1. Album art muncul di tengah (membesar + fade in)
            var popAnim = new DoubleAnimation
            {
                From = 0.3,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
            };
            IntroScale.BeginAnimation(ScaleTransform.ScaleXProperty, popAnim);
            IntroScale.BeginAnimation(ScaleTransform.ScaleYProperty, popAnim);

            AlbumContainer.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

            // 2. Island melebar, album art ikut bergeser ke kiri
            var widthAnim = new DoubleAnimation
            {
                To = CompactWidth,
                BeginTime = TimeSpan.FromMilliseconds(700),
                Duration = TimeSpan.FromMilliseconds(550),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            widthAnim.Completed += (s, e) => isIntroPlaying = false;
            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

            // 3. Judul dan ikon musik muncul perlahan
            var fadeAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                BeginTime = TimeSpan.FromMilliseconds(1000),
                Duration = TimeSpan.FromMilliseconds(400)
            };
            CompactTitleText.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            CompactMusicIcon.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        private async Task UpdateMediaInfoAsync()
        {
            if (currentSession == null) return;

            try
            {
                var props = await currentSession.TryGetMediaPropertiesAsync();
                if (props != null)
                {
                    TitleText.Text = string.IsNullOrEmpty(props.Title) ? "Unknown Track" : props.Title;
                    ArtistText.Text = string.IsNullOrEmpty(props.Artist) ? "Unknown Artist" : props.Artist;
                    if (!showLyrics)
                        CompactTitleText.Text = string.IsNullOrEmpty(props.Title) ? "Unknown Track" : props.Title;

                    currentTitle = props.Title ?? "";
                    currentArtist = props.Artist ?? "";

                    // Ambil Album Art / Thumbnail YouTube/Spotify
                    if (props.Thumbnail != null)
                    {
                        IRandomAccessStreamWithContentType stream = await props.Thumbnail.OpenReadAsync();
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream.AsStream();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        CompactAlbumArt.Source = bitmap;
                        ExpandedAlbumArt.Source = bitmap;
                    }
                    else
                    {
                        CompactAlbumArt.Source = null;
                        ExpandedAlbumArt.Source = null;
                    }

                    if (showLyrics) await LoadLyricsAsync();
                }
            }
            catch { }
        }

        private void UpdatePlaybackInfo()
        {
            if (currentSession == null) return;

            var playback = currentSession.GetPlaybackInfo();
            if (playback != null)
            {
                bool isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                // E769 = Pause, E768 = Play (Segoe MDL2 Assets)
                BtnPlayPause.Content = isPlaying ? "\uE769" : "\uE768";

                if (isPlaying) StartRotation();
                else StopRotation();
            }
        }

        // ================= ROTASI ALBUM ART =================
        private void StartRotation()
        {
            if (isRotating) return;
            isRotating = true;

            double start = AlbumRotate.Angle % 360;
            var anim = new DoubleAnimation
            {
                From = start,
                To = start + 360,
                Duration = TimeSpan.FromSeconds(8),   // makin kecil = makin cepat
                RepeatBehavior = RepeatBehavior.Forever
            };
            AlbumRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void StopRotation()
        {
            if (!isRotating) return;
            isRotating = false;

            // Simpan sudut saat ini supaya berhenti di posisi terakhir (tidak lompat ke 0)
            double current = AlbumRotate.Angle;
            AlbumRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            AlbumRotate.Angle = current;
        }

        // ================= ANIMASI EXPAND / COLLAPSE (HOVER) =================
        private void HoverZone_MouseEnter(object sender, MouseEventArgs e)
        {
            SetExpanded(true);
        }

        private void IslandBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            SetExpanded(false);
        }

        private void SetExpanded(bool expand)
        {
            if (isIntroPlaying) return;

            if (isExpanded == expand) return;
            isExpanded = expand;
            if (expand) HideExitPill();

            var widthAnimation = new DoubleAnimation
            {
                To = expand ? ExpandedWidth : CollapsedWidth,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var heightAnimation = new DoubleAnimation
            {
                To = expand ? 140 : 45,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            CompactView.Visibility = expand ? Visibility.Collapsed : Visibility.Visible;
            ExpandedView.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;

            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
            IslandBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
        }

        // Mengubah teks lirik di panel expanded dan di compact sekaligus
        private void SetLyric(string current, string next)
        {
            LyricCurrent.Text = current;
            LyricNext.Text = next;

            if (showLyrics)
                CompactTitleText.Text = current;
        }

        private void AnimateWidth(double width)
        {
            var anim = new DoubleAnimation
            {
                To = width,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }

        // ================= MEDIA CONTROLS CLICK EVENTS =================
        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (currentSession != null)
            {
                var info = currentSession.GetPlaybackInfo();
                if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    await currentSession.TryPauseAsync();
                else
                    await currentSession.TryPlayAsync();
            }
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (currentSession != null) await currentSession.TrySkipPreviousAsync();
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (currentSession != null) await currentSession.TrySkipNextAsync();
        }

        // ================= LIRIK =================
        private async void BtnLyrics_Click(object sender, RoutedEventArgs e)
        {
            showLyrics = !showLyrics;

            InfoPanel.Visibility = showLyrics ? Visibility.Collapsed : Visibility.Visible;
            LyricsPanel.Visibility = showLyrics ? Visibility.Visible : Visibility.Collapsed;
            BtnLyrics.Opacity = showLyrics ? 1.0 : 0.5;

            // Jika island sedang compact, langsung sesuaikan lebarnya
            if (!isExpanded) AnimateWidth(CollapsedWidth);

            if (showLyrics)
            {
                lastLyricIndex = -2;                         // paksa refresh teks lirik
                CompactTitleText.Text = LyricCurrent.Text;   // sinkronkan compact
                await LoadLyricsAsync();
                lyricTimer.Start();
            }
            else
            {
                lyricTimer.Stop();
                CompactTitleText.Text = string.IsNullOrEmpty(currentTitle) ? "Tidak ada media" : currentTitle;
            }
        }

        private async Task LoadLyricsAsync()
        {
            string key = $"{currentArtist}|{currentTitle}";
            if (key == lyricsKey) return;   // lagu sama, tidak perlu load ulang
            lyricsKey = key;

            lyricLines.Clear();
            lastLyricIndex = -2;

            if (string.IsNullOrWhiteSpace(currentTitle))
            {
                SetLyric("Tidak ada media", "");
                LyricNext.Text = "";
                return;
            }

            SetLyric("Memuat lirik...", "");
            LyricNext.Text = "";

            string? synced = null;

            try
            {
                // Durasi lagu membantu LRCLIB mencocokkan versi yang tepat
                int duration = 0;
                var tl = currentSession?.GetTimelineProperties();
                if (tl != null) duration = (int)(tl.EndTime - tl.StartTime).TotalSeconds;

                string getUrl = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(currentArtist)}&track_name={Uri.EscapeDataString(currentTitle)}";
                if (duration > 0) getUrl += $"&duration={duration}";

                var resp = await http.GetAsync(getUrl);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    synced = GetSynced(doc.RootElement);
                }

                // Cadangan: pakai pencarian jika /get tidak menemukan
                if (string.IsNullOrEmpty(synced))
                {
                    string searchUrl = $"https://lrclib.net/api/search?artist_name={Uri.EscapeDataString(currentArtist)}&track_name={Uri.EscapeDataString(currentTitle)}";
                    var sresp = await http.GetAsync(searchUrl);
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

            // Jika lagu sudah berganti selama proses download, abaikan hasilnya
            if (key != lyricsKey) return;

            if (!string.IsNullOrEmpty(synced))
            {
                lyricLines = ParseLrc(synced);
            }

            if (lyricLines.Count == 0)
            {
                SetLyric("Lirik tidak ditemukan", "");
                LyricNext.Text = "";
            }
        }

        private static string? GetSynced(JsonElement el)
        {
            if (el.TryGetProperty("syncedLyrics", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
            return null;
        }

        private static List<(TimeSpan Time, string Text)> ParseLrc(string lrc)
        {
            var list = new List<(TimeSpan Time, string Text)>();
            var rx = new Regex(@"\[(\d+):(\d+(?:\.\d+)?)\]");

            foreach (var line in lrc.Split('\n'))
            {
                var matches = rx.Matches(line);
                if (matches.Count == 0) continue;

                string text = rx.Replace(line, "").Trim();
                foreach (Match m in matches)
                {
                    double min = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    double sec = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    list.Add((TimeSpan.FromSeconds(min * 60 + sec), text));
                }
            }

            return list.OrderBy(x => x.Time).ToList();
        }

        private void LyricTimer_Tick(object? sender, EventArgs e)
        {
            if (!showLyrics || currentSession == null || lyricLines.Count == 0) return;

            var tl = currentSession.GetTimelineProperties();
            var pb = currentSession.GetPlaybackInfo();

            // Posisi lagu + waktu yang berlalu sejak terakhir diperbarui (agar halus)
            TimeSpan pos = tl.Position;
            if (pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                pos += DateTimeOffset.Now - tl.LastUpdatedTime;

            int idx = -1;
            for (int i = 0; i < lyricLines.Count; i++)
            {
                if (lyricLines[i].Time <= pos) idx = i;
                else break;
            }

            if (idx == lastLyricIndex) return;
            lastLyricIndex = idx;

            string current = idx >= 0 ? lyricLines[idx].Text : "";
            string next = idx + 1 < lyricLines.Count ? lyricLines[idx + 1].Text : "";
            SetLyric(string.IsNullOrWhiteSpace(current) ? "♪" : current, next);
        }

        // ================= PILL KELUAR =================
        private void MusicIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (isIntroPlaying || isExiting) return;

            if (isPillVisible) HideExitPill();
            else ShowExitPill();
        }

        private void ShowExitPill()
        {
            if (isPillVisible) return;
            isPillVisible = true;

            // Posisi akhir: di sebelah kanan island (mengikuti lebar island saat ini)
            double targetX = IslandBorder.ActualWidth / 2 + 10 + ExitPill.Width / 2;
            double startX = IslandBorder.ActualWidth / 2 - 20;   // muncul dari tepi island

            ExitPill.Visibility = Visibility.Visible;

            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            var dur = TimeSpan.FromMilliseconds(350);

            ExitPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(startX, targetX, dur) { EasingFunction = ease });
            ExitPillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.5, 1, dur) { EasingFunction = ease });
            ExitPillScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.5, 1, dur) { EasingFunction = ease });
            ExitPill.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        }

        private void HideExitPill()
        {
            if (!isPillVisible) return;
            isPillVisible = false;

            var dur = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            ExitPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(IslandBorder.ActualWidth / 2 - 20, dur) { EasingFunction = ease });
            ExitPillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.5, dur) { EasingFunction = ease });
            ExitPillScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.5, dur) { EasingFunction = ease });

            var fade = new DoubleAnimation(0, dur);
            fade.Completed += (s, e) =>
            {
                if (!isPillVisible) ExitPill.Visibility = Visibility.Collapsed;
            };
            ExitPill.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void ExitPill_Click(object sender, MouseButtonEventArgs e)
        {
            ExitWithAnimation();
        }

        // ================= ANIMASI KELUAR (KEBALIKAN INTRO) =================
        private void ExitWithAnimation()
        {
            if (isExiting) return;
            isExiting = true;
            isIntroPlaying = true;   // blokir hover selama animasi keluar
            lyricTimer.Stop();
            HideExitPill();

            // 1. Judul/lirik dan ikon musik memudar
            var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(250) };
            CompactTitleText.BeginAnimation(UIElement.OpacityProperty, fade);
            CompactMusicIcon.BeginAnimation(UIElement.OpacityProperty, fade);

            // 2. Island menyempit ke tengah, album art ikut bergeser ke tengah
            var widthAnim = new DoubleAnimation
            {
                To = 48,
                BeginTime = TimeSpan.FromMilliseconds(250),
                Duration = TimeSpan.FromMilliseconds(550),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

            // 3. Album art mengecil dan hilang
            var shrink = new DoubleAnimation
            {
                To = 0.3,
                BeginTime = TimeSpan.FromMilliseconds(800),
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.5 }
            };
            IntroScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            IntroScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

            AlbumContainer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                To = 0,
                BeginTime = TimeSpan.FromMilliseconds(800),
                Duration = TimeSpan.FromMilliseconds(300)
            });

            // 4. Island ikut menghilang, lalu aplikasi ditutup
            var islandFade = new DoubleAnimation
            {
                To = 0,
                BeginTime = TimeSpan.FromMilliseconds(1000),
                Duration = TimeSpan.FromMilliseconds(200)
            };
            islandFade.Completed += (s, e) => Application.Current.Shutdown();
            IslandBorder.BeginAnimation(UIElement.OpacityProperty, islandFade);
        }
    }
}