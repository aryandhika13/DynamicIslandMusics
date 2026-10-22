using System.Configuration;
using System.Data;
using System.Windows;

namespace DynamicIslandDesktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Nama unik per aplikasi. "Local\" = berlaku per sesi Windows pengguna.
        private const string MutexName = @"Local\DynamicIslandDesktop-7C4E2B1A-5F3D-4A9E-8B6C-2D1F0E9A3B54";
        private const string SignalName = @"Local\DynamicIslandDesktop-7C4E2B1A-5F3D-4A9E-8B6C-2D1F0E9A3B54-activate";

        private Mutex? mutex;
        private bool ownsMutex;
        private EventWaitHandle? activateSignal;
        private RegisteredWaitHandle? signalWait;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Sinyal dibuat lebih dulu: siapa pun yang datang (pertama atau kedua) memakai objek yang sama
            activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);

            mutex = new Mutex(true, MutexName, out bool createdNew);
            ownsMutex = createdNew;

            if (!createdNew)
            {
                // Instance kedua: beri tahu yang pertama, lalu keluar tanpa membuat window
                activateSignal.Set();
                Shutdown();
                return;
            }

            // Instance pertama: tunggu sinyal tanpa menyita thread khusus
            signalWait = ThreadPool.RegisterWaitForSingleObject(
                activateSignal,
                (state, timedOut) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Current.MainWindow is MainWindow w) w.OnSecondInstanceLaunched();
                })),
                null, Timeout.Infinite, executeOnlyOnce: false);

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            signalWait?.Unregister(null);
            activateSignal?.Dispose();

            if (ownsMutex) mutex?.ReleaseMutex();
            mutex?.Dispose();

            base.OnExit(e);
        }
    }

}
