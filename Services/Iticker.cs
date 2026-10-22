using System;

namespace DynamicIslandDesktop.Services
{
    /// <summary>
    /// Abstraksi timer berkala. ViewModel hanya mengenal interface ini, sehingga
    /// tidak bergantung pada timer milik WPF dan mudah diganti saat unit test.
    /// </summary>
    public interface ITicker
    {
        event Action? Tick;
        void Start();
        void Stop();
    }
}