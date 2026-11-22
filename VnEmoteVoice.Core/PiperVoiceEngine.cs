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

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Motor de síntesis de voz (TTS) local basado en la tecnología Piper y modelos neuronales ONNX.
    /// Permite cargar modelos externos compuestos por dos archivos:
    ///   1. Un archivo binario de red neuronal (.onnx).
    ///   2. Un archivo de configuración de texto (.json) con el mapa de fonemas y parámetros de audio.
    /// Piper convierte el texto a fonemas (sonidos elementales) y la red neuronal genera el audio.
    /// </summary>
    public sealed class PiperVoiceEngine : IDisposable
    {
        // ─── Identificadores para la interfaz de usuario ─────────────────────
        // Texto que se muestra en el selector de voces cuando se activa Piper externo
        public const string ExternalVoiceDisplayName = "Pipper (Externo)";
        // Clave interna para identificar la voz externa en memoria
        public const string ExternalVoiceId = "__external__";

        // ─── Parámetros de procesamiento de audio ─────────────────────────────
        // Silencio en segundos insertado entre frases para que la locución suene natural
        private const float PauseBetweenSegments = 0.10f;

        // Límite de fonemas por cada segmento enviado al modelo de red neuronal
        private const int MaxPhonemes = 400;

        // ──────────────────────────────────────────────────────────────────────
        //  Estructura de datos para la voz
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Representa la información esencial de una voz Piper cargada:
        /// rutas a los archivos, idioma y código fonético para eSpeak.
        /// </summary>
        public sealed record PiperVoiceInfo(
            string Id,
            string Language,
            string OnnxFile,
            string ConfigFile,
            string EspeakVoice)
        {
            public string DisplayName => ExternalVoiceDisplayName;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Variables internas y sincronización
        // ──────────────────────────────────────────────────────────────────────

        // Bloqueo para evitar que dos subprocesos accedan o carguen modelos al mismo tiempo
        private readonly object _lock = new();

        // Sesión activa del modelo ONNX (el motor de inteligencia artificial que genera el sonido)
        private InferenceSession? _session;

        // Parámetros de configuración extraídos del archivo .json (tasa de muestreo, fonemas, etc.)
        private PiperConfig? _config;

        // Diccionario de conversores de texto a fonemas (eSpeak G2P) organizados por código de idioma
        private readonly Dictionary<string, EspeakG2P> _espeak = new(StringComparer.OrdinalIgnoreCase);

        // Información de la voz externa actualmente cargada en memoria
        private PiperVoiceInfo? _externalVoice;

        // ──────────────────────────────────────────────────────────────────────
        //  Propiedades públicas y comprobaciones
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Indica si hay un modelo Piper externo cargado y listo para sintetizar voz.
        /// </summary>
        public bool IsAvailable => _externalVoice != null;

        /// <summary>
        /// Indica si ya se cargó un modelo Piper externo.
        /// </summary>
        public bool HasExternalVoice => _externalVoice != null;

        /// <summary>
        /// Comprueba si el nombre seleccionado por el usuario corresponde a la voz Piper externa.
        /// </summary>
        public static bool IsExternalVoice(string voiceName) =>
            string.Equals(voiceName, ExternalVoiceDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(voiceName, "Piper (Externo)", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Devuelve la información de la voz externa si el nombre coincide; de lo contrario devuelve null.
        /// </summary>
        public PiperVoiceInfo? FindVoice(string voiceName)
        {
            if (string.IsNullOrEmpty(voiceName)) return null;

            if (IsExternalVoice(voiceName))
            {
                return _externalVoice;
            }

            return null;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Carga dinámica de modelos externos (.onnx + .json)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lee los archivos .onnx (modelo neuronal) y .json (configuración de fonemas y audio),
        /// cargando el motor en memoria para que pueda hablar inmediatamente.
        /// </summary>
        /// <param name="onnxPath">Ruta absoluta del archivo con extensión .onnx</param>
        /// <param name="jsonPath">Ruta absoluta del archivo con extensión .json</param>
        public void LoadExternalVoice(string onnxPath, string jsonPath)
        {
            if (!File.Exists(onnxPath))
                throw new FileNotFoundException($"Archivo de modelo ONNX no encontrado: {onnxPath}");
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"Archivo de configuración JSON no encontrado: {jsonPath}");

            // 1. Leer y analizar la configuración del modelo desde el archivo JSON
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            var config = PiperConfig.Parse(json);

            lock (_lock)
            {
                // Si había una sesión ONNX abierta anteriormente, la liberamos de memoria RAM
                _session?.Dispose();

                // 2. Crear una nueva sesión de inferencia de ONNX Runtime con el archivo .onnx
                _session = new InferenceSession(onnxPath);
                _config = config;

                // 3. Registrar la información de la voz
                _externalVoice = new PiperVoiceInfo(
                    Id: ExternalVoiceId,
                    Language: config.Language,
                    OnnxFile: onnxPath,
                    ConfigFile: jsonPath,
                    EspeakVoice: config.EspeakVoice
                );
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Síntesis de voz (Generación de audio a partir de texto)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Convierte el texto en audio y lo guarda en un archivo WAV de forma asíncrona (en segundo plano).
        /// </summary>
        /// <param name="text">Texto a pronunciar.</param>
        /// <param name="outputPath">Ruta donde se guardará el archivo de audio (.wav).</param>
        /// <param name="voice">Información de la voz Piper a utilizar.</param>
        /// <param name="speed">Velocidad del habla (0.5 = lento, 1.0 = normal, 2.0 = rápido).</param>
        /// <param name="volume">Volumen del audio (0.0 a 1.0).</param>
        public Task SynthesizeToWavAsync(
            string text,
            string outputPath,
            PiperVoiceInfo voice,
            float speed,
            float volume)
            => Task.Run(() => SynthesizeToWav(text, outputPath, voice, speed, volume));

        /// <summary>
        /// Lógica de síntesis paso a paso:
        /// 1. Convierte el texto en fonemas y números (IDs).
        /// 2. Ejecuta el modelo neuronal para generar las ondas sonoras.
        /// 3. Escribe las ondas en un archivo de formato WAV.
        /// </summary>
        private void SynthesizeToWav(
            string text,
            string outputPath,
            PiperVoiceInfo voice,
            float speed,
            float volume)
        {
            InferenceSession session;
            PiperConfig config;

            lock (_lock)
            {
                session = _session ?? throw new InvalidOperationException("No se ha inicializado el modelo ONNX de Piper.");
                config = _config ?? throw new InvalidOperationException("No se ha cargado la configuración JSON de Piper.");
            }

            // Asegurar que la velocidad y el volumen estén dentro de límites seguros
            speed = Math.Clamp(speed, 0.5f, 2.0f);
            volume = Math.Clamp(volume, 0.0f, 1.0f);

            var samples = new List<float>();

            // Dividir el texto en fonemas procesables y enviarlos al modelo
            foreach (var segment in Tokenize(text, voice.EspeakVoice, config))
            {
                // Inferencia: la red neuronal calcula las muestras de sonido para este segmento
                var result = Infer(session, config, segment, speed);
                samples.AddRange(result);

                // Insertar un breve silencio entre frases para mayor naturalidad
                samples.AddRange(new float[(int)(config.SampleRate * PauseBetweenSegments)]);
            }

            // Guardar todas las muestras calculadas en un archivo de audio WAV de 16 bits y 1 canal (mono)
            using var writer = new WaveFileWriter(outputPath, new WaveFormat(config.SampleRate, 16, 1));
            foreach (var s in samples)
            {
                // Multiplicar por el volumen seleccionado y limitar entre -1.0 y 1.0 para evitar distorsión
                writer.WriteSample(Math.Clamp(s * volume, -1f, 1f));
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Inferencia en red neuronal (ONNX Runtime)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Pasa la secuencia de fonemas a la red neuronal ONNX y devuelve las muestras de audio generadas.
        /// </summary>
        private static float[] Infer(
            InferenceSession session,
            PiperConfig config,
            long[] phonemeIds,
            float speed)
        {
            int length = phonemeIds.Length;

            // 1. Tensor de entrada con la secuencia de IDs de fonemas: dimensión [1, longitud]
            var inputTensor = new DenseTensor<long>(new[] { 1, length });
            for (int i = 0; i < length; i++)
                inputTensor[0, i] = phonemeIds[i];

            // 2. Tensor con la cantidad de fonemas en la secuencia
            var lengthsTensor = new DenseTensor<long>(new[] { 1 });
            lengthsTensor[0] = length;

            // 3. Tensor de escalas que controlan la velocidad y la entonación:
            //    - noise_scale: variabilidad del tono/expresividad.
            //    - length_scale: duración de los fonemas (inversamente proporcional a la velocidad).
            //    - noise_w: variabilidad de la duración de cada sonido.
            float lengthScale = config.LengthScale / speed;
            var scalesTensor = new DenseTensor<float>(new[] { 3 });
            scalesTensor[0] = config.NoiseScale;
            scalesTensor[1] = lengthScale;
            scalesTensor[2] = config.NoiseW;

            // Ensamblar los parámetros de entrada requeridos por el modelo Piper VITS
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input",          inputTensor),
                NamedOnnxValue.CreateFromTensor("input_lengths",  lengthsTensor),
                NamedOnnxValue.CreateFromTensor("scales",         scalesTensor),
            };

            // Ejecutar la inferencia en el modelo de forma sincronizada
            lock (session)
            {
                using var results = session.Run(inputs);
                return results[0].AsTensor<float>().ToArray();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Conversión de texto a fonemas (G2P: Grapheme-to-Phoneme)
        // ──────────────────────────────────────────────────────────────────────

        // Expresión regular para separar texto por signos de puntuación (. ! ? ; : salto de línea)
        private static readonly Regex SentenceSplit =
            new(@"(?<=[.!?;:\n])\s*", RegexOptions.Compiled);

        /// <summary>
        /// Convierte una cadena de texto en listas de identificadores numéricos de fonemas,
        /// dividiéndola por oraciones y limitando el tamaño de cada segmento.
        /// </summary>
        private IEnumerable<long[]> Tokenize(
            string text,
            string espeakVoice,
            PiperConfig config)
        {
            text = NormalizeText(text);

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Convertir la oración escrita en caracteres fonéticos (alfabeto fonético internacional)
                string phonemes = Phonemize(line.Trim(), espeakVoice);
                if (string.IsNullOrEmpty(phonemes)) continue;

                // Si la oración es muy larga, dividirla en trozos más pequeños
                foreach (var segment in SplitIntoSegments(phonemes))
                {
                    // Traducir los caracteres fonéticos a números mediante el mapa de la voz
                    var ids = PhonemeIdsFromString(segment, config.PhonemeIdMap);
                    if (ids.Length > 0)
                        yield return ids;
                }
            }
        }

        /// <summary>
        /// Asocia cada carácter fonético con su identificador numérico correspondiente
        /// según el diccionario provisto en el archivo .json del modelo.
        /// </summary>
        private static long[] PhonemeIdsFromString(
            string phonemes,
            IReadOnlyDictionary<string, long[]> phonemeIdMap)
        {
            var ids = new List<long>();

            // Símbolo especial de inicio de frase (^)
            if (phonemeIdMap.TryGetValue("^", out var start))
                ids.AddRange(start);

            foreach (char ch in phonemes)
            {
                string key = ch.ToString();
                if (phonemeIdMap.TryGetValue(key, out var mapped))
                {
                    ids.AddRange(mapped);

                    // Separador entre fonemas (espacio)
                    if (phonemeIdMap.TryGetValue(" ", out var sep))
                        ids.AddRange(sep);
                }
            }

            // Símbolo especial de fin de frase ($)
            if (phonemeIdMap.TryGetValue("$", out var end))
                ids.AddRange(end);

            return ids.ToArray();
        }

        /// <summary>
        /// Utiliza la biblioteca eSpeak para traducir texto escrito al alfabeto fonético.
        /// Si una variante de idioma no está disponible, utiliza el idioma base (ej: "es" para español).
        /// </summary>
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
                        // Si la variante regional falla (ej: es-MX), intentar con el idioma base ("es")
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

        /// <summary>
        /// Divide cadenas de fonemas muy extensas en fragmentos de tamaño máximo para no sobrecargar el modelo.
        /// </summary>
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

        /// <summary>
        /// Limpia caracteres especiales o símbolos incompatibles antes de enviar el texto al fonetizador.
        /// </summary>
        private static string NormalizeText(string text)
        {
            text = text.Replace("\r\n", "\n")
                       .Replace('\u2018', '"')
                       .Replace('\u2019', '"');
            foreach (var c in "`[]{}~*#")
                text = text.Replace(c.ToString(), " ");
            return Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        }

        /// <summary>
        /// Extrae la parte principal de una etiqueta de idioma (ej: "es" a partir de "es-MX").
        /// </summary>
        private static string PrimaryTag(string lang)
        {
            int i = lang.IndexOf('-');
            return i >= 0 ? lang[..i] : lang;
        }

        /// <summary>
        /// Libera los recursos de red neuronal retenidos en memoria al cerrar la aplicación.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                _session?.Dispose();
                _session = null;
                _config = null;
                _externalVoice = null;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Estructura de configuración leída desde el archivo .json del modelo
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Almacena los metadatos y configuraciones técnicas definidos en el archivo JSON
    /// que acompaña al modelo ONNX de Piper (frecuencia de muestreo, fonemas, etc.).
    /// </summary>
    internal sealed class PiperConfig
    {
        // Frecuencia de muestreo de audio en Hertz (por defecto 22050 Hz)
        public int SampleRate { get; private set; } = 22050;

        // Factores de variabilidad y entonación acústica de la red neuronal
        public float NoiseScale { get; private set; } = 0.667f;
        public float LengthScale { get; private set; } = 1.0f;
        public float NoiseW { get; private set; } = 0.8f;

        // Identificador del idioma del modelo y la voz fonetizadora correspondiente
        public string Language { get; private set; } = "es-ES";
        public string EspeakVoice { get; private set; } = "es";

        // Mapeo entre símbolos fonéticos y sus números de identificación para la red neuronal
        public IReadOnlyDictionary<string, long[]> PhonemeIdMap { get; private set; }
            = new Dictionary<string, long[]>();

        private PiperConfig() { }

        /// <summary>
        /// Lee e interpreta el texto JSON del modelo Piper para extraer todos los parámetros necesarios.
        /// </summary>
        public static PiperConfig Parse(string json)
        {
            var cfg = new PiperConfig();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 1. Frecuencia de muestreo (audio.sample_rate)
            if (root.TryGetProperty("audio", out var audio) &&
                audio.TryGetProperty("sample_rate", out var sr))
                cfg.SampleRate = sr.GetInt32();

            // 2. Parámetros de inferencia (noise_scale, length_scale, noise_w)
            if (root.TryGetProperty("inference", out var inf))
            {
                if (inf.TryGetProperty("noise_scale", out var ns)) cfg.NoiseScale = ns.GetSingle();
                if (inf.TryGetProperty("length_scale", out var ls)) cfg.LengthScale = ls.GetSingle();
                if (inf.TryGetProperty("noise_w", out var nw)) cfg.NoiseW = nw.GetSingle();
            }

            // 3. Voz de eSpeak configurada (espeak.voice)
            if (root.TryGetProperty("espeak", out var espeak) &&
                espeak.TryGetProperty("voice", out var ev) &&
                !string.IsNullOrWhiteSpace(ev.GetString()))
            {
                cfg.EspeakVoice = ev.GetString()!;
            }

            // 4. Idioma declarado (language.code o texto directo)
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

            // Si falta alguno de los datos de idioma, inferir de forma segura
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

            // 5. Tabla de correspondencia fonema -> IDs numéricos
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
