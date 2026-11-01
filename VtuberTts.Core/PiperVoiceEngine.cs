using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MisakiSharp;
using NAudio.Wave;

namespace VtuberTts.Core
{
    /// <summary>
    /// Motor TTS local basado en Piper (ONNX).
    /// Permite cargar modelos Piper externos (.onnx y .json) o integrados,
    /// exponiéndolos de forma transparente para su síntesis.
    /// </summary>
    public sealed class PiperVoiceEngine : IDisposable
    {
        // ─── Etiqueta visual y nombres de voz ───────────────────────────────
        public const string VoiceLabel = " (Piper)";
        public const string ExternalVoiceDisplayName = "Pipper (Externo)";
        public const string ExternalVoiceId = "__external__";

        // ─── Sub-carpeta dentro de __Models donde viven modelos Piper locales ──
        private const string PiperSubfolder = "claude";

        // ─── Separación silenciosa entre segmentos (segundos) ───────────────
        private const float PauseBetweenSegments = 0.10f;

        // ─── Número máximo de fonemas por inferencia ─────────────────────────
        private const int MaxPhonemes = 400;

        // ──────────────────────────────────────────────────────────────────────
        //  Catálogo de voces Piper
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Información de una voz Piper: id visual, idioma BCP-47,
        /// nombre o ruta del archivo ONNX, JSON y código eSpeak.
        /// </summary>
        public sealed record PiperVoiceInfo(
            string Id,
            string Language,
            string OnnxFile,
            string ConfigFile,
            string EspeakVoice)
        {
            public string DisplayName =>
                Id == ExternalVoiceId ? ExternalVoiceDisplayName : Id + VoiceLabel;
        }

        /// <summary>
        /// Voces Piper publicadas en el catálogo.
        /// Agregar nuevas voces aquí para que el <see cref="VoiceManager"/>
        /// las descubra automáticamente.
        /// </summary>
        public static readonly IReadOnlyList<PiperVoiceInfo> Catalog = new[]
        {
            new PiperVoiceInfo(
                Id:           "claude",
                Language:     "es-MX",
                OnnxFile:     "es_MX-claude-high.onnx",
                ConfigFile:   "es_es_MX_claude_high_es_MX-claude-high.onnx.json",
                EspeakVoice:  "es"),          // eSpeak lang tag para español
        };

        // ──────────────────────────────────────────────────────────────────────
        //  Estado interno por sesión
        // ──────────────────────────────────────────────────────────────────────

        private readonly object _lock = new();

        /// <summary>Sesiones ONNX indexadas por Id de voz.</summary>
        private readonly Dictionary<string, InferenceSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Configuraciones indexadas por Id de voz.</summary>
        private readonly Dictionary<string, PiperConfig> _configs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Instancias de eSpeak G2P indexadas por código de voz eSpeak.</summary>
        private readonly Dictionary<string, EspeakG2P> _espeak = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Voz externa cargada dinámicamente mediante archivos ONNX y JSON.</summary>
        private PiperVoiceInfo? _externalVoice;

        private string? _modelsDir;

        // ──────────────────────────────────────────────────────────────────────
        //  Propiedades públicas y verificación
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Indica si hay algún modelo disponible (en catálogo local o externo cargado).
        /// </summary>
        public bool IsAvailable => _externalVoice != null || Catalog.Any(v => IsVoiceAvailable(v));

        /// <summary>
        /// Indica si ya se cargó un modelo Piper externo.
        /// </summary>
        public bool HasExternalVoice => _externalVoice != null;

        /// <summary>
        /// Determina si el nombre corresponde a la opción de Piper externo.
        /// </summary>
        public static bool IsExternalVoice(string voiceName) =>
            string.Equals(voiceName, ExternalVoiceDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(voiceName, "Piper (Externo)", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Determina si una voz debe ser procesada por este motor.
        /// </summary>
        public static bool IsPiperVoice(string voiceNameWithLabel) =>
            !string.IsNullOrEmpty(voiceNameWithLabel) &&
            (voiceNameWithLabel.EndsWith(VoiceLabel, StringComparison.OrdinalIgnoreCase) ||
             IsExternalVoice(voiceNameWithLabel));

        /// <summary>
        /// Busca la información de voz correspondiente al nombre proporcionado.
        /// </summary>
        public PiperVoiceInfo? FindVoice(string voiceNameWithLabel)
        {
            if (string.IsNullOrEmpty(voiceNameWithLabel)) return null;

            if (IsExternalVoice(voiceNameWithLabel))
            {
                return _externalVoice;
            }

            if (!voiceNameWithLabel.EndsWith(VoiceLabel, StringComparison.OrdinalIgnoreCase))
                return null;

            string id = voiceNameWithLabel[..^VoiceLabel.Length].Trim();
            return Catalog.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Carga un modelo Piper externo a partir de sus rutas de archivo .onnx y .json.
        /// Lee la configuración de Language y EspeakVoice directamente del JSON.
        /// </summary>
        public void LoadExternalVoice(string onnxPath, string jsonPath)
        {
            if (!File.Exists(onnxPath))
                throw new FileNotFoundException($"Archivo de modelo ONNX no encontrado: {onnxPath}");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Archivo de configuración JSON no encontrado: {jsonPath}");

            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            var config = PiperConfig.Parse(json);

            lock (_lock)
            {
                if (_sessions.TryGetValue(ExternalVoiceId, out var oldSession))
                {
                    oldSession.Dispose();
                    _sessions.Remove(ExternalVoiceId);
                }

                var session = new InferenceSession(onnxPath);
                _sessions[ExternalVoiceId] = session;
                _configs[ExternalVoiceId] = config;

                _externalVoice = new PiperVoiceInfo(
                    Id: ExternalVoiceId, // Ejemplo: "claude"
                    Language: config.Language, // Ejemplo: "es-MX"
                    OnnxFile: onnxPath, // Ejemplo: "es_MX-claude-high.onnx"
                    ConfigFile: jsonPath, // Ejemplo: "es_es_MX_claude_high_es_MX-claude-high.onnx.json"
                    EspeakVoice: config.EspeakVoice // Ejemplo: "es"
                );
            }
        }

        /// <summary>Coincide por idioma exacto o por sub-etiqueta primaria (es-MX coincide con es-ES).</summary>
        public static bool MatchesLanguage(PiperVoiceInfo voice, string language)
        {
            if (string.IsNullOrEmpty(language)) return true;
            return PrimaryTag(voice.Language).Equals(PrimaryTag(language), StringComparison.OrdinalIgnoreCase);
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Punto de entrada: síntesis
        // ──────────────────────────────────────────────────────────────────────

        /// <param name="speed">
        ///   Factor de velocidad: 0.5 (lento) – 2.0 (rápido). 1.0 = normal.
        ///   Internamente se aplica como <c>length_scale = base / speed</c>.
        /// </param>
        /// <param name="volume">0.0 – 1.0, multiplicador lineal de amplitud.</param>
        public Task SynthesizeToWavAsync(
            string text,
            string outputPath,
            PiperVoiceInfo voice,
            float speed,
            float volume)
            => Task.Run(() => SynthesizeToWav(text, outputPath, voice, speed, volume));

        private void SynthesizeToWav(
            string text,
            string outputPath,
            PiperVoiceInfo voice,
            float speed,
            float volume)
        {
            var session = GetSession(voice);
            var config = GetConfig(voice);

            speed = Math.Clamp(speed, 0.5f, 2.0f);
            volume = Math.Clamp(volume, 0.0f, 1.0f);

            var samples = new List<float>();

            foreach (var segment in Tokenize(text, voice.EspeakVoice, config))
            {
                var result = Infer(session, config, segment, speed);
                samples.AddRange(result);
                // Pequeña pausa entre segmentos
                samples.AddRange(new float[(int)(config.SampleRate * PauseBetweenSegments)]);
            }

            // Escribir WAV final
            using var writer = new WaveFileWriter(outputPath, new WaveFormat(config.SampleRate, 16, 1));
            foreach (var s in samples)
                writer.WriteSample(Math.Clamp(s * volume, -1f, 1f));
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Inferencia ONNX (arquitectura Piper / VITS)
        // ──────────────────────────────────────────────────────────────────────

        private static float[] Infer(
            InferenceSession session,
            PiperConfig config,
            long[] phonemeIds,
            float speed)
        {
            int length = phonemeIds.Length;

            // input: [1, length]
            var inputTensor = new DenseTensor<long>(new[] { 1, length });
            for (int i = 0; i < length; i++)
                inputTensor[0, i] = phonemeIds[i];

            // input_lengths: [1]
            var lengthsTensor = new DenseTensor<long>(new[] { 1 });
            lengthsTensor[0] = length;

            // scales: [3] — noise_scale, length_scale (ajustado por speed), noise_w
            float lengthScale = config.LengthScale / speed;
            var scalesTensor = new DenseTensor<float>(new[] { 3 });
            scalesTensor[0] = config.NoiseScale;
            scalesTensor[1] = lengthScale;
            scalesTensor[2] = config.NoiseW;

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input",          inputTensor),
                NamedOnnxValue.CreateFromTensor("input_lengths",  lengthsTensor),
                NamedOnnxValue.CreateFromTensor("scales",         scalesTensor),
            };

            lock (session)
            {
                using var results = session.Run(inputs);
                return results[0].AsTensor<float>().ToArray();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Texto → segmentos de IDs de fonemas
        // ──────────────────────────────────────────────────────────────────────

        private static readonly Regex SentenceSplit =
            new(@"(?<=[.!?;:\n])\s*", RegexOptions.Compiled);

        private IEnumerable<long[]> Tokenize(
            string text,
            string espeakVoice,
            PiperConfig config)
        {
            text = NormalizeText(text);

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string phonemes = Phonemize(line.Trim(), espeakVoice);
                if (string.IsNullOrEmpty(phonemes)) continue;

                foreach (var segment in SplitIntoSegments(phonemes))
                {
                    var ids = PhonemeIdsFromString(segment, config.PhonemeIdMap);
                    if (ids.Length > 0)
                        yield return ids;
                }
            }
        }

        private static long[] PhonemeIdsFromString(
            string phonemes,
            IReadOnlyDictionary<string, long[]> phonemeIdMap)
        {
            var ids = new List<long>();

            if (phonemeIdMap.TryGetValue("^", out var start))
                ids.AddRange(start);

            foreach (char ch in phonemes)
            {
                string key = ch.ToString();
                if (phonemeIdMap.TryGetValue(key, out var mapped))
                {
                    ids.AddRange(mapped);

                    if (phonemeIdMap.TryGetValue(" ", out var sep))
                        ids.AddRange(sep);
                }
            }

            if (phonemeIdMap.TryGetValue("$", out var end))
                ids.AddRange(end);

            return ids.ToArray();
        }

        private string Phonemize(string text, string espeakVoice)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            lock (_lock)
            {
                if (!_espeak.TryGetValue(espeakVoice, out var g2p))
                {
                    try
                    {
                        g2p = new EspeakG2P(EspeakReplay.Provider(espeakVoice));
                    }
                    catch
                    {
                        // Fallback al tag primario si la variante regional no está soportada directamente
                        string primary = PrimaryTag(espeakVoice);
                        if (!string.Equals(primary, espeakVoice, StringComparison.OrdinalIgnoreCase))
                        {
                            try { g2p = new EspeakG2P(EspeakReplay.Provider(primary)); }
                            catch { g2p = new EspeakG2P(EspeakReplay.Provider("es")); }
                        }
                        else
                        {
                            g2p = new EspeakG2P(EspeakReplay.Provider("es"));
                        }
                    }
                    _espeak[espeakVoice] = g2p;
                }
                return g2p.Phonemize(text);
            }
        }

        private static IEnumerable<string> SplitIntoSegments(string phonemes)
        {
            var sb = new StringBuilder();
            foreach (var sentence in SentenceSplit.Split(phonemes))
            {
                if (sentence.Length == 0) continue;

                if (sb.Length + sentence.Length > MaxPhonemes && sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }

                string remaining = sentence;
                while (remaining.Length > MaxPhonemes)
                {
                    int cut = remaining.LastIndexOf(' ', MaxPhonemes - 1);
                    if (cut <= 0) cut = MaxPhonemes;
                    sb.Append(remaining[..cut]);
                    yield return sb.ToString();
                    sb.Clear();
                    remaining = remaining[cut..];
                }

                sb.Append(remaining);
            }

            if (sb.Length > 0) yield return sb.ToString();
        }

        private static string NormalizeText(string text)
        {
            text = text.Replace("\r\n", "\n")
                       .Replace('\u2018', '"')
                       .Replace('\u2019', '"');
            foreach (var c in "`[]{}~*#")
                text = text.Replace(c.ToString(), " ");
            return Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Carga de modelos y configuración
        // ──────────────────────────────────────────────────────────────────────

        private bool IsVoiceAvailable(PiperVoiceInfo voice)
        {
            if (File.Exists(voice.OnnxFile) && File.Exists(voice.ConfigFile))
                return true;

            string? dir = FindModelsDir();
            if (dir == null) return false;
            return File.Exists(Path.Combine(dir, PiperSubfolder, voice.OnnxFile)) &&
                   File.Exists(Path.Combine(dir, PiperSubfolder, voice.ConfigFile));
        }

        private string? FindModelsDir()
        {
            if (_modelsDir != null) return _modelsDir;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "__Models");
                if (Directory.Exists(candidate))
                    return _modelsDir = candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private InferenceSession GetSession(PiperVoiceInfo voice)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(voice.Id, out var cached)) return cached;

                if (File.Exists(voice.OnnxFile))
                {
                    var session = new InferenceSession(voice.OnnxFile);
                    _sessions[voice.Id] = session;
                    return session;
                }

                string dir = FindModelsDir()
                    ?? throw new FileNotFoundException(
                        $"No se encontró la carpeta __Models para cargar el modelo Piper '{voice.Id}'.");

                string onnxPath = Path.Combine(dir, PiperSubfolder, voice.OnnxFile);
                if (!File.Exists(onnxPath))
                    throw new FileNotFoundException($"Modelo Piper no encontrado: {onnxPath}");

                var s = new InferenceSession(onnxPath);
                _sessions[voice.Id] = s;
                return s;
            }
        }

        private PiperConfig GetConfig(PiperVoiceInfo voice)
        {
            lock (_lock)
            {
                if (_configs.TryGetValue(voice.Id, out var cached)) return cached;

                if (File.Exists(voice.ConfigFile))
                {
                    string jsonContent = File.ReadAllText(voice.ConfigFile, Encoding.UTF8);
                    var parsed = PiperConfig.Parse(jsonContent);
                    _configs[voice.Id] = parsed;
                    return parsed;
                }

                string dir = FindModelsDir()
                    ?? throw new FileNotFoundException(
                        $"No se encontró la carpeta __Models para cargar la configuración Piper '{voice.Id}'.");

                string jsonPath = Path.Combine(dir, PiperSubfolder, voice.ConfigFile);
                if (!File.Exists(jsonPath))
                    throw new FileNotFoundException($"Configuración Piper no encontrada: {jsonPath}");

                string json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var config = PiperConfig.Parse(json);
                _configs[voice.Id] = config;
                return config;
            }
        }

        private static string PrimaryTag(string lang)
        {
            int i = lang.IndexOf('-');
            return i >= 0 ? lang[..i] : lang;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var s in _sessions.Values) s.Dispose();
                _sessions.Clear();
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Configuración Piper leída del archivo .onnx.json
    // ══════════════════════════════════════════════════════════════════════════

    internal sealed class PiperConfig
    {
        public int SampleRate { get; private set; } = 22050;

        public float NoiseScale { get; private set; } = 0.667f;
        public float LengthScale { get; private set; } = 1.0f;
        public float NoiseW { get; private set; } = 0.8f;

        public string Language { get; private set; } = "es-ES";
        public string EspeakVoice { get; private set; } = "es";

        public IReadOnlyDictionary<string, long[]> PhonemeIdMap { get; private set; }
            = new Dictionary<string, long[]>();

        private PiperConfig() { }

        public static PiperConfig Parse(string json)
        {
            var cfg = new PiperConfig();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // audio.sample_rate
            if (root.TryGetProperty("audio", out var audio) &&
                audio.TryGetProperty("sample_rate", out var sr))
                cfg.SampleRate = sr.GetInt32();

            // inference: noise_scale, length_scale, noise_w
            if (root.TryGetProperty("inference", out var inf))
            {
                if (inf.TryGetProperty("noise_scale", out var ns)) cfg.NoiseScale = ns.GetSingle();
                if (inf.TryGetProperty("length_scale", out var ls)) cfg.LengthScale = ls.GetSingle();
                if (inf.TryGetProperty("noise_w", out var nw)) cfg.NoiseW = nw.GetSingle();
            }

            // espeak.voice
            if (root.TryGetProperty("espeak", out var espeak) &&
                espeak.TryGetProperty("voice", out var ev) &&
                !string.IsNullOrWhiteSpace(ev.GetString()))
            {
                cfg.EspeakVoice = ev.GetString()!;
            }

            // language: { "code": "es_MX" } o "es_MX"
            if (root.TryGetProperty("language", out var langProp))
            {
                if (langProp.ValueKind == JsonValueKind.Object &&
                    langProp.TryGetProperty("code", out var code) &&
                    !string.IsNullOrWhiteSpace(code.GetString()))
                {
                    cfg.Language = code.GetString()!.Replace('_', '-');
                }
                else if (langProp.ValueKind == JsonValueKind.String &&
                         !string.IsNullOrWhiteSpace(langProp.GetString()))
                {
                    cfg.Language = langProp.GetString()!.Replace('_', '-');
                }
            }

            // Fallback de lenguaje / espeak si uno de los dos no viene explícito
            if (string.IsNullOrWhiteSpace(cfg.Language) && !string.IsNullOrWhiteSpace(cfg.EspeakVoice))
            {
                cfg.Language = cfg.EspeakVoice.Replace('_', '-');
            }
            if (string.IsNullOrWhiteSpace(cfg.EspeakVoice))
            {
                int dash = cfg.Language.IndexOf('-');
                cfg.EspeakVoice = dash > 0 ? cfg.Language[..dash] : cfg.Language;
            }
            if (string.IsNullOrWhiteSpace(cfg.EspeakVoice))
            {
                cfg.EspeakVoice = "es";
            }

            // phoneme_id_map
            if (root.TryGetProperty("phoneme_id_map", out var pim))
            {
                var map = new Dictionary<string, long[]>(StringComparer.Ordinal);
                foreach (var prop in pim.EnumerateObject())
                {
                    var ids = prop.Value.EnumerateArray()
                                  .Select(e => e.GetInt64())
                                  .ToArray();
                    map[prop.Name] = ids;
                }
                cfg.PhonemeIdMap = map;
            }

            return cfg;
        }
    }
}
