using System;
using System.Windows;

namespace VtuberTts.Console
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
                MessageBox.Show($"Error en la interfaz: {args.Exception.Message}\n\n{args.Exception.StackTrace}", "Error de UI", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}