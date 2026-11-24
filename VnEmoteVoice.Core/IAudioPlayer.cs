using System;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Contrato de reproducción de audio agnóstico de la plataforma.
    /// Permite reproducir, pausar, reanudar y controlar el volumen de los archivos de voz generados
    /// tanto en Windows como en Linux y macOS.
    /// </summary>
    public interface IAudioPlayer : IDisposable
    {
        /// <summary>
        /// Indica si el reproductor está emitiendo sonido en este momento.
        /// </summary>
        bool IsPlaying { get; }

        /// <summary>
        /// Inicia la reproducción de un archivo de audio WAV en el volumen indicado (0.0 a 1.0).
        /// </summary>
        void Play(string filePath, float volume);

        /// <summary>
        /// Pausa la reproducción activa del audio.
        /// </summary>
        void Pause();

        /// <summary>
        /// Reanuda la reproducción pausada.
        /// </summary>
        void Resume();

        /// <summary>
        /// Detiene la reproducción y libera los recursos de audio.
        /// </summary>
        void Stop();

        /// <summary>
        /// Ajusta el volumen del audio activo en tiempo real.
        /// </summary>
        void SetVolume(float volume);
    }
}
