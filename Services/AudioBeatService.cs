using Microsoft.VisualBasic;
using NAudio.Wave;
using System;
using System.Threading;

namespace DynamicIslandDesktop.Services
{
    /// <summary>
    /// Membaca level energi audio output Windows (WASAPI Loopback) untuk memicu efek spike beat.
    /// </summary>
    public sealed class AudioBeatService : IDisposable
    {
        private readonly SynchronizationContext? ui = SynchronizationContext.Current;
        private WasapiLoopbackCapture? capture;
        private float smoothedLevel;
        private float peakFloor = 0.08f;
        private long lastUiTickMs;
        private bool disposed;

        /// <summary>Nilai intensitas beat (0.0 - 1.0) yang sudah dihaluskan.</summary>
        public event Action<float>? BeatUpdated;

        public void Start()
        {
            if (disposed || capture != null) return;

            try
            {
                capture = new WasapiLoopbackCapture();
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += (_, __) => CleanupCapture();
                capture.StartRecording();
            }
            catch
            {
                CleanupCapture();
            }
        }

        public void Stop()
        {
            if (capture == null) return;
            try
            {
                capture.StopRecording();
            }
            catch
            {
                CleanupCapture();
            }
            RaiseBeat(0f);
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || capture == null) return;

            var fmt = capture.WaveFormat;
            int bytesPerSample = fmt.BitsPerSample / 8;
            int channels = Math.Max(1, fmt.Channels);
            int frameSize = bytesPerSample * channels;
            int frames = e.BytesRecorded / frameSize;
            if (frames <= 0) return;

            double sumSq = 0;
            int count = 0;

            // Ambil rata-rata RMS channel pertama (cukup ringan & responsif untuk beat)
            for (int i = 0; i < frames; i++)
            {
                int offset = i * frameSize;
                float sample = ReadSample(e.Buffer, offset, fmt);
                sumSq += sample * sample;
                count++;
            }

            float rms = count > 0 ? (float)Math.Sqrt(sumSq / count) : 0f;

            // Adaptasi ambang batas agar lagu pelan maupun keras tetap menghasilkan spike
            if (rms > peakFloor)
                peakFloor = peakFloor * 0.92f + rms * 0.08f;
            else
                peakFloor = Math.Max(0.04f, peakFloor * 0.995f);

            float normalized = Math.Clamp((rms - peakFloor * 0.35f) / Math.Max(0.05f, peakFloor * 1.15f), 0f, 1f);

            // Attack cepat saat beat masuk, release lebih halus agar spike tidak berkedip kasar
            if (normalized > smoothedLevel)
                smoothedLevel = smoothedLevel * 0.35f + normalized * 0.65f;
            else
                smoothedLevel = smoothedLevel * 0.82f + normalized * 0.18f;

            long nowMs = Environment.TickCount64;
            if (nowMs - lastUiTickMs >= 30)
            {
                lastUiTickMs = nowMs;
                float val = smoothedLevel;
                if (ui == null) BeatUpdated?.Invoke(val);
                else ui.Post(_ => BeatUpdated?.Invoke(val), null);
            }
        }

        private static float ReadSample(byte[] buffer, int offset, WaveFormat fmt)
        {
            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
                return BitConverter.ToSingle(buffer, offset);

            if (fmt.BitsPerSample == 16)
                return BitConverter.ToInt16(buffer, offset) / 32768f;

            if (fmt.BitsPerSample == 24 && offset + 2 < buffer.Length)
            {
                int b0 = buffer[offset];
                int b1 = buffer[offset + 1];
                int b2 = (sbyte)buffer[offset + 2];
                int val = (b2 << 16) | (b1 << 8) | b0;
                return val / 8388608f;
            }

            if (fmt.BitsPerSample == 32)
                return BitConverter.ToInt32(buffer, offset) / 2147483648f;

            return 0f;
        }

        private void RaiseBeat(float val)
        {
            smoothedLevel = val;
            if (ui == null) BeatUpdated?.Invoke(val);
            else ui.Post(_ => BeatUpdated?.Invoke(val), null);
        }

        private void CleanupCapture()
        {
            var c = capture;
            capture = null;
            if (c != null)
            {
                c.DataAvailable -= OnDataAvailable;
                c.Dispose();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
            CleanupCapture();
        }
    }
}