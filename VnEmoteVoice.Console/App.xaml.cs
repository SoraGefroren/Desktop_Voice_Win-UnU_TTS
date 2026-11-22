using System;
using System.IO;
using System.Text;
using System.Windows;

namespace VnEmoteVoice.Console
{
    /// <summary>
    /// Clase principal de la aplicación WPF (Windows Presentation Foundation).
    /// Controla el ciclo de vida del programa y captura cualquier error no controlado
    /// para registrarlo en un archivo de texto antes de cerrar la aplicación.
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Registrar capturadores globales de errores antes de que se abra la ventana principal.
            // Si ocurre un error inesperado en cualquier parte del programa, lo interceptamos aquí.
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                LogCrash("DispatcherUnhandledException", args.Exception);
                args.Handled = true; // Evita que la aplicación colapse abruptamente sin avisar al usuario.
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

        /// <summary>
        /// Guarda los detalles de cualquier fallo crítico en un archivo de texto llamado 'crash_log.txt'.
        /// Esto permite diagnosticar problemas fácilmente, incluso si el programa no llega a abrirse.
        /// </summary>
        /// <param name="source">Lugar o evento donde se originó el fallo.</param>
        /// <param name="ex">La excepción o error ocurrido.</param>
        private static void LogCrash(string source, Exception? ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== REPORTE DE ERROR [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}) ===");
            Exception? curr = ex;
            while (curr != null)
            {
                sb.AppendLine($"[{curr.GetType().FullName}]: {curr.Message}");
                sb.AppendLine(curr.StackTrace);
                sb.AppendLine("----------------------------------------");
                curr = curr.InnerException;
            }

            string logText = sb.ToString();

            // Intentar guardar el reporte tanto en la carpeta actual como junto al ejecutable
            try { File.WriteAllText("crash_log.txt", logText); } catch { }
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt"), logText); } catch { }

            // Mostrar una ventana emergente informando al usuario sobre el problema
            MessageBox.Show(
                $"Se produjo un error al iniciar Vn Emote Voice:\n\n{logText}",
                "Error de Inicio - Vn Emote Voice",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
