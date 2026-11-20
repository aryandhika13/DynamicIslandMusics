using DynamicIslandDesktop.Helpers;
using DynamicIslandDesktop.Models;
using DynamicIslandDesktop.Services;
using DynamicIslandDesktop.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicIslandDesktop
{
    /// <summary>View: hanya animasi, posisi window, dan event murni UI. Logika ada di ViewModel/Service.</summary>
    public partial class MainWindow : Window
    {
        // Ukuran dasar (ukuran font lirik = default). Saat mode lirik, ukuran ikut skala font.
        private const double CompactWidth = 180;
        private const double CompactHeight = 45;
        private const double CompactTextOverhead = 93;   // album 30 + ikon 30 + margin 16 + 16 + 1
        private const double CompactTextPadding = 24;    // ruang atas-bawah di sekitar teks
        private const double ExpandedWidth = 360;
        private const double ExpandedHeight = 140;
        private static readonly Color MenuIconGreen = Color.FromRgb(0x1D, 0xB9, 0x54);   // ikon panah
        private static readonly Color MenuIconRed = Color.FromRgb(0xFF, 0x52, 0x52);     // ikon silang

        private const double DockedWidth = 48;
        private const double DockedHeight = 225;
        private const double DockSnapDistance = 120;   // jarak ke tepi kanan layar agar island menempel (px)
        private bool isDocked = false;
        private bool dockedLeft = false;
        private int PillSide => dockedLeft ? +1 : -1;

        private readonly MainViewModel vm;
        private readonly AppSettings settings = new();
        private bool isSettingsOpen = false;

        private bool isExpanded = false;
        private bool isRotating = false;
        private bool isMenuVisible = false;
        private int menuIconVersion;
        private bool isExiting = false;
        private bool isIntroPlaying = true;
        private int lyricBubbleVersion;
        private string? shownLyric;

        // Skala hanya berlaku saat mode lirik aktif (mode judul/artis tidak berubah)
        private double Scale => vm.ShowLyrics ? vm.LyricScale : 1.0;

        private double CollapsedWidth => isDocked ? DockedWidth : (vm.ShowLyrics ? 80 + 240 * Scale : CompactWidth);   // skala 1 = 320
        private double CollapsedHeight
        {
            get
            {
                if (isDocked) return DockedHeight;
                double baseHeight = CompactHeight + (Scale - 1) * 20;
                if (!vm.ShowLyrics) return baseHeight;

                // Tinggi lirik yang membungkus ke bawah; island tumbuh mengikuti jumlah barisnya
                var typeface = new Typeface(CompactTitleText.FontFamily, CompactTitleText.FontStyle,
                    CompactTitleText.FontWeight, CompactTitleText.FontStretch);

                var (_, textHeight) = TextFit.Compute(
                    vm.CompactText, typeface, VisualTreeHelper.GetDpi(this).PixelsPerDip,
                    vm.CompactFontSize, TextFit.GetMinFontSize(CompactTitleText),
                    CollapsedWidth - CompactTextOverhead, true, vm.CompactLyricMaxHeight);

                return Math.Max(baseHeight, textHeight + CompactTextPadding);
            }
        }
        private double CurrentExpandedWidth => ExpandedWidth + (Scale - 1) * 200;
        private double CurrentExpandedHeight =>
    Math.Max(ExpandedHeight + (Scale - 1) * 70, isDocked ? DockedHeight : 0);

        public MainWindow()
        {
            InitializeComponent();

            AlbumContainer.MouseEnter += AlbumContainer_MouseEnter;
            SettingsPill.MouseEnter += Pill_MouseEnter;
            SettingsPill.MouseLeave += Pill_MouseLeave;
            ExitPill.MouseEnter += Pill_MouseEnter;
            ExitPill.MouseLeave += Pill_MouseLeave;
            vm = new MainViewModel(new MediaSessionService(), new LyricsService(), new DispatcherTicker(), settings);
            vm.PropertyChanged += Vm_PropertyChanged;
            settings.PropertyChanged += Settings_PropertyChanged;
            DataContext = vm;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Posisikan window di tengah atas layar
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;

            PlayIntroAnimation();

            await vm.InitializeAsync();
        }

        // Bereaksi terhadap perubahan state ViewModel yang punya efek visual non-binding
        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsPlaying):
                    if (vm.IsPlaying) StartRotation();
                    else StopRotation();
                    break;

                case nameof(MainViewModel.ShowLyrics):
                    ApplySize();
                    UpdateLyricBubble();
                    break;

                case nameof(MainViewModel.CurrentLyric):
                    UpdateLyricBubble();
                    break;

                case nameof(MainViewModel.CompactText):
                    if (vm.ShowLyrics) ApplySize();
                    break;
            }
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.RotateAlbumArt):
                    if (settings.RotateAlbumArt && vm.IsPlaying) StartRotation();
                    else StopRotation();
                    break;

                case nameof(AppSettings.LyricFontSize):
                    ApplySize();
                    break;

                case nameof(AppSettings.AlwaysOnTop):
                    Topmost = settings.AlwaysOnTop;
                    break;
            }
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

            // 3. Judul dan ikon menu muncul perlahan
            var fadeAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                BeginTime = TimeSpan.FromMilliseconds(1000),
                Duration = TimeSpan.FromMilliseconds(400)
            };
            CompactTitleText.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            CompactMenuIcon.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        // ================= ROTASI ALBUM ART =================
        private void StartRotation()
        {
            if (isRotating || !settings.RotateAlbumArt) return;
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
        private void HoverZone_MouseEnter(object sender, MouseEventArgs e) => SetExpanded(true);

        private void IslandBorder_MouseLeave(object sender, MouseEventArgs e) => SetExpanded(false);

        private void SetExpanded(bool expand)
        {
            if (isIntroPlaying) return;

            // Saat menu (Setting/Keluar) terbuka, hover tidak boleh memperbesar island
            if (expand && isMenuVisible) return;

            if (isExpanded == expand) return;
            isExpanded = expand;

            var widthAnimation = new DoubleAnimation
            {
                To = expand ? CurrentExpandedWidth : CollapsedWidth,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var heightAnimation = new DoubleAnimation
            {
                To = expand ? CurrentExpandedHeight : CollapsedHeight,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            CompactView.Visibility = expand ? Visibility.Collapsed : Visibility.Visible;
            ExpandedView.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;

            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
            IslandBorder.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
            AnimateDockShift(expand ? CurrentExpandedWidth : CollapsedWidth);
            AnimateDockShift(expand ? CurrentExpandedWidth : CollapsedWidth);
            UpdateLyricBubble();   
        }

        // Menyesuaikan ukuran island dengan state saat ini (mode lirik + ukuran font)
        private void ApplySize()
        {
            if (isIntroPlaying || isExiting) return;

            double width = isExpanded ? CurrentExpandedWidth : CollapsedWidth;
            double height = isExpanded ? CurrentExpandedHeight : CollapsedHeight;
            AnimateSize(width, height);
            RepositionMenu(width, height);
        }

        // Dipanggil saat pengguna mencoba membuka aplikasi untuk kedua kalinya
        public void OnSecondInstanceLaunched()
        {
            if (isIntroPlaying || isExiting) return;

            // Saat island sedang expanded (mouse di atasnya), cukup mantul tanpa melebar
            PlayBounceAnimation(widen: !isExpanded && !isDocked);
        }

        // Melebar -> mantul ke bawah 3 kali (makin kecil) -> kembali ke ukuran normal.
        // Jika menu terbuka, pill Setting/Keluar (dan panel pengaturan) ikut melebar dan mantul.
        private void PlayBounceAnimation(bool widen)
        {
            const double WidenBy = 60;       // tambahan lebar saat melebar (px)
            const double BounceStart = 300;  // mantul dimulai setelah melebar (ms)
            const double BounceHalf = 120;   // durasi turun, dan juga naik, per mantulan (ms)
            double[] heights = { 12, 8, 4 }; // jarak turun tiap mantulan (px); jumlah isi = jumlah mantulan

            static KeyTime Ms(double ms) => KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms));

            // 1. Lebar: island, pill, dan panel memakai keyframe yang sama supaya selalu sejajar
            if (widen)
            {
                double from = IslandBorder.ActualWidth;
                double normal = CollapsedWidth;
                double wide = normal + WidenBy;

                // map: mengubah lebar island menjadi nilai milik elemen yang dianimasikan
                DoubleAnimationUsingKeyFrames WidthKeys(Func<double, double> map)
                {
                    var a = new DoubleAnimationUsingKeyFrames();
                    a.KeyFrames.Add(new LinearDoubleKeyFrame(map(from), Ms(0)));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(map(wide), Ms(300),
                        new CubicEase { EasingMode = EasingMode.EaseOut }));
                    a.KeyFrames.Add(new LinearDoubleKeyFrame(map(wide), Ms(1200)));
                    a.KeyFrames.Add(new EasingDoubleKeyFrame(map(normal), Ms(1600),
                        new CubicEase { EasingMode = EasingMode.EaseInOut }));
                    return a;
                }

                IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, WidthKeys(w => w));

                if (isMenuVisible)
                {
                    // Pill menjaga jarak 10 px dari tepi island, jadi posisinya mengikuti lebar island
                    SettingsPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                        WidthKeys(w => -(w / 2 + 10 + SettingsPill.Width / 2)));
                    ExitPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                        WidthKeys(w => w / 2 + 10 + ExitPill.Width / 2));

                    if (isSettingsOpen)
                        SettingsPanel.BeginAnimation(FrameworkElement.WidthProperty,
                            WidthKeys(w => SettingsPanelGeometry(w).Width));
                }
            }

            // 2. Mantul: turun (melambat di titik bawah), lalu naik kembali (makin cepat), tiga kali
            var bounce = new DoubleAnimationUsingKeyFrames();
            double t = widen ? BounceStart : 0;
            if (t > 0) bounce.KeyFrames.Add(new LinearDoubleKeyFrame(0, Ms(t)));   // diam dulu sambil melebar

            foreach (double h in heights)
            {
                t += BounceHalf;
                bounce.KeyFrames.Add(new EasingDoubleKeyFrame(h, Ms(t),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                t += BounceHalf;
                bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0, Ms(t),
                    new CubicEase { EasingMode = EasingMode.EaseIn }));
            }

            IslandShift.BeginAnimation(TranslateTransform.YProperty, bounce);

            if (isMenuVisible)
            {
                // Pill dan panel ikut mantul sebagai satu kelompok dengan island
                SettingsPillTranslate.BeginAnimation(TranslateTransform.YProperty, bounce);
                ExitPillTranslate.BeginAnimation(TranslateTransform.YProperty, bounce);
                if (isSettingsOpen)
                    SettingsPanelShift.BeginAnimation(TranslateTransform.YProperty, bounce);
            }
        }

        private void AnimateSize(double width, double height)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(300);

            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation { To = width, Duration = dur, EasingFunction = ease });
            IslandBorder.BeginAnimation(FrameworkElement.HeightProperty,
                new DoubleAnimation { To = height, Duration = dur, EasingFunction = ease });
            AnimateDockShift(width);
        }

        // ================= DRAG & DOCK KE KANAN =================
        // Saat menempel, tombol sudah ada di pill; hover di album art untuk melihat judul/lirik (expanded)
        private void AlbumContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            if (isDocked) SetExpanded(true);
        }

        private void IslandBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isIntroPlaying || isExiting) return;
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (IsInteractiveSource(e.OriginalSource as DependencyObject)) return;   // tombol & ikon menu tetap bisa diklik

            double oldLeft = Left, oldTop = Top;
            if (isDocked) HideLyricBubble(immediate: true);   // <-- baru
            try { DragMove(); }
            catch (InvalidOperationException) { return; }

            if (Math.Abs(Left - oldLeft) < 1 && Math.Abs(Top - oldTop) < 1)
            {
                UpdateLyricBubble();   // <-- baru
                return;
            }
            OnDragCompleted();
        }

        private bool IsInteractiveSource(DependencyObject? source)
        {
            while (source != null && source != IslandBorder)
            {
                if (source is System.Windows.Controls.Primitives.ButtonBase || source == MenuIconButton)
                    return true;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }

        private void OnDragCompleted()
        {
            // Jika island sedang bergeser karena offset dock, jadikan posisi window yang sebenarnya
            double shiftX = IslandShift.X;
            if (shiftX != 0)
            {
                IslandShift.BeginAnimation(TranslateTransform.XProperty, null);
                IslandShift.X = 0;
                Left += shiftX;
            }

            var work = SystemParameters.WorkArea;
            double center = Left + ActualWidth / 2;
            double half = IslandBorder.ActualWidth / 2;
            double distRight = work.Right - (center + half);
            double distLeft = (center - half) - work.Left;

            bool nearRight = distRight <= DockSnapDistance;
            bool nearLeft = distLeft <= DockSnapDistance;

            if (nearRight || nearLeft)
            {
                bool toLeft = nearLeft && (!nearRight || distLeft < distRight);
                DockTo(toLeft, work);
            }
            else if (isDocked) Undock();
        }

        private void DockTo(bool left, Rect work)
        {
            // Kembali ke tampilan compact dulu
            isExpanded = false;
            CompactView.Visibility = Visibility.Visible;
            ExpandedView.Visibility = Visibility.Collapsed;
            HideMenu();   // masih memakai sisi lama, jadi harus sebelum dockedLeft diubah

            dockedLeft = left;
            isDocked = true;
            ApplyDockedVisuals(true);

            // Tepi island = tepi layar (kiri atau kanan)
            double targetLeft = left
                ? work.Left + DockedWidth / 2 - ActualWidth / 2
                : work.Right - DockedWidth / 2 - ActualWidth / 2;
            double minTop = work.Top - 10;
            double maxTop = Math.Max(minTop, work.Bottom - 10 - CurrentExpandedHeight);
            double targetTop = Math.Clamp(Top, minTop, maxTop);

            AnimateWindowTo(targetLeft, targetTop);
            AnimateSize(DockedWidth, DockedHeight);
            UpdateLyricBubble();
        }

        private void Undock()
        {
            HideMenu();
            isDocked = false;
            isExpanded = false;
            CompactView.Visibility = Visibility.Visible;
            ExpandedView.Visibility = Visibility.Collapsed;
            ApplyDockedVisuals(false);

            AnimateSize(CollapsedWidth, CollapsedHeight);
        }

        // Mode vertikal: hanya album art di atas + grip; judul & ikon menu disembunyikan
        private void ApplyDockedVisuals(bool docked)
        {
            HoverZone.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
            MenuIconButton.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
            DockControls.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
            AlbumContainer.VerticalAlignment = docked ? VerticalAlignment.Top : VerticalAlignment.Center;
            AlbumContainer.Margin = docked ? new Thickness(0, 4, 0, 0) : new Thickness(0);
        }

        // Menjaga tepi kanan island tetap menempel saat island melebar ke kiri (expanded)
        private void AnimateDockShift(double width)
        {
            double grow = (width - DockedWidth) / 2;
            double x = isDocked ? (dockedLeft ? grow : -grow) : 0;
            IslandShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(x, TimeSpan.FromMilliseconds(300))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private void AnimateWindowTo(double left, double top)
        {
            double fromLeft = Left, fromTop = Top;
            Left = left;   // nilai akhir diset langsung, animasi hanya untuk transisi
            Top = top;

            var dur = TimeSpan.FromMilliseconds(350);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(LeftProperty, new DoubleAnimation(fromLeft, left, dur)
            { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            BeginAnimation(TopProperty, new DoubleAnimation(fromTop, top, dur)
            { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }

        // ================= POP-UP LIRIK (SAAT MENEMPEL) =================
        private bool ShouldShowLyricBubble =>
            isDocked && !isExpanded && !isExiting && !isIntroPlaying && !isMenuVisible
            && vm.ShowLyrics && !string.IsNullOrWhiteSpace(vm.CurrentLyric);

        private void UpdateLyricBubble()
        {
            if (!ShouldShowLyricBubble)
            {
                HideLyricBubble();
                return;
            }

            string text = vm.CurrentLyric!.Trim();
            if (text == shownLyric && LyricBubble.Visibility == Visibility.Visible) return;

            int version = ++lyricBubbleVersion;

            if (LyricBubble.Visibility == Visibility.Visible && LyricBubble.Opacity > 0.05)
            {
                // Lirik lama bergeser ke atas sambil memudar, baru lirik baru muncul dari bawah
                var dur = TimeSpan.FromMilliseconds(220);
                var fadeOut = new DoubleAnimation(0, dur)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                fadeOut.Completed += (s, e) =>
                {
                    if (version == lyricBubbleVersion) PopInLyric(text);
                };
                LyricBubbleTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-28, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                LyricBubble.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                PopInLyric(text);
            }
        }

        // Bubble berada di sisi seberang island: ekor menghadap island, sudut lancip di sisi ekor
        private void ApplyBubbleSide()
        {
            bool left = dockedLeft;
            LyricBubble.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            LyricBubble.RenderTransformOrigin = new Point(left ? 0 : 1, 1);
            LyricBubbleBody.CornerRadius = left ? new CornerRadius(18, 18, 18, 3) : new CornerRadius(18, 18, 3, 18);
            LyricBubbleBody.Margin = left ? new Thickness(8, 0, 0, 0) : new Thickness(0, 0, 8, 0);
            LyricBubbleTail.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            LyricBubbleTail.Data = Geometry.Parse(left
                ? "M12,0 C12,7 8,11 0,12 L12,12 Z"
                : "M0,0 C0,7 4,11 12,12 L0,12 Z");
        }

        private void PopInLyric(string text)
        {
            shownLyric = text;
            LyricBubbleText.Text = text;
            ApplyBubbleSide();

            // Tepi bawah bubble = 12 px di atas tepi bawah pill; jarak 4 px dari tepi island
            double bottom = 10 + DockedHeight - 12;
            double gap = ActualWidth / 2 + DockedWidth / 2 + 4;
            Thickness MarginFor(double top) => dockedLeft
                ? new Thickness(gap, top, 0, 0)
                : new Thickness(0, top, gap, 0);

            LyricBubble.VerticalAlignment = VerticalAlignment.Top;
            LyricBubble.Margin = MarginFor(0);
            LyricBubble.Visibility = Visibility.Visible;
            LyricBubble.UpdateLayout();

            // Bubble yang lebih tinggi tumbuh ke atas, tepi bawahnya tetap
            LyricBubble.Margin = MarginFor(bottom - LyricBubble.ActualHeight);

            var dur = TimeSpan.FromMilliseconds(320);
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            double slideFrom = dockedLeft ? -16 : 16;   // masuk dari arah island

            LyricBubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.8, 1, dur) { EasingFunction = ease });
            LyricBubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.8, 1, dur) { EasingFunction = ease });
            LyricBubbleTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(slideFrom, 0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            LyricBubbleTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            LyricBubble.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        private void HideLyricBubble(bool immediate = false)
        {
            int version = ++lyricBubbleVersion;
            shownLyric = null;

            if (LyricBubble.Visibility != Visibility.Visible) return;

            if (immediate)
            {
                LyricBubble.BeginAnimation(UIElement.OpacityProperty, null);
                LyricBubble.Opacity = 0;
                LyricBubble.Visibility = Visibility.Collapsed;
                return;
            }

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fade.Completed += (s, e) =>
            {
                if (version == lyricBubbleVersion) LyricBubble.Visibility = Visibility.Collapsed;
            };
            LyricBubble.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // ================= MENU (SETTING & KELUAR) =================
        private void MenuIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (isIntroPlaying || isExiting) return;

            if (isMenuVisible) HideMenu();
            else ShowMenu();
        }

        private void ShowMenu()
        {
            if (isMenuVisible) return;
            isMenuVisible = true;

            if (isDocked)
            {
                // Menempel: kedua pill bertumpuk di sisi seberang layar
                SettingsPill.Margin = new Thickness(0, 14, 0, 0);
                ExitPill.Margin = new Thickness(0, 58, 0, 0);
                AnimateDockMenuButton(toClose: true);
                ShowPill(SettingsPill, SettingsPillScale, SettingsPillTranslate, PillSide);
                ShowPill(ExitPill, ExitPillScale, ExitPillTranslate, PillSide);
            }
            else
            {
                SettingsPill.Margin = ExitPill.Margin = new Thickness(0, 14, 0, 0);
                AnimateMenuIcon(toClose: true);
                ShowPill(SettingsPill, SettingsPillScale, SettingsPillTranslate, -1);   // kiri
                ShowPill(ExitPill, ExitPillScale, ExitPillTranslate, +1);               // kanan
            }
            UpdateLyricBubble();
        }

        private void HideMenu()
        {
            if (!isMenuVisible) return;
            isMenuVisible = false;

            if (isDocked) AnimateDockMenuButton(toClose: false);
            else AnimateMenuIcon(toClose: false);

            HideSettingsPanel();
            HidePill(SettingsPill, SettingsPillScale, SettingsPillTranslate, isDocked ? PillSide : -1);
            HidePill(ExitPill, ExitPillScale, ExitPillTranslate, isDocked ? PillSide : +1);
            UpdateLyricBubble();
        }

        // Ikon berputar 360°; di tengah putaran bentuknya berganti, dan warnanya bergradasi hijau <-> merah
        private void AnimateMenuIcon(bool toClose)
        {
            var target = (Geometry)FindResource(toClose ? "CloseCircleIcon" : "ChevronCircleIcon");
            var current = CompactMenuIcon.Data;

            var total = TimeSpan.FromMilliseconds(400);
            var middle = TimeSpan.FromMilliseconds(200);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // Putaran (mulai dari sudut saat ini kalau animasi sebelumnya belum selesai)
            double start = MenuIconRotate.Angle % 360;
            MenuIconRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(start, start + 360, total) { EasingFunction = ease });

            // Transisi warna: tanpa From, jadi mulai dari warna saat ini
            MenuIconBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation
                {
                    To = toClose ? MenuIconRed : MenuIconGreen,
                    Duration = total,
                    EasingFunction = ease
                });

            // Ganti bentuk di tengah putaran
            var swap = new ObjectAnimationUsingKeyFrames { Duration = total };
            swap.KeyFrames.Add(new DiscreteObjectKeyFrame(current, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            swap.KeyFrames.Add(new DiscreteObjectKeyFrame(target, KeyTime.FromTimeSpan(middle)));
            CompactMenuIcon.BeginAnimation(System.Windows.Shapes.Path.DataProperty, swap);
        }

        // side: -1 = kiri island, +1 = kanan island
        private void ShowPill(Border pill, ScaleTransform scale, TranslateTransform translate, int side)
        {
            double half = IslandBorder.ActualWidth / 2;
            double targetX = side * (half + 10 + pill.Width / 2);   // di samping island
            double startX = side * (half - 20);                     // muncul dari tepi island

            pill.Visibility = Visibility.Visible;

            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            var dur = TimeSpan.FromMilliseconds(350);

            translate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(startX, targetX, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.5, 1, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.5, 1, dur) { EasingFunction = ease });
            pill.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        }

        private void HidePill(Border pill, ScaleTransform scale, TranslateTransform translate, int side)
        {
            var dur = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            translate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(side * (IslandBorder.ActualWidth / 2 - 20), dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.5, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.5, dur) { EasingFunction = ease });

            var fade = new DoubleAnimation(0, dur);
            fade.Completed += (s, e) =>
            {
                if (!isMenuVisible) pill.Visibility = Visibility.Collapsed;
            };
            pill.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void SettingsPill_Click(object sender, MouseButtonEventArgs e)
        {
            if (isIntroPlaying || isExiting) return;

            if (isSettingsOpen) HideSettingsPanel();
            else ShowSettingsPanel();
        }

        // Panel membentang dari tepi kiri pill Setting sampai tepi kanan island
        private (double Width, double CenterX) SettingsPanelGeometry(double islandWidth)
        {
            double half = islandWidth / 2;
            double left = -(half + 10 + SettingsPill.Width);   // tepi kiri pill Setting
            double right = half;                               // tepi kanan island
            return (right - left, (left + right) / 2);
        }

        private void LayoutSettingsPanel(double islandWidth, double islandHeight, TimeSpan duration)
        {
            double width, centerX, top;

            if (isDocked)
            {
                // Panel di sebelah pill Setting (sisi yang sama dengan pill), sejajar dengan pill tersebut
                width = 274;
                double pillEdge = DockedWidth / 2 + 10 + SettingsPill.Width;   // tepi pill terjauh dari tengah
                centerX = PillSide * (pillEdge + 8 + width / 2);
                top = 14;
            }
            else
            {
                (width, centerX) = SettingsPanelGeometry(islandWidth);
                top = 10 + islandHeight + 8;
            }

            var margin = new Thickness(0, top, -2 * centerX, 0);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            SettingsPanel.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(width, duration) { EasingFunction = ease });
            SettingsPanel.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(margin, duration) { EasingFunction = ease });
        }

        private void ShowSettingsPanel()
        {
            if (isSettingsOpen) return;
            isSettingsOpen = true;

            LayoutSettingsPanel(IslandBorder.ActualWidth, IslandBorder.ActualHeight, TimeSpan.Zero);
            SettingsPanel.Visibility = Visibility.Visible;

            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
            SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
            SettingsPanel.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        }

        private void HideSettingsPanel()
        {
            if (!isSettingsOpen) return;
            isSettingsOpen = false;

            var dur = TimeSpan.FromMilliseconds(200);
            SettingsPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.6, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

            var fade = new DoubleAnimation(0, dur);
            fade.Completed += (s, e) =>
            {
                if (!isSettingsOpen) SettingsPanel.Visibility = Visibility.Collapsed;
            };
            SettingsPanel.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // Saat island berubah ukuran (mode lirik + slider), pill dan panel ikut menyesuaikan
        private void RepositionMenu(double islandWidth, double islandHeight)
        {
            if (!isMenuVisible) return;

            var dur = TimeSpan.FromMilliseconds(300);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (isDocked)
            {
                double x = PillSide * (DockedWidth / 2 + 10 + SettingsPill.Width / 2);
                SettingsPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(x, dur) { EasingFunction = ease });
                ExitPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(x, dur) { EasingFunction = ease });
                if (isSettingsOpen) LayoutSettingsPanel(islandWidth, islandHeight, dur);
                return;
            }

            double half = islandWidth / 2;
            SettingsPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-(half + 10 + SettingsPill.Width / 2), dur) { EasingFunction = ease });
            ExitPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(half + 10 + ExitPill.Width / 2, dur) { EasingFunction = ease });

            if (isSettingsOpen) LayoutSettingsPanel(islandWidth, islandHeight, dur);
        }

        // ================= MENU SAAT MENEMPEL =================
        private void DockMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (isIntroPlaying || isExiting) return;

            if (isMenuVisible) HideMenu();
            else ShowMenu();
        }

        // Ikon titik tiga berputar 360°, bentuk berganti di tengah putaran, warna hijau <-> merah
        private void AnimateDockMenuButton(bool toClose)
        {
            var total = TimeSpan.FromMilliseconds(400);
            var middle = TimeSpan.FromMilliseconds(200);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            double start = DockMenuRotate.Angle % 360;
            DockMenuRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(start, start + 360, total) { EasingFunction = ease });

            DockMenuBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = toClose ? MenuIconRed : MenuIconGreen,
                Duration = total,
                EasingFunction = ease
            });

            var swap = new ObjectAnimationUsingKeyFrames { Duration = total };
            swap.KeyFrames.Add(new DiscreteObjectKeyFrame(toClose ? "\uE712" : "\uE711", KeyTime.FromTimeSpan(TimeSpan.Zero)));
            swap.KeyFrames.Add(new DiscreteObjectKeyFrame(toClose ? "\uE711" : "\uE712", KeyTime.FromTimeSpan(middle)));
            DockMenuButton.BeginAnimation(ContentControl.ContentProperty, swap);
        }

        // Pill Setting/Keluar sedikit membesar saat di-hover
        private void Pill_MouseEnter(object sender, MouseEventArgs e) => ScalePill(sender, 1.08);
        private void Pill_MouseLeave(object sender, MouseEventArgs e) => ScalePill(sender, 1.0);

        private void ScalePill(object sender, double to)
        {
            var scale = sender == SettingsPill ? SettingsPillScale : ExitPillScale;
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void ExitPill_Click(object sender, MouseButtonEventArgs e) => ExitWithAnimation();

        // ================= ANIMASI KELUAR (KEBALIKAN INTRO) =================
        private void ExitWithAnimation()
        {
            if (isExiting) return;
            isExiting = true;
            isIntroPlaying = true;   // blokir hover selama animasi keluar
            vm.StopTimers();
            HideMenu();

            if (isDocked)
            {
                DockControls.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
                IslandBorder.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation
                {
                    To = CompactHeight,
                    BeginTime = TimeSpan.FromMilliseconds(250),
                    Duration = TimeSpan.FromMilliseconds(550),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
            }
            HideLyricBubble(immediate: true);

            // 1. Judul/lirik dan ikon menu memudar
            var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(250) };
            CompactTitleText.BeginAnimation(UIElement.OpacityProperty, fade);
            CompactMenuIcon.BeginAnimation(UIElement.OpacityProperty, fade);

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