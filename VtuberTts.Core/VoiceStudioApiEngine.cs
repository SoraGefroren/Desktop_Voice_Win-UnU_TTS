using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VtuberTts.Core
{
    /// <summary>
    /// Motor cliente para conectarse con una API REST externa compatible con VoiceStudio / OpenAI TTS.
    /// Permite descubrir perfiles/voces disponibles y sintetizar texto a archivo WAV.
    /// 
    /// Arquitectura de VoiceStudio:
    ///   - Puerto 3900: Python FastAPI backend (API real) → usar este
    ///   - Puerto 3902: Electron UI frontend → NO usar para API
    /// 
    /// Endpoints utilizados:
    ///   - GET  {baseUrl}/api/profiles → lista de perfiles [{id, name, ...}]
    ///   - POST {baseUrl}/v1/audio/speech → síntesis (compatible OpenAI)
    ///     Body: { model, input, voice, response_format, speed }
    ///     Response: bytes WAV binarios directos
    /// </summary>
    public sealed class VoiceStudioApiEngine : IDisposable
    {
        public const string ExternalVoiceDisplayName = "VoiceStudio Api (Externo)";

        // HttpClient con timeout extendido (VoiceStudio puede tardar hasta 5 min en cargar el modelo)
        private readonly HttpClient _httpClient;
        private string _baseUrl = "http://localhost:3900";

        // Separamos nombre visible del ID real que enviar en la petición
        private string _selectedProfileId   = string.Empty;
        private string _selectedProfileName = string.Empty;

        // Diccionario nombre → id para resolver la selección del ComboBox
        private Dictionary<string, string> _profileMap = new();

        public VoiceStudioApiEngine(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient
            {
                // Timeout largo: el primer request puede tardar mientras VoiceStudio carga el modelo
                Timeout = TimeSpan.FromSeconds(300)
            };
        }

        public string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = (value ?? string.Empty).Trim().TrimEnd('/');
        }

        /// <summary>Nombre visible del perfil seleccionado (mostrado en ComboBox).</summary>
        public string SelectedProfileName => _selectedProfileName;

        /// <summary>ID interno del perfil seleccionado (enviado al API).</summary>
        public string SelectedProfileId => _selectedProfileId;

        /// <summary>
        /// True cuando hay URL y perfil configurados listos para sintetizar.
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_baseUrl) &&
            !string.IsNullOrWhiteSpace(_selectedProfileId);

        public static bool IsVoiceStudioApiVoice(string? voiceName) =>
            !string.IsNullOrEmpty(voiceName) &&
            string.Equals(voiceName, ExternalVoiceDisplayName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Establece la URL base y el perfil seleccionado a partir del nombre visible.
        /// Resuelve el ID interno usando el mapa cargado en FetchProfilesAsync.
        /// </summary>
        public void SetConfig(string baseUrl, string profileName)
        {
            BaseUrl = baseUrl;
            _selectedProfileName = (profileName ?? string.Empty).Trim();

            // Intentar resolver el ID desde el mapa; si no existe, usar el nombre como ID fallback
            if (_profileMap.TryGetValue(_selectedProfileName, out var id) && !string.IsNullOrWhiteSpace(id))
                _selectedProfileId = id;
            else
                _selectedProfileId = _selectedProfileName; // fallback: usar nombre como ID
        }

        /// <summary>
        /// Consulta la API de VoiceStudio para obtener los perfiles o voces disponibles.
        /// Devuelve lista de nombres visibles (para mostrar en ComboBox).
        /// Internamente guarda también los IDs en el mapa.
        /// </summary>
        public async Task<List<string>> FetchProfilesAsync(string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? BaseUrl : baseUrl.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("La URL del API de VoiceStudio no puede estar vacía.");

            // Rutas habituales para perfiles — probar en orden de preferencia
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
                throw new HttpRequestException(
                    $"No se pudo conectar con el servidor VoiceStudio en '{url}': {lastEx.Message}", lastEx);

            throw new HttpRequestException(
                $"El servidor en '{url}' respondió, pero no devolvió ningún perfil ni lista de voces válida.\n" +
                "Verifica que la URL sea la del backend Python (ej. http://localhost:3900), no la del frontend Electron.");
        }

        /// <summary>
        /// Sintetiza texto a audio WAV usando el endpoint OpenAI-compatible de VoiceStudio.
        /// Endpoint: POST {baseUrl}/v1/audio/speech
        /// Body: { "model": "tts-1", "input": text, "voice": profileId, "response_format": "wav", "speed": N }
        /// Response: bytes WAV directos (no JSON)
        /// </summary>
        public async Task SynthesizeToWavAsync(
            string text,
            string outputPath,
            float speed = 1.0f,
            float pitch = 1.0f,
            float volume = 1.0f,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "VoiceStudio API no está configurado. Ingresa el enlace del API backend (ej. http://localhost:3900), " +
                    "presiona 'Cargar' y selecciona un perfil de voz.");

            // Endpoint único y correcto para VoiceStudio (OpenAI-compatible)
            string target = _baseUrl.TrimEnd('/') + "/v1/audio/speech";

            // Payload compatible con la API de VoiceStudio (OpenAI TTS format)
            var payload = new
            {
                model          = "tts-1",
                input          = text,
                voice          = _selectedProfileId,  // ID del perfil, no el nombre
                response_format = "wav",
                speed          = (double)Math.Clamp(speed, 0.25f, 4.0f)
            };

            string jsonBody = JsonSerializer.Serialize(payload);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, target)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };

                // Leer respuesta completa como stream para manejar archivos grandes
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new HttpRequestException(
                        $"El API de VoiceStudio respondió con error {(int)response.StatusCode}: {response.ReasonPhrase}\n{errBody}");
                }

                byte[] data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                if (data.Length == 0)
                    throw new HttpRequestException("El API de VoiceStudio devolvió una respuesta vacía.");

                // Verificar si la respuesta es JSON con audio en base64 (fallback)
                if (IsJsonBytes(data))
                {
                    string jsonStr = Encoding.UTF8.GetString(data);
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonStr);
                        JsonElement audioProp;
                        if (doc.RootElement.TryGetProperty("audio", out audioProp)       ||
                            doc.RootElement.TryGetProperty("audio_base64", out audioProp) ||
                            doc.RootElement.TryGetProperty("data", out audioProp))
                        {
                            string? b64 = audioProp.GetString();
                            if (!string.IsNullOrEmpty(b64))
                                data = Convert.FromBase64String(b64);
                        }
                        else if (doc.RootElement.TryGetProperty("error", out var errProp))
                        {
                            throw new HttpRequestException(
                                $"Error desde VoiceStudio API: {errProp.GetString()}");
                        }
                    }
                    catch (JsonException)
                    {
                        // No es JSON válido — asumir que son bytes WAV directos
                    }
                }

                await File.WriteAllBytesAsync(outputPath, data, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "El API de VoiceStudio tardó demasiado en responder (>5 min). " +
                    "El modelo puede estar cargándose por primera vez. Inténtalo nuevamente en unos segundos.");
            }
        }

        // ─── Parsing de perfiles ─────────────────────────────────────────────────

        /// <summary>
        /// Parsea la respuesta JSON de /api/profiles y devuelve (lista de nombres, mapa nombre→id).
        /// Soporta: array de strings, array de {id, name, ...}, objeto con propiedad "profiles"/"voices".
        /// </summary>
        private static (List<string> names, Dictionary<string, string> map) ParseProfilesFromJson(string json)
        {
            var names = new List<string>();
            var map   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                    // Buscar propiedad que contenga el array
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
            catch
            {
                // JSON inválido → devolver vacío
            }

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
                        map[str] = str; // Sin ID separado → usar el mismo valor
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    // Extraer nombre e ID del objeto perfil
                    string? id   = null;
                    string? name = null;

                    if (item.TryGetProperty("id",      out var idProp)   && idProp.ValueKind   == JsonValueKind.String)
                        id   = idProp.GetString();
                    if (item.TryGetProperty("name",    out var nameProp)  && nameProp.ValueKind  == JsonValueKind.String)
                        name = nameProp.GetString();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        if (item.TryGetProperty("profile", out var prProp) && prProp.ValueKind == JsonValueKind.String)
                            name = prProp.GetString();
                        else if (item.TryGetProperty("display_name", out var dnProp) && dnProp.ValueKind == JsonValueKind.String)
                            name = dnProp.GetString();
                    }

                    // Usar nombre como clave visible; ID como valor para la API
                    string displayName = name ?? id ?? string.Empty;
                    string resolvedId  = id   ?? name ?? string.Empty;

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
            // WAV comienza con "RIFF"; JSON comienza con '{' o '['
            char first = (char)bytes[0];
            return first == '{' || first == '[';
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
