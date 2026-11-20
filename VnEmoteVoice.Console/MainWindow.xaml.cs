using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VnEmoteVoice.Core;

namespace VnEmoteVoice.Console
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
        /// <summary>Flag que indica si alguna configuración cambió mientras la reproducción estaba pausada.</summary>
        private bool _settingsChangedWhilePaused = false;
        /// <summary>Temporizador para amortiguar (debounce) eventos de sliders durante la reproducción.</summary>
        private System.Windows.Threading.DispatcherTimer? _sliderDebounceTimer;

        /// <summary>Ruta al archivo .onnx del modelo Piper externo seleccionado por el usuario.</summary>
        private string _piperOnnxPath = string.Empty;
        /// <summary>Ruta al archivo .json de configuración del modelo Piper externo.</summary>
        private string _piperJsonPath = string.Empty;

        public MainWindow()
        {
            _voiceManager = new VoiceManager();
            InitializeComponent();
            LoadLanguages();
            SetInitialText("Hola. ¡Bienvenido al sistema de conversión de texto a voz! Aquí puedes escribir cualquier texto y convertirlo en audio usando diferentes voces e idiomas disponibles. También, puedes ajustar la velocidad, el tono y el volumen de la voz para personalizar tu experiencia.\n\n¡Escribe algo y pruébalo!");
        }

        private void SetInitialText(string text)
        {
            RtbInput.Document.Blocks.Clear();
            Paragraph p = new Paragraph();
            AppendTextWithLineBreaks(p, text, null, Brushes.White, null);
            RtbInput.Document.Blocks.Add(p);
            _fullPlainText = text;
            UpdateCharCount();
        }

        /// <summary>
        /// Agrega texto a un párrafo respetando fielmente todos los saltos de línea (\r\n o \n) mediante elementos LineBreak.
        /// Permite especificar fondo, color y grosor de fuente para resaltar segmentos activos.
        /// </summary>
        private static void AppendTextWithLineBreaks(Paragraph p, string text, Brush? background, Brush foreground, object? tag, FontWeight? fontWeight = null)
        {
            if (string.IsNullOrEmpty(text)) return;

            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int j = 0; j < lines.Length; j++)
            {
                if (lines[j].Length > 0)
                {
                    Run run = new Run(lines[j])
                    {
                        Foreground = foreground,
                        FontWeight = fontWeight ?? FontWeights.Normal
                    };
                    if (background != null) run.Background = background;
                    if (tag != null) run.Tag = tag;
                    p.Inlines.Add(run);
                }

                if (j < lines.Length - 1)
                {
                    p.Inlines.Add(new LineBreak());
                }
            }
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

        private void ChkExternalModel_Checked(object sender, RoutedEventArgs e)
        {
            if (LblLanguageTitle != null)
                LblLanguageTitle.Text = "🧠 Selector de Modelos";

            // Ocultar selector de voz
            if (PanelVoiceSelector != null)
                PanelVoiceSelector.Visibility = Visibility.Collapsed;

            // Convertir selector de idiomas a selector de modelos con opciones externas
            CbLanguage.ItemsSource = new List<string>
            {
                PiperVoiceEngine.ExternalVoiceDisplayName,
                VoiceStudioApiEngine.ExternalVoiceDisplayName
            };
            CbLanguage.SelectedIndex = 0;

            // Actualizar visibilidad de paneles externos según selección inicial
            UpdateExternalPanelsVisibility();

            if (_isPlaying)
            {
                RestartCurrentSegmentWithNewSettings();
            }
        }

        private void ChkExternalModel_Unchecked(object sender, RoutedEventArgs e)
        {
            if (LblLanguageTitle != null)
                LblLanguageTitle.Text = "🌐 Idioma";

            // Restaurar selector de idiomas disponibles
            LoadLanguages();

            // Mostrar nuevamente el selector de voz
            if (PanelVoiceSelector != null)
                PanelVoiceSelector.Visibility = Visibility.Visible;

            // Ocultar cargadores de modelos externos
            if (PanelPiperExterno != null)
                PanelPiperExterno.Visibility = Visibility.Collapsed;
            if (PanelVoiceStudioExterno != null)
                PanelVoiceStudioExterno.Visibility = Visibility.Collapsed;

            if (_isPlaying)
            {
                RestartCurrentSegmentWithNewSettings();
            }
        }

        private void CbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Si está en modo de modelo externo, alternar paneles según el modelo elegido
            if (ChkExternalModel?.IsChecked == true)
            {
                UpdateExternalPanelsVisibility();
                if (_isPlaying)
                {
                    RestartCurrentSegmentWithNewSettings();
                }
                return;
            }

            if (CbLanguage.SelectedItem is string selectedLang)
            {
                var voices = _voiceManager.GetVoicesForLanguage(selectedLang);
                CbVoices.ItemsSource = voices;
                if (voices.Count > 0) CbVoices.SelectedIndex = 0;

                if (_isPlaying)
                {
                    RestartCurrentSegmentWithNewSettings();
                }
            }
        }

        private void CbVoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPlaying)
            {
                RestartCurrentSegmentWithNewSettings();
            }
        }

        /// <summary>
        /// Aplica inmediatamente cualquier cambio de idioma, voz, modelo o configuración
        /// durante la reproducción activa sin necesidad de detener el proceso.
        /// </summary>
        private void RestartCurrentSegmentWithNewSettings()
        {
            if (!_isPlaying) return;

            if (_isPaused)
            {
                // Si está en pausa, marcamos que hubo un cambio para re-sintetizar al reanudar
                _settingsChangedWhilePaused = true;
                _voiceManager.StopAudio();
                return;
            }

            // Forzar reinicio inmediato del segmento actual con las nuevas configuraciones
            _jumpRequested = true;
            _voiceManager.StopAudio();
        }

        /// <summary>
        /// Amortigua (debounce) cambios continuos en sliders (velocidad, tono, parámetros VoiceStudio)
        /// para aplicar el cambio a los 250ms de pausar el arrastre sin saturar la síntesis.
        /// </summary>
        private void OnSliderSettingChangedDebounced()
        {
            if (!_isPlaying) return;

            if (_sliderDebounceTimer == null)
            {
                _sliderDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(250)
                };
                _sliderDebounceTimer.Tick += (s, e) =>
                {
                    _sliderDebounceTimer.Stop();
                    RestartCurrentSegmentWithNewSettings();
                };
            }

            _sliderDebounceTimer.Stop();
            _sliderDebounceTimer.Start();
        }

        private void UpdateExternalPanelsVisibility()
        {
            string selected = CbLanguage.SelectedItem?.ToString() ?? "";
            bool isPiper = string.Equals(selected, PiperVoiceEngine.ExternalVoiceDisplayName, StringComparison.OrdinalIgnoreCase);
            bool isVoiceStudio = VoiceStudioApiEngine.IsVoiceStudioApiVoice(selected);

            if (PanelVoiceSelector != null)
                PanelVoiceSelector.Visibility = Visibility.Collapsed;

            if (PanelPiperExterno != null)
                PanelPiperExterno.Visibility = isPiper ? Visibility.Visible : Visibility.Collapsed;

            if (PanelVoiceStudioExterno != null)
                PanelVoiceStudioExterno.Visibility = isVoiceStudio ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnBrowseOnnx_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleccionar modelo Piper (.onnx)",
                Filter = "Modelo ONNX (*.onnx)|*.onnx|Todos los archivos (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                _piperOnnxPath = dlg.FileName;
                LblOnnxPath.Text = System.IO.Path.GetFileName(_piperOnnxPath);
                LblOnnxPath.Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250)); // #a78bfa
                TryLoadExternalPiperModel();
            }
        }

        private void BtnBrowseJson_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Seleccionar configuración Piper (.json)",
                Filter = "Configuración JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                _piperJsonPath = dlg.FileName;
                LblJsonPath.Text = System.IO.Path.GetFileName(_piperJsonPath);
                LblJsonPath.Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250)); // #a78bfa
                TryLoadExternalPiperModel();
            }
        }

        /// <summary>
        /// Cuando ambos archivos (.onnx y .json) estén seleccionados, intenta cargar
        /// el modelo externo en el motor Piper para que quede listo para síntesis.
        /// </summary>
        private void TryLoadExternalPiperModel()
        {
            if (string.IsNullOrEmpty(_piperOnnxPath) || string.IsNullOrEmpty(_piperJsonPath))
                return;

            try
            {
                _voiceManager.SetExternalPiperModel(_piperOnnxPath, _piperJsonPath);

                var successBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // #34d399
                LblOnnxPath.Foreground = successBrush;
                LblJsonPath.Foreground = successBrush;

                if (_isPlaying)
                {
                    RestartCurrentSegmentWithNewSettings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error al cargar el modelo Piper externo:\n{ex.Message}",
                    "Error de modelo", MessageBoxButton.OK, MessageBoxImage.Error);

                var errorBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113)); // #f87171
                if (ex.Message.Contains("onnx", StringComparison.OrdinalIgnoreCase))
                    LblOnnxPath.Foreground = errorBrush;
                else
                    LblJsonPath.Foreground = errorBrush;
            }
        }

        private async void BtnConnectVoiceStudio_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtVoiceStudioUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Por favor ingresa el enlace del API de VoiceStudio.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnConnectVoiceStudio.IsEnabled = false;
            LblVoiceStudioStatus.Text = "Conectando al API...";
            LblVoiceStudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Amarillo

            try
            {
                var profiles = await _voiceManager.FetchVoiceStudioProfilesAsync(url);
                CbVoiceStudioProfiles.ItemsSource = profiles;
                if (profiles.Count > 0)
                {
                    CbVoiceStudioProfiles.SelectedIndex = 0;
                    LblVoiceStudioStatus.Text = $"Conectado: {profiles.Count} perfil(es) disponible(s)";
                    LblVoiceStudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Verde
                    _voiceManager.SetVoiceStudioConfig(url, profiles[0]);

                    if (_isPlaying)
                    {
                        RestartCurrentSegmentWithNewSettings();
                    }
                }
                else
                {
                    LblVoiceStudioStatus.Text = "Conectado, pero no devolvió perfiles.";
                    LblVoiceStudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Rojo
                }
            }
            catch (Exception ex)
            {
                LblVoiceStudioStatus.Text = "Error de conexión";
                LblVoiceStudioStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Rojo
                MessageBox.Show($"Error al conectar con VoiceStudio API:\n{ex.Message}", "Error de conexión", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnConnectVoiceStudio.IsEnabled = true;
            }
        }

        private void CbVoiceStudioProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbVoiceStudioProfiles.SelectedItem is string profile)
            {
                _voiceManager.SetVoiceStudioConfig(TxtVoiceStudioUrl.Text.Trim(), profile);

                if (_isPlaying)
                {
                    RestartCurrentSegmentWithNewSettings();
                }
            }
        }

        private void SliderVoiceStudio_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_voiceManager?.VoiceStudioEngine?.Settings == null) return;
            var s = _voiceManager.VoiceStudioEngine.Settings;

            if (SliderGuidanceScale != null && LblGuidanceScale != null)
            {
                s.GuidanceScale = (float)SliderGuidanceScale.Value;
                LblGuidanceScale.Text = $"{s.GuidanceScale:F1}";
            }
            if (SliderTShift != null && LblTShift != null)
            {
                s.TShift = (float)SliderTShift.Value;
                LblTShift.Text = $"{s.TShift:F2}";
            }
            if (SliderPosTemp != null && LblPosTemp != null)
            {
                s.PositionTemperature = (float)SliderPosTemp.Value;
                LblPosTemp.Text = $"{s.PositionTemperature:F1}";
            }
            if (SliderClassTemp != null && LblClassTemp != null)
            {
                s.ClassTemperature = (float)SliderClassTemp.Value;
                LblClassTemp.Text = $"{s.ClassTemperature:F1}";
            }
            if (SliderLayerPenalty != null && LblLayerPenalty != null)
            {
                s.LayerPenaltyFactor = (float)SliderLayerPenalty.Value;
                LblLayerPenalty.Text = $"{s.LayerPenaltyFactor:F1}";
            }
            if (SliderNumStep != null && LblNumStep != null)
            {
                s.NumStep = (int)SliderNumStep.Value;
                LblNumStep.Text = $"{s.NumStep}";
            }

            if (_isPlaying)
            {
                OnSliderSettingChangedDebounced();
            }
        }

        private void ChkVoiceStudio_Changed(object sender, RoutedEventArgs e)
        {
            if (_voiceManager?.VoiceStudioEngine?.Settings == null) return;
            var s = _voiceManager.VoiceStudioEngine.Settings;

            if (ChkBalanceVolume != null) s.BalanceVolume = ChkBalanceVolume.IsChecked == true;
            if (ChkDenoise != null) s.Denoise = ChkDenoise.IsChecked == true;
            if (ChkPostprocess != null) s.PostprocessOutput = ChkPostprocess.IsChecked == true;

            if (_isPlaying)
            {
                RestartCurrentSegmentWithNewSettings();
            }
        }

        private string GetCurrentSelectedVoice()
        {
            if (ChkExternalModel?.IsChecked == true)
            {
                return CbLanguage.SelectedItem?.ToString() ?? "";
            }
            return CbVoices.SelectedItem?.ToString() ?? "";
        }

        private string GetCurrentSelectedLanguage()
        {
            if (ChkExternalModel?.IsChecked == true)
            {
                string selected = CbLanguage.SelectedItem?.ToString() ?? "";
                if (VoiceStudioApiEngine.IsVoiceStudioApiVoice(selected))
                {
                    return _voiceManager.VoiceStudioEngine.Settings.Language;
                }
                return "";
            }
            return CbLanguage.SelectedItem?.ToString() ?? "es-ES";
        }

        private bool ValidateReadyToSynthesize()
        {
            if (ChkExternalModel?.IsChecked == true)
            {
                string selected = CbLanguage.SelectedItem?.ToString() ?? "";
                if (string.Equals(selected, PiperVoiceEngine.ExternalVoiceDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!_voiceManager.HasExternalPiperModel)
                    {
                        MessageBox.Show(
                            "Para usar 'Pipper (Externo)' debes cargar primero un archivo .onnx y su .json de configuración.",
                            "Modelo no cargado", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
                else if (VoiceStudioApiEngine.IsVoiceStudioApiVoice(selected))
                {
                    if (!_voiceManager.HasVoiceStudioConfig)
                    {
                        MessageBox.Show(
                            "Para usar 'VoiceStudio Api (Externo)' debes ingresar el enlace, presionar 'Cargar' y seleccionar un perfil disponible.",
                            "API no configurada", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                }
            }
            return true;
        }

        private void SliderSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblSpeed != null) LblSpeed.Text = $"{e.NewValue:F1}x";
            if (_isPlaying)
            {
                OnSliderSettingChangedDebounced();
            }
        }

        private void SliderPitch_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblPitch != null) LblPitch.Text = $"{e.NewValue:F1}";
            if (_isPlaying)
            {
                OnSliderSettingChangedDebounced();
            }
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

        // ─── Control de Modelo de Emociones ───────────────────────────────────────────

        private void ChkUseDefaultEmotionModel_Changed(object sender, RoutedEventArgs e)
        {
            bool usarPorDefecto = ChkUseDefaultEmotionModel?.IsChecked == true;

            if (PanelCustomEmotionModel != null)
                PanelCustomEmotionModel.Visibility = usarPorDefecto ? Visibility.Collapsed : Visibility.Visible;

            if (usarPorDefecto)
            {
                EmotionAnalyzer.LoadDefaultSpanishModel();

                if (LblEmotionModelStatus != null)
                {
                    LblEmotionModelStatus.Text = $"✅ Español por defecto ({EmotionAnalyzer.ActiveEmotionsCount} emociones cargadas)";
                    LblEmotionModelStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                }
            }
        }

        private void BtnLoadEmotionModel_Click(object sender, RoutedEventArgs e)
        {
            var dialogo = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar archivo de emociones",
                Filter = "Archivos JSON de emociones (*.json)|*.json",
                CheckFileExists = true
            };

            if (dialogo.ShowDialog() == true)
            {
                string ruta = dialogo.FileName;

                if (TxtEmotionModelPath != null)
                    TxtEmotionModelPath.Text = ruta;

                if (LblEmotionModelStatus != null)
                {
                    LblEmotionModelStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                    LblEmotionModelStatus.Text = "⏳ Validando archivo...";
                }

                bool cargadoOk = EmotionAnalyzer.LoadFromJsonFile(ruta, out string mensajeError);

                if (LblEmotionModelStatus != null)
                {
                    if (cargadoOk)
                    {
                        LblEmotionModelStatus.Text = $"✅ {EmotionAnalyzer.ActiveLanguageName} ({EmotionAnalyzer.ActiveEmotionsCount} emociones cargadas)";
                        LblEmotionModelStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    }
                    else
                    {
                        LblEmotionModelStatus.Text = $"❌ {mensajeError}";
                        LblEmotionModelStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));

                        // Restaurar al modelo por defecto si el archivo no es válido
                        EmotionAnalyzer.LoadDefaultSpanishModel();

                        if (ChkUseDefaultEmotionModel != null)
                            ChkUseDefaultEmotionModel.IsChecked = true;
                    }
                }
            }
        }

        private void RtbInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isHighlighting) return;

            if (_isPlaying)
            {
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
                if (_settingsChangedWhilePaused)
                {
                    _settingsChangedWhilePaused = false;
                    _isPaused = false;
                    BtnPause.Content = "⏸";
                    _jumpRequested = true;
                    _voiceManager.StopAudio();
                }
                else
                {
                    _voiceManager.ResumeAudio();
                    _isPaused = false;
                    BtnPause.Content = "⏸";
                }
                return;
            }

            if (!ValidateReadyToSynthesize()) return;

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
            while (_currentSegmentIndex < _segments.Count && _isPlaying)
            {
                _jumpRequested = false;
                int activeIndex = _currentSegmentIndex;
                var segment = _segments[activeIndex];

                if (string.IsNullOrWhiteSpace(segment.Text))
                {
                    _currentSegmentIndex++;
                    continue;
                }

                if (!ValidateReadyToSynthesize())
                {
                    _isPlaying = false;
                    ResetToNeutralState();
                    break;
                }

                string selectedLang = GetCurrentSelectedLanguage();
                string selectedVoice = GetCurrentSelectedVoice();
                float speed = (float)SliderSpeed.Value;
                float pitch = (float)SliderPitch.Value;
                float volume = (float)(SliderVolume.Value / 100.0);

                LblEmotionEmoji.Text = segment.Emoji;
                LblEmotionName.Text = segment.Emotion;

                if (segment.HasTransition)
                {
                    BadgeTransition.Visibility = Visibility.Visible;
                    BadgeTransition.ToolTip = $"Transición ({segment.Transition.TransitionType}): {segment.Transition.FromEmotion} ({segment.Transition.FromEmoji}) ➔ {segment.Transition.ToEmotion} ({segment.Transition.ToEmoji})";
                }
                else
                {
                    BadgeTransition.Visibility = Visibility.Collapsed;
                }

                HighlightSegment(activeIndex);

                string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"segment_{activeIndex}.wav");
                await _voiceManager.SynthesizeSpeechAsync(segment.Text, tempPath, selectedVoice, selectedLang, speed, pitch, volume);

                if (!_isPlaying) break;
                if (_jumpRequested) continue;

                // Asegurar que el texto resaltado esté visible justo antes de comenzar a reproducir
                ScrollActiveSegmentIntoView(activeIndex);

                _voiceManager.PlayAudio(tempPath, volume);

                await Task.Delay(250);
                while (_voiceManager.IsPlaying && !_isPaused && _isPlaying && !_jumpRequested)
                {
                    // Mantener el texto resaltado visible de forma continua durante la reproducción
                    ScrollActiveSegmentIntoView(activeIndex);
                    await Task.Delay(150);
                }

                if (!_isPlaying) break;

                if (_jumpRequested)
                {
                    continue;
                }

                if (_isPaused)
                {
                    while (_isPaused && _isPlaying && !_jumpRequested)
                    {
                        await Task.Delay(100);
                    }
                    if (!_isPlaying) break;
                    if (_jumpRequested) continue;
                }

                _currentSegmentIndex++;
            }

            ResetToNeutralState();
        }

        private void HighlightSegment(int activeIndex)
        {
            _isHighlighting = true;
            try
            {
                RtbInput.Document.Blocks.Clear();
                Paragraph p = new Paragraph();

                int cursor = 0;
                int textLength = _fullPlainText.Length;

                Brush normalForeground = new SolidColorBrush(Color.FromArgb(220, 240, 246, 252));
                Brush activeForeground = Brushes.White;
                // Púrpura vibrante #7c3aed para que el texto resaltado se distinga claramente en todo momento
                Brush activeBackground = new SolidColorBrush(Color.FromRgb(124, 58, 237));

                for (int i = 0; i < _segments.Count; i++)
                {
                    var seg = _segments[i];

                    if (seg.StartIndex > cursor && cursor < textLength)
                    {
                        int gapLength = Math.Min(seg.StartIndex - cursor, textLength - cursor);
                        string gapText = _fullPlainText.Substring(cursor, gapLength);
                        AppendTextWithLineBreaks(p, gapText, null, normalForeground, null);
                        cursor = seg.StartIndex;
                    }

                    bool isActive = (i == activeIndex);
                    Brush? bg = isActive ? activeBackground : null;
                    Brush fg = isActive ? activeForeground : normalForeground;
                    FontWeight fw = isActive ? FontWeights.Bold : FontWeights.Normal;

                    AppendTextWithLineBreaks(p, seg.Text, bg, fg, i, fw);
                    cursor = Math.Max(cursor, seg.StartIndex + seg.Length);
                }

                if (cursor < textLength)
                {
                    string trailingText = _fullPlainText.Substring(cursor);
                    AppendTextWithLineBreaks(p, trailingText, null, normalForeground, null);
                }

                RtbInput.Document.Blocks.Add(p);

                // Desplazar automáticamente el scroll hacia el segmento activo
                ScrollActiveSegmentIntoView(activeIndex);
            }
            finally
            {
                _isHighlighting = false;
            }
        }

        /// <summary>
        /// Asegura que el segmento resaltado activo quede completamente visible en el RichTextBox,
        /// moviendo el scroll automáticamente si el texto se encuentra fuera del viewport.
        /// </summary>
        private void ScrollActiveSegmentIntoView(int activeIndex)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var p = RtbInput.Document.Blocks.OfType<Paragraph>().FirstOrDefault();
                    if (p == null) return;

                    var activeRun = p.Inlines.OfType<Run>().FirstOrDefault(r => r.Tag is int idx && idx == activeIndex);
                    if (activeRun == null) return;

                    // 1. BringIntoView directo en el elemento del FlowDocument
                    activeRun.BringIntoView();

                    // 2. Control preciso y garantizado con el ScrollViewer interno del RichTextBox
                    var sv = FindVisualChild<ScrollViewer>(RtbInput);
                    if (sv != null)
                    {
                        Rect rect = activeRun.ContentStart.GetCharacterRect(LogicalDirection.Forward);

                        // Si la posición superior o inferior del texto resaltado está fuera del viewport
                        if (rect.Top < 15 || rect.Bottom > (sv.ViewportHeight - 25))
                        {
                            // Centrar el texto activo en el tercio superior de la vista
                            double targetOffset = sv.VerticalOffset + rect.Top - (sv.ViewportHeight / 3.0);
                            targetOffset = Math.Clamp(targetOffset, 0, sv.ScrollableHeight);
                            sv.ScrollToVerticalOffset(targetOffset);
                        }
                    }
                }
                catch { }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        private static bool IsClickOnScrollBar(object? source)
        {
            try
            {
                // 1. Si es un elemento del FlowDocument (Run, Paragraph, Span, etc.), es texto, no ScrollBar
                if (source is FrameworkContentElement)
                    return false;

                // 2. Solo los controles visuales (Thumb, Track, RepeatButton, ScrollBar) se inspeccionan
                if (source is Visual visual)
                {
                    DependencyObject? current = visual;
                    while (current != null)
                    {
                        if (current is System.Windows.Controls.Primitives.ScrollBar ||
                            current is System.Windows.Controls.Primitives.Thumb ||
                            current is System.Windows.Controls.Primitives.Track ||
                            current is System.Windows.Controls.Primitives.RepeatButton)
                        {
                            return true;
                        }

                        // VisualTreeHelper.GetParent solo es válido para Visual o Visual3D
                        if (current is Visual v)
                            current = VisualTreeHelper.GetParent(v);
                        else
                            break;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private void RtbInput_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Si el clic fue en la barra de scroll (thumb, track o botones), no interceptar
            // para permitir arrastrar o hacer clic en el scroll sin pausar o saltar segmento
            if (IsClickOnScrollBar(e.OriginalSource))
            {
                return;
            }

            if (_isPlaying && _segments.Count > 0)
            {
                Point mousePos = e.GetPosition(RtbInput);
                TextPointer pointer = RtbInput.GetPositionFromPoint(mousePos, true);
                if (pointer != null)
                {
                    int targetIndex = -1;

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

                    _currentSegmentIndex = targetIndex;
                    _jumpRequested = true;
                    _voiceManager.StopAudio();

                    if (_isPaused)
                    {
                        _isPaused = false;
                        BtnPause.Content = "⏸";
                    }

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
                if (_settingsChangedWhilePaused)
                {
                    _settingsChangedWhilePaused = false;
                    _isPaused = false;
                    BtnPause.Content = "⏸";
                    _jumpRequested = true;
                    _voiceManager.StopAudio();
                }
                else
                {
                    _voiceManager.ResumeAudio();
                    _isPaused = false;
                    BtnPause.Content = "⏸";
                }
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
            _settingsChangedWhilePaused = false;
            _sliderDebounceTimer?.Stop();
            _currentSegmentIndex = 0;

            Dispatcher.Invoke(() =>
            {
                PanelPauseStop.Visibility = Visibility.Collapsed;
                BtnPlay.Visibility = Visibility.Visible;
                BtnPause.Content = "⏸";
                LblEmotionEmoji.Text = "😐";
                LblEmotionName.Text = "Neutral";
                BadgeTransition.Visibility = Visibility.Collapsed;

                _isHighlighting = true;
                try
                {
                    RtbInput.Document.Blocks.Clear();
                    Paragraph p = new Paragraph();
                    AppendTextWithLineBreaks(p, _fullPlainText, null, Brushes.White, null);
                    RtbInput.Document.Blocks.Add(p);
                }
                finally
                {
                    _isHighlighting = false;
                }
            });
        }

        private async void BtnDownloadAudio_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateReadyToSynthesize()) return;

            string text = GetPlainTextFromRichTextBox();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("No hay texto para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Archivo de audio WAV (*.wav)|*.wav",
                FileName = $"vnarrator_audio_{DateTime.Now:yyyyMMdd_HHmmss}.wav",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string selectedLang = GetCurrentSelectedLanguage();
                    string selectedVoice = GetCurrentSelectedVoice();
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
            if (!ValidateReadyToSynthesize()) return;

            string text = GetPlainTextFromRichTextBox();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("No hay texto para exportar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var folderDialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Selecciona la carpeta donde guardar los segmentos de audio",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Multiselect = false,
            };

            if (folderDialog.ShowDialog() != true) return;

            string outputFolder = folderDialog.FolderName;
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var segments = EmotionAnalyzer.SegmentAndAnalyze(text);
            if (segments.Count == 0) return;

            string selectedLang = GetCurrentSelectedLanguage();
            string selectedVoice = GetCurrentSelectedVoice();
            float speed = (float)SliderSpeed.Value;
            float pitch = (float)SliderPitch.Value;

            BtnSegmentedDownload.IsEnabled = false;
            BtnDownloadAudio.IsEnabled = false;

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
                        index = i + 1,
                        file = fileName,
                        text = seg.Text.Trim(),
                        emotion = seg.Emotion,
                        emoji = seg.Emoji,
                        primary_emotion = seg.PrimaryEmotion,
                        primary_emoji = seg.PrimaryEmoji,
                        base_emotion = seg.BaseEmotion,
                        modifier = seg.Modifier,
                        has_transition = seg.HasTransition,
                        transition = seg.HasTransition ? new
                        {
                            from = seg.Transition.FromEmotion,
                            from_emoji = seg.Transition.FromEmoji,
                            to = seg.Transition.ToEmotion,
                            to_emoji = seg.Transition.ToEmoji,
                            transition_type = seg.Transition.TransitionType,
                            description = seg.Transition.DescriptionWithEmoji
                        } : null,
                        sub_segments = seg.SubEmotions.Select(sub => new
                        {
                            part = sub.PartIndex,
                            text = sub.Text,
                            emotion = sub.Emotion,
                            base_emotion = sub.BaseEmotion,
                            modifier = sub.Modifier,
                            emoji = sub.Emoji,
                            relative_start_index = sub.RelativeStartIndex,
                            length = sub.Length
                        }).ToList(),
                        startIndex = seg.StartIndex,
                        length = seg.Length
                    });
                }

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
                BtnDownloadAudio.IsEnabled = true;
            }
        }
    }
}
