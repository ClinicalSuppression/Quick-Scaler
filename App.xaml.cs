using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace WindowsQuickScaler
{
    public partial class App : System.Windows.Application
    {
        private Mutex? singleInstanceMutex;
        private bool ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (sender, args) =>
            {
                args.Handled = true;
                LogError(args.Exception);
                MessageBox.Show(
                    $"Something went wrong, but the app kept running.\n\n{args.Exception.Message}\n\nDetails were written to the log in %LOCALAPPDATA%\\WindowsQuickScaler\\app.log.",
                    "Windows Quick Scaler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    LogError(ex);
            };

            CliOptions options = CliArgs.Parse(e.Args);

            if (options.Help)
            {
                MessageBox.Show(
                    CliArgs.HelpText,
                    "Windows Quick Scaler — help",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Single instance. A named mutex is released automatically if the process dies,
            // so a crash never leaves a stale lock behind.
            singleInstanceMutex = new Mutex(initiallyOwned: false, @"Local\WindowsQuickScaler.SingleInstance");
            try
            {
                ownsMutex = singleInstanceMutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                MessageBox.Show(
                    "Windows Quick Scaler is already running. Look for its icon in the system tray (click the ^ arrow if hidden).",
                    "Windows Quick Scaler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var window = new MainWindow(options);
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (ownsMutex)
                    singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // Releasing an unowned mutex is harmless to ignore on shutdown.
            }

            singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        internal static void LogError(Exception exception)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsQuickScaler");
                Directory.CreateDirectory(directory);
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(directory, "app.log"), line);
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
