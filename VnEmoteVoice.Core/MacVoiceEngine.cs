using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Implementación del motor de síntesis de voz nativo para Apple macOS.
    /// Utiliza el subsistema de voz nativo del sistema (/usr/bin/say), ofreciendo
    /// acceso a todas las voces del sistema instaladas en macOS (Siri, Paulina, Mónica, Samantha, etc.).
    /// </summary>
    public sealed class MacVoiceEngine : ISystemVoiceEngine
    {
        public bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists("/usr/bin/say");

        public string EngineName => "Apple macOS Native Speech Engine (say)";

        private sealed record MacVoiceRecord(string Name, string Bcp47Lang, string Description);

        private List<MacVoiceRecord>? _cachedVoices;

        /// <summary>
        /// Obtiene la lista de idiomas instalados en el sistema operativo macOS.
        /// </summary>
        public List<string> GetInstalledLanguages()
        {
            var voices = GetMacVoices();
            return voices
                .Select(v => v.Bcp47Lang)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l)
                .ToList();
        }

        /// <summary>
        /// Devuelve los nombres legibles de las voces nativas disponibles en macOS para el idioma dado.
        /// </summary>
        public List<string> GetVoicesForLanguage(string language)
        {
            var voices = GetMacVoices();
            return voices
                .Where(v => string.IsNullOrEmpty(language) ||
                            v.Bcp47Lang.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                            v.Bcp47Lang.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
                .Select(v => $"{v.Name} (macOS)")
                .ToList();
        }

        /// <summary>
        /// Sintetiza el texto a un archivo WAV utilizando la utilidad /usr/bin/say con salida lineal PCM a 22.05 kHz.
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
                throw new PlatformNotSupportedException("MacVoiceEngine solo puede ejecutarse en Apple macOS.");

            string cleanVoice = VoiceManager.ExtractVoiceName(voiceName);

            // Calcular palabras por minuto (wpm): velocidad base estándar de macOS es ~175 ppm
            int rateWpm = (int)(175.0f * Math.Clamp(speed, 0.5f, 2.5f));

            // Archivo de texto temporal para evitar desbordar los argumentos de línea de comandos en textos largos
            string tempTextFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempTextFile, text);

                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/say",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                // Formato estándar de alta fidelidad: Little-Endian Integer 16 bits a 22050 Hz (formato WAV)
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add(tempTextFile);
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add(outputPath);
                psi.ArgumentList.Add("--data-format=LEI16@22050");

                if (!string.IsNullOrWhiteSpace(cleanVoice))
                {
                    psi.ArgumentList.Add("-v");
                    psi.ArgumentList.Add(cleanVoice);
                }

                psi.ArgumentList.Add("-r");
                psi.ArgumentList.Add(rateWpm.ToString());

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string stderr = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"Error al sintetizar en macOS con /usr/bin/say (Código {process.ExitCode}): {stderr}");
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tempTextFile)) File.Delete(tempTextFile); } catch { }
            }
        }

        /// <summary>
        /// Obtiene el idioma y la voz predeterminada del sistema macOS.
        /// </summary>
        public (string Language, string VoiceName) GetSystemDefaultLanguageAndVoice()
        {
            string defaultVoiceName = string.Empty;

            if (IsAvailable)
            {
                try
                {
                    // Leer la voz predeterminada desde las preferencias de voz del usuario en macOS
                    var psi = new ProcessStartInfo
                    {
                        FileName = "defaults",
                        Arguments = "read com.apple.speech.voice.prefs SelectedVoiceName",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd().Trim();
                        proc.WaitForExit();
                        if (!string.IsNullOrEmpty(output))
                        {
                            defaultVoiceName = output;
                        }
                    }
                }
                catch
                {
                }
            }

            var voices = GetMacVoices();

            if (!string.IsNullOrEmpty(defaultVoiceName))
            {
                var match = voices.FirstOrDefault(v => v.Name.Equals(defaultVoiceName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return (match.Bcp47Lang, $"{match.Name} (macOS)");
                }
            }

            // Fallback según la cultura del sistema
            string sysLang = CultureInfo.CurrentUICulture.Name;
            var voiceForLang = voices.FirstOrDefault(v => v.Bcp47Lang.StartsWith(sysLang.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            if (voiceForLang != null)
            {
                return (voiceForLang.Bcp47Lang, $"{voiceForLang.Name} (macOS)");
            }

            return (sysLang, string.Empty);
        }

        private List<MacVoiceRecord> GetMacVoices()
        {
            if (_cachedVoices != null) return _cachedVoices;

            var list = new List<MacVoiceRecord>();
            if (!IsAvailable) return _cachedVoices = list;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/say",
                    Arguments = "-v \"?\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    // Formato típico de salida:
                    // Mónica              es_ES    # ¡Hola! Me llamo Mónica y soy una voz española.
                    var regex = new Regex(@"^([^\s]+(?:\s+[^\s]+)*?)\s+([a-z]{2}_[A-Z]{2})\s*#(.*)$", RegexOptions.Multiline);
                    foreach (Match m in regex.Matches(output))
                    {
                        string name = m.Groups[1].Value.Trim();
                        string rawLang = m.Groups[2].Value.Trim();
                        string desc = m.Groups[3].Value.Trim();
                        string bcp47 = rawLang.Replace('_', '-');

                        list.Add(new MacVoiceRecord(name, bcp47, desc));
                    }
                }
            }
            catch
            {
            }

            return _cachedVoices = list;
        }

        public void Dispose()
        {
            _cachedVoices = null;
        }
    }
}
