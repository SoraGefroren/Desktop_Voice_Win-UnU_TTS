using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MisakiSharp;
using NAudio.Wave;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Motor de síntesis de voz (TTS) local basado en la tecnología Kokoro v1.0 y ONNX Runtime.
    /// Kokoro es una red neuronal de alta calidad que produce audio a 24,000 Hz.
    /// Utiliza un archivo de modelo central ('kokoro-v1.0.onnx') y un paquete comprimido
    /// con los timbres o estilos de voz ('voices-v1.0.bin').
    /// </summary>
    public sealed class KokoroVoiceEngine : IDisposable
    {
        // ─── Constantes del motor ─────────────────────────────────────────────
        // Etiqueta añadida a cada voz para distinguirla en la lista desplegable de la interfaz
        public const string VoiceLabel = " (Kokoro)";

        // Frecuencia de muestreo estándar de Kokoro: 24 kHz (24,000 muestras de audio por segundo)
        public const int SampleRate = 24000;

        // Longitud máxima de tokens procesables en una sola pasada de red neuronal
        private const int MaxTokens = 510;

        // Tamaño recomendado para trocear frases largas (300 tokens)
        private const int SegmentTokens = 300;

        // Nombres de los archivos físicos necesarios en la carpeta __Models
        private const string ModelFile = "kokoro-v1.0.onnx";
        private const string VoicesFile = "voices-v1.0.bin";

        // ──────────────────────────────────────────────────────────────────────
        //  Estructura de datos para las voces de Kokoro
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Representa una voz específica dentro del catálogo de Kokoro.
        /// </summary>
        /// <param name="Id">Identificador técnico del estilo (ejemplo: 'ef_dora').</param>
        /// <param name="Language">Código internacional del idioma (ejemplo: 'es-ES', 'en-US').</param>
        /// <param name="G2PCode">Código del convertidor de texto a fonemas correspondiente.</param>
        /// <param name="DisplayName">Nombre legible formateado para la lista visual.</param>
        public sealed record KokoroVoiceInfo(string Id, string Language, string G2PCode, string DisplayName)
        {
            /// <summary>
            /// Genera el nombre visual con formato "(Prefijo) Nombre (Kokoro)".
            /// Por ejemplo, para 'ef_dora' genera '(EF) Dora (Kokoro)'.
            /// </summary>
            public static KokoroVoiceInfo Create(string id, string language, string g2pCode)
            {
                int underscore = id.IndexOf('_');
                string displayName;
                if (underscore > 0)
                {
                    string clave = id[..underscore].ToUpperInvariant();
                    string rawName = id[(underscore + 1)..];
                    string name = rawName.Length > 0
                        ? char.ToUpperInvariant(rawName[0]) + rawName[1..]
                        : rawName;
                    displayName = $"({clave}) {name}{VoiceLabel}";
                }
                else
                {
                    displayName = $"{id}{VoiceLabel}";
                }

                return new KokoroVoiceInfo(id, language, g2pCode, displayName);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Catálogo predefinido de voces Kokoro v1.0
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lista completa de todas las voces disponibles en Kokoro v1.0 ordenadas por idioma y acento.
        /// </summary>
        public static readonly IReadOnlyList<KokoroVoiceInfo> Catalog = new[]
        {
            // 🇺🇸 Inglés Americano (en-US)
            KokoroVoiceInfo.Create("af_heart",     "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_alloy",     "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_aoede",     "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_bella",     "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_jessica",   "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_kore",      "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_nicole",    "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_nova",      "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_river",     "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_sarah",     "en-US", "en-us"),
            KokoroVoiceInfo.Create("af_sky",       "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_adam",      "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_echo",      "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_eric",      "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_fenrir",    "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_liam",      "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_michael",   "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_onyx",      "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_puck",      "en-US", "en-us"),
            KokoroVoiceInfo.Create("am_santa",     "en-US", "en-us"),

            // 🇬🇧 Inglés Británico (en-GB)
            KokoroVoiceInfo.Create("bf_alice",     "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bf_emma",      "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bf_isabella",  "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bf_lily",      "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bm_daniel",    "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bm_fable",     "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bm_george",    "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bm_lewis",     "en-GB", "en-gb"),

            // 🇪🇸 Español (es-ES)
            KokoroVoiceInfo.Create("ef_dora",      "es-ES", "es"),
            KokoroVoiceInfo.Create("em_alex",      "es-ES", "es"),
            KokoroVoiceInfo.Create("em_santa",     "es-ES", "es"),

            // 🇫🇷 Francés (fr-FR)
            KokoroVoiceInfo.Create("ff_siwis",     "fr-FR", "fr-fr"),

            // 🇮🇳 Hindi (hi-IN)
            KokoroVoiceInfo.Create("hf_alpha",     "hi-IN", "hi"),
            KokoroVoiceInfo.Create("hf_beta",      "hi-IN", "hi"),
            KokoroVoiceInfo.Create("hm_omega",     "hi-IN", "hi"),
            KokoroVoiceInfo.Create("hm_psi",       "hi-IN", "hi"),

            // 🇮🇹 Italiano (it-IT)
            KokoroVoiceInfo.Create("if_sara",      "it-IT", "it"),
            KokoroVoiceInfo.Create("im_nicola",    "it-IT", "it"),

            // 🇯🇵 Japonés (ja-JP)
            KokoroVoiceInfo.Create("jf_alpha",      "ja-JP", "ja"),
            KokoroVoiceInfo.Create("jf_gongitsune", "ja-JP", "ja"),
            KokoroVoiceInfo.Create("jf_nezumi",     "ja-JP", "ja"),
            KokoroVoiceInfo.Create("jf_tebukuro",   "ja-JP", "ja"),
            KokoroVoiceInfo.Create("jm_kumo",       "ja-JP", "ja"),

            // 🇧🇷 Portugués de Brasil (pt-BR)
            KokoroVoiceInfo.Create("pf_dora",      "pt-BR", "pt-br"),
            KokoroVoiceInfo.Create("pm_alex",      "pt-BR", "pt-br"),
            KokoroVoiceInfo.Create("pm_santa",     "pt-BR", "pt-br"),

            // 🇨🇳 Chino Mandarín (zh-CN)
            KokoroVoiceInfo.Create("zf_xiaobei",   "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zf_xiaoni",    "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zf_xiaoxiao",  "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zf_xiaoyi",    "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zm_yunjian",   "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zm_yunxi",     "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zm_yunxia",    "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zm_yunyang",   "zh-CN", "cmn"),
        };

        // Diccionario que relaciona cada símbolo fonético con su ID numérico reconocido por Kokoro
        private static readonly Dictionary<char, int> Vocab = BuildVocab();

        // Expresión regular para separar el texto por signos ortográficos
        private static readonly Regex Punctuation = new(@"(?<=[;:,.!?…¿\n])\s*", RegexOptions.Compiled);

        // ──────────────────────────────────────────────────────────────────────
        //  Variables internas y caché de modelos
        // ──────────────────────────────────────────────────────────────────────

        private readonly object _lock = new();

        // Caché de vectores de estilo (el "timbre" de voz de cada personaje)
        private readonly Dictionary<string, float[]> _styles = new(StringComparer.OrdinalIgnoreCase);

        // Caché de conversores eSpeak para idiomas adicionales
        private readonly Dictionary<string, EspeakG2P> _espeak = new();

        // Motores especializados para inglés americano y británico
        private static EnglishG2P? _englishUs;
        private static EnglishG2P? _englishGb;

        // Sesión activa del modelo neuronal ONNX
        private InferenceSession? _session;

        // Ruta de la carpeta donde se encontraron los archivos del modelo
        private string? _modelsDir;

        // ──────────────────────────────────────────────────────────────────────
        //  Propiedades públicas y comprobaciones de disponibilidad
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Comprueba si los archivos 'kokoro-v1.0.onnx' y 'voices-v1.0.bin' están presentes en el sistema.
        /// Si están presentes, el motor está listo para funcionar; si faltan, las voces se ocultan.
        /// </summary>
        public bool IsAvailable => FindModelsDir() != null;

        /// <summary>
        /// Determina si un nombre de voz pertenece a Kokoro basándose en su sufijo "(Kokoro)".
        /// </summary>
        public static bool IsKokoroVoice(string voiceNameWithLabel) =>
            !string.IsNullOrEmpty(voiceNameWithLabel) &&
            voiceNameWithLabel.EndsWith(VoiceLabel, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Busca la información de una voz en el catálogo mediante su nombre visual o identificador técnico.
        /// </summary>
        public KokoroVoiceInfo? FindVoice(string voiceNameWithLabel)
        {
            if (string.IsNullOrWhiteSpace(voiceNameWithLabel)) return null;

            // 1. Búsqueda por nombre visual exacto (ej: "(EF) Dora (Kokoro)")
            var match = Catalog.FirstOrDefault(v => v.DisplayName.Equals(voiceNameWithLabel, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // 2. Búsqueda por identificador con sufijo (ej: "ef_dora (Kokoro)")
            if (IsKokoroVoice(voiceNameWithLabel))
            {
                string id = voiceNameWithLabel[..^VoiceLabel.Length].Trim();
                match = Catalog.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            // 3. Búsqueda por identificador directo (ej: "ef_dora")
            return Catalog.FirstOrDefault(v => v.Id.Equals(voiceNameWithLabel, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Comprueba si una voz coincide con el idioma seleccionado (ej: 'es-ES' coincide con 'es').
        /// </summary>
        public static bool MatchesLanguage(KokoroVoiceInfo voice, string language)
        {
            if (string.IsNullOrEmpty(language)) return true;
            if (voice.Language.Equals(language, StringComparison.OrdinalIgnoreCase)) return true;
            return PrimaryTag(voice.Language).Equals(PrimaryTag(language), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extrae el prefijo principal de un código de idioma (ej: "es" a partir de "es-MX").
        /// </summary>
        private static string PrimaryTag(string lang)
        {
            int i = lang.IndexOf('-');
            return i >= 0 ? lang[..i] : lang;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Síntesis de voz (Texto a Audio)
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Genera el archivo de audio (.wav) correspondiente al texto de forma asíncrona.
        /// </summary>
        /// <param name="text">Texto que dirá la voz.</param>
        /// <param name="outputPath">Ruta donde se escribirá el archivo WAV.</param>
        /// <param name="voice">Voz seleccionada de Kokoro.</param>
        /// <param name="speed">Velocidad de locución (0.5 a 2.0).</param>
        /// <param name="volume">Volumen general (0.0 a 1.0).</param>
        public Task SynthesizeToWavAsync(string text, string outputPath, KokoroVoiceInfo voice, float speed, float volume)
            => Task.Run(() => SynthesizeToWav(text, outputPath, voice, speed, volume));

        /// <summary>
        /// Proceso secuencial de síntesis:
        /// 1. Carga la red neuronal y el estilo acústico de la voz.
        /// 2. Convierte el texto en fonemas numéricos.
        /// 3. Ejecuta la inferencia de sonido.
        /// 4. Graba el resultado en formato WAV a 24,000 Hz.
        /// </summary>
        private void SynthesizeToWav(string text, string outputPath, KokoroVoiceInfo voice, float speed, float volume)
        {
            var session = GetSession();
            var style = GetStyle(voice.Id);
            speed = Math.Clamp(speed, 0.5f, 2.0f);
            volume = Math.Clamp(volume, 0f, 1f);

            var samples = new List<float>();

            // Procesar cada fragmento de texto
            foreach (var tokens in Tokenize(text, voice.G2PCode))
            {
                samples.AddRange(Infer(session, tokens, style, speed));

                // Breve silencio (120 milisegundos) entre oraciones para una pausa natural
                samples.AddRange(new float[(int)(SampleRate * 0.12f)]);
            }

            // Guardar en un archivo WAV con formato PCM a 24 kHz y 16 bits mono
            using var writer = new WaveFileWriter(outputPath, new WaveFormat(SampleRate, 16, 1));
            foreach (var s in samples)
            {
                writer.WriteSample(Math.Clamp(s * volume, -1f, 1f));
            }
        }

        /// <summary>
        /// Ejecuta el pase hacia adelante (inferencia) en el modelo ONNX de Kokoro.
        /// </summary>
        private static float[] Infer(InferenceSession session, int[] tokens, float[,,] styles, float speed)
        {
            int count = Math.Min(tokens.Length, MaxTokens);

            // Tensor de entrada con los tokens: [1, longitud + 2] (incluye padding al inicio y fin)
            var tokenTensor = new DenseTensor<long>(new[] { 1, count + 2 });
            for (int i = 0; i < count; i++) tokenTensor[0, i + 1] = tokens[i];

            // Tensor de estilo: matriz que define la identidad acústica del hablante
            int dim = styles.GetLength(2);
            var styleTensor = new DenseTensor<float>(new[] { 1, dim });
            for (int j = 0; j < dim; j++) styleTensor[0, j] = styles[count - 1, 0, j];

            // Parámetros de entrada para el modelo ONNX
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("tokens", tokenTensor),
                NamedOnnxValue.CreateFromTensor("style", styleTensor),
                NamedOnnxValue.CreateFromTensor("speed", new DenseTensor<float>(new[] { speed }, new[] { 1 })),
            };

            lock (session)
            {
                using var results = session.Run(inputs);
                return results[0].AsTensor<float>().ToArray();
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Conversión de texto a fonemas y tokens numéricos
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Normaliza el texto, lo fonetiza y traduce los fonemas a los números requeridos por la red.
        /// </summary>
        private IEnumerable<int[]> Tokenize(string text, string g2pCode)
        {
            foreach (var line in Normalize(text).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string phonemes = Phonemize(line.Trim(), g2pCode);
                foreach (var chunk in SplitPhonemes(phonemes))
                {
                    var tokens = chunk.Where(Vocab.ContainsKey).Select(c => Vocab[c]).ToArray();
                    if (tokens.Length > 0) yield return tokens;
                }
            }
        }

        /// <summary>
        /// Limpia caracteres ortográficos incompatibles y normaliza los espacios en blanco.
        /// </summary>
        private static string Normalize(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('‘', '"').Replace('’', '"').Replace("**", "*");
            foreach (var c in "-`()[]{}~*#") text = text.Replace(c.ToString(), " ");
            return Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        }

        /// <summary>
        /// Convierte el texto escrito en caracteres fonéticos según el idioma del hablante.
        /// </summary>
        private string Phonemize(string line, string g2pCode)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";
            lock (_lock)
            {
                // Chino Mandarín
                if (g2pCode == "cmn")
                {
                    ChineseG2P.EnglishPhonemizer ??= EnglishPhonemes;
                    return ChineseG2P.Phonemize(line);
                }

                // Inglés Americano
                if (g2pCode == "en-us" || g2pCode == "a")
                {
                    _englishUs ??= new EnglishG2P(EnglishG2P.DefaultTagger, british: false);
                    return new string(_englishUs.Phonemize(line).Phonemes.Where(Vocab.ContainsKey).ToArray());
                }

                // Inglés Británico
                if (g2pCode == "en-gb" || g2pCode == "b")
                {
                    _englishGb ??= new EnglishG2P(EnglishG2P.DefaultTagger, british: true);
                    return new string(_englishGb.Phonemize(line).Phonemes.Where(Vocab.ContainsKey).ToArray());
                }

                // Otros idiomas (Español, Francés, Japonés, etc.) procesados vía eSpeak
                if (!_espeak.TryGetValue(g2pCode, out var g2p))
                {
                    try
                    {
                        g2p = new EspeakG2P(EspeakReplay.Provider(g2pCode));
                    }
                    catch
                    {
                        string primary = PrimaryTag(g2pCode);
                        if (!string.Equals(primary, g2pCode, StringComparison.OrdinalIgnoreCase))
                        {
                            try { g2p = new EspeakG2P(EspeakReplay.Provider(primary)); }
                            catch { g2p = new EspeakG2P(EspeakReplay.Provider("es")); }
                        }
                        else
                        {
                            g2p = new EspeakG2P(EspeakReplay.Provider("es"));
                        }
                    }
                    _espeak[g2pCode] = g2p;
                }

                return g2p.Phonemize(line);
            }
        }

        private static string EnglishPhonemes(string text)
        {
            _englishUs ??= new EnglishG2P(EnglishG2P.DefaultTagger, british: false);
            return new string(_englishUs.Phonemize(text).Phonemes.Where(Vocab.ContainsKey).ToArray());
        }

        /// <summary>
        /// Divide los fonemas en oraciones de hasta SegmentTokens caracteres respetando la puntuación.
        /// </summary>
        private static IEnumerable<string> SplitPhonemes(string phonemes)
        {
            var sb = new StringBuilder();
            foreach (var sentence in Punctuation.Split(phonemes))
            {
                if (sentence.Length == 0) continue;
                if (sb.Length + sentence.Length > SegmentTokens && sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                foreach (var piece in HardSplit(sentence)) 
                {
                    if (sb.Length + piece.Length > SegmentTokens && sb.Length > 0)
                    {
                        yield return sb.ToString();
                        sb.Clear();
                    }
                    sb.Append(piece);
                }
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        private static IEnumerable<string> HardSplit(string s)
        {
            while (s.Length > SegmentTokens)
            {
                int cut = s.LastIndexOf(' ', SegmentTokens - 1);
                if (cut <= 0) cut = SegmentTokens;
                yield return s[..cut];
                s = s[cut..];
            }
            yield return s;
        }

        /// <summary>
        /// Construye el mapa numérico completo de fonemas que comprende la red neuronal de Kokoro.
        /// </summary>
        private static Dictionary<char, int> BuildVocab()
        {
            var vocab = new Dictionary<char, int>
            {
                ['$'] = 0, [';'] = 1, [':'] = 2, [','] = 3, ['.'] = 4, ['!'] = 5, ['?'] = 6, ['¡'] = 7, ['¿'] = 8, ['—'] = 9, ['…'] = 10, ['"'] = 11, ['('] = 12, [')'] = 13, ['“'] = 14, ['”'] = 15, [' '] = 16, ['\u0303'] = 17, ['ʣ'] = 18, ['ʥ'] = 19, ['ʦ'] = 20, ['ʨ'] = 21, ['ᵝ'] = 22, ['\uAB67'] = 23, ['A'] = 24, ['I'] = 25, ['O'] = 31, ['Q'] = 33, ['S'] = 35, ['T'] = 36, ['W'] = 39, ['Y'] = 41, ['ᵊ'] = 42, ['a'] = 43, ['b'] = 44, ['c'] = 45, ['d'] = 46, ['e'] = 47, ['f'] = 48, ['h'] = 50, ['i'] = 51, ['j'] = 52, ['k'] = 53, ['l'] = 54, ['m'] = 55, ['n'] = 56, ['o'] = 57, ['p'] = 58, ['q'] = 59, ['r'] = 60, ['s'] = 61, ['t'] = 62, ['u'] = 63, ['v'] = 64, ['w'] = 65, ['x'] = 66, ['y'] = 67, ['z'] = 68, ['ɑ'] = 69, ['ɐ'] = 70, ['ɒ'] = 71, ['æ'] = 72, ['β'] = 75, ['ɔ'] = 76, ['ɕ'] = 77, ['ç'] = 78, ['ɖ'] = 80, ['ð'] = 81, ['ʤ'] = 82, ['ə'] = 83, ['ɚ'] = 85, ['ɛ'] = 86, ['ɜ'] = 87, ['ɟ'] = 90, ['ɡ'] = 92, ['ɥ'] = 99, ['ɨ'] = 101, ['ɪ'] = 102, ['ʝ'] = 103, ['ɯ'] = 110, ['ɰ'] = 111, ['ŋ'] = 112, ['ɳ'] = 113, ['ɲ'] = 114, ['ɴ'] = 115, ['ø'] = 116, ['ɸ'] = 118, ['θ'] = 119, ['œ'] = 120, ['ɹ'] = 123, ['ɾ'] = 125, ['ɻ'] = 126, ['ʁ'] = 128, ['ɽ'] = 129, ['ʂ'] = 130, ['ʃ'] = 131, ['ʈ'] = 132, ['ʧ'] = 133, ['ʊ'] = 135, ['ʋ'] = 136, ['ʌ'] = 138, ['ɣ'] = 139, ['ɤ'] = 140, ['χ'] = 142, ['ʎ'] = 143, ['ʒ'] = 147, ['ʔ'] = 148, ['ˈ'] = 156, ['ˌ'] = 157, ['ː'] = 158, ['ʰ'] = 162, ['ʲ'] = 164, ['↓'] = 169, ['→'] = 171, ['↗'] = 172, ['↘'] = 173, ['ᵻ'] = 177,
            };

            var zh = new Dictionary<char, int>
            {
                ['/'] = 7, ['ㄓ'] = 23, ['ㄅ'] = 30, ['ㄆ'] = 32, ['R'] = 34, ['ㄇ'] = 37, ['ㄈ'] = 38, ['ㄉ'] = 40, ['ㄊ'] = 49, ['ㄋ'] = 73, ['ㄌ'] = 74, ['ㄍ'] = 79, ['ㄎ'] = 84, ['ㄦ'] = 85, ['ㄏ'] = 88, ['ㄐ'] = 89, ['ㄑ'] = 91, ['ㄒ'] = 93, ['ㄔ'] = 94, ['ㄕ'] = 95, ['ㄗ'] = 96, ['ㄘ'] = 97, ['ㄙ'] = 98, ['月'] = 99, ['ㄚ'] = 100, ['ㄛ'] = 104, ['ㄟ'] = 105, ['ㄞ'] = 106, ['ㄠ'] = 108, ['ㄡ'] = 109, ['ㄢ'] = 117, ['ㄣ'] = 121, ['ㄤ'] = 122, ['ㄥ'] = 124, ['ㄖ'] = 126, ['ㄧ'] = 127, ['ㄨ'] = 134, ['ㄩ'] = 137, ['ㄜ'] = 140, ['ㄭ'] = 141, ['十'] = 144, ['压'] = 145, ['言'] = 146, ['阳'] = 149, ['要'] = 150, ['阴'] = 151, ['应'] = 152, ['用'] = 153, ['又'] = 154, ['中'] = 155, ['穵'] = 159, ['外'] = 160, ['万'] = 161, ['王'] = 163, ['为'] = 165, ['文'] = 166, ['瓮'] = 167, ['我'] = 168, ['3'] = 169, ['5'] = 170, ['1'] = 171, ['2'] = 172, ['4'] = 173, ['元'] = 175, ['云'] = 176,
            };
            foreach (var kv in zh) vocab[kv.Key] = kv.Value;
            return vocab;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  Carga de archivos de modelo y estilos
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Localiza la carpeta '__Models' en la ruta de ejecución o en los directorios superiores del proyecto.
        /// </summary>
        private string? FindModelsDir()
        {
            if (_modelsDir != null) return _modelsDir;

            string localModels = Path.Combine(AppContext.BaseDirectory, "__Models");
            if (File.Exists(Path.Combine(localModels, ModelFile)) && File.Exists(Path.Combine(localModels, VoicesFile)))
                return _modelsDir = localModels;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "__Models");
                if (File.Exists(Path.Combine(candidate, ModelFile)) && File.Exists(Path.Combine(candidate, VoicesFile)))
                    return _modelsDir = candidate;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>
        /// Obtiene o inicializa la sesión de inferencia ONNX para el modelo Kokoro.
        /// </summary>
        private InferenceSession GetSession()
        {
            lock (_lock)
            {
                if (_session != null) return _session;
                string dir = FindModelsDir()
                    ?? throw new FileNotFoundException($"No se encontró __Models\\{ModelFile} / {VoicesFile} para las voces Kokoro.");
                return _session = new InferenceSession(Path.Combine(dir, ModelFile));
            }
        }

        /// <summary>
        /// Extrae el archivo de estilo (.npy) de la voz solicitada desde el archivo comprimido 'voices-v1.0.bin'.
        /// </summary>
        private float[,,] GetStyle(string voiceId)
        {
            lock (_lock)
            {
                if (_styles.TryGetValue(voiceId, out var cached)) return Reshape(cached);

                using var zip = ZipFile.OpenRead(Path.Combine(FindModelsDir()!, VoicesFile));
                var entry = zip.GetEntry(voiceId + ".npy")
                    ?? throw new InvalidDataException($"La voz '{voiceId}' no existe en {VoicesFile}.");
                using var stream = entry.Open();
                var data = ReadNpyFloats(stream);
                _styles[voiceId] = data;
                return Reshape(data);
            }
        }

        /// <summary>
        /// Reorganiza el arreglo unidimensional en una matriz tridimensional [filas, 1, 256].
        /// </summary>
        private static float[,,] Reshape(float[] data)
        {
            const int dim = 256;
            int rows = data.Length / dim;
            var result = new float[rows, 1, dim];
            Buffer.BlockCopy(data, 0, result, 0, data.Length * sizeof(float));
            return result;
        }

        /// <summary>
        /// Lee arreglos de números en formato NumPy (.npy) desde una secuencia de bytes.
        /// </summary>
        private static float[] ReadNpyFloats(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();

            if (bytes.Length < 10 || bytes[0] != 0x93 || Encoding.ASCII.GetString(bytes, 1, 5) != "NUMPY")
                throw new InvalidDataException("Formato .npy inválido.");

            int headerLen, headerStart;
            if (bytes[6] == 1) { headerLen = BitConverter.ToUInt16(bytes, 8); headerStart = 10; }
            else { headerLen = (int)BitConverter.ToUInt32(bytes, 8); headerStart = 12; }

            string header = Encoding.ASCII.GetString(bytes, headerStart, headerLen);
            if (!header.Contains("<f4") || header.Contains("True"))
                throw new InvalidDataException("Se esperaba un arreglo float32 en orden C.");

            int offset = headerStart + headerLen;
            var floats = new float[(bytes.Length - offset) / sizeof(float)];
            Buffer.BlockCopy(bytes, offset, floats, 0, floats.Length * sizeof(float));
            return floats;
        }

        /// <summary>
        /// Libera de la memoria la sesión de ONNX Runtime al finalizar la aplicación.
        /// </summary>
        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
