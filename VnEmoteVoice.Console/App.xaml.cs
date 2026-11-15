using System;
using System.Windows;

namespace VnEmoteVoice.Console
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Capturar cualquier error no controlado al iniciar la interfaz
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                MessageBox.Show($"Error crítico no controlado: {ex?.Message}\n\n{ex?.StackTrace}", "Error Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                var sb = new System.Text.StringBuilder();
                Exception? ex = args.Exception;
                while (ex != null)
                {
                    sb.AppendLine($"[{ex.GetType().Name}]: {ex.Message}");
                    sb.AppendLine(ex.StackTrace);
                    sb.AppendLine("------------------");
                    ex = ex.InnerException;
                }
                try { System.IO.File.WriteAllText("crash_log.txt", sb.ToString()); } catch { }
                MessageBox.Show($"Error en la interfaz:\n\n{sb}", "Error de UI", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}