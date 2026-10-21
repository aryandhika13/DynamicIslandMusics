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

        private readonly MainViewModel vm;
        private readonly AppSettings settings = new();
        private bool isSettingsOpen = false;

        private bool isExpanded = false;
        private bool isRotating = false;
        private bool isMenuVisible = false;
        private int menuIconVersion;
        private bool isExiting = false;
        private bool isIntroPlaying = true;

        // Skala hanya berlaku saat mode lirik aktif (mode judul/artis tidak berubah)
        private double Scale => vm.ShowLyrics ? vm.LyricScale : 1.0;

        private double CollapsedWidth => vm.ShowLyrics ? 80 + 240 * Scale : CompactWidth;   // skala 1 = 320
        private double CollapsedHeight
        {
            get
            {
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
        private double CurrentExpandedHeight => ExpandedHeight + (Scale - 1) * 70;

        public MainWindow()
        {
            InitializeComponent();

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

        private void AnimateSize(double width, double height)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(300);

            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation { To = width, Duration = dur, EasingFunction = ease });
            IslandBorder.BeginAnimation(FrameworkElement.HeightProperty,
                new DoubleAnimation { To = height, Duration = dur, EasingFunction = ease });
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

            AnimateMenuIcon(toClose: true);
            ShowPill(SettingsPill, SettingsPillScale, SettingsPillTranslate, -1);   // kiri
            ShowPill(ExitPill, ExitPillScale, ExitPillTranslate, +1);               // kanan
        }

        private void HideMenu()
        {
            if (!isMenuVisible) return;
            isMenuVisible = false;

            AnimateMenuIcon(toClose: false);
            HideSettingsPanel();
            HidePill(SettingsPill, SettingsPillScale, SettingsPillTranslate, -1);
            HidePill(ExitPill, ExitPillScale, ExitPillTranslate, +1);
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
            var (width, centerX) = SettingsPanelGeometry(islandWidth);

            // Margin kanan menggeser panel (yang rata tengah) ke posisi centerX; atas = di bawah island
            var margin = new Thickness(0, 10 + islandHeight + 8, -2 * centerX, 0);
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
            double half = islandWidth / 2;

            SettingsPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-(half + 10 + SettingsPill.Width / 2), dur) { EasingFunction = ease });
            ExitPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(half + 10 + ExitPill.Width / 2, dur) { EasingFunction = ease });

            if (isSettingsOpen) LayoutSettingsPanel(islandWidth, islandHeight, dur);
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