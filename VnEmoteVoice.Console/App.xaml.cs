using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace VnEmoteVoice.Console
{
    public partial class App : Application
    {
        public App()
        {
            // Registrar capturadores de errores antes de que se cargue cualquier ventana
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                LogCrash("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                LogCrash("OnStartup.Exception", ex);
                throw;
            }
        }

        private static void LogCrash(string source, Exception? ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== CRASH LOG [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}) ===");
            Exception? curr = ex;
            while (curr != null)
            {
                sb.AppendLine($"[{curr.GetType().FullName}]: {curr.Message}");
                sb.AppendLine(curr.StackTrace);
                sb.AppendLine("----------------------------------------");
                curr = curr.InnerException;
            }

            string logText = sb.ToString();

            // Guardar en directorio de trabajo y en directorio del ejecutable
            try { File.WriteAllText("crash_log.txt", logText); } catch { }
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt"), logText); } catch { }

            MessageBox.Show(
                $"Se produjo un error al iniciar Vn Emote Voice:\n\n{logText}",
                "Error de Inicio - Vn Emote Voice",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}