using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Administrador central de voces y reproducción de audio.
    /// Orquesta de forma unificada y multiplataforma:
    ///   1. Modelos externos Piper (.onnx y .json).
    ///   2. Modelos remotos vía VoiceStudio API (HTTP).
    ///   3. Modelos neuronales locales Kokoro v1.0 (ONNX).
    ///   4. Motor de voces nativas del sistema operativo anfitrión (Windows, macOS o Linux).
    ///   5. Reproducción de audio agnóstica de la plataforma.
    /// </summary>
    public class VoiceManager : IDisposable
    {
        private readonly IAudioPlayer _audioPlayer;
        private readonly ISystemVoiceEngine _systemVoice;
        private readonly KokoroVoiceEngine _kokoro = new();
        private readonly PiperVoiceEngine  _piper  = new();
        private readonly VoiceStudioApiEngine _voiceStudio = new();

        /// <summary>Nombre de la opción de voz externa Piper expuesta.</summary>
        public const string ExternalVoiceDisplayName = PiperVoiceEngine.ExternalVoiceDisplayName;
        /// <summary>Nombre de la opción de API externa VoiceStudio.</summary>
        public const string VoiceStudioApiDisplayName = VoiceStudioApiEngine.ExternalVoiceDisplayName;

        /// <summary>
        /// Indica si el reproductor de sonido está activo en este momento.
        /// </summary>
        public bool IsPlaying => _audioPlayer.IsPlaying;

        public VoiceManager(ISystemVoiceEngine? systemVoiceEngine = null, IAudioPlayer? audioPlayer = null)
        {
            _systemVoice = systemVoiceEngine ?? SystemVoiceEngineFactory.Create();
            _audioPlayer = audioPlayer ?? new CrossPlatformAudioPlayer();
        }

        /// <summary>
        /// Obtiene el idioma y nombre de la voz predeterminada configurados en el sistema operativo anfitrión.
        /// </summary>
        public (string Language, string VoiceName) GetSystemDefaultLanguageAndVoice()
            => _systemVoice.GetSystemDefaultLanguageAndVoice();

        /// <summary>
        /// Devuelve los idiomas disponibles en el sistema (voces nativas del S.O. y modelos Kokoro).
        /// </summary>
        public List<string> GetAvailableLanguages()
        {
            var kokoroLanguages = _kokoro.IsAvailable
                ? KokoroVoiceEngine.Catalog.Select(v => v.Language)
                : Enumerable.Empty<string>();

            var allLanguages = _systemVoice.GetInstalledLanguages()
                .Concat(kokoroLanguages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l)
                .ToList();

            if (allLanguages.Count == 0)
                allLanguages.Add("es-ES");

            return allLanguages;
        }

        /// <summary>
        /// Devuelve las voces disponibles para el idioma dado (voces nativas del sistema y Kokoro).
        /// </summary>
        public List<string> GetVoicesForLanguage(string language)
        {
            var systemVoices = _systemVoice.GetVoicesForLanguage(language);

            var kokoroVoices = _kokoro.IsAvailable
                ? KokoroVoiceEngine.Catalog
                      .Where(v => KokoroVoiceEngine.MatchesLanguage(v, language))
                      .Select(v => v.DisplayName)
                : Enumerable.Empty<string>();

            return systemVoices
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
        /// Extrae el nombre base de la voz eliminando sufijos descriptivos entre paréntesis
        /// como "(Online)", "(Local)", "(macOS)", "(Linux)", etc.
        /// </summary>
        public static string ExtractVoiceName(string displayNameWithLabel)
        {
            if (string.IsNullOrEmpty(displayNameWithLabel)) return displayNameWithLabel;
            int parenIdx = displayNameWithLabel.LastIndexOf(" (", StringComparison.Ordinal);
            return parenIdx >= 0 ? displayNameWithLabel[..parenIdx].Trim() : displayNameWithLabel;
        }

        /// <summary>
        /// Sintetiza texto a audio WAV en la ruta <paramref name="outputPath"/> según el motor correspondiente.
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
                await _voiceStudio.SynthesizeToWavAsync(text, outputPath, language, speed);
                return outputPath;
            }

            // 3. Voces Kokoro (el tono no es soportado por Kokoro)
            if (_kokoro.IsAvailable)
            {
                var kokoroVoice = _kokoro.FindVoice(selectedVoiceName);
                if (kokoroVoice != null)
                {
                    await _kokoro.SynthesizeToWavAsync(text, outputPath, kokoroVoice, speed, volume);
                    return outputPath;
                }
            }

            // 4. Voces nativas del sistema operativo (Windows, macOS o Linux)
            await _systemVoice.SynthesizeToWavAsync(
                text,
                outputPath,
                selectedVoiceName,
                language,
                speed,
                pitch,
                volume);

            return outputPath;
        }

        /// <summary>
        /// Inicia la reproducción del archivo de audio con el volumen especificado (0.0 a 1.0).
        /// </summary>
        public void PlayAudio(string filePath, float volume = 1.0f)
            => _audioPlayer.Play(filePath, volume);

        /// <summary>
        /// Ajusta el volumen de la reproducción activa.
        /// </summary>
        public void SetVolume(float volume)
            => _audioPlayer.SetVolume(volume);

        /// <summary>
        /// Pausa la reproducción del audio activo.
        /// </summary>
        public void PauseAudio()
            => _audioPlayer.Pause();

        /// <summary>
        /// Reanuda la reproducción pausada.
        /// </summary>
        public void ResumeAudio()
            => _audioPlayer.Resume();

        /// <summary>
        /// Detiene la reproducción y libera el archivo de audio.
        /// </summary>
        public void StopAudio()
            => _audioPlayer.Stop();

        /// <summary>
        /// Libera todos los recursos retenidos por los motores de voz y audio.
        /// </summary>
        public void Dispose()
        {
            _audioPlayer.Dispose();
            _systemVoice.Dispose();
            _kokoro.Dispose();
            _piper.Dispose();
        }
    }
}
