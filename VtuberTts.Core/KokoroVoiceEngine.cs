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

namespace VtuberTts.Core
{
    /// <summary>
    /// Motor TTS local basado en Kokoro (ONNX). Expone voces con formato intuitivo
    /// "(Clave) Nombre (Kokoro)" para que <see cref="VoiceManager"/> las trate
    /// de manera transparente junto con las voces del sistema.
    /// </summary>
    public sealed class KokoroVoiceEngine : IDisposable
    {
        public const string VoiceLabel = " (Kokoro)";
        public const int SampleRate = 24000;
        private const int MaxTokens = 510;
        private const int SegmentTokens = 300;
        private const string ModelFile = "kokoro-v1.0.onnx";
        private const string VoicesFile = "voices-v1.0.bin";

        /// <summary>Información de una voz Kokoro, su idioma BCP-47, código G2P y nombre para mostrar.</summary>
        public sealed record KokoroVoiceInfo(string Id, string Language, string G2PCode, string DisplayName)
        {
            /// <summary>
            /// Construye una voz Kokoro formateando su nombre para mostrar como "(Clave) Nombre (Kokoro)".
            /// Ej: "ef_dora" -> "(EF) Dora (Kokoro)"
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

        /// <summary>
        /// Catálogo completo de voces Kokoro v1.0 extraído de VOICES.md.
        /// </summary>
        public static readonly IReadOnlyList<KokoroVoiceInfo> Catalog = new[]
        {
            // 🇺🇸 American English (en-US) - 11F 9M
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

            // 🇬🇧 British English (en-GB) - 4F 4M
            KokoroVoiceInfo.Create("bf_alice",     "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bf_emma",      "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bf_isabella",  "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bf_lily",      "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bm_daniel",    "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bm_fable",     "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bm_george",    "en-GB", "en-gb"),
            KokoroVoiceInfo.Create("bm_lewis",     "en-GB", "en-gb"),

            // 🇪🇸 Spanish (es-ES) - 1F 2M
            KokoroVoiceInfo.Create("ef_dora",      "es-ES", "es"),
            KokoroVoiceInfo.Create("em_alex",      "es-ES", "es"),
            KokoroVoiceInfo.Create("em_santa",     "es-ES", "es"),

            // 🇫🇷 French (fr-FR) - 1F
            KokoroVoiceInfo.Create("ff_siwis",     "fr-FR", "fr-fr"),

            // 🇮🇳 Hindi (hi-IN) - 2F 2M
            KokoroVoiceInfo.Create("hf_alpha",     "hi-IN", "hi"),
            KokoroVoiceInfo.Create("hf_beta",      "hi-IN", "hi"),
            KokoroVoiceInfo.Create("hm_omega",     "hi-IN", "hi"),
            KokoroVoiceInfo.Create("hm_psi",       "hi-IN", "hi"),

            // 🇮🇹 Italian (it-IT) - 1F 1M
            KokoroVoiceInfo.Create("if_sara",      "it-IT", "it"),
            KokoroVoiceInfo.Create("im_nicola",    "it-IT", "it"),

            // 🇯🇵 Japanese (ja-JP) - 4F 1M
            KokoroVoiceInfo.Create("jf_alpha",      "ja-JP", "ja"),
            KokoroVoiceInfo.Create("jf_gongitsune", "ja-JP", "ja"),
            KokoroVoiceInfo.Create("jf_nezumi",     "ja-JP", "ja"),
            KokoroVoiceInfo.Create("jf_tebukuro",   "ja-JP", "ja"),
            KokoroVoiceInfo.Create("jm_kumo",       "ja-JP", "ja"),

            // 🇧🇷 Brazilian Portuguese (pt-BR) - 1F 2M
            KokoroVoiceInfo.Create("pf_dora",      "pt-BR", "pt-br"),
            KokoroVoiceInfo.Create("pm_alex",      "pt-BR", "pt-br"),
            KokoroVoiceInfo.Create("pm_santa",     "pt-BR", "pt-br"),

            // 🇨🇳 Mandarin Chinese (zh-CN) - 4F 4M
            KokoroVoiceInfo.Create("zf_xiaobei",   "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zf_xiaoni",    "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zf_xiaoxiao",  "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zf_xiaoyi",    "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zm_yunjian",   "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zm_yunxi",     "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zm_yunxia",    "zh-CN", "cmn"),
            KokoroVoiceInfo.Create("zm_yunyang",   "zh-CN", "cmn"),
        };

        private static readonly Dictionary<char, int> Vocab = BuildVocab();
        private static readonly Regex Punctuation = new(@"(?<=[;:,.!?…¿\n])\s*", RegexOptions.Compiled);

        private readonly object _lock = new();
        private readonly Dictionary<string, float[]> _styles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EspeakG2P> _espeak = new();
        private static EnglishG2P? _englishUs;
        private static EnglishG2P? _englishGb;
        private InferenceSession? _session;
        private string? _modelsDir;

        /// <summary>
        /// Valida si el archivo VtuberTts.Core.csproj declara los modelos
        /// "kokoro-v1.0.onnx" y "voices-v1.0.bin".
        /// </summary>
        public static bool HasKokoroModelsInCsproj()
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string candidate1 = Path.Combine(dir.FullName, "VtuberTts.Core", "VtuberTts.Core.csproj");
                    string candidate2 = Path.Combine(dir.FullName, "VtuberTts.Core.csproj");
                    string? csprojPath = File.Exists(candidate1) ? candidate1 : (File.Exists(candidate2) ? candidate2 : null);

                    if (csprojPath != null)
                    {
                        string content = File.ReadAllText(csprojPath);
                        bool hasOnnx = content.Contains("kokoro-v1.0.onnx", StringComparison.OrdinalIgnoreCase);
                        bool hasBin  = content.Contains("voices-v1.0.bin", StringComparison.OrdinalIgnoreCase);
                        return hasOnnx && hasBin;
                    }
                    dir = dir.Parent;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// True si VtuberTts.Core.csproj contiene los modelos declarados Y existen físicamente en disco.
        /// Si alguna condición no se cumple, las voces de Kokoro no estarán disponibles ni se mostrarán.
        /// </summary>
        public bool IsAvailable => HasKokoroModelsInCsproj() && FindModelsDir() != null;

        public static bool IsKokoroVoice(string voiceNameWithLabel) =>
            !string.IsNullOrEmpty(voiceNameWithLabel) &&
            voiceNameWithLabel.EndsWith(VoiceLabel, StringComparison.OrdinalIgnoreCase);

        public KokoroVoiceInfo? FindVoice(string voiceNameWithLabel)
        {
            if (string.IsNullOrWhiteSpace(voiceNameWithLabel)) return null;

            // 1. Coincidencia por DisplayName exacto (ej. "(EF) Dora (Kokoro)")
            var match = Catalog.FirstOrDefault(v => v.DisplayName.Equals(voiceNameWithLabel, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // 2. Coincidencia por Id con sufijo (ej. "ef_dora (Kokoro)")
            if (IsKokoroVoice(voiceNameWithLabel))
            {
                string id = voiceNameWithLabel[..^VoiceLabel.Length].Trim();
                match = Catalog.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            // 3. Coincidencia por Id directo (ej. "ef_dora")
            return Catalog.FirstOrDefault(v => v.Id.Equals(voiceNameWithLabel, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Coincide por idioma exacto o por subetiqueta primaria (es-MX coincide con es-ES).</summary>
        public static bool MatchesLanguage(KokoroVoiceInfo voice, string language)
        {
            if (string.IsNullOrEmpty(language)) return true;
            if (voice.Language.Equals(language, StringComparison.OrdinalIgnoreCase)) return true;
            return PrimaryTag(voice.Language).Equals(PrimaryTag(language), StringComparison.OrdinalIgnoreCase);
        }

        private static string PrimaryTag(string lang)
        {
            int i = lang.IndexOf('-');
            return i >= 0 ? lang[..i] : lang;
        }

        /// <param name="speed">0.5 a 2.0 (Kokoro no soporta valores mayores de forma fiable).</param>
        /// <param name="volume">0.0 a 1.0, aplicado a las muestras.</param>
        public Task SynthesizeToWavAsync(string text, string outputPath, KokoroVoiceInfo voice, float speed, float volume)
            => Task.Run(() => SynthesizeToWav(text, outputPath, voice, speed, volume));

        private void SynthesizeToWav(string text, string outputPath, KokoroVoiceInfo voice, float speed, float volume)
        {
            var session = GetSession();
            var style = GetStyle(voice.Id);
            speed = Math.Clamp(speed, 0.5f, 2.0f);
            volume = Math.Clamp(volume, 0f, 1f);

            var samples = new List<float>();
            foreach (var tokens in Tokenize(text, voice.G2PCode))
            {
                samples.AddRange(Infer(session, tokens, style, speed));
                // Pausa corta entre segmentos
                samples.AddRange(new float[(int)(SampleRate * 0.12f)]);
            }

            using var writer = new WaveFileWriter(outputPath, new WaveFormat(SampleRate, 16, 1));
            foreach (var s in samples)
            {
                writer.WriteSample(Math.Clamp(s * volume, -1f, 1f));
            }
        }

        private static float[] Infer(InferenceSession session, int[] tokens, float[,,] styles, float speed)
        {
            int count = Math.Min(tokens.Length, MaxTokens);
            var tokenTensor = new DenseTensor<long>(new[] { 1, count + 2 }); // <pad>{texto}<pad>
            for (int i = 0; i < count; i++) tokenTensor[0, i + 1] = tokens[i];

            int dim = styles.GetLength(2);
            var styleTensor = new DenseTensor<float>(new[] { 1, dim });
            for (int j = 0; j < dim; j++) styleTensor[0, j] = styles[count - 1, 0, j];

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

        #region Texto -> tokens

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

        private static string Normalize(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('‘', '"').Replace('’', '"').Replace("**", "*");
            foreach (var c in "-`()[]{}~*#") text = text.Replace(c.ToString(), " ");
            return Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        }

        private string Phonemize(string line, string g2pCode)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";
            lock (_lock)
            {
                if (g2pCode == "cmn")
                {
                    ChineseG2P.EnglishPhonemizer ??= EnglishPhonemes;
                    return ChineseG2P.Phonemize(line);
                }

                if (g2pCode == "en-us" || g2pCode == "a")
                {
                    _englishUs ??= new EnglishG2P(EnglishG2P.DefaultTagger, british: false);
                    return new string(_englishUs.Phonemize(line).Phonemes.Where(Vocab.ContainsKey).ToArray());
                }

                if (g2pCode == "en-gb" || g2pCode == "b")
                {
                    _englishGb ??= new EnglishG2P(EnglishG2P.DefaultTagger, british: true);
                    return new string(_englishGb.Phonemize(line).Phonemes.Where(Vocab.ContainsKey).ToArray());
                }

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

        /// <summary>Divide los fonemas en frases (≤ SegmentTokens) respetando la puntuación.</summary>
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

        private static Dictionary<char, int> BuildVocab()
        {
            var vocab = new Dictionary<char, int>
            {
                ['$'] = 0, [';'] = 1, [':'] = 2, [','] = 3, ['.'] = 4, ['!'] = 5, ['?'] = 6, ['¡'] = 7, ['¿'] = 8, ['—'] = 9, ['…'] = 10, ['"'] = 11, ['('] = 12, [')'] = 13, ['“'] = 14, ['”'] = 15, [' '] = 16, ['\u0303'] = 17, ['ʣ'] = 18, ['ʥ'] = 19, ['ʦ'] = 20, ['ʨ'] = 21, ['ᵝ'] = 22, ['\uAB67'] = 23, ['A'] = 24, ['I'] = 25, ['O'] = 31, ['Q'] = 33, ['S'] = 35, ['T'] = 36, ['W'] = 39, ['Y'] = 41, ['ᵊ'] = 42, ['a'] = 43, ['b'] = 44, ['c'] = 45, ['d'] = 46, ['e'] = 47, ['f'] = 48, ['h'] = 50, ['i'] = 51, ['j'] = 52, ['k'] = 53, ['l'] = 54, ['m'] = 55, ['n'] = 56, ['o'] = 57, ['p'] = 58, ['q'] = 59, ['r'] = 60, ['s'] = 61, ['t'] = 62, ['u'] = 63, ['v'] = 64, ['w'] = 65, ['x'] = 66, ['y'] = 67, ['z'] = 68, ['ɑ'] = 69, ['ɐ'] = 70, ['ɒ'] = 71, ['æ'] = 72, ['β'] = 75, ['ɔ'] = 76, ['ɕ'] = 77, ['ç'] = 78, ['ɖ'] = 80, ['ð'] = 81, ['ʤ'] = 82, ['ə'] = 83, ['ɚ'] = 85, ['ɛ'] = 86, ['ɜ'] = 87, ['ɟ'] = 90, ['ɡ'] = 92, ['ɥ'] = 99, ['ɨ'] = 101, ['ɪ'] = 102, ['ʝ'] = 103, ['ɯ'] = 110, ['ɰ'] = 111, ['ŋ'] = 112, ['ɳ'] = 113, ['ɲ'] = 114, ['ɴ'] = 115, ['ø'] = 116, ['ɸ'] = 118, ['θ'] = 119, ['œ'] = 120, ['ɹ'] = 123, ['ɾ'] = 125, ['ɻ'] = 126, ['ʁ'] = 128, ['ɽ'] = 129, ['ʂ'] = 130, ['ʃ'] = 131, ['ʈ'] = 132, ['ʧ'] = 133, ['ʊ'] = 135, ['ʋ'] = 136, ['ʌ'] = 138, ['ɣ'] = 139, ['ɤ'] = 140, ['χ'] = 142, ['ʎ'] = 143, ['ʒ'] = 147, ['ʔ'] = 148, ['ˈ'] = 156, ['ˌ'] = 157, ['ː'] = 158, ['ʰ'] = 162, ['ʲ'] = 164, ['↓'] = 169, ['→'] = 171, ['↗'] = 172, ['↘'] = 173, ['ᵻ'] = 177,
            };

            var zh = new Dictionary<char, int>
            {
                ['/'] = 7, ['ㄓ'] = 23, ['ㄅ'] = 30, ['ㄆ'] = 32, ['R'] = 34, ['ㄇ'] = 37, ['ㄈ'] = 38, ['ㄉ'] = 40, ['ㄊ'] = 49, ['ㄋ'] = 73, ['ㄌ'] = 74, ['ㄍ'] = 79, ['ㄎ'] = 84, ['ㄦ'] = 85, ['ㄏ'] = 88, ['ㄐ'] = 89, ['ㄑ'] = 91, ['ㄒ'] = 93, ['ㄔ'] = 94, ['ㄕ'] = 95, ['ㄗ'] = 96, ['ㄘ'] = 97, ['ㄙ'] = 98, ['月'] = 99, ['ㄚ'] = 100, ['ㄛ'] = 104, ['ㄟ'] = 105, ['ㄞ'] = 106, ['ㄟ'] = 107, ['ㄠ'] = 108, ['ㄡ'] = 109, ['ㄢ'] = 117, ['ㄣ'] = 121, ['ㄤ'] = 122, ['ㄥ'] = 124, ['ㄖ'] = 126, ['ㄧ'] = 127, ['ㄨ'] = 134, ['ㄩ'] = 137, ['ㄜ'] = 140, ['ㄭ'] = 141, ['十'] = 144, ['压'] = 145, ['言'] = 146, ['阳'] = 149, ['要'] = 150, ['阴'] = 151, ['应'] = 152, ['用'] = 153, ['又'] = 154, ['中'] = 155, ['穵'] = 159, ['外'] = 160, ['万'] = 161, ['王'] = 163, ['为'] = 165, ['文'] = 166, ['瓮'] = 167, ['我'] = 168, ['3'] = 169, ['5'] = 170, ['1'] = 171, ['2'] = 172, ['4'] = 173, ['元'] = 175, ['云'] = 176,
            };
            foreach (var kv in zh) vocab[kv.Key] = kv.Value;
            return vocab;
        }

        #endregion

        #region Modelos y voces

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

        private static float[,,] Reshape(float[] data)
        {
            const int dim = 256;
            int rows = data.Length / dim;
            var result = new float[rows, 1, dim];
            Buffer.BlockCopy(data, 0, result, 0, data.Length * sizeof(float));
            return result;
        }

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

        #endregion

        public void Dispose()
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
