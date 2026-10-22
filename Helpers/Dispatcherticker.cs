using System;
using System.Windows.Threading;
using DynamicIslandDesktop.Services;

namespace DynamicIslandDesktop.Helpers
{
    /// <summary>Implementasi ITicker untuk WPF. Tick berjalan di thread UI.</summary>
    public class DispatcherTicker : ITicker
    {
        private readonly DispatcherTimer timer;

        public event Action? Tick;

        public DispatcherTicker(TimeSpan? interval = null)
        {
            timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) => Tick?.Invoke();
        }

        public void Start() => timer.Start();
        public void Stop() => timer.Stop();
    }
}