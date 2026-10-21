using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DynamicIslandDesktop.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIslandDesktop.Services
{
    /// <summary>
    /// Pembungkus Windows Media Transport Controls. Tidak tahu apa pun tentang View/ViewModel;
    /// semua event dikirim kembali ke thread UI.
    /// </summary>
    public class MediaSessionService
    {
        private GlobalSystemMediaTransportControlsSessionManager? manager;
        private GlobalSystemMediaTransportControlsSession? session;
        private SynchronizationContext? ui;
        private int mediaVersion;

        /// <summary>Info lagu berubah. null = tidak ada sesi media.</summary>
        public event Action<MediaInfo?>? MediaChanged;

        /// <summary>Status play/pause berubah (true = sedang diputar).</summary>
        public event Action<bool>? PlaybackChanged;

        /// <summary>Harus dipanggil dari thread UI.</summary>
        public async Task InitializeAsync()
        {
            ui = SynchronizationContext.Current;

            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (manager == null) return;

            // Event ketika aplikasi pemutar berubah (misal dari Spotify pindah ke Chrome/YouTube)
            manager.CurrentSessionChanged += (s, _) => Post(() => AttachSession(s.GetCurrentSession()));

            AttachSession(manager.GetCurrentSession());
        }

        // ================= KONTROL =================
        public async Task TogglePlayPauseAsync()
        {
            if (session == null) return;

            var info = session.GetPlaybackInfo();
            if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await session.TryPauseAsync();
            else
                await session.TryPlayAsync();
        }

        public async Task NextAsync()
        {
            if (session != null) await session.TrySkipNextAsync();
        }

        public async Task PreviousAsync()
        {
            if (session != null) await session.TrySkipPreviousAsync();
        }

        // ================= INFO WAKTU =================
        public int GetDurationSeconds()
        {
            try
            {
                var tl = session?.GetTimelineProperties();
                return tl == null ? 0 : (int)(tl.EndTime - tl.StartTime).TotalSeconds;
            }
            catch { return 0; }
        }

        /// <summary>Posisi lagu saat ini, diinterpolasi dengan waktu sejak update terakhir agar halus.</summary>
        public TimeSpan? GetPosition()
        {
            if (session == null) return null;

            try
            {
                var tl = session.GetTimelineProperties();
                var pb = session.GetPlaybackInfo();

                TimeSpan pos = tl.Position;
                if (pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    pos += DateTimeOffset.Now - tl.LastUpdatedTime;
                return pos;
            }
            catch { return null; }
        }

        // ================= INTERNAL =================
        private void AttachSession(GlobalSystemMediaTransportControlsSession? newSession)
        {
            if (session != null)
            {
                session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
            }

            session = newSession;
            mediaVersion++;   // batalkan pembacaan thumbnail yang masih berjalan

            if (session == null)
            {
                MediaChanged?.Invoke(null);
                PlaybackChanged?.Invoke(false);
                return;
            }

            session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
            session.PlaybackInfoChanged += Session_PlaybackInfoChanged;

            _ = RefreshMediaAsync();
            RaisePlayback();
        }

        private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
            => Post(() => _ = RefreshMediaAsync());

        private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
            => Post(RaisePlayback);

        private async Task RefreshMediaAsync()
        {
            var s = session;
            if (s == null) return;

            int version = ++mediaVersion;

            try
            {
                var props = await s.TryGetMediaPropertiesAsync();
                if (props == null) return;

                var thumb = await LoadThumbnailAsync(props.Thumbnail);

                // Abaikan hasil jika lagu/sesi sudah berganti selama proses
                if (version != mediaVersion || s != session) return;

                MediaChanged?.Invoke(new MediaInfo(props.Title ?? "", props.Artist ?? "", thumb));
            }
            catch { }
        }

        private void RaisePlayback()
        {
            try
            {
                var pb = session?.GetPlaybackInfo();
                bool playing = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                PlaybackChanged?.Invoke(playing);
            }
            catch { }
        }

        // Mengembalikan data gambar mentah; pengubahan ke gambar WPF dilakukan di View (converter)
        private static async Task<byte[]?> LoadThumbnailAsync(IRandomAccessStreamReference? thumbnail)
        {
            if (thumbnail == null) return null;

            try
            {
                using var stream = await thumbnail.OpenReadAsync();
                using var netStream = stream.AsStream();
                using var ms = new MemoryStream();
                await netStream.CopyToAsync(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        // Event WinRT datang dari thread lain, jadi kembalikan ke thread UI
        private void Post(Action action)
        {
            if (ui == null) action();
            else ui.Post(_ => action(), null);
        }
    }
}