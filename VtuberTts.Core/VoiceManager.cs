using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace VtuberTts.Core
{
    public class VoiceManager
    {
        private IWavePlayer? _waveOutDevice;
        private AudioFileReader? _audioFileReader;
        private readonly KokoroVoiceEngine _kokoro = new();
        private readonly PiperVoiceEngine  _piper  = new();
        private readonly VoiceStudioApiEngine _voiceStudio = new();
        public bool IsPlaying => _waveOutDevice?.PlaybackState == PlaybackState.Playing;

        /// <summary>Nombre de la opción de voz externa Piper expuesta.</summary>
        public const string ExternalVoiceDisplayName = PiperVoiceEngine.ExternalVoiceDisplayName;
        /// <summary>Nombre de la opción de API externa VoiceStudio.</summary>
        public const string VoiceStudioApiDisplayName = VoiceStudioApiEngine.ExternalVoiceDisplayName;

        /// <summary>
        /// Devuelve los idiomas disponibles en el sistema (voces de Windows y Kokoro).
        /// </summary>
        public List<string> GetAvailableLanguages()
        {
            var kokoroLanguages = _kokoro.IsAvailable
                ? KokoroVoiceEngine.Catalog.Select(v => v.Language)
                : Enumerable.Empty<string>();

            var allLanguages = SpeechSynthesizer.AllVoices
                .Select(v => v.Language)
                .Concat(kokoroLanguages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l)
                .ToList();

            if (allLanguages.Count == 0)
                allLanguages.Add("es-ES");

            return allLanguages;
        }

        /// <summary>
        /// Devuelve las voces disponibles para el idioma dado (voces de Windows y Kokoro).
        /// Ya no incluye "Pipper (Externo)" en este listado, pues los modelos externos
        /// se seleccionan desde el Selector de Modelos.
        /// </summary>
        public List<string> GetVoicesForLanguage(string language)
        {
            var windowsVoices = SpeechSynthesizer.AllVoices
                .Where(v => string.IsNullOrEmpty(language) ||
                            v.Language.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                            v.Language.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => IsOnlineVoice(v) ? 0 : 1) // Online primero
                .ThenBy(v => v.DisplayName)
                .Select(v =>
                {
                    string typeLabel = IsOnlineVoice(v) ? " (Online)" : " (Local)";
                    return v.DisplayName + typeLabel;
                })
                .ToList();

            var kokoroVoices = _kokoro.IsAvailable
                ? KokoroVoiceEngine.Catalog
                      .Where(v => KokoroVoiceEngine.MatchesLanguage(v, language))
                      .Select(v => v.DisplayName)
                : Enumerable.Empty<string>();

            return windowsVoices
                .Concat(kokoroVoices)
                .ToList();
        }

        /// <summary>
        /// Carga un modelo Piper externo a partir de sus archivos .onnx y .json.
        /// </summary>
        public void SetExternalPiperModel(string onnxPath, string jsonPath)
            => _piper.LoadExternalVoice(onnxPath, jsonPath);

        /// <summary>
        /// Indica si el motor Piper ya tiene cargado un modelo externo.
        /// </summary>
        public bool HasExternalPiperModel => _piper.HasExternalVoice;

        /// <summary>
        /// Acceso al motor VoiceStudio API para configurar parámetros avanzados.
        /// </summary>
        public VoiceStudioApiEngine VoiceStudioEngine => _voiceStudio;

        /// <summary>
        /// Obtiene los perfiles de voz disponibles consultando el API de VoiceStudio.
        /// </summary>
        public Task<List<string>> FetchVoiceStudioProfilesAsync(string? url = null)
            => _voiceStudio.FetchProfilesAsync(url);

        /// <summary>
        /// Establece la URL y el perfil seleccionado para VoiceStudio API.
        /// </summary>
        public void SetVoiceStudioConfig(string baseUrl, string profile)
            => _voiceStudio.SetConfig(baseUrl, profile);

        /// <summary>
        /// Indica si VoiceStudio API está configurado con URL y perfil.
        /// </summary>
        public bool HasVoiceStudioConfig => _voiceStudio.IsConfigured;

        /// <summary>
        /// Heurística para detectar voces Online de Windows.
        /// </summary>
        private static bool IsOnlineVoice(Windows.Media.SpeechSynthesis.VoiceInformation voice)
        {
            return voice.Id.Contains("online", StringComparison.OrdinalIgnoreCase)
                || voice.DisplayName.Contains("Online", StringComparison.OrdinalIgnoreCase)
                || voice.DisplayName.Contains("Natural", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extrae el nombre base de la voz eliminando el sufijo "(Online)" o "(Local)" si existe.
        /// </summary>
        private static string ExtractVoiceName(string displayNameWithLabel)
        {
            if (string.IsNullOrEmpty(displayNameWithLabel)) return displayNameWithLabel;
            int parenIdx = displayNameWithLabel.LastIndexOf(" (", StringComparison.Ordinal);
            return parenIdx >= 0 ? displayNameWithLabel[..parenIdx].Trim() : displayNameWithLabel;
        }

        /// <summary>
        /// Sintetiza texto a audio y lo guarda en <paramref name="outputPath"/>.
        /// Gestiona la síntesis con PiperVoiceEngine cuando se indica el modelo "Pipper (Externo)".
        /// </summary>
        public async Task<string> SynthesizeSpeechAsync(
            string text,
            string outputPath,
            string selectedVoiceName = "",
            string language = "",
            float speed = 1.0f,
            float pitch = 1.0f,
            float volume = 1.0f)
        {
            // 1. Manejo de voz externa Piper
            if (PiperVoiceEngine.IsExternalVoice(selectedVoiceName))
            {
                var externalVoice = _piper.FindVoice(selectedVoiceName)
                    ?? throw new InvalidOperationException("No se ha cargado ningún modelo Piper externo (.onnx y .json). Carga los archivos antes de sintetizar.");
                await _piper.SynthesizeToWavAsync(text, outputPath, externalVoice, speed, volume);
                return outputPath;
            }

            // 2. Manejo de VoiceStudio API externa
            if (VoiceStudioApiEngine.IsVoiceStudioApiVoice(selectedVoiceName))
            {
                await _voiceStudio.SynthesizeToWavAsync(text, outputPath, language, speed, pitch, volume);
                return outputPath;
            }

            // 2. Voces Kokoro (el tono no es soportado por Kokoro)
            if (_kokoro.IsAvailable)
            {
                var kokoroVoice = _kokoro.FindVoice(selectedVoiceName);
                if (kokoroVoice != null)
                {
                    await _kokoro.SynthesizeToWavAsync(text, outputPath, kokoroVoice, speed, volume);
                    return outputPath;
                }
            }

            // 3. Voces Piper del catálogo local (si existieran)
            var piperVoice = _piper.FindVoice(selectedVoiceName);
            if (piperVoice != null)
            {
                await _piper.SynthesizeToWavAsync(text, outputPath, piperVoice, speed, volume);
                return outputPath;
            }

            // 4. Voces de Windows (WinRT SpeechSynthesizer)
            using var synthesizer = new SpeechSynthesizer();

            string baseName = ExtractVoiceName(selectedVoiceName);

            if (!string.IsNullOrEmpty(baseName))
            {
                var voice = SpeechSynthesizer.AllVoices
                    .FirstOrDefault(v =>
                        v.DisplayName.Equals(baseName, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(language) || v.Language.Equals(language, StringComparison.OrdinalIgnoreCase)));
                if (voice != null)
                {
                    synthesizer.Voice = voice;
                }
            }

            synthesizer.Options.SpeakingRate = Math.Clamp(speed, 0.5, 6.0);
            synthesizer.Options.AudioPitch = Math.Clamp(pitch, 0.0, 2.0);
            synthesizer.Options.AudioVolume = Math.Clamp(volume, 0.0, 1.0);

            string langCode = string.IsNullOrEmpty(language) ? "es-ES" : language;
            string encodedText = System.Net.WebUtility.HtmlEncode(text);
            string ssml =
                $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{langCode}'>" +
                $"{encodedText}" +
                "</speak>";

            SpeechSynthesisStream stream = await synthesizer.SynthesizeSsmlToStreamAsync(ssml);

            using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                await stream.AsStreamForRead().CopyToAsync(fileStream);
            }

            return outputPath;
        }

        public void PlayAudio(string filePath, float volume = 1.0f)
        {
            StopAudio();

            _waveOutDevice = new WaveOut();
            _audioFileReader = new AudioFileReader(filePath)
            {
                Volume = Math.Clamp(volume, 0.0f, 1.0f)
            };
            _waveOutDevice.Init(_audioFileReader);
            _waveOutDevice.Play();
        }

        public void SetVolume(float volume)
        {
            if (_audioFileReader != null)
            {
                _audioFileReader.Volume = Math.Clamp(volume, 0.0f, 1.0f);
            }
        }

        public void PauseAudio() => _waveOutDevice?.Pause();
        public void ResumeAudio() => _waveOutDevice?.Play();

        public void StopAudio()
        {
            try
            {
                _waveOutDevice?.Stop();
                _waveOutDevice?.Dispose();
                _waveOutDevice = null;

                _audioFileReader?.Dispose();
                _audioFileReader = null;
            }
            catch { }
        }
    }
}
