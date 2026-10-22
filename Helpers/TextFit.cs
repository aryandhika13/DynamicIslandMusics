using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIslandDesktop.Helpers
{
    /// <summary>
    /// Mengecilkan font TextBlock otomatis sampai teksnya muat.
    /// - NoWrap: teks harus muat selebar parent.
    /// - Wrap  : teks harus muat dalam MaxHeight.
    /// </summary>
    public static class TextFit
    {
        public static readonly DependencyProperty MaxFontSizeProperty =
            DependencyProperty.RegisterAttached("MaxFontSize", typeof(double), typeof(TextFit),
                new PropertyMetadata(0.0, OnMaxFontSizeChanged));

        public static readonly DependencyProperty MinFontSizeProperty =
            DependencyProperty.RegisterAttached("MinFontSize", typeof(double), typeof(TextFit),
                new PropertyMetadata(8.0));

        private static readonly DependencyProperty HookedProperty =
            DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(TextFit),
                new PropertyMetadata(false));

        public static double GetMaxFontSize(DependencyObject o) => (double)o.GetValue(MaxFontSizeProperty);
        public static void SetMaxFontSize(DependencyObject o, double v) => o.SetValue(MaxFontSizeProperty, v);
        public static double GetMinFontSize(DependencyObject o) => (double)o.GetValue(MinFontSizeProperty);
        public static void SetMinFontSize(DependencyObject o, double v) => o.SetValue(MinFontSizeProperty, v);

        /// <summary>
        /// Menghitung ukuran font terbesar (minFont..maxFont) yang membuat teks muat,
        /// beserta tinggi teksnya (dibatasi maxHeight saat wrap).
        /// </summary>
        public static (double FontSize, double Height) Compute(
            string? text, Typeface typeface, double pixelsPerDip,
            double maxFont, double minFont, double width, bool wrap, double maxHeight)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return (maxFont, 0);
            minFont = Math.Min(minFont, maxFont);

            FormattedText Make(double size)
            {
                var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, size, Brushes.Black, pixelsPerDip);
                if (wrap) ft.MaxTextWidth = width;
                return ft;
            }

            bool Fits(FormattedText ft) => wrap ? ft.Height <= maxHeight : ft.Width <= width;
            double Cap(double h) => wrap ? Math.Min(h, maxHeight) : h;

            var full = Make(maxFont);
            if (Fits(full)) return (maxFont, Cap(full.Height));

            // Binary search: ukuran terbesar antara min..max yang masih muat
            double lo = minFont, hi = maxFont;
            for (int i = 0; i < 8; i++)
            {
                double mid = (lo + hi) / 2;
                if (Fits(Make(mid))) lo = mid;
                else hi = mid;
            }
            return (lo, Cap(Make(lo).Height));
        }

        private static void OnMaxFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            Hook(tb);
            Fit(tb);
        }

        // Hitung ulang saat teks, MaxHeight, TextWrapping, atau lebar parent berubah
        private static void Hook(TextBlock tb)
        {
            if ((bool)tb.GetValue(HookedProperty)) return;
            tb.SetValue(HookedProperty, true);

            void refit(object? s, EventArgs a) => Fit(tb);

            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                .AddValueChanged(tb, refit);
            DependencyPropertyDescriptor.FromProperty(FrameworkElement.MaxHeightProperty, typeof(FrameworkElement))
                .AddValueChanged(tb, refit);
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextWrappingProperty, typeof(TextBlock))
                .AddValueChanged(tb, refit);

            void attachParent()
            {
                if (tb.Parent is FrameworkElement p) p.SizeChanged += (_, _) => Fit(tb);
            }

            if (tb.IsLoaded) attachParent();
            else
            {
                RoutedEventHandler onLoaded = null!;
                onLoaded = (_, _) =>
                {
                    tb.Loaded -= onLoaded;
                    attachParent();
                    Fit(tb);
                };
                tb.Loaded += onLoaded;
            }
        }

        private static void Fit(TextBlock tb)
        {
            double max = GetMaxFontSize(tb);
            if (max <= 0) return;

            var parent = tb.Parent as FrameworkElement;
            double width = parent == null ? 0 : parent.ActualWidth - tb.Margin.Left - tb.Margin.Right - 1;

            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            double dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
            bool wrap = tb.TextWrapping != TextWrapping.NoWrap;

            var (size, _) = Compute(tb.Text, typeface, dpi, max, GetMinFontSize(tb), width, wrap, tb.MaxHeight);

            // SetCurrentValue agar tidak menghapus binding/style yang ada
            tb.SetCurrentValue(TextBlock.FontSizeProperty, size);
        }
    }
}