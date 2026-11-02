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
    /// Motor cliente para conectarse con una API REST externa de VoiceStudio / TTS.
    /// Permite descubrir perfiles/voces disponibles y sintetizar texto a archivo WAV.
    /// </summary>
    public sealed class VoiceStudioApiEngine : IDisposable
    {
        public const string ExternalVoiceDisplayName = "VoiceStudio Api (Externo)";

        private readonly HttpClient _httpClient;
        private string _baseUrl = "http://localhost:5000";
        private string _selectedProfile = string.Empty;

        public VoiceStudioApiEngine(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = (value ?? string.Empty).Trim().TrimEnd('/');
        }

        public string SelectedProfile
        {
            get => _selectedProfile;
            set => _selectedProfile = (value ?? string.Empty).Trim();
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_selectedProfile);

        public static bool IsVoiceStudioApiVoice(string? voiceName) =>
            !string.IsNullOrEmpty(voiceName) &&
            string.Equals(voiceName, ExternalVoiceDisplayName, StringComparison.OrdinalIgnoreCase);

        public void SetConfig(string baseUrl, string selectedProfile)
        {
            BaseUrl = baseUrl;
            SelectedProfile = selectedProfile;
        }

        /// <summary>
        /// Consulta la API de VoiceStudio para obtener los perfiles o voces disponibles.
        /// Intenta consultar rutas estándar como /api/profiles, /api/voices, /voices o /v1/models.
        /// </summary>
        public async Task<List<string>> FetchProfilesAsync(string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            string url = string.IsNullOrWhiteSpace(baseUrl) ? BaseUrl : baseUrl.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("La URL del API de VoiceStudio no puede estar vacía.");

            // Rutas habituales en servidores de VoiceStudio / TTS
            string[] probePaths = { "/api/profiles", "/api/voices", "/voices", "/v1/models", "/profiles" };
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
                    var profiles = ParseProfilesFromJson(json);
                    if (profiles.Count > 0)
                    {
                        BaseUrl = url;
                        return profiles;
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
        /// Sintetiza texto a audio WAV consultando el endpoint de síntesis de VoiceStudio.
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
                throw new InvalidOperationException("VoiceStudio API no está configurado. Asegúrate de ingresar el enlace y seleccionar un perfil.");

            string[] ttsPaths = { "/api/tts", "/tts", "/api/synthesize", "/v1/audio/speech" };
            Exception? lastEx = null;

            var payloadObj = new
            {
                text = text,
                input = text,
                voice = _selectedProfile,
                profile = _selectedProfile,
                speed = speed,
                pitch = pitch,
                volume = volume
            };
            string jsonBody = JsonSerializer.Serialize(payloadObj);

            foreach (var path in ttsPaths)
            {
                try
                {
                    string target = _baseUrl + path;
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

                    // Si la respuesta es JSON con base64 (ej. { "audio": "..." })
                    if (IsJson(data))
                    {
                        string jsonStr = Encoding.UTF8.GetString(data);
                        using var doc = JsonDocument.Parse(jsonStr);
                        if (doc.RootElement.TryGetProperty("audio", out var audioProp) ||
                            doc.RootElement.TryGetProperty("audio_base64", out audioProp) ||
                            doc.RootElement.TryGetProperty("data", out audioProp))
                        {
                            string? b64 = audioProp.GetString();
                            if (!string.IsNullOrEmpty(b64))
                            {
                                data = Convert.FromBase64String(b64);
                            }
                        }
                    }

                    await File.WriteAllBytesAsync(outputPath, data, cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (lastEx != null)
                throw new HttpRequestException($"Error al sintetizar audio en VoiceStudio API: {lastEx.Message}", lastEx);

            throw new HttpRequestException("No se pudo sintetizar audio. Ningún endpoint de TTS respondió satisfactoriamente.");
        }

        private static List<string> ParseProfilesFromJson(string json)
        {
            var result = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Caso 1: Arreglo directo de cadenas o de objetos
                if (root.ValueKind == JsonValueKind.Array)
                {
                    ExtractFromElements(root.EnumerateArray(), result);
                }
                // Caso 2: Objeto contenedor { "profiles": [...] }, { "voices": [...] } o { "data": [...] }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("profiles", out var p) && p.ValueKind == JsonValueKind.Array)
                        ExtractFromElements(p.EnumerateArray(), result);
                    else if (root.TryGetProperty("voices", out var v) && v.ValueKind == JsonValueKind.Array)
                        ExtractFromElements(v.EnumerateArray(), result);
                    else if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
                        ExtractFromElements(d.EnumerateArray(), result);
                }
            }
            catch
            {
            }

            return result;
        }

        private static void ExtractFromElements(JsonElement.ArrayEnumerator arrayEnum, List<string> output)
        {
            foreach (var item in arrayEnum)
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? str = item.GetString();
                    if (!string.IsNullOrWhiteSpace(str) && !output.Contains(str))
                        output.Add(str);
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    // Buscar campos id, name, o profile
                    string? name = null;
                    if (item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        name = n.GetString();
                    else if (item.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String)
                        name = i.GetString();
                    else if (item.TryGetProperty("profile", out var pr) && pr.ValueKind == JsonValueKind.String)
                        name = pr.GetString();

                    if (!string.IsNullOrWhiteSpace(name) && !output.Contains(name))
                        output.Add(name);
                }
            }
        }

        private static bool IsJson(byte[] bytes)
        {
            if (bytes.Length < 2) return false;
            // Un WAV comienza con "RIFF", un JSON comienza con '{' o '['
            char first = (char)bytes[0];
            return first == '{' || first == '[';
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}

