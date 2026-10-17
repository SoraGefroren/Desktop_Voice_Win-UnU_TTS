using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VtuberTts.Core;

namespace VtuberTts.Console
{
    public partial class MainWindow : Window
    {
        private readonly VoiceManager _voiceManager;
        private List<TextSegment> _segments = new();
        private int _currentSegmentIndex = 0;
        private bool _isPlaying = false;
        private bool _isPaused = false;
        private string _fullPlainText = string.Empty;

        public MainWindow()
        {
            _voiceManager = new VoiceManager();
            InitializeComponent();
            LoadLanguages();
            SetInitialText("¡Bienvenido al sistema de conversión de texto a voz! Aquí puedes escribir cualquier texto y convertirlo en audio usando diferentes voces e idiomas disponibles. También puedes ajustar la velocidad, el tono y el volumen de la voz para personalizar tu experiencia. ¡Escribe algo y pruébalo!");
        }

        private void SetInitialText(string text)
        {
            RtbInput.Document.Blocks.Clear();
            Paragraph p = new Paragraph(new Run(text));
            RtbInput.Document.Blocks.Add(p);
            _fullPlainText = text;
            UpdateCharCount();
        }

        private string GetPlainTextFromRichTextBox()
        {
            TextRange textRange = new TextRange(RtbInput.Document.ContentStart, RtbInput.Document.ContentEnd);
            return textRange.Text.TrimEnd('\r', '\n');
        }

        private void LoadLanguages()
        {
            try
            {
                var languages = _voiceManager.GetAvailableLanguages();
                CbLanguage.ItemsSource = languages;
                var spanishLang = languages.FirstOrDefault(l => l.StartsWith("es", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(spanishLang)) CbLanguage.SelectedItem = spanishLang;
                else if (languages.Count > 0) CbLanguage.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar idiomas: {ex.Message}");
            }
        }

        private void CbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbLanguage.SelectedItem is string selectedLang)
            {
                var voices = _voiceManager.GetVoicesForLanguage(selectedLang);
                CbVoices.ItemsSource = voices;
                if (voices.Count > 0) CbVoices.SelectedIndex = 0;
            }
        }

        private void SliderSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblSpeed != null) LblSpeed.Text = $"{e.NewValue:F1}x";
        }

        private void SliderPitch_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblPitch != null) LblPitch.Text = $"{e.NewValue:F1}";
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblVolume != null)
            {
                int volPercent = (int)e.NewValue;
                LblVolume.Text = $"{volPercent}%";
                _voiceManager?.SetVolume((float)(e.NewValue / 100.0));
            }
        }

        private void RtbInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPlaying)
            {
                _fullPlainText = GetPlainTextFromRichTextBox();
                UpdateCharCount();
            }
        }

        private void UpdateCharCount()
        {
            if (LblCharCount != null)
            {
                LblCharCount.Text = $"{_fullPlainText.Length} caracteres";
            }
        }

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying && _isPaused)
            {
                // Reanudar desde pausa
                _voiceManager.ResumeAudio();
                _isPaused = false;
                BtnPause.Content = "⏸";
                return;
            }

            _fullPlainText = GetPlainTextFromRichTextBox();
            if (string.IsNullOrWhiteSpace(_fullPlainText))
            {
                MessageBox.Show("Por favor, ingresa texto para sintetizar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _segments = EmotionAnalyzer.SegmentAndAnalyze(_fullPlainText);
            if (_segments.Count == 0) return;

            _isPlaying = true;
            _currentSegmentIndex = 0;
            RtbInput.IsReadOnly = true; // Bloquear edición durante reproducción

            BtnPlay.Visibility = Visibility.Collapsed;
            PanelPauseStop.Visibility = Visibility.Visible;
            _isPaused = false;

            await PlaySegmentsLoop();
        }

        private async Task PlaySegmentsLoop()
        {
            string selectedLang = CbLanguage.SelectedItem?.ToString() ?? "es-ES";
            string selectedVoice = CbVoices.SelectedItem?.ToString() ?? "";
            float speed = (float)SliderSpeed.Value;
            float pitch = (float)SliderPitch.Value;
            float volume = (float)(SliderVolume.Value / 100.0);

            while (_currentSegmentIndex < _segments.Count && _isPlaying)
            {
                var segment = _segments[_currentSegmentIndex];

                // Actualizar emoción y rostro visual
                LblEmotionEmoji.Text = segment.Emoji;
                LblEmotionName.Text = segment.Emotion;

                // Resaltar texto activo en el RichTextBox
                HighlightSegment(_currentSegmentIndex);

                string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"segment_{_currentSegmentIndex}.wav");
                await _voiceManager.SynthesizeSpeechAsync(segment.Text, tempPath, selectedVoice, selectedLang, speed, pitch);
                
                _voiceManager.PlayAudio(tempPath, volume);

                // Esperar a que termine el audio del segmento actual o se interrumpa por clic
                await Task.Delay(300);
                while (_voiceManager.IsPlaying && !_isPaused && _isPlaying)
                {
                    await Task.Delay(100);
                }

                if (!_isPlaying) break; // Si dieron Stop

                if (_isPaused)
                {
                    // Mantener el bucle en pausa hasta que reanuden
                    while (_isPaused && _isPlaying)
                    {
                        await Task.Delay(100);
                    }
                    if (!_isPlaying) break;
                }

                // Avanzar al siguiente segmento automáticamente (a menos que un clic haya cambiado _currentSegmentIndex)
                _currentSegmentIndex++;
            }

            // Al finalizar todos los segmentos, volver a estado neutral conservando el texto
            ResetToNeutralState();
        }

        private void HighlightSegment(int activeIndex)
        {
            RtbInput.Document.Blocks.Clear();
            Paragraph p = new Paragraph();

            for (int i = 0; i < _segments.Count; i++)
            {
                Run run = new Run(_segments[i].Text + " ");
                if (i == activeIndex)
                {
                    run.Background = new SolidColorBrush(Color.FromArgb(90, 124, 58, 237)); // Morado resaltado activo
                    run.Foreground = Brushes.White;
                }
                else
                {
                    run.Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
                }
                p.Inlines.Add(run);
            }
            RtbInput.Document.Blocks.Add(p);
        }

        private void RtbInput_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isPlaying && _segments.Count > 0)
            {
                // Obtener la posición exacta del cursor donde se hizo clic en el RichTextBox
                TextPointer pointer = RtbInput.GetPositionFromPoint(e.GetPosition(RtbInput), true);
                if (pointer != null)
                {
                    // Calcular el desplazamiento en caracteres respecto al inicio del documento
                    int charOffset = RtbInput.Document.ContentStart.GetOffsetToPosition(pointer);
                    int targetIndex = 0;
                    int minDistance = int.MaxValue;

                    // Encontrar el segmento correspondiente al clic
                    for (int i = 0; i < _segments.Count; i++)
                    {
                        int start = _segments[i].StartIndex;
                        int end = start + _segments[i].Length;

                        if (charOffset >= start && charOffset <= end)
                        {
                            targetIndex = i;
                            break;
                        }

                        int distance = Math.Min(Math.Abs(charOffset - start), Math.Abs(charOffset - end));
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            targetIndex = i;
                        }
                    }

                    // Actualizar el índice del segmento y detener el reproductor actual para forzar el salto inmediato
                    _currentSegmentIndex = targetIndex;
                    _voiceManager.StopAudio();

                    // Si estaba en pausa, reanudar automáticamente al hacer clic en el texto
                    if (_isPaused)
                    {
                        _isPaused = false;
                        BtnPause.Content = "⏸";
                    }

                    // Evitar que el RichTextBox seleccione texto de forma nativa e interrumpa el salto
                    e.Handled = true;
                }
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPaused)
            {
                _voiceManager.PauseAudio();
                _isPaused = true;
                BtnPause.Content = "▶";
            }
            else
            {
                _voiceManager.ResumeAudio();
                _isPaused = false;
                BtnPause.Content = "⏸";
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _isPlaying = false;
            _voiceManager.StopAudio();
            ResetToNeutralState();
        }

        private void ResetToNeutralState()
        {
            _isPlaying = false;
            _isPaused = false;
            _currentSegmentIndex = 0;

            Dispatcher.Invoke(() =>
            {
                PanelPauseStop.Visibility = Visibility.Collapsed;
                BtnPlay.Visibility = Visibility.Visible;
                BtnPause.Content = "⏸";
                LblEmotionEmoji.Text = "😐";
                LblEmotionName.Text = "Neutral";
                RtbInput.IsReadOnly = false;

                // Restaurar texto completo conservando lo ingresado por el usuario
                RtbInput.Document.Blocks.Clear();
                RtbInput.Document.Blocks.Add(new Paragraph(new Run(_fullPlainText)));
            });
        }

        private async void BtnDownloadAudio_Click(object sender, RoutedEventArgs e)
        {
            string text = GetPlainTextFromRichTextBox();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("No hay texto para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Archivo de audio WAV (*.wav)|*.wav",
                FileName = $"vtuber_audio_{DateTime.Now:yyyyMMdd_HHmmss}.wav",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string selectedLang = CbLanguage.SelectedItem?.ToString() ?? "es-ES";
                    string selectedVoice = CbVoices.SelectedItem?.ToString() ?? "";
                    float speed = (float)SliderSpeed.Value;
                    float pitch = (float)SliderPitch.Value;

                    await _voiceManager.SynthesizeSpeechAsync(text, saveFileDialog.FileName, selectedVoice, selectedLang, speed, pitch);
                    MessageBox.Show("Audio guardado exitosamente en Descargas.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al guardar el archivo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}