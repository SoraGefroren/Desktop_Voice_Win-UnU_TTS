using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
        /// <summary>Flag para ignorar TextChanged cuando el cambio es programático (resaltado de segmento).</summary>
        private bool _isHighlighting = false;
        /// <summary>Flag que indica si el usuario hizo clic en un segmento para forzar un salto inmediato.</summary>
        private bool _jumpRequested = false;

        public MainWindow()
        {
            _voiceManager = new VoiceManager();
            InitializeComponent();
            LoadLanguages();
            SetInitialText("Hola. ¡Bienvenido al sistema de conversión de texto a voz! Aquí puedes escribir cualquier texto y convertirlo en audio usando diferentes voces e idiomas disponibles. También, puedes ajustar la velocidad, el tono y el volumen de la voz para personalizar tu experiencia. ¡Escribe algo y pruébalo!");
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
            // Ignorar cambios programáticos del resaltado de segmentos durante la reproducción
            if (_isHighlighting) return;

            if (_isPlaying)
            {
                // El usuario editó el texto manualmente durante la reproducción → detener
                _isPlaying = false;
                _voiceManager.StopAudio();
                ResetToNeutralState();
            }
            else
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
            _jumpRequested = false;

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
                _jumpRequested = false;
                int activeIndex = _currentSegmentIndex;
                var segment = _segments[activeIndex];

                // Actualizar emoción y rostro visual
                LblEmotionEmoji.Text = segment.Emoji;
                LblEmotionName.Text = segment.Emotion;

                // Resaltar texto activo en el RichTextBox
                HighlightSegment(activeIndex);

                string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"segment_{activeIndex}.wav");
                await _voiceManager.SynthesizeSpeechAsync(segment.Text, tempPath, selectedVoice, selectedLang, speed, pitch, volume);

                // Si dieron Stop o hubo un clic en otro segmento durante la síntesis, no reproducir audio viejo
                if (!_isPlaying) break;
                if (_jumpRequested) continue;

                _voiceManager.PlayAudio(tempPath, volume);

                // Esperar a que termine el audio del segmento actual o se interrumpa por clic o stop
                await Task.Delay(300);
                while (_voiceManager.IsPlaying && !_isPaused && _isPlaying && !_jumpRequested)
                {
                    await Task.Delay(100);
                }

                if (!_isPlaying) break; // Si dieron Stop

                // Si el usuario hizo clic en un segmento, saltar de inmediato sin incrementar _currentSegmentIndex
                if (_jumpRequested)
                {
                    continue;
                }

                if (_isPaused)
                {
                    // Mantener el bucle en pausa hasta que reanuden o cliquen otro segmento
                    while (_isPaused && _isPlaying && !_jumpRequested)
                    {
                        await Task.Delay(100);
                    }
                    if (!_isPlaying) break;
                    if (_jumpRequested) continue;
                }

                // Avanzar al siguiente segmento automáticamente solo si no hubo un salto manual
                _currentSegmentIndex++;
            }

            // Al finalizar todos los segmentos, volver a estado neutral conservando el texto
            ResetToNeutralState();
        }

        private void HighlightSegment(int activeIndex)
        {
            _isHighlighting = true;
            try
            {
                RtbInput.Document.Blocks.Clear();
                Paragraph p = new Paragraph();

                for (int i = 0; i < _segments.Count; i++)
                {
                    Run run = new Run(_segments[i].Text + " ")
                    {
                        Tag = i
                    };
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
            finally
            {
                _isHighlighting = false;
            }
        }

        private void RtbInput_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isPlaying && _segments.Count > 0)
            {
                // Obtener la posición exacta del cursor donde se hizo clic en el RichTextBox
                Point mousePos = e.GetPosition(RtbInput);
                TextPointer pointer = RtbInput.GetPositionFromPoint(mousePos, true);
                if (pointer != null)
                {
                    int targetIndex = -1;

                    // Método 1: Detección directa mediante el Tag del Run cliceado
                    if (pointer.Parent is Run directRun && directRun.Tag is int tagIdx)
                    {
                        targetIndex = tagIdx;
                    }
                    else
                    {
                        var adjacentRun = pointer.GetAdjacentElement(LogicalDirection.Forward) as Run
                                          ?? pointer.GetAdjacentElement(LogicalDirection.Backward) as Run;
                        if (adjacentRun?.Tag is int adjIdx)
                        {
                            targetIndex = adjIdx;
                        }
                    }

                    // Método 2 (Fallback): Si no se encontró por Tag, calcular por offset de caracteres
                    if (targetIndex < 0 || targetIndex >= _segments.Count)
                    {
                        TextRange rangeToPointer = new TextRange(RtbInput.Document.ContentStart, pointer);
                        int charOffset = rangeToPointer.Text.Length;

                        int minDistance = int.MaxValue;
                        targetIndex = 0;

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
                    }

                    // Establecer el segmento elegido y marcar solicitud de salto para evitar _currentSegmentIndex++
                    _currentSegmentIndex = targetIndex;
                    _jumpRequested = true;
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
            _jumpRequested = false;
            _currentSegmentIndex = 0;

            Dispatcher.Invoke(() =>
            {
                PanelPauseStop.Visibility = Visibility.Collapsed;
                BtnPlay.Visibility = Visibility.Visible;
                BtnPause.Content = "⏸";
                LblEmotionEmoji.Text = "😐";
                LblEmotionName.Text = "Neutral";

                // Restaurar el texto plano sin colores de resaltado
                _isHighlighting = true;
                try
                {
                    RtbInput.Document.Blocks.Clear();
                    RtbInput.Document.Blocks.Add(new Paragraph(new Run(_fullPlainText)));
                }
                finally
                {
                    _isHighlighting = false;
                }
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
                    float volume = (float)(SliderVolume.Value / 100.0);

                    await _voiceManager.SynthesizeSpeechAsync(text, saveFileDialog.FileName, selectedVoice, selectedLang, speed, pitch, volume);
                    MessageBox.Show("Audio guardado exitosamente en Descargas.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al guardar el archivo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnSegmentedDownload_Click(object sender, RoutedEventArgs e)
        {
            string text = GetPlainTextFromRichTextBox();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("No hay texto para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Seleccionar carpeta de destino usando el nombre completo para evitar conflictos
            using var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecciona la carpeta donde guardar los segmentos de audio",
                UseDescriptionForTitle = true,
                SelectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            string outputFolder = folderDialog.SelectedPath;
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var segments = EmotionAnalyzer.SegmentAndAnalyze(text);
            if (segments.Count == 0) return;

            string selectedLang  = CbLanguage.SelectedItem?.ToString() ?? "es-ES";
            string selectedVoice = CbVoices.SelectedItem?.ToString() ?? "";
            float speed = (float)SliderSpeed.Value;
            float pitch = (float)SliderPitch.Value;

            BtnSegmentedDownload.IsEnabled = false;
            BtnDownloadAudio.IsEnabled    = false;

            try
            {
                var jsonEntries = new List<object>();

                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    string fileName = $"segment_{i + 1:D3}_{timestamp}.wav";
                    string filePath = Path.Combine(outputFolder, fileName);

                    await _voiceManager.SynthesizeSpeechAsync(seg.Text.Trim(), filePath, selectedVoice, selectedLang, speed, pitch, (float)(SliderVolume.Value / 100.0));

                    jsonEntries.Add(new
                    {
                        index    = i + 1,
                        file     = fileName,
                        text     = seg.Text.Trim(),
                        emotion  = seg.Emotion,
                        emoji    = seg.Emoji,
                        startIndex = seg.StartIndex,
                        length   = seg.Length
                    });
                }

                // Guardar metadata JSON con info de emociones para el avatar VTuber
                string jsonPath = Path.Combine(outputFolder, $"segments_{timestamp}.json");
                string jsonContent = JsonSerializer.Serialize(jsonEntries, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(jsonPath, jsonContent);

                MessageBox.Show(
                    $"Exportación completada:\n• {segments.Count} archivos WAV\n• 1 archivo JSON con emociones\n\nCarpeta: {outputFolder}",
                    "Descarga segmentada exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error durante la exportación: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnSegmentedDownload.IsEnabled = true;
                BtnDownloadAudio.IsEnabled    = true;
            }
        }
    }
}