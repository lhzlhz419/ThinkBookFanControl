using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace ThinkBookFanControl;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) => LogException(args.ExceptionObject as Exception);

            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            app.DispatcherUnhandledException += (_, args) =>
            {
                LogException(args.Exception);
                MessageBox.Show(args.Exception.ToString(), "ThinkBook Fan Control error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            var startToTrayRequested = args.Any(arg => string.Equals(arg, "--startup-tray", StringComparison.OrdinalIgnoreCase));
            app.Run(new MainWindow(startToTrayRequested));
        }
        catch (Exception ex)
        {
            LogException(ex);
            MessageBox.Show(ex.ToString(), "ThinkBook Fan Control startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void LogException(Exception? exception)
    {
        if (exception is null)
            return;

        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".thinkbook_fan_control");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "csharp-crash.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}]\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Last-ditch logging must not create a second crash.
        }
    }
}
