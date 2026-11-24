using System;
using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Reproductor de audio multiplataforma que se adapta dinámicamente al sistema operativo:
    /// - En Windows: Utiliza NAudio (WaveOut / AudioFileReader).
    /// - En macOS: Utiliza el reproductor nativo del sistema (/usr/bin/afplay).
    /// - En Linux: Utiliza el reproductor nativo de escritorio (paplay / aplay).
    /// </summary>
    public sealed class CrossPlatformAudioPlayer : IAudioPlayer
    {
        // ─── Estado para Windows (NAudio) ─────────────────────────────────────
        private IWavePlayer? _waveOut;
        private AudioFileReader? _audioFileReader;

        // ─── Estado para Unix (macOS / Linux) ──────────────────────────────────
        private Process? _unixProcess;
        private bool _isUnixPaused;
        private string? _currentFilePath;
        private float _currentVolume = 1.0f;

        public bool IsPlaying
        {
            get
            {
                if (OperatingSystem.IsWindows())
                {
                    return _waveOut?.PlaybackState == PlaybackState.Playing;
                }

                if (_unixProcess != null && !_unixProcess.HasExited)
                {
                    return !_isUnixPaused;
                }

                return false;
            }
        }

        public void Play(string filePath, float volume)
        {
            Stop();

            _currentFilePath = filePath;
            _currentVolume = Math.Clamp(volume, 0.0f, 1.0f);
            _isUnixPaused = false;

            if (OperatingSystem.IsWindows())
            {
                _waveOut = new WaveOut();
                _audioFileReader = new AudioFileReader(filePath)
                {
                    Volume = _currentVolume
                };
                _waveOut.Init(_audioFileReader);
                _waveOut.Play();
                return;
            }

            // Reproducción nativa en macOS mediante afplay
            if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/afplay",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(filePath);
                psi.ArgumentList.Add("-v");
                psi.ArgumentList.Add(_currentVolume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));

                _unixProcess = Process.Start(psi);
                return;
            }

            // Reproducción nativa en Linux mediante paplay (PulseAudio/PipeWire) o aplay (ALSA)
            if (OperatingSystem.IsLinux())
            {
                string player = File.Exists("/usr/bin/paplay") ? "/usr/bin/paplay" : "/usr/bin/aplay";
                var psi = new ProcessStartInfo
                {
                    FileName = player,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(filePath);

                _unixProcess = Process.Start(psi);
            }
        }

        public void Pause()
        {
            if (OperatingSystem.IsWindows())
            {
                _waveOut?.Pause();
                return;
            }

            if (_unixProcess != null && !_unixProcess.HasExited && !_isUnixPaused)
            {
                try
                {
                    // Enviar señal SIGSTOP para congelar la reproducción del proceso en Unix
                    Process.Start("kill", $"-STOP {_unixProcess.Id}")?.WaitForExit(200);
                    _isUnixPaused = true;
                }
                catch { }
            }
        }

        public void Resume()
        {
            if (OperatingSystem.IsWindows())
            {
                _waveOut?.Play();
                return;
            }

            if (_unixProcess != null && !_unixProcess.HasExited && _isUnixPaused)
            {
                try
                {
                    // Enviar señal SIGCONT para reanudar la reproducción del proceso en Unix
                    Process.Start("kill", $"-CONT {_unixProcess.Id}")?.WaitForExit(200);
                    _isUnixPaused = false;
                }
                catch { }
            }
        }

        public void Stop()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _waveOut?.Stop();
                    _waveOut?.Dispose();
                    _waveOut = null;

                    _audioFileReader?.Dispose();
                    _audioFileReader = null;
                }
                catch { }
                return;
            }

            if (_unixProcess != null)
            {
                try
                {
                    if (!_unixProcess.HasExited)
                    {
                        _unixProcess.Kill(true);
                    }
                    _unixProcess.Dispose();
                }
                catch { }
                finally
                {
                    _unixProcess = null;
                    _isUnixPaused = false;
                }
            }
        }

        public void SetVolume(float volume)
        {
            _currentVolume = Math.Clamp(volume, 0.0f, 1.0f);

            if (OperatingSystem.IsWindows() && _audioFileReader != null)
            {
                _audioFileReader.Volume = _currentVolume;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
