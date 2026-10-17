using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Windows.Media.SpeechSynthesis;
using NAudio.Wave;

namespace VtuberTts.Core
{
    public class VoiceManager
    {
        private IWavePlayer? _waveOutDevice;
        private AudioFileReader? _audioFileReader;
        public bool IsPlaying => _waveOutDevice?.PlaybackState == PlaybackState.Playing;

        public List<string> GetAvailableLanguages()
        {
            return SpeechSynthesizer.AllVoices
                .Select(v => v.Language)
                .Distinct()
                .OrderBy(l => l)
                .ToList();
        }

        public List<string> GetVoicesForLanguage(string language)
        {
            return SpeechSynthesizer.AllVoices
                .Where(v => string.IsNullOrEmpty(language) || v.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
                .Select(v => v.DisplayName)
                .ToList();
        }

        public async Task<string> SynthesizeSpeechAsync(string text, string outputPath, string selectedVoiceName = "", string language = "", float speed = 1.0f, float pitch = 1.0f)
        {
            using var synthesizer = new SpeechSynthesizer();

            if (!string.IsNullOrEmpty(selectedVoiceName))
            {
                var voice = SpeechSynthesizer.AllVoices
                    .FirstOrDefault(v => v.DisplayName.Equals(selectedVoiceName, StringComparison.OrdinalIgnoreCase) && 
                                         (string.IsNullOrEmpty(language) || v.Language.Equals(language, StringComparison.OrdinalIgnoreCase)));
                if (voice != null)
                {
                    synthesizer.Voice = voice;
                }
            }

            int ratePercentage = (int)(speed * 100);
            string pitchStr = pitch switch {
                < 1.0f => "low",
                > 1.0f => "high",
                _ => "medium"
            };

            string langCode = string.IsNullOrEmpty(language) ? "es-ES" : language;
            string ssml = $@"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{langCode}'>" +
                          $@"<prosody rate='{ratePercentage}%' pitch='{pitchStr}'>{System.Net.WebUtility.HtmlEncode(text)}</prosody>" +
                          @"</speak>";

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