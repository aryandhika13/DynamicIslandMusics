using CommunityToolkit.Mvvm.ComponentModel;

namespace DynamicIslandDesktop.Models
{
    /// <summary>Pengaturan aplikasi. Tambahkan properti baru di sini lalu bind di SettingsWindow.</summary>
    public partial class AppSettings : ObservableObject
    {
        public const double DefaultLyricFontSize = 14;

        [ObservableProperty]
        private bool rotateAlbumArt = true;

        [ObservableProperty]
        private bool alwaysOnTop = true;

        /// <summary>Ukuran font lirik utama (mode expanded). Compact dan ukuran island ikut berskala.</summary>
        [ObservableProperty]
        private double lyricFontSize = DefaultLyricFontSize;
    }
}