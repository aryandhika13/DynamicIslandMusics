using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DynamicIslandDesktop.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIslandDesktop.Services
{
    public class MediaSessionService
    {
        private GlobalSystemMediaTransportControlsSessionManager? manager;
        private GlobalSystemMediaTransportControlsSession? session;
        private SynchronizationContext? ui;
        private int mediaVersion;

        public event Action<MediaInfo?>? MediaChanged;
        public event Action<bool>? PlaybackChanged;
        public event Action? TimelineChanged; // <-- BARU: untuk seek / perubahan durasi

        public async Task InitializeAsync()
        {
            ui = SynchronizationContext.Current;

            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (manager == null) return;

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
            var (_, duration) = GetTimeline();
            return (int)duration.TotalSeconds;
        }

        public TimeSpan? GetPosition()
        {
            if (session == null) return null;
            var (pos, _) = GetTimeline();
            return pos;
        }

        /// <summary>Mengambil posisi berjalan dan total durasi sekaligus.</summary>
        public (TimeSpan Position, TimeSpan Duration) GetTimeline()
        {
            if (session == null) return (TimeSpan.Zero, TimeSpan.Zero);

            try
            {
                var tl = session.GetTimelineProperties();
                var pb = session.GetPlaybackInfo();

                TimeSpan duration = tl.EndTime - tl.StartTime;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

                TimeSpan pos = tl.Position - tl.StartTime;
                if (pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    && tl.LastUpdatedTime != default)
                {
                    pos += DateTimeOffset.Now - tl.LastUpdatedTime;
                }

                if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
                if (duration > TimeSpan.Zero && pos > duration) pos = duration;

                return (pos, duration);
            }
            catch
            {
                return (TimeSpan.Zero, TimeSpan.Zero);
            }
        }

        // ================= INTERNAL =================
        private void AttachSession(GlobalSystemMediaTransportControlsSession? newSession)
        {
            if (session != null)
            {
                session.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                session.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
                session.TimelinePropertiesChanged -= Session_TimelinePropertiesChanged;
            }

            session = newSession;
            mediaVersion++;

            if (session == null)
            {
                MediaChanged?.Invoke(null);
                PlaybackChanged?.Invoke(false);
                TimelineChanged?.Invoke();
                return;
            }

            session.MediaPropertiesChanged += Session_MediaPropertiesChanged;
            session.PlaybackInfoChanged += Session_PlaybackInfoChanged;
            session.TimelinePropertiesChanged += Session_TimelinePropertiesChanged;

            _ = RefreshMediaAsync();
            RaisePlayback();
            TimelineChanged?.Invoke();
        }

        private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
            => Post(() => _ = RefreshMediaAsync());

        private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
            => Post(() => { RaisePlayback(); TimelineChanged?.Invoke(); });

        private void Session_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
            => Post(() => TimelineChanged?.Invoke());

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

                if (version != mediaVersion || s != session) return;

                MediaChanged?.Invoke(new MediaInfo(props.Title ?? "", props.Artist ?? "", thumb));
                TimelineChanged?.Invoke();
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

        private void Post(Action action)
        {
            if (ui == null) action();
            else ui.Post(_ => action(), null);
        }
    }
}