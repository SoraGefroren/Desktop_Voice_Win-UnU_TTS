using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Implementación del motor de voces nativas para sistemas operativos Microsoft Windows.
    /// Utiliza la API moderna de Windows Runtime (WinRT SpeechSynthesizer), ofreciendo acceso
    /// a voces locales instaladas (David, Zira, Helena, Sabina, etc.) y voces naturales online de Windows 11.
    /// </summary>
    public sealed class WindowsVoiceEngine : ISystemVoiceEngine
    {
        private SpeechSynthesizer? _synthesizer;
        private readonly object _syncLock = new();

        public bool IsAvailable => OperatingSystem.IsWindows();

        public string EngineName => "Windows WinRT Speech Engine";

        /// <summary>
        /// Obtiene los códigos BCP-47 de los idiomas instalados en Windows.
        /// </summary>
        public List<string> GetInstalledLanguages()
        {
            if (!IsAvailable) return new List<string>();

            try
            {
                return SpeechSynthesizer.AllVoices
                    .Select(v => v.Language)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(l => l)
                    .ToList();
            }
            catch
            {
                return new List<string> { CultureInfo.CurrentUICulture.Name };
            }
        }

        /// <summary>
        /// Devuelve los nombres legibles de las voces registradas en Windows para el idioma indicado.
        /// </summary>
        public List<string> GetVoicesForLanguage(string language)
        {
            if (!IsAvailable) return new List<string>();

            try
            {
                return SpeechSynthesizer.AllVoices
                    .Where(v => string.IsNullOrEmpty(language) ||
                                v.Language.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                                v.Language.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(v => IsOnlineVoice(v) ? 0 : 1) // Voces online primero
                    .ThenBy(v => v.DisplayName)
                    .Select(v =>
                    {
                        string typeLabel = IsOnlineVoice(v) ? " (Online)" : " (Local)";
                        return v.DisplayName + typeLabel;
                    })
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Obtiene o inicializa la instancia reutilizable de SpeechSynthesizer para evitar fragmentación en COM.
        /// </summary>
        private SpeechSynthesizer GetOrCreateSynthesizer()
        {
            lock (_syncLock)
            {
                return _synthesizer ??= new SpeechSynthesizer();
            }
        }

        /// <summary>
        /// Sintetiza el texto utilizando el SpeechSynthesizer nativo de Windows y escribe el audio en un archivo WAV.
        /// Libera explícitamente todos los flujos de memoria COM nativos para evitar System.OutOfMemoryException
        /// tras horas de uso continuo.
        /// </summary>
        public async Task SynthesizeToWavAsync(
            string text,
            string outputPath,
            string voiceName,
            string language,
            float speed,
            float pitch,
            float volume)
        {
            if (!IsAvailable)
                throw new PlatformNotSupportedException("WindowsVoiceEngine solo puede ejecutarse en Microsoft Windows.");

            var synthesizer = GetOrCreateSynthesizer();

            try
            {
                string baseName = VoiceManager.ExtractVoiceName(voiceName);

                if (!string.IsNullOrEmpty(baseName))
                {
                    var voice = SpeechSynthesizer.AllVoices
                        .FirstOrDefault(v =>
                            v.DisplayName.Equals(baseName, StringComparison.OrdinalIgnoreCase) &&
                            (string.IsNullOrEmpty(language) || v.Language.Equals(language, StringComparison.OrdinalIgnoreCase)));
                    if (voice != null && synthesizer.Voice.Id != voice.Id)
                    {
                        synthesizer.Voice = voice;
                    }
                }

                // Configurar opciones de velocidad, tono y volumen en el sintetizador
                synthesizer.Options.SpeakingRate = Math.Clamp(speed, 0.5, 6.0);
                synthesizer.Options.AudioPitch = Math.Clamp(pitch, 0.0, 2.0);
                synthesizer.Options.AudioVolume = Math.Clamp(volume, 0.0, 1.0);

                string langCode = string.IsNullOrEmpty(language) ? "es-ES" : language;
                string encodedText = WebUtility.HtmlEncode(text);
                string ssml =
                    $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{langCode}'>" +
                    $"{encodedText}" +
                    "</speak>";

                // CRÍTICO: SpeechSynthesisStream es un recurso COM nativo de Windows (IClosable / IDisposable).
                // Debe liberarse de inmediato para evitar que los búferes de audio PCM no administrados
                // se acumulen en el heap de Windows causando OutOfMemoryException tras síntesis prolongadas.
                using SpeechSynthesisStream stream = await synthesizer.SynthesizeSsmlToStreamAsync(ssml);
                using Stream readStream = stream.AsStreamForRead();
                using FileStream fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await readStream.CopyToAsync(fileStream);
            }
            catch
            {
                // En caso de fallo o estado inválido en COM, reiniciar la instancia del sintetizador
                lock (_syncLock)
                {
                    try { _synthesizer?.Dispose(); } catch { }
                    _synthesizer = null;
                }
                throw;
            }
        }

        /// <summary>
        /// Consulta la voz y el idioma predeterminados configurados en el sistema operativo Windows.
        /// </summary>
        public (string Language, string VoiceName) GetSystemDefaultLanguageAndVoice()
        {
            if (IsAvailable)
            {
                try
                {
                    var defaultVoice = SpeechSynthesizer.DefaultVoice;
                    if (defaultVoice != null)
                    {
                        string typeLabel = IsOnlineVoice(defaultVoice) ? " (Online)" : " (Local)";
                        return (defaultVoice.Language, defaultVoice.DisplayName + typeLabel);
                    }
                }
                catch
                {
                }
            }

            string cultureLang = CultureInfo.CurrentUICulture.Name;
            return (cultureLang, string.Empty);
        }

        private static bool IsOnlineVoice(VoiceInformation voice)
        {
            return voice.DisplayName.Contains("Natural", StringComparison.OrdinalIgnoreCase) ||
                   voice.DisplayName.Contains("Online", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                _synthesizer?.Dispose();
                _synthesizer = null;
            }
        }
    }
}
