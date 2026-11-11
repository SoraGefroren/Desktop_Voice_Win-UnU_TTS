using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VtuberTts.Core
{
    /// <summary>
    /// Configuración de parámetros avanzados de síntesis de VoiceStudio.
    /// Homologados exactamente con la interfaz y motor de VoiceStudio (OmniVoice).
    /// </summary>
    public sealed class VoiceStudioSettings
    {
        /// <summary>Influencia de la indicación (guidance_scale). Predeterminado: 2.0</summary>
        public float GuidanceScale { get; set; } = 2.0f;

        /// <summary>Momento del refinamiento (t_shift). Predeterminado: 0.10</summary>
        public float TShift { get; set; } = 0.10f;

        /// <summary>Variación del orden de muestreo (position_temperature). Predeterminado: 5.0</summary>
        public float PositionTemperature { get; set; } = 5.0f;

        /// <summary>Variación del sonido (class_temperature). Predeterminado: 0.0</summary>
        public float ClassTemperature { get; set; } = 0.0f;

        /// <summary>Equilibrio entre grueso y fino (layer_penalty_factor). Predeterminado: 5.0</summary>
        public float LayerPenaltyFactor { get; set; } = 5.0f;

        /// <summary>Refinamiento de voz / pasos de difusión (num_step). Predeterminado: 16</summary>
        public int NumStep { get; set; } = 16;

        /// <summary>Limpiar ruido de referencia (denoise). Predeterminado: true</summary>
        public bool Denoise { get; set; } = true;

        /// <summary>Pulir el audio generado (postprocess_output). Predeterminado: true</summary>
        public bool PostprocessOutput { get; set; } = true;

        /// <summary>Preprocesar audio de referencia (preprocess_prompt). Predeterminado: true</summary>
        public bool PreprocessPrompt { get; set; } = true;

        /// <summary>Equilibrar volumen / masterización (broadcast effect preset). Predeterminado: true</summary>
        public bool BalanceVolume { get; set; } = true;

        /// <summary>Preset de efectos DSP ("broadcast" para masterizado equilibrado, "none" para crudo)</summary>
        public string EffectPreset => BalanceVolume ? "broadcast" : "none";

        /// <summary>Profundidad de bits de audio PCM ("16", "24" o "32"). Predeterminado: "16"</summary>
        public string WavBits { get; set; } = "16";

        /// <summary>Idioma de síntesis (ej: "Spanish", "English", "Japanese"). Predeterminado: "Spanish"</summary>
        public string Language { get; set; } = "Spanish";
    }

    /// <summary>
    /// Motor cliente para conectarse con la API de VoiceStudio (OmniVoice).
    /// Permite descubrir perfiles/voces disponibles y sintetizar texto a archivo WAV
    /// con los mismos parámetros acústicos y de refinamiento que utiliza la interfaz oficial de VoiceStudio.
    /// </summary>
    public sealed class VoiceStudioApiEngine : IDisposable
    {
        public const string ExternalVoiceDisplayName = "VoiceStudio Api (Externo)";

        private readonly HttpClient _httpClient;
        private string _baseUrl = "http://localhost:3902";

        private string _selectedProfileId = string.Empty;
        private string _selectedProfileName = string.Empty;

        // Diccionario nombre visible → id de perfil
        private Dictionary<string, string> _profileMap = new();

        /// <summary>Parámetros avanzados de síntesis homologados con VoiceStudio</summary>
        public VoiceStudioSettings Settings { get; } = new VoiceStudioSettings();

        public VoiceStudioApiEngine(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(300)
            };
        }

        public string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = (value ?? string.Empty).Trim().TrimEnd('/');
        }

        public string SelectedProfileName => _selectedProfileName;
        public string SelectedProfileId => _selectedProfileId;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_baseUrl) &&
            !string.IsNullOrWhiteSpace(_selectedProfileId);

        public static bool IsVoiceStudioApiVoice(string? voiceName) =>
            !string.IsNullOrEmpty(voiceName) &&
            string.Equals(voiceName, ExternalVoiceDisplayName, StringComparison.OrdinalIgnoreCase);

        public void SetConfig(string baseUrl, string profileName)
        {
            BaseUrl = baseUrl;
            _selectedProfileName = (profileName ?? string.Empty).Trim();

            if (_profileMap.TryGetValue(_selectedProfileName, out var id) && !string.IsNullOrWhiteSpace(id))
                _selectedProfileId = id;
            else
                _selectedProfileId = _selectedProfileName;
        }

        /// <summary>
        /// Consulta la API de VoiceStudio para obtener los perfiles o voces disponibles.
        /// </summary>
        public async Task<List<string>> FetchProfilesAsync(string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? BaseUrl : baseUrl.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("La URL del API de VoiceStudio no puede estar vacía.");

            // Rutas para descubrir perfiles tanto en el frontend web (3902) como en el backend directo (3900)
            string[] probePaths = { "/api/profiles", "/profiles", "/api/voices", "/voices" };
            Exception? lastEx = null;

            foreach (var path in probePaths)
            {
                try
                {
                    string target = url + path;
                    using var response = await _httpClient.GetAsync(target, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        continue;

                    string json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var (names, map) = ParseProfilesFromJson(json);

                    if (names.Count > 0)
                    {
                        BaseUrl = url;
                        _profileMap = map;
                        return names;
                    }
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (lastEx != null)
                throw new HttpRequestException($"No se pudo conectar con el servidor VoiceStudio en '{url}': {lastEx.Message}", lastEx);

            throw new HttpRequestException($"El servidor en '{url}' respondió, pero no devolvió ningún perfil ni lista de voces válida.");
        }

        /// <summary>
        /// Sintetiza texto a audio WAV utilizando los valores y endpoints de VoiceStudio.
        /// Intenta primero el endpoint nativo /api/generate (o /generate) con multipart/form-data
        /// para aplicar exactamente el mismo procesado DSP, volumen equilibrado y parámetros avanzados.
        /// En caso de contingencia, recurre a /api/v1/audio/speech.
        /// </summary>
        public async Task SynthesizeToWavAsync(
            string text,
            string outputPath,
            string language = "",
            float speed = 1.0f,
            float pitch = 1.0f,
            float volume = 1.0f,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("VoiceStudio API no está configurado. Carga un perfil de voz antes de sintetizar.");

            string resolvedLang = !string.IsNullOrWhiteSpace(language)
                ? NormalizeLanguage(language)
                : Settings.Language;

            string cultureInvariantSpeed = speed.ToString("0.00", CultureInfo.InvariantCulture);

            // ── 1. Intentar endpoint nativo de VoiceStudio (/api/generate o /generate) ──
            // Este es el endpoint idéntico al botón "Sintetizar audio" de la UI de VoiceStudio.
            string[] generateEndpoints = { "/api/generate", "/generate" };

            foreach (var path in generateEndpoints)
            {
                try
                {
                    string target = _baseUrl.TrimEnd('/') + path;
                    using var form = new MultipartFormDataContent();

                    form.Add(new StringContent(text, Encoding.UTF8), "text");
                    form.Add(new StringContent(_selectedProfileId), "profile_id");
                    form.Add(new StringContent(resolvedLang), "language");
                    form.Add(new StringContent(cultureInvariantSpeed), "speed");

                    // Parámetros avanzados del modelo
                    form.Add(new StringContent(Settings.GuidanceScale.ToString("0.0", CultureInfo.InvariantCulture)), "guidance_scale");
                    form.Add(new StringContent(Settings.TShift.ToString("0.00", CultureInfo.InvariantCulture)), "t_shift");
                    form.Add(new StringContent(Settings.PositionTemperature.ToString("0.0", CultureInfo.InvariantCulture)), "position_temperature");
                    form.Add(new StringContent(Settings.ClassTemperature.ToString("0.0", CultureInfo.InvariantCulture)), "class_temperature");
                    form.Add(new StringContent(Settings.LayerPenaltyFactor.ToString("0.0", CultureInfo.InvariantCulture)), "layer_penalty_factor");
                    form.Add(new StringContent(Settings.NumStep.ToString()), "num_step");

                    // Controles de calidad y DSP
                    form.Add(new StringContent(Settings.Denoise ? "true" : "false"), "denoise");
                    form.Add(new StringContent(Settings.PostprocessOutput ? "true" : "false"), "postprocess_output");
                    form.Add(new StringContent(Settings.EffectPreset), "effect_preset");
                    form.Add(new StringContent(Settings.WavBits), "wav_bits");

                    using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = form };
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        if (data.Length > 0 && !IsJsonBytes(data))
                        {
                            await File.WriteAllBytesAsync(outputPath, data, cancellationToken);
                            return;
                        }
                    }
                }
                catch
                {
                    // Fallback al siguiente endpoint
                }
            }

            // ── 2. Fallback: Endpoint OpenAI-compatible (/api/v1/audio/speech o /v1/audio/speech) ──
            string[] speechEndpoints = { "/api/v1/audio/speech", "/v1/audio/speech" };

            var payload = new
            {
                model = "omnivoice",
                input = text,
                voice = _selectedProfileId,
                response_format = "wav",
                speed = (double)Math.Clamp(speed, 0.25f, 4.0f),
                guidance_scale = Settings.GuidanceScale,
                t_shift = Settings.TShift,
                position_temperature = Settings.PositionTemperature,
                class_temperature = Settings.ClassTemperature,
                layer_penalty_factor = Settings.LayerPenaltyFactor,
                num_step = Settings.NumStep,
                denoise = Settings.Denoise,
                preprocess_prompt = Settings.PreprocessPrompt,
                language = resolvedLang
            };

            string jsonBody = JsonSerializer.Serialize(payload);

            foreach (var path in speechEndpoints)
            {
                try
                {
                    string target = _baseUrl.TrimEnd('/') + path;
                    using var request = new HttpRequestMessage(HttpMethod.Post, target)
                    {
                        Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                    };

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        continue;

                    byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (data.Length == 0)
                        continue;

                    if (IsJsonBytes(data))
                    {
                        string jsonStr = Encoding.UTF8.GetString(data);
                        using var doc = JsonDocument.Parse(jsonStr);
                        if (doc.RootElement.TryGetProperty("audio", out var audioProp) ||
                            doc.RootElement.TryGetProperty("audio_base64", out audioProp) ||
                            doc.RootElement.TryGetProperty("data", out audioProp))
                        {
                            string? b64 = audioProp.GetString();
                            if (!string.IsNullOrEmpty(b64))
                                data = Convert.FromBase64String(b64);
                        }
                    }

                    await File.WriteAllBytesAsync(outputPath, data, cancellationToken);
                    return;
                }
                catch
                {
                    // Continuar al siguiente endpoint
                }
            }

            throw new HttpRequestException("No se pudo sintetizar audio en VoiceStudio API. Verifica que VoiceStudio esté en ejecución.");
        }

        public static string NormalizeLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "Spanish";
            string l = lang.Trim().ToLowerInvariant();
            if (l.StartsWith("es")) return "Spanish";
            if (l.StartsWith("en")) return "English";
            if (l.StartsWith("ja")) return "Japanese";
            if (l.StartsWith("zh")) return "Chinese";
            if (l.StartsWith("fr")) return "French";
            if (l.StartsWith("de")) return "German";
            if (l.StartsWith("it")) return "Italian";
            if (l.StartsWith("pt")) return "Portuguese";
            if (l.StartsWith("ru")) return "Russian";
            if (l.StartsWith("ko")) return "Korean";
            return lang;
        }

        private static (List<string> names, Dictionary<string, string> map) ParseProfilesFromJson(string json)
        {
            var names = new List<string>();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    ExtractFromElements(root.EnumerateArray(), names, map);
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "profiles", "voices", "data", "results" })
                    {
                        if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        {
                            ExtractFromElements(arr.EnumerateArray(), names, map);
                            break;
                        }
                    }
                }
            }
            catch { }

            return (names, map);
        }

        private static void ExtractFromElements(
            JsonElement.ArrayEnumerator arrayEnum,
            List<string> names,
            Dictionary<string, string> map)
        {
            foreach (var item in arrayEnum)
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? str = item.GetString();
                    if (!string.IsNullOrWhiteSpace(str) && !names.Contains(str))
                    {
                        names.Add(str);
                        map[str] = str;
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    string? id = null;
                    string? name = null;

                    if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                        id = idProp.GetString();
                    if (item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                        name = nameProp.GetString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        if (item.TryGetProperty("profile", out var prProp) && prProp.ValueKind == JsonValueKind.String)
                            name = prProp.GetString();
                        else if (item.TryGetProperty("display_name", out var dnProp) && dnProp.ValueKind == JsonValueKind.String)
                            name = dnProp.GetString();
                    }

                    string displayName = name ?? id ?? string.Empty;
                    string resolvedId = id ?? name ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(displayName) && !names.Contains(displayName))
                    {
                        names.Add(displayName);
                        map[displayName] = resolvedId;
                    }
                }
            }
        }

        private static bool IsJsonBytes(byte[] bytes)
        {
            if (bytes.Length < 2) return false;
            char first = (char)bytes[0];
            return first == '{' || first == '[';
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
