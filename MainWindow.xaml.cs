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
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DynamicIslandDesktop
{
    /// <summary>View: hanya animasi, posisi window, dan event murni UI. Logika ada di ViewModel/Service.</summary>
    public partial class MainWindow : Window
    {
        private const double CompactWidth = 248;
        private const double CompactHeight = 46;
        private const double CompactTextOverhead = 116;
        private const double CompactTextPadding = 24;
        private const double ExpandedWidth = 340;
        private const double ExpandedHeight = 132;
        private static readonly Color MenuIconGreen = Color.FromRgb(0x1D, 0xB9, 0x54);
        private static readonly Color MenuIconRed = Color.FromRgb(0xFF, 0x52, 0x52);

        private const double DockedWidth = 60;
        private const double DockedHeight = 270;
        private const double DockSnapDistance = 120;
        private bool isDocked = false;
        private bool dockedLeft = false;
        private int PillSide => dockedLeft ? +1 : -1;

        // Field di MainWindow:
        private readonly AudioBeatService beatService = new();
        private Border[]? beatBars;
        private Border[]? compactBeatBars;
        private readonly double[] barLevels = new double[7];
        private readonly double[] compactBarLevels = new double[5];
        private readonly DispatcherTimer chartTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        private double currentBeatEnergy = 0;
        private long lastRealBeatTick = 0;

        private readonly MainViewModel vm;
        private readonly AppSettings settings = new();
        private bool isSettingsOpen = false;

        private bool isExpanded = false;
        private bool isRotating = false;
        private bool isMenuVisible = false;
        private bool isExiting = false;
        private bool isIntroPlaying = true;
        private int lyricBubbleVersion;

        private Brush? normalBackground;
        private Brush? normalBorderBrush;
        private Thickness normalBorderThickness;
        private Effect? normalEffect;
        private CornerRadius normalCornerRadius;
        private string? shownLyric;

        private double Scale => vm.ShowLyrics ? vm.LyricScale : 1.0;

        private double CollapsedWidth => isDocked ? DockedWidth : (vm.ShowLyrics ? 80 + 240 * Scale : CompactWidth);
        private double CollapsedHeight
        {
            get
            {
                if (isDocked) return DockedHeight;
                double baseHeight = CompactHeight + (Scale - 1) * 20;
                if (!vm.ShowLyrics) return baseHeight;

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
            DockAlbum.MouseEnter += AlbumContainer_MouseEnter;

            normalBackground = IslandBorder.Background;
            normalBorderBrush = IslandBorder.BorderBrush;
            normalBorderThickness = IslandBorder.BorderThickness;
            normalEffect = IslandBorder.Effect;
            normalCornerRadius = IslandBorder.CornerRadius;

            SettingsPill.MouseEnter += Pill_MouseEnter;
            SettingsPill.MouseLeave += Pill_MouseLeave;
            ExitPill.MouseEnter += Pill_MouseEnter;
            ExitPill.MouseLeave += Pill_MouseLeave;

            vm = new MainViewModel(new MediaSessionService(), new LyricsService(), new DispatcherTicker(), settings);
            vm.PropertyChanged += Vm_PropertyChanged;
            settings.PropertyChanged += Settings_PropertyChanged;
            DataContext = vm;

            beatBars = new[] { BeatBar0, BeatBar1, BeatBar2, BeatBar3, BeatBar4, BeatBar5, BeatBar6 };
            compactBeatBars = new[] { CompactBar0, CompactBar1, CompactBar2, CompactBar3, CompactBar4 };
            chartTimer.Tick += (_, __) => OnChartTimerTick();
            beatService.BeatUpdated += OnBeatUpdated;
            Closed += (_, __) =>
            {
                chartTimer.Stop();
                beatService.Dispose();
            };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;

            PlayIntroAnimation();

            await vm.InitializeAsync();
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsPlaying):
                    if (vm.IsPlaying)
                    {
                        StartRotation();
                        if (!isExiting)
                        {
                            beatService.Start();
                            if (!chartTimer.IsEnabled) chartTimer.Start();
                        }
                    }
                    else
                    {
                        StopRotation();
                        beatService.Stop();
                        chartTimer.Stop();
                        currentBeatEnergy = 0;
                        ApplyBeatChartHeights(0);
                    }
                    break;

                case nameof(MainViewModel.ProgressPercent):
                    UpdateDockProgressArc(vm.ProgressPercent);
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

            var widthAnim = new DoubleAnimation
            {
                To = CompactWidth,
                BeginTime = TimeSpan.FromMilliseconds(700),
                Duration = TimeSpan.FromMilliseconds(550),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            widthAnim.Completed += (s, e) => isIntroPlaying = false;
            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

            var fadeAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                BeginTime = TimeSpan.FromMilliseconds(1000),
                Duration = TimeSpan.FromMilliseconds(400)
            };
            CompactTitleText.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            CompactBeatChart.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            CompactMenuIcon.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        // ================= ROTASI ALBUM ART =================
        private void StartRotation()
        {
            if (isRotating || !settings.RotateAlbumArt) return;
            isRotating = true;

            foreach (var rotate in new[] { AlbumRotate, DockAlbumRotate })
            {
                double start = rotate.Angle % 360;
                var anim = new DoubleAnimation
                {
                    From = start,
                    To = start + 360,
                    Duration = TimeSpan.FromSeconds(8),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
        }

        private void StopRotation()
        {
            if (!isRotating) return;
            isRotating = false;

            foreach (var rotate in new[] { AlbumRotate, DockAlbumRotate })
            {
                double current = rotate.Angle;
                rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                rotate.Angle = current;
            }
        }

        // ================= ANIMASI EXPAND / COLLAPSE (HOVER) =================
        private void HoverZone_MouseEnter(object sender, MouseEventArgs e) => SetExpanded(true);

        private void IslandBorder_MouseLeave(object sender, MouseEventArgs e) => SetExpanded(false);

        private void SetExpanded(bool expand)
        {
            if (isIntroPlaying) return;
            if (expand && isMenuVisible) return;
            if (isExpanded == expand) return;
            isExpanded = expand;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(280);

            double targetW = expand ? CurrentExpandedWidth : CollapsedWidth;
            double targetH = expand ? CurrentExpandedHeight : CollapsedHeight;

            CompactView.Visibility = expand ? Visibility.Collapsed : Visibility.Visible;
            ExpandedView.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;

            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation { To = targetW, Duration = dur, EasingFunction = ease });
            IslandBorder.BeginAnimation(FrameworkElement.HeightProperty,
                new DoubleAnimation { To = targetH, Duration = dur, EasingFunction = ease });

            if (!isDocked)
            {
                double targetRadius = expand ? 22 : 23;
                normalCornerRadius = new CornerRadius(targetRadius);
                IslandBorder.CornerRadius = normalCornerRadius;
            }

            AnimateDockShift(targetW);
            // Di SetExpanded(bool expand), setelah AnimateDockShift(targetW):
            if (!expand && vm.IsPlaying && !isExiting && !chartTimer.IsEnabled)
                chartTimer.Start();
            UpdateLyricBubble();
        }

        private void ApplySize()
        {
            if (isIntroPlaying || isExiting) return;

            double width = isExpanded ? CurrentExpandedWidth : CollapsedWidth;
            double height = isExpanded ? CurrentExpandedHeight : CollapsedHeight;
            AnimateSize(width, height);
            RepositionMenu(width, height);
        }

        public void OnSecondInstanceLaunched()
        {
            if (isIntroPlaying || isExiting) return;
            PlayBounceAnimation(widen: !isExpanded && !isDocked);
        }

        private void PlayBounceAnimation(bool widen)
        {
            const double WidenBy = 60;
            const double BounceStart = 300;
            const double BounceHalf = 120;
            double[] heights = { 12, 8, 4 };

            static KeyTime Ms(double ms) => KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms));

            if (widen)
            {
                double from = IslandBorder.ActualWidth;
                double normal = CollapsedWidth;
                double wide = normal + WidenBy;

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
                    SettingsPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                        WidthKeys(w => -(w / 2 + 10 + SettingsPill.Width / 2)));
                    ExitPillTranslate.BeginAnimation(TranslateTransform.XProperty,
                        WidthKeys(w => w / 2 + 10 + ExitPill.Width / 2));

                    if (isSettingsOpen)
                        SettingsPanel.BeginAnimation(FrameworkElement.WidthProperty,
                            WidthKeys(w => SettingsPanelGeometry(w).Width));
                }
            }

            var bounce = new DoubleAnimationUsingKeyFrames();
            double t = widen ? BounceStart : 0;
            if (t > 0) bounce.KeyFrames.Add(new LinearDoubleKeyFrame(0, Ms(t)));

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

        // ================= DRAG & DOCK KE KIRI/KANAN =================
        private void AlbumContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            if (isDocked) SetExpanded(true);
        }

        private void IslandBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isIntroPlaying || isExiting) return;
            if (e.ButtonState != MouseButtonState.Pressed) return;
            if (IsInteractiveSource(e.OriginalSource as DependencyObject)) return;

            double oldLeft = Left, oldTop = Top;
            if (isDocked) HideLyricBubble(immediate: true);
            try { DragMove(); }
            catch (InvalidOperationException) { return; }

            if (Math.Abs(Left - oldLeft) < 1 && Math.Abs(Top - oldTop) < 1)
            {
                UpdateLyricBubble();
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
            isExpanded = false;
            CompactView.Visibility = Visibility.Visible;
            ExpandedView.Visibility = Visibility.Collapsed;
            HideMenu();

            dockedLeft = left;
            isDocked = true;
            ApplyDockedVisuals(true);

            // Pastikan chartTimer & beatService aktif jika lagu sedang diputar
            if (vm.IsPlaying && !isExiting)
            {
                beatService.Start();
                if (!chartTimer.IsEnabled)
                    chartTimer.Start();
            }
            else
            {
                ApplyBeatChartHeights(0);
            }

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
            chartTimer.Stop();
            currentBeatEnergy = 0;
            ApplyBeatChartHeights(0);
            CompactView.Visibility = Visibility.Visible;
            ExpandedView.Visibility = Visibility.Collapsed;
            ApplyDockedVisuals(false);

            AnimateSize(CollapsedWidth, CollapsedHeight);
        }

        private void ApplyDockedVisuals(bool docked)
        {
            HoverZone.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
            MenuIconButton.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
            AlbumContainer.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
            AlbumContainer.VerticalAlignment = VerticalAlignment.Center;
            AlbumContainer.Margin = new Thickness(0);
            DockControls.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
            CompactBeatChart.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;

            if (docked)
            {
                IslandBorder.Background = (Brush)FindResource("DockedBackground");
                IslandBorder.BorderBrush = (Brush)FindResource("DockedBorderBrush");
                IslandBorder.BorderThickness = new Thickness(1);
                IslandBorder.Effect = (Effect)FindResource("DockedGlow");
                IslandBorder.CornerRadius = new CornerRadius(DockedWidth / 2);
            }
            else
            {
                IslandBorder.Background = normalBackground;
                IslandBorder.BorderBrush = normalBorderBrush;
                IslandBorder.BorderThickness = normalBorderThickness;
                IslandBorder.Effect = normalEffect;
                IslandBorder.CornerRadius = normalCornerRadius;
            }
        }

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
            Left = left;
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

        private void ApplyBubbleSide()
        {
            bool left = dockedLeft;
            var side = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;

            LyricBubble.HorizontalAlignment = side;
            LyricBubble.RenderTransformOrigin = new Point(left ? 0 : 1, 0.5);
            LyricBubbleBody.Margin = left ? new Thickness(8, 0, 0, 0) : new Thickness(0, 0, 8, 0);

            // Ekor menunjuk ke arah kapsul
            LyricBubbleLink.HorizontalAlignment = side;
            LyricBubbleTailFill.Data = Geometry.Parse(left ? "M 9,0 L 0,6 L 9,12 Z" : "M 0,0 L 9,6 L 0,12 Z");
            LyricBubbleTailLine.Data = Geometry.Parse(left ? "M 8.5,0 L 0.5,6 L 8.5,12" : "M 0.5,0 L 8.5,6 L 0.5,12");
        }

        private void PopInLyric(string text)
        {
            shownLyric = text;
            LyricBubbleText.Text = text;
            ApplyBubbleSide();

            double center = 10 + DockedHeight / 2;
            double gap = ActualWidth / 2 + DockedWidth / 2 + 4;
            Thickness MarginFor(double top) => dockedLeft
                ? new Thickness(gap, top, 0, 0)
                : new Thickness(0, top, gap, 0);

            LyricBubble.VerticalAlignment = VerticalAlignment.Top;
            LyricBubble.Margin = MarginFor(0);
            LyricBubble.Visibility = Visibility.Visible;
            LyricBubble.UpdateLayout();

            LyricBubble.Margin = MarginFor(center - LyricBubble.ActualHeight / 2);

            var dur = TimeSpan.FromMilliseconds(320);
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            double slideFrom = dockedLeft ? -16 : 16;

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
                ShowPill(SettingsPill, SettingsPillScale, SettingsPillTranslate, -1);
                ShowPill(ExitPill, ExitPillScale, ExitPillTranslate, +1);
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

        private void AnimateMenuIcon(bool toClose)
        {
            var target = (Geometry)FindResource(toClose ? "CloseCircleIcon" : "ChevronCircleIcon");
            var current = CompactMenuIcon.Data;

            var total = TimeSpan.FromMilliseconds(400);
            var middle = TimeSpan.FromMilliseconds(200);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            double start = MenuIconRotate.Angle % 360;
            MenuIconRotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(start, start + 360, total) { EasingFunction = ease });

            MenuIconBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation
                {
                    To = toClose ? MenuIconRed : MenuIconGreen,
                    Duration = total,
                    EasingFunction = ease
                });

            var swap = new ObjectAnimationUsingKeyFrames { Duration = total };
            swap.KeyFrames.Add(new DiscreteObjectKeyFrame(current, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            swap.KeyFrames.Add(new DiscreteObjectKeyFrame(target, KeyTime.FromTimeSpan(middle)));
            CompactMenuIcon.BeginAnimation(System.Windows.Shapes.Path.DataProperty, swap);
        }

        private void ShowPill(Border pill, ScaleTransform scale, TranslateTransform translate, int side)
        {
            double half = IslandBorder.ActualWidth / 2;
            double targetX = side * (half + 10 + pill.Width / 2);
            double startX = side * (half - 20);

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

        private (double Width, double CenterX) SettingsPanelGeometry(double islandWidth)
        {
            double half = islandWidth / 2;
            double left = -(half + 10 + SettingsPill.Width);
            double right = half;
            return (right - left, (left + right) / 2);
        }

        private void LayoutSettingsPanel(double islandWidth, double islandHeight, TimeSpan duration)
        {
            double width, centerX, top;

            if (isDocked)
            {
                width = 274;
                double pillEdge = DockedWidth / 2 + 10 + SettingsPill.Width;
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

        // ================= ANIMASI KELUAR =================
        private void ExitWithAnimation()
        {
            if (isExiting) return;
            isExiting = true;
            isIntroPlaying = true;
            vm.StopTimers();
            beatService.Stop();
            HideMenu();

            if (isDocked)
            {
                AlbumContainer.VerticalAlignment = VerticalAlignment.Top;
                AlbumContainer.Margin = new Thickness(0, 4, 0, 0);
                AlbumContainer.Visibility = Visibility.Visible;
                AlbumContainer.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));

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

            var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(250) };
            CompactTitleText.BeginAnimation(UIElement.OpacityProperty, fade);
            CompactBeatChart.BeginAnimation(UIElement.OpacityProperty, fade);
            CompactMenuIcon.BeginAnimation(UIElement.OpacityProperty, fade);

            var widthAnim = new DoubleAnimation
            {
                To = 48,
                BeginTime = TimeSpan.FromMilliseconds(250),
                Duration = TimeSpan.FromMilliseconds(550),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

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

            var islandFade = new DoubleAnimation
            {
                To = 0,
                BeginTime = TimeSpan.FromMilliseconds(1000),
                Duration = TimeSpan.FromMilliseconds(200)
            };
            islandFade.Completed += (s, e) => Application.Current.Shutdown();
            IslandBorder.BeginAnimation(UIElement.OpacityProperty, islandFade);
        }

        // ================= BEAT CHART DI PILL BAWAH ALBUM =================
        private void OnBeatUpdated(float rawEnergy)
        {
            lastRealBeatTick = Environment.TickCount64;
            currentBeatEnergy = Math.Max(currentBeatEnergy * 0.45, Math.Clamp(rawEnergy * 1.4f, 0f, 1f));
        }

        // Perbarui OnChartTimerTick() & ApplyBeatChartHeights():
        private void OnChartTimerTick()
        {
            // Berjalan saat docked ATAU saat mode compact (tidak sedang di-hover/expanded)
            bool shouldAnimate = (isDocked || !isExpanded) && !isExiting && vm.IsPlaying;
            if (!shouldAnimate)
            {
                chartTimer.Stop();
                currentBeatEnergy = 0;
                ApplyBeatChartHeights(0);
                return;
            }

            double energy = currentBeatEnergy;
            if (Environment.TickCount64 - lastRealBeatTick > 250 || energy < 0.15)
            {
                double t = Environment.TickCount64 * 0.009;
                double beatPulse = Math.Pow(0.5 + 0.5 * Math.Sin(t * 2.4), 1.6);
                energy = Math.Max(energy, 0.28 + 0.65 * beatPulse);
            }
            else
            {
                currentBeatEnergy *= 0.88;
            }

            ApplyBeatChartHeights(energy);
        }

        private void ApplyBeatChartHeights(double energy)
        {
            double clamped = Math.Clamp(energy, 0.0, 1.0);
            double phase = Environment.TickCount64 * 0.015;

            // 1. Update 7 batang di mode Docked
            if (beatBars != null)
            {
                const double minH = 2.5, maxH = 11.0;
                int center = beatBars.Length / 2;
                double maxDist = Math.Max(1.0, beatBars.Length / 2.0);

                for (int i = 0; i < beatBars.Length; i++)
                {
                    double dome = 1.0 - 0.35 * (Math.Abs(i - center) / maxDist);
                    double wave = 0.55 + 0.45 * Math.Sin(phase + i * 1.15);
                    double target = clamped * dome * wave;

                    barLevels[i] = target > barLevels[i]
                        ? barLevels[i] * 0.25 + target * 0.75
                        : barLevels[i] * 0.72 + target * 0.28;

                    if (beatBars[i] != null)
                        beatBars[i].Height = minH + (maxH - minH) * Math.Clamp(barLevels[i], 0.0, 1.0);
                }
            }

            // 2. Update 5 batang di mode Compact (tidak di-hover)
            if (compactBeatBars != null)
            {
                const double minH = 3.5, maxH = 14.0;
                int center = compactBeatBars.Length / 2;
                double maxDist = Math.Max(1.0, compactBeatBars.Length / 2.0);

                for (int i = 0; i < compactBeatBars.Length; i++)
                {
                    double dome = 1.0 - 0.45 * (Math.Abs(i - center) / maxDist);
                    double wave = 0.55 + 0.45 * Math.Sin(phase + i * 1.3);
                    double target = clamped * dome * wave;

                    compactBarLevels[i] = target > compactBarLevels[i]
                        ? compactBarLevels[i] * 0.25 + target * 0.75
                        : compactBarLevels[i] * 0.72 + target * 0.28;

                    if (compactBeatBars[i] != null)
                        compactBeatBars[i].Height = minH + (maxH - minH) * Math.Clamp(compactBarLevels[i], 0.0, 1.0);
                }
            }
        }
        // ================= RING TIMESTAMP DI DOCK ALBUM =================
        private void UpdateDockProgressArc(double percent)
        {
            if (DockProgressArc == null) return;

            double clamped = Math.Clamp(percent, 0.0, 100.0);
            if (clamped <= 0.1)
            {
                DockProgressArc.Data = null;
                return;
            }

            const double size = 44.0;
            const double strokeThickness = 2.5;
            double radius = (size - strokeThickness) / 2.0; // pas di tengah ketebalan stroke
            double cx = size / 2.0;
            double cy = size / 2.0;

            // Jika mendekati 100%, gambar lingkaran penuh agar tidak ada celah kecil di ujung
            if (clamped >= 99.8)
            {
                var fullGeo = new EllipseGeometry(new Point(cx, cy), radius, radius);
                fullGeo.Freeze();
                DockProgressArc.Data = fullGeo;
                return;
            }

            double sweepAngle = 360.0 * (clamped / 100.0);
            double startRad = -Math.PI / 2.0; // mulai dari jam 12 (atas)
            double endRad = startRad + (sweepAngle * Math.PI / 180.0);

            var startPoint = new Point(
                cx + radius * Math.Cos(startRad),
                cy + radius * Math.Sin(startRad));

            var endPoint = new Point(
                cx + radius * Math.Cos(endRad),
                cy + radius * Math.Sin(endRad));

            bool isLargeArc = sweepAngle > 180.0;

            var pathFigure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false,
                IsFilled = false
            };

            pathFigure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                RotationAngle = 0,
                IsLargeArc = isLargeArc,
                SweepDirection = SweepDirection.Clockwise,
                IsStroked = true
            });

            var pathGeo = new PathGeometry();
            pathGeo.Figures.Add(pathFigure);
            pathGeo.Freeze();
            DockProgressArc.Data = pathGeo;
        }
    }
}