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
        public bool IsPlaying => _waveOutDevice?.PlaybackState == PlaybackState.Playing;

        /// <summary>
        /// Devuelve todos los idiomas disponibles incluyendo voces Online e instaladas localmente.
        /// Las voces Online aparecen si están habilitadas en Configuración → Tiempo e idioma → Voz.
        /// </summary>
        public List<string> GetAvailableLanguages()
        {
            var kokoroLanguages = _kokoro.IsAvailable
                ? KokoroVoiceEngine.Catalog.Select(v => v.Language)
                : Enumerable.Empty<string>();

            var piperLanguages = _piper.IsAvailable
                ? PiperVoiceEngine.Catalog.Select(v => v.Language)
                : Enumerable.Empty<string>();

            return SpeechSynthesizer.AllVoices
                .Select(v => v.Language)
                .Concat(kokoroLanguages)
                .Concat(piperLanguages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l)
                .ToList();
        }

        /// <summary>
        /// Devuelve el nombre para mostrar y el tipo (Online/Offline) de cada voz del idioma dado.
        /// Formato: "Nombre (Online)" o "Nombre (Local)" para diferenciarlas visualmente.
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

            var piperVoices = _piper.IsAvailable
                ? PiperVoiceEngine.Catalog
                      .Where(v => PiperVoiceEngine.MatchesLanguage(v, language))
                      .Select(v => v.DisplayName)
                : Enumerable.Empty<string>();

            return windowsVoices
                .Concat(kokoroVoices)
                .Concat(piperVoices)
                .ToList();
        }

        /// <summary>
        /// Heurística para detectar voces Online: su Id contiene "online" (case-insensitive)
        /// o su DisplayName contiene "Online" o "Natural".
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
        /// Usa <see cref="SpeechSynthesizerOptions"/> para aplicar velocidad, tono y volumen
        /// de forma nativa sin necesidad de SSML personalizado.
        /// </summary>
        /// <param name="speed">Velocidad: 0.5 (lenta) a 2.0 (rápida). 1.0 = normal.</param>
        /// <param name="pitch">Tono: 0.5 (grave) a 1.5 (agudo). 1.0 = normal.</param>
        /// <param name="volume">Volumen de síntesis: 0.0 (silencio) a 1.0 (máximo).</param>
        public async Task<string> SynthesizeSpeechAsync(string text, string outputPath, string selectedVoiceName = "", string language = "", float speed = 1.0f, float pitch = 1.0f, float volume = 1.0f)
        {
            // Las voces Kokoro se sintetizan con su propio motor (el tono no es soportado por Kokoro)
            var kokoroVoice = _kokoro.FindVoice(selectedVoiceName);
            if (kokoroVoice != null)
            {
                await _kokoro.SynthesizeToWavAsync(text, outputPath, kokoroVoice, speed, volume);
                return outputPath;
            }

            // Las voces Piper (claude, etc.) se sintetizan con PiperVoiceEngine
            var piperVoice = _piper.FindVoice(selectedVoiceName);
            if (piperVoice != null)
            {
                await _piper.SynthesizeToWavAsync(text, outputPath, piperVoice, speed, volume);
                return outputPath;
            }

            using var synthesizer = new SpeechSynthesizer();

            // Extraer el nombre base eliminando el sufijo "(Online)" / "(Local)" si viene del ComboBox
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

            // Aplicar velocidad, tono y volumen mediante SpeechSynthesizerOptions (API nativa WinRT)
            // SpeakingRate: rango [0.5, 6.0] — 1.0 = normal
            // AudioPitch:   rango [0.0, 2.0] — 1.0 = normal
            // AudioVolume:  rango [0.0, 1.0] — 1.0 = máximo
            synthesizer.Options.SpeakingRate = Math.Clamp(speed, 0.5, 6.0);
            synthesizer.Options.AudioPitch = Math.Clamp(pitch, 0.0, 2.0);
            synthesizer.Options.AudioVolume = Math.Clamp(volume, 0.0, 1.0);

            string langCode = string.IsNullOrEmpty(language) ? "es-ES" : language;
            string encodedText = System.Net.WebUtility.HtmlEncode(text);
            // SSML mínimo solo para especificar el idioma; los parámetros van en Options
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