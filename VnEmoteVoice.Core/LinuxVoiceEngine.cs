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
    /// Implementación del motor de síntesis de voz nativo para distribuciones Linux.
    /// Utiliza el subsistema de voz estándar de Linux (speech-dispatcher / espeak-ng / festival),
    /// generando archivos WAV mediante herramientas nativas del entorno de escritorio.
    /// </summary>
    public sealed class LinuxVoiceEngine : ISystemVoiceEngine
    {
        public bool IsAvailable => OperatingSystem.IsLinux() && (HasBinary("espeak-ng") || HasBinary("espeak") || HasBinary("spd-say"));

        public string EngineName => "Linux Speech Dispatcher / eSpeak Engine";

        private sealed record LinuxVoiceRecord(string Name, string Bcp47Lang, string Binary);

        private List<LinuxVoiceRecord>? _cachedVoices;

        public List<string> GetInstalledLanguages()
        {
            var voices = GetLinuxVoices();
            return voices
                .Select(v => v.Bcp47Lang)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l)
                .ToList();
        }

        public List<string> GetVoicesForLanguage(string language)
        {
            var voices = GetLinuxVoices();
            return voices
                .Where(v => string.IsNullOrEmpty(language) ||
                            v.Bcp47Lang.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                            v.Bcp47Lang.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase))
                .Select(v => $"{v.Name} (Linux)")
                .ToList();
        }

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
                throw new PlatformNotSupportedException("LinuxVoiceEngine solo puede ejecutarse en sistemas Linux con soporte de voz.");

            string cleanVoice = VoiceManager.ExtractVoiceName(voiceName);

            // Determinar la herramienta disponible (prioridad: espeak-ng -> espeak -> spd-say)
            string binary = HasBinary("espeak-ng") ? "espeak-ng" : (HasBinary("espeak") ? "espeak" : "spd-say");

            if (binary == "espeak-ng" || binary == "espeak")
            {
                // Velocidad estándar de espeak: ~175 ppm; rango de 80 a 450
                int speedWpm = (int)(175.0f * Math.Clamp(speed, 0.5f, 2.5f));
                // Tono en espeak: 0 a 99 (50 = normal)
                int pitchVal = (int)(50.0f * Math.Clamp(pitch, 0.5f, 1.8f));
                // Amplitud / volumen: 0 a 200 (100 = normal)
                int ampVal = (int)(100.0f * Math.Clamp(volume, 0.0f, 1.0f));

                var psi = new ProcessStartInfo
                {
                    FileName = binary,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                psi.ArgumentList.Add("-w");
                psi.ArgumentList.Add(outputPath);
                psi.ArgumentList.Add("-s");
                psi.ArgumentList.Add(speedWpm.ToString());
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(pitchVal.ToString());
                psi.ArgumentList.Add("-a");
                psi.ArgumentList.Add(ampVal.ToString());

                if (!string.IsNullOrEmpty(cleanVoice))
                {
                    psi.ArgumentList.Add("-v");
                    psi.ArgumentList.Add(cleanVoice);
                }
                else if (!string.IsNullOrEmpty(language))
                {
                    psi.ArgumentList.Add("-v");
                    psi.ArgumentList.Add(language);
                }

                psi.ArgumentList.Add(text);

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string stderr = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"Error al sintetizar en Linux con {binary} (Código {process.ExitCode}): {stderr}");
                    }
                }
            }
            else
            {
                // Fallback con spd-say
                var psi = new ProcessStartInfo
                {
                    FileName = "spd-say",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                if (!string.IsNullOrEmpty(cleanVoice))
                {
                    psi.ArgumentList.Add("-y");
                    psi.ArgumentList.Add(cleanVoice);
                }
                psi.ArgumentList.Add(text);

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                }
            }
        }

        public (string Language, string VoiceName) GetSystemDefaultLanguageAndVoice()
        {
            string sysLang = Environment.GetEnvironmentVariable("LANG") ?? CultureInfo.CurrentUICulture.Name;
            if (sysLang.Contains('.')) sysLang = sysLang[..sysLang.IndexOf('.')];
            sysLang = sysLang.Replace('_', '-');

            var voices = GetLinuxVoices();
            var match = voices.FirstOrDefault(v => v.Bcp47Lang.Equals(sysLang, StringComparison.OrdinalIgnoreCase))
                        ?? voices.FirstOrDefault(v => v.Bcp47Lang.StartsWith(sysLang.Split('-')[0], StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return (match.Bcp47Lang, $"{match.Name} (Linux)");
            }

            return (sysLang, string.Empty);
        }

        private List<LinuxVoiceRecord> GetLinuxVoices()
        {
            if (_cachedVoices != null) return _cachedVoices;

            var list = new List<LinuxVoiceRecord>();
            if (!IsAvailable) return _cachedVoices = list;

            try
            {
                string binary = HasBinary("espeak-ng") ? "espeak-ng" : "espeak";
                var psi = new ProcessStartInfo
                {
                    FileName = binary,
                    Arguments = "--voices",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    // Encabezado: Pty Language Age/Gender VoiceName File Other Langs
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
                    {
                        var parts = Regex.Split(line.Trim(), @"\s+");
                        if (parts.Length >= 4)
                        {
                            string rawLang = parts[1];
                            string voiceName = parts[3];
                            string bcp47 = rawLang.Replace('_', '-');

                            list.Add(new LinuxVoiceRecord(voiceName, bcp47, binary));
                        }
                    }
                }
            }
            catch
            {
            }

            return _cachedVoices = list;
        }

        private static bool HasBinary(string name)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = name,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch { }
            return false;
        }

        public void Dispose()
        {
            _cachedVoices = null;
        }
    }
}
