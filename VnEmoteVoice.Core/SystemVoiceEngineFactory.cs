using System;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Fábrica encargada de instanciar el motor de voz nativo correspondiente
    /// al sistema operativo en el que se está ejecutando la aplicación.
    /// </summary>
    public static class SystemVoiceEngineFactory
    {
        public static ISystemVoiceEngine Create()
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsVoiceEngine();
            }

            if (OperatingSystem.IsMacOS())
            {
                return new MacVoiceEngine();
            }

            if (OperatingSystem.IsLinux())
            {
                return new LinuxVoiceEngine();
            }

            // Fallback genérico para entornos desconocidos
            return new WindowsVoiceEngine();
        }
    }
}
