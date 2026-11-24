using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Contrato de abstracción para el motor de síntesis de voz nativo del sistema operativo anfitrión.
    /// Permite que cada plataforma (Windows, macOS y Linux) provea sus propias voces locales
    /// de manera transparente para el resto de la aplicación.
    /// </summary>
    public interface ISystemVoiceEngine : IDisposable
    {
        /// <summary>
        /// Indica si el motor de voz nativo está disponible y listo para sintetizar audio.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Nombre descriptivo del motor de voz del sistema (ej: "Windows WinRT", "macOS say", "Linux speech-dispatcher").
        /// </summary>
        string EngineName { get; }

        /// <summary>
        /// Obtiene la lista de códigos de idioma BCP-47 instalados en el sistema operativo (ej: "es-MX", "es-ES", "en-US").
        /// </summary>
        List<string> GetInstalledLanguages();

        /// <summary>
        /// Obtiene los nombres visuales de las voces nativas disponibles para el idioma indicado.
        /// </summary>
        List<string> GetVoicesForLanguage(string language);

        /// <summary>
        /// Sintetiza una cadena de texto a un archivo de audio WAV utilizando la voz nativa seleccionada.
        /// </summary>
        /// <param name="text">Texto a pronunciar.</param>
        /// <param name="outputPath">Ruta del archivo WAV de salida.</param>
        /// <param name="voiceName">Nombre de la voz seleccionada.</param>
        /// <param name="language">Código BCP-47 del idioma.</param>
        /// <param name="speed">Factor de velocidad (0.5x a 2.0x).</param>
        /// <param name="pitch">Factor de tono (0.5 a 1.5).</param>
        /// <param name="volume">Volumen normalizado (0.0 a 1.0).</param>
        Task SynthesizeToWavAsync(
            string text,
            string outputPath,
            string voiceName,
            string language,
            float speed,
            float pitch,
            float volume);

        /// <summary>
        /// Obtiene el idioma y nombre de la voz predeterminada según la configuración del sistema operativo.
        /// </summary>
        (string Language, string VoiceName) GetSystemDefaultLanguageAndVoice();
    }
}
