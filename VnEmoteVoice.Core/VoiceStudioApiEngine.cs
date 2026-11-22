using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Configuración de Parámetros Acústicos para VoiceStudio (OmniVoice).
    /// 
    /// ¿Qué es esto?
    /// Son los ajustes de calidad y estilo para el generador de voz VoiceStudio:
    /// Permiten controlar desde la fidelidad al personaje hasta el nivel de limpieza del sonido.
    /// </summary>
    public sealed class VoiceStudioSettings
    {
        // Controla qué tan fiel debe ser la voz al personaje original (2.0 es el valor recomendado)
        public float GuidanceScale { get; set; } = 2.0f;

        // Momento de inicio del proceso de refinamiento del audio (0.10 por defecto)
        public float TShift { get; set; } = 0.10f;

        // Variación en la colocación de los sonidos (5.0 por defecto)
        public float PositionTemperature { get; set; } = 5.0f;

        // Estabilidad del tono (0.0 significa máxima estabilidad y consistencia)
        public float ClassTemperature { get; set; } = 0.0f;

        // Equilibrio entre sonido grueso y fino (5.0 por defecto)
        public float LayerPenaltyFactor { get; set; } = 5.0f;

        // Cantidad de pasos para limpiar y formar la voz (16 pasos es el balance ideal entre rapidez y calidad)
        public int NumStep { get; set; } = 16;

        // Activa la eliminación de ruidos de fondo en la voz de origen
        public bool Denoise { get; set; } = true;

        // Pule el audio final para que suene más suave
        public bool PostprocessOutput { get; set; } = true;

        // Normaliza el audio de prueba antes de generar la voz
        public bool PreprocessPrompt { get; set; } = true;

        // Aplica un efecto para que el volumen suene parejo y profesional como en la radio
        public bool BalanceVolume { get; set; } = true;

        // Modo de efecto de sonido ("broadcast" para radio o "none" para audio sin procesar)
        public string EffectPreset => BalanceVolume ? "broadcast" : "none";

        // Calidad de bits del archivo de audio (16 bits es la calidad estándar de CD)
        public string WavBits { get; set; } = "16";

        // Idioma en el que hablará la voz (por defecto: "Spanish")
        public string Language { get; set; } = "Spanish";
    }

    /// <summary>
    /// Conector con la API externa de VoiceStudio (OmniVoice).
    /// 
    /// ¿Qué hace esta clase?
    /// Se comunica a través de la red local con el programa VoiceStudio:
    /// 1. Pregunta qué voces o personajes tiene disponibles.
    /// 2. Le envía el texto que queremos escuchar.
    /// 3. Recibe el archivo de audio (.wav) ya generado listo para reproducirse.
    /// </summary>
    public sealed class VoiceStudioApiEngine : IDisposable
    {
        // Nombre descriptivo con el que aparece esta opción en la pantalla
        public const string ExternalVoiceDisplayName = "VoiceStudio Api (Externo)";

        // Cliente para enviar y recibir datos por la red
        private readonly HttpClient _httpClient;

        // Dirección web donde está funcionando VoiceStudio (por defecto en tu propia computadora: http://localhost:3902)
        private string _baseUrl = "http://localhost:3902";

        // Identificador y nombre del personaje de voz seleccionado
        private string _selectedProfileId = string.Empty;
        private string _selectedProfileName = string.Empty;

        // Diccionario para traducir el nombre visible de la voz a su código interno
        private Dictionary<string, string> _profileMap = new(StringComparer.OrdinalIgnoreCase);

        // Ajustes detallados de calidad acústica
        public VoiceStudioSettings Settings { get; } = new VoiceStudioSettings();

        public VoiceStudioApiEngine(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(300) // Espera máxima de 5 minutos para textos largos
            };
        }

        // Dirección del servidor (limpiando barras sobrantes al final)
        public string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = (value ?? string.Empty).Trim().TrimEnd('/');
        }

        // Revisa si ya tenemos una dirección web y un personaje seleccionado
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_baseUrl) &&
            !string.IsNullOrWhiteSpace(_selectedProfileId);

        // Comprueba si el nombre de una voz corresponde a este motor externo
        public static bool IsVoiceStudioApiVoice(string? voiceName) =>
            !string.IsNullOrEmpty(voiceName) &&
            string.Equals(voiceName, ExternalVoiceDisplayName, StringComparison.OrdinalIgnoreCase);

        // Guarda la dirección web y el personaje de voz elegido
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
        /// Conecta con VoiceStudio y obtiene la lista de todos los personajes o voces disponibles.
        /// </summary>
        public async Task<List<string>> FetchProfilesAsync(string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? BaseUrl : baseUrl.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("La dirección web (URL) de VoiceStudio no puede estar vacía.");

            // Rutas habituales donde VoiceStudio publica sus listas de voces
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
                throw new HttpRequestException($"No se pudo conectar con VoiceStudio en '{url}': {lastEx.Message}", lastEx);

            throw new HttpRequestException($"El servidor en '{url}' respondió, pero no devolvió ninguna voz válida.");
        }

        /// <summary>
        /// Envía el texto a VoiceStudio para que lo convierta en un archivo de audio WAV.
        /// (Nota: Se eliminaron los parámetros no utilizados de tono y volumen, ya que VoiceStudio
        /// aplica su propio volumen masterizado y el tono no forma parte de su API).
        /// </summary>
        public async Task SynthesizeToWavAsync(
            string text,
            string outputPath,
            string language = "",
            float speed = 1.0f,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException("VoiceStudio no está configurado. Conéctalo y elige una voz antes de generar audio.");

            string resolvedLang = !string.IsNullOrWhiteSpace(language)
                ? NormalizeLanguage(language)
                : Settings.Language;

            string cultureInvariantSpeed = speed.ToString("0.00", CultureInfo.InvariantCulture);

            // ─── 1. Intentar el método oficial nativo (/api/generate o /generate) ───
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

                    // Parámetros de calidad acústica
                    form.Add(new StringContent(Settings.GuidanceScale.ToString("0.0", CultureInfo.InvariantCulture)), "guidance_scale");
                    form.Add(new StringContent(Settings.TShift.ToString("0.00", CultureInfo.InvariantCulture)), "t_shift");
                    form.Add(new StringContent(Settings.PositionTemperature.ToString("0.0", CultureInfo.InvariantCulture)), "position_temperature");
                    form.Add(new StringContent(Settings.ClassTemperature.ToString("0.0", CultureInfo.InvariantCulture)), "class_temperature");
                    form.Add(new StringContent(Settings.LayerPenaltyFactor.ToString("0.0", CultureInfo.InvariantCulture)), "layer_penalty_factor");
                    form.Add(new StringContent(Settings.NumStep.ToString()), "num_step");

                    // Controles de filtros y limpieza de sonido
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
                    // Si falla esta ruta, continúa probando la siguiente
                }
            }

            // ─── 2. Método alternativo de respaldo (/api/v1/audio/speech) ───
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

                    // Si el servidor devolvió audio codificado en Base64 dentro de un JSON, lo decodificamos
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
                    // Continuar probando
                }
            }

            throw new HttpRequestException("No se pudo generar audio con VoiceStudio. Asegúrate de que el programa VoiceStudio esté encendido y funcionando.");
        }

        // Convierte códigos cortos de idioma como "es" o "en" en nombres completos como "Spanish" o "English"
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

        // Extrae los nombres de las voces desde el formato JSON recibido del servidor
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

        // Cierra la conexión de red al salir
        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
