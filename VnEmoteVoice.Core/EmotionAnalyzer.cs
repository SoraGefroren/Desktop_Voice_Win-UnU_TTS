using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VnEmoteVoice.Core
{
    /// <summary>
    /// Información detallada de una transición entre emociones dentro de un segmento.
    /// </summary>
    public class EmotionTransitionInfo
    {
        public bool HasTransition { get; set; } = false;
        public string FromEmotion { get; set; } = string.Empty;
        public string FromEmoji { get; set; } = string.Empty;
        public string ToEmotion { get; set; } = string.Empty;
        public string ToEmoji { get; set; } = string.Empty;
        public string TransitionType { get; set; } = "Ninguna"; // "Contraste", "Giro Súbito", "Pausa Dramática", "Ruptura Emocional", "Giro Temporal"
        public string Description => HasTransition ? $"{FromEmotion} ➔ {ToEmotion}" : FromEmotion;
        public string DescriptionWithEmoji => HasTransition ? $"{FromEmoji} {FromEmotion} ➔ {ToEmoji} {ToEmotion}" : $"{FromEmoji} {FromEmotion}";
        public string CombinedEmoji => HasTransition ? $"{FromEmoji}➔{ToEmoji}" : FromEmoji;
    }

    /// <summary>
    /// Representa un fragmento o subfrase evaluada dentro de un segmento.
    /// </summary>
    public class SubSegmentEmotion
    {
        public int PartIndex { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Emotion { get; set; } = "Neutral";
        public string BaseEmotion { get; set; } = "Neutral";
        public string Modifier { get; set; } = string.Empty;
        public string Emoji { get; set; } = "😐";
        public int RelativeStartIndex { get; set; }
        public int Length { get; set; }
        public double Score { get; set; }
    }

    /// <summary>
    /// Segmento de texto procesado para síntesis y actuación de avatar VTuber.
    /// </summary>
    public class TextSegment
    {
        public string Text { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public int Length { get; set; }
        
        /// <summary>
        /// Emoción representativa calificada con modificadores de puntuación (ej. "Alegría/Exaltada", "Tristeza/Preguntando"
        /// o "Duda/Preguntando ➔ Furia/Gritando" si hay transición).
        /// </summary>
        public string Emotion { get; set; } = "Neutral";
        
        /// <summary>
        /// Emoji representativo (si hay transición, muestra "EmojiA➔EmojiB").
        /// </summary>
        public string Emoji { get; set; } = "😐";

        /// <summary>Emoción principal dominante calificada con su matiz (ej. "Alegría/Gritando").</summary>
        public string PrimaryEmotion { get; set; } = "Neutral";

        /// <summary>Emoción base sin matices ni modificadores de signos (ej. "Alegría").</summary>
        public string BaseEmotion { get; set; } = "Neutral";

        /// <summary>Modificador de entonación según signos ortográficos (ej. "Agitada", "Exaltada", "Gritando", "Preguntando", "Cuestionando").</summary>
        public string Modifier { get; set; } = string.Empty;

        /// <summary>Emoji principal dominante.</summary>
        public string PrimaryEmoji { get; set; } = "😐";

        /// <summary>Indica si el segmento presenta un cambio o transición interna de emociones.</summary>
        public bool HasTransition { get; set; } = false;

        /// <summary>Detalles de la transición emocional (inicial, final, tipo de cambio).</summary>
        public EmotionTransitionInfo Transition { get; set; } = new();

        /// <summary>Desglose de sub-oraciones o cláusulas con sus emociones correspondientes.</summary>
        public List<SubSegmentEmotion> SubEmotions { get; set; } = new();

        /// <summary>Confianza calculada en el análisis (0.0 a 1.0).</summary>
        public double Confidence { get; set; } = 1.0;
    }

    #region Modelos de Configuración JSON para Soporte Multilenguaje

    public class EmotionDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("emoji")]
        public string Emoji { get; set; } = "😐";

        [JsonPropertyName("group")]
        public string Group { get; set; } = string.Empty;

        [JsonPropertyName("weight")]
        public double Weight { get; set; } = 1.0;

        [JsonPropertyName("phrases")]
        public List<string> Phrases { get; set; } = new();

        [JsonPropertyName("stems")]
        public List<string> Stems { get; set; } = new();
    }

    public class PunctuationModifiersConfig
    {
        [JsonPropertyName("mixed_excl_quest")]
        public string MixedExclQuest { get; set; } = "Agitada";

        [JsonPropertyName("multiple_excl")]
        public string MultipleExcl { get; set; } = "Gritando";

        [JsonPropertyName("single_excl")]
        public string SingleExcl { get; set; } = "Exaltada";

        [JsonPropertyName("multiple_quest")]
        public string MultipleQuest { get; set; } = "Cuestionando";

        [JsonPropertyName("single_quest")]
        public string SingleQuest { get; set; } = "Preguntando";

        [JsonPropertyName("all_uppercase")]
        public string AllUppercase { get; set; } = "Gritando";

        [JsonPropertyName("ellipsis_mapping")]
        public Dictionary<string, string> EllipsisMapping { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class TransitionPivotConfig
    {
        [JsonPropertyName("pattern")]
        public string Pattern { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "Contraste";
    }

    public class NegationRuleConfig
    {
        [JsonPropertyName("trigger_phrases")]
        public List<string> TriggerPhrases { get; set; } = new();

        [JsonPropertyName("remove_emotion")]
        public string? RemoveEmotion { get; set; }

        [JsonPropertyName("add_emotion")]
        public string? AddEmotion { get; set; }

        [JsonPropertyName("bonus_score")]
        public double BonusScore { get; set; } = 3.0;
    }

    public class EmotionModelConfig
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = "es";

        [JsonPropertyName("language_name")]
        public string LanguageName { get; set; } = "Español";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("punctuation_modifiers")]
        public PunctuationModifiersConfig PunctuationModifiers { get; set; } = new();

        [JsonPropertyName("transition_pivot_patterns")]
        public List<TransitionPivotConfig> TransitionPivotPatterns { get; set; } = new();

        [JsonPropertyName("negation_rules")]
        public List<NegationRuleConfig> NegationRules { get; set; } = new();

        [JsonPropertyName("emotions")]
        public List<EmotionDefinition> Emotions { get; set; } = new();
    }

    #endregion

    /// <summary>
    /// Motor extensible de análisis emocional de texto para avatares y VTubers.
    /// Soporta carga dinámica de modelos por archivo JSON en múltiples idiomas y configuraciones.
    /// </summary>
    public static class EmotionAnalyzer
    {
        private sealed class CompiledEmotionRule
        {
            public string Name { get; }
            public string Emoji { get; }
            public string Group { get; }
            public string[] Phrases { get; }
            public string[] Stems { get; }
            public double Weight { get; }

            public CompiledEmotionRule(string name, string emoji, string group, IEnumerable<string> phrases, IEnumerable<string> stems, double weight = 1.0)
            {
                Name = name;
                Emoji = emoji;
                Group = group;
                Phrases = phrases.Select(Normalize).Where(p => !string.IsNullOrEmpty(p)).ToArray();
                Stems = stems.Select(Normalize).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                Weight = weight;
            }
        }

        private class CompiledPivotPattern
        {
            public Regex Pattern { get; }
            public string Type { get; }

            public CompiledPivotPattern(string regexPattern, string type)
            {
                Pattern = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                Type = type;
            }
        }

        private static readonly object _syncLock = new();
        private static readonly List<CompiledEmotionRule> _compiledRules = new();
        private static readonly List<CompiledPivotPattern> _compiledPivots = new();
        private static EmotionModelConfig _currentModel = new();

        public static EmotionModelConfig CurrentModel
        {
            get
            {
                lock (_syncLock)
                {
                    return _currentModel;
                }
            }
        }

        public static string ActiveLanguage => CurrentModel?.Language ?? "es";
        public static string ActiveLanguageName => CurrentModel?.LanguageName ?? "Español";
        public static int ActiveEmotionsCount => CurrentModel?.Emotions?.Count ?? 0;

        static EmotionAnalyzer()
        {
            LoadDefaultSpanishModel();
        }

        /// <summary>
        /// Valida la estructura y contenido de un JSON de modelo de emociones.
        /// </summary>
        public static (bool IsValid, string Message, EmotionModelConfig? Model) ValidateModel(string jsonContent)
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
                return (false, "El archivo JSON está vacío.", null);

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                var model = JsonSerializer.Deserialize<EmotionModelConfig>(jsonContent, options);
                if (model == null)
                    return (false, "No se pudo interpretar el archivo JSON como un modelo de emociones válido.", null);

                if (string.IsNullOrWhiteSpace(model.Language))
                    return (false, "El campo 'language' es obligatorio (ej. 'es', 'en', 'ja').", null);

                if (string.IsNullOrWhiteSpace(model.LanguageName))
                    return (false, "El campo 'language_name' es obligatorio (ej. 'Español', 'English').", null);

                if (model.Emotions == null || model.Emotions.Count == 0)
                    return (false, "El archivo no contiene ninguna emoción en el campo 'emotions'.", null);

                foreach (var em in model.Emotions)
                {
                    if (string.IsNullOrWhiteSpace(em.Name))
                        return (false, "Se encontró una emoción sin nombre ('name').", null);

                    if (string.IsNullOrWhiteSpace(em.Emoji))
                        return (false, $"La emoción '{em.Name}' no tiene un emoji asignado ('emoji').", null);

                    bool hasPhrases = em.Phrases != null && em.Phrases.Any(p => !string.IsNullOrWhiteSpace(p));
                    bool hasStems = em.Stems != null && em.Stems.Any(s => !string.IsNullOrWhiteSpace(s));

                    if (!hasPhrases && !hasStems)
                        return (false, $"La emoción '{em.Name}' debe tener al menos una frase o raíz léxica en 'phrases' o 'stems'.", null);
                }

                return (true, $"Modelo válido para idioma {model.LanguageName} ({model.Language}) con {model.Emotions.Count} emociones.", model);
            }
            catch (JsonException jEx)
            {
                return (false, $"Error de sintaxis JSON: {jEx.Message}", null);
            }
            catch (Exception ex)
            {
                return (false, $"Error al validar el archivo: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Carga un modelo de emociones desde un archivo JSON externo tras validarlo.
        /// </summary>
        public static bool LoadFromJsonFile(string filePath, out string errorMessage)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    errorMessage = $"El archivo '{filePath}' no existe.";
                    return false;
                }

                string jsonContent = File.ReadAllText(filePath, Encoding.UTF8);
                return LoadFromJsonContent(jsonContent, out errorMessage);
            }
            catch (Exception ex)
            {
                errorMessage = $"Error al leer el archivo: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Carga y aplica un modelo de emociones desde una cadena JSON tras validarla.
        /// </summary>
        public static bool LoadFromJsonContent(string jsonContent, out string errorMessage)
        {
            var (isValid, message, model) = ValidateModel(jsonContent);
            if (!isValid || model == null)
            {
                errorMessage = message;
                return false;
            }

            ApplyModel(model);
            errorMessage = string.Empty;
            return true;
        }

        private static void ApplyModel(EmotionModelConfig model)
        {
            lock (_syncLock)
            {
                _currentModel = model;
                _compiledRules.Clear();
                _compiledPivots.Clear();

                foreach (var em in model.Emotions)
                {
                    _compiledRules.Add(new CompiledEmotionRule(
                        em.Name,
                        em.Emoji,
                        em.Group,
                        em.Phrases ?? new List<string>(),
                        em.Stems ?? new List<string>(),
                        em.Weight > 0 ? em.Weight : 1.0));
                }

                if (model.TransitionPivotPatterns != null)
                {
                    foreach (var pivot in model.TransitionPivotPatterns)
                    {
                        if (!string.IsNullOrWhiteSpace(pivot.Pattern))
                        {
                            try
                            {
                                _compiledPivots.Add(new CompiledPivotPattern(pivot.Pattern, pivot.Type));
                            }
                            catch
                            {
                                // Si algún patrón regex personalizado fuese inválido, se ignora de forma segura
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Carga el modelo por defecto en español desde el archivo assets o fallback integrado.
        /// </summary>
        public static void LoadDefaultSpanishModel()
        {
            // Intentar cargar desde __Assets/emotions_es.json
            string[] possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "__Assets", "emotions_es.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "__Assets", "emotions_es.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "__Assets", "emotions_es.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "emotions_es.json")
            };

            foreach (var path in possiblePaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        if (LoadFromJsonFile(path, out _))
                            return;
                    }
                }
                catch
                {
                    // Continuar buscando
                }
            }

            // Fallback integrado si el archivo no estuviese disponible en disco
            var fallback = CreateBuiltInSpanishModel();
            ApplyModel(fallback);
        }

        private static EmotionModelConfig CreateBuiltInSpanishModel()
        {
            return new EmotionModelConfig
            {
                Language = "es",
                LanguageName = "Español",
                Version = "1.0",
                Description = "Modelo base en español integrado.",
                PunctuationModifiers = new PunctuationModifiersConfig
                {
                    MixedExclQuest = "Agitada",
                    MultipleExcl = "Gritando",
                    SingleExcl = "Exaltada",
                    MultipleQuest = "Cuestionando",
                    SingleQuest = "Preguntando",
                    AllUppercase = "Gritando",
                    EllipsisMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "Tristeza", "Quebrada" },
                        { "Cansancio", "Suspirando" },
                        { "Duda", "Vacilante" },
                        { "Furia", "Contenida" },
                        { "Miedo", "Tembloroso" },
                        { "Intriga", "Susurrante" },
                        { "Alegría", "Contenida" },
                        { "Alegria", "Contenida" },
                        { "default", "Vacilante" }
                    }
                },
                TransitionPivotPatterns = new List<TransitionPivotConfig>
                {
                    new() { Pattern = @"\b(sin\s+embargo|no\s+obstante|por\s+el\s+contrario|en\s+cambio|a\s+pesar\s+de\s+(?:todo|eso))\b", Type = "Contraste" },
                    new() { Pattern = @"(?:,\s*|\.\.\.\s*|\s+)\b(pero|aunque|mas)\b(?:\s+ahora)?", Type = "Contraste" },
                    new() { Pattern = @"\b(de\s+repente|de\s+pronto|subitamente|inesperadamente)\b", Type = "Giro Súbito" },
                    new() { Pattern = @"\b(pero\s+ahora|y\s+ahora|al\s+final|finalmente|de\s+golpe)\b", Type = "Giro Temporal" },
                    new() { Pattern = @"(?:\.{3,}|…|——|--|;)\s*", Type = "Pausa Dramática" },
                    new() { Pattern = @"\?\s*(?:¡|!)", Type = "Ruptura Emocional" }
                },
                NegationRules = new List<NegationRuleConfig>
                {
                    new() { TriggerPhrases = new List<string> { "no tengo miedo", "sin miedo", "no temo" }, RemoveEmotion = "Miedo", AddEmotion = "Confianza", BonusScore = 3.0 },
                    new() { TriggerPhrases = new List<string> { "no estoy seguro", "no se si" }, AddEmotion = "Duda", BonusScore = 3.0 },
                    new() { TriggerPhrases = new List<string> { "no puedo mas", "no doy mas" }, AddEmotion = "Cansancio", BonusScore = 3.5 }
                },
                Emotions = new List<EmotionDefinition>
                {
                    new() { Name = "Furia", Emoji = "🤬", Group = "Ira", Weight = 1.4, Phrases = new List<string> { "te matare", "te odio", "maldita sea" }, Stems = new List<string> { "furia", "furios", "rabia", "ira", "maldit", "odio" } },
                    new() { Name = "Alegria", Emoji = "😄", Group = "Felicidad", Weight = 1.2, Phrases = new List<string> { "estoy tan feliz", "que alegria" }, Stems = new List<string> { "alegr", "feliz", "content", "sonris" } },
                    new() { Name = "Tristeza", Emoji = "😢", Group = "Melancolia", Weight = 1.3, Phrases = new List<string> { "ganas de llorar", "me duele el alma" }, Stems = new List<string> { "trist", "llor", "lagrim", "deprim" } },
                    new() { Name = "Duda", Emoji = "🤨", Group = "Incertidumbre", Weight = 1.0, Phrases = new List<string> { "no lo se", "tal vez" }, Stems = new List<string> { "duda", "dudar", "quiza", "insegur" } }
                }
            };
        }

        /// <summary>
        /// Determina el modificador de puntuación para una emoción base según los signos del texto.
        /// </summary>
        public static (string QualifiedEmotion, string Modifier) DeterminePunctuationModifier(string baseEmotion, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || baseEmotion == "Neutral")
                return (baseEmotion, string.Empty);

            string trimmed = text.Trim();
            var modConfig = CurrentModel.PunctuationModifiers ?? new PunctuationModifiersConfig();

            bool hasBothExclAndQuest = (trimmed.Contains("!") || trimmed.Contains("¡")) &&
                                       (trimmed.Contains("?") || trimmed.Contains("¿"));

            bool hasMultipleExcl = trimmed.Contains("!!") || trimmed.Contains("¡¡");
            bool hasSingleExcl = trimmed.Contains("!") || trimmed.Contains("¡");
            bool hasMultipleQuest = trimmed.Contains("??") || trimmed.Contains("¿¿");
            bool hasSingleQuest = trimmed.Contains("?") || trimmed.Contains("¿");
            bool hasEllipsis = trimmed.Contains("...") || trimmed.Contains("…");
            bool isAllUpper = trimmed.Length > 4 && trimmed.Where(char.IsLetter).All(char.IsUpper);

            string modifier = string.Empty;

            // Prioridad 1: Mixto (! + ?) => "Agitada"
            if (hasBothExclAndQuest)
            {
                modifier = modConfig.MixedExclQuest;
            }
            // Prioridad 2: Exclamación múltiple o mayúsculas sostenidas => "Gritando"
            else if (hasMultipleExcl || (isAllUpper && hasSingleExcl))
            {
                modifier = modConfig.MultipleExcl;
            }
            // Prioridad 3: Exclamación simple => "Exaltada"
            else if (hasSingleExcl)
            {
                modifier = modConfig.SingleExcl;
            }
            // Prioridad 4: Interrogación múltiple => "Cuestionando"
            else if (hasMultipleQuest)
            {
                modifier = modConfig.MultipleQuest;
            }
            // Prioridad 5: Interrogación simple => "Preguntando"
            else if (hasSingleQuest)
            {
                modifier = modConfig.SingleQuest;
            }
            // Prioridad 6: Puntos suspensivos => Matiz según mapeo
            else if (hasEllipsis)
            {
                if (modConfig.EllipsisMapping != null && modConfig.EllipsisMapping.TryGetValue(baseEmotion, out var mappedMod))
                {
                    modifier = mappedMod;
                }
                else if (modConfig.EllipsisMapping != null && modConfig.EllipsisMapping.TryGetValue("default", out var defaultMod))
                {
                    modifier = defaultMod;
                }
                else
                {
                    modifier = "Vacilante";
                }
            }
            // Prioridad 7: Todo mayúsculas sin signos => "Gritando"
            else if (isAllUpper)
            {
                modifier = modConfig.AllUppercase;
            }

            if (string.IsNullOrEmpty(modifier))
                return (baseEmotion, string.Empty);

            return ($"{baseEmotion}/{modifier}", modifier);
        }

        /// <summary>
        /// Divide el texto general en segmentos basados en puntuación terminal (. ! ? \n)
        /// y evalúa la emoción principal y las transiciones internas de cada segmento.
        /// </summary>
        public static List<TextSegment> SegmentAndAnalyze(string fullText)
        {
            var segments = new List<TextSegment>();
            if (string.IsNullOrWhiteSpace(fullText)) return segments;

            char[] punctuation = { '.', '!', '?' };
            int i = 0;
            int n = fullText.Length;

            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(fullText[i])) i++;
                if (i >= n) break;

                int segmentStart = i;

                while (i < n && Array.IndexOf(punctuation, fullText[i]) < 0 && fullText[i] != '\n')
                {
                    i++;
                }

                if (i < n && Array.IndexOf(punctuation, fullText[i]) >= 0)
                {
                    while (i < n && Array.IndexOf(punctuation, fullText[i]) >= 0)
                    {
                        i++;
                    }
                }

                int length = i - segmentStart;
                string part = fullText.Substring(segmentStart, length).TrimEnd('\r');

                if (!string.IsNullOrWhiteSpace(part))
                {
                    var seg = AnalyzeSingleSegment(part, segmentStart, length);
                    segments.Add(seg);
                }
            }

            if (segments.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
            {
                string trimmed = fullText.Trim();
                segments.Add(AnalyzeSingleSegment(trimmed, 0, trimmed.Length));
            }

            return segments;
        }

        /// <summary>
        /// Analiza un segmento individual en busca de su emoción dominante o de transiciones intra-segmento,
        /// aplicando los matices de signos de puntuación.
        /// </summary>
        public static TextSegment AnalyzeSingleSegment(string text, int startIndex = 0, int length = 0)
        {
            if (length <= 0) length = text.Length;

            var seg = new TextSegment
            {
                Text = text,
                StartIndex = startIndex,
                Length = length
            };

            var transitionResult = DetectTransition(text);

            if (transitionResult.HasTransition)
            {
                seg.HasTransition = true;
                seg.Transition = transitionResult;

                string toFull = transitionResult.ToEmotion;
                string baseTo = toFull.Contains('/') ? toFull.Split('/')[0] : toFull;
                string modTo = toFull.Contains('/') ? toFull.Split('/')[1] : string.Empty;

                seg.PrimaryEmotion = toFull;
                seg.BaseEmotion = baseTo;
                seg.Modifier = modTo;
                seg.PrimaryEmoji = transitionResult.ToEmoji;
                seg.Emotion = transitionResult.Description;
                seg.Emoji = transitionResult.CombinedEmoji;
                seg.SubEmotions = transitionResult.SubEmotions;
                seg.Confidence = 0.95;
            }
            else
            {
                var (baseEmotion, emoji, score) = EvaluateEmotion(text);
                var (qualifiedEmotion, modifier) = DeterminePunctuationModifier(baseEmotion, text);

                seg.HasTransition = false;
                seg.BaseEmotion = baseEmotion;
                seg.Modifier = modifier;
                seg.PrimaryEmotion = qualifiedEmotion;
                seg.PrimaryEmoji = emoji;
                seg.Emotion = qualifiedEmotion;
                seg.Emoji = emoji;
                seg.Confidence = Math.Min(1.0, score > 0 ? 0.7 + (score * 0.1) : 0.5);

                seg.SubEmotions.Add(new SubSegmentEmotion
                {
                    PartIndex = 1,
                    Text = text,
                    Emotion = qualifiedEmotion,
                    BaseEmotion = baseEmotion,
                    Modifier = modifier,
                    Emoji = emoji,
                    RelativeStartIndex = 0,
                    Length = text.Length,
                    Score = score
                });
            }

            return seg;
        }

        private class DetailedTransitionResult : EmotionTransitionInfo
        {
            public List<SubSegmentEmotion> SubEmotions { get; set; } = new();
        }

        private static DetailedTransitionResult DetectTransition(string text)
        {
            var result = new DetailedTransitionResult();

            List<CompiledPivotPattern> pivots;
            lock (_syncLock)
            {
                pivots = new List<CompiledPivotPattern>(_compiledPivots);
            }

            foreach (var pivot in pivots)
            {
                var match = pivot.Pattern.Match(text);
                if (match.Success)
                {
                    int splitIndex = match.Index;
                    int splitLength = match.Length;

                    string part1 = text.Substring(0, splitIndex).Trim();
                    string part2 = text.Substring(splitIndex + splitLength).Trim();

                    if (part1.Length >= 4 && part2.Length >= 4)
                    {
                        var eval1 = EvaluateEmotion(part1);
                        var eval2 = EvaluateEmotion(part2);

                        if (eval1.Emotion == "Neutral" && (part1.Contains("?") || part1.Contains("¿")) && eval2.Emotion != "Neutral")
                        {
                            eval1 = ("Duda", "🤨", 1.0);
                        }

                        var (qual1, mod1) = DeterminePunctuationModifier(eval1.Emotion, part1);
                        var (qual2, mod2) = DeterminePunctuationModifier(eval2.Emotion, part2);

                        if (!string.Equals(qual1, qual2, StringComparison.OrdinalIgnoreCase))
                        {
                            bool validTransition = (eval1.Emotion != "Neutral" && eval2.Emotion != "Neutral") ||
                                                   (eval1.Score >= 1.0 && eval2.Score >= 1.0);

                            if (validTransition)
                            {
                                result.HasTransition = true;
                                result.FromEmotion = qual1;
                                result.FromEmoji = eval1.Emoji;
                                result.ToEmotion = qual2;
                                result.ToEmoji = eval2.Emoji;
                                result.TransitionType = pivot.Type;

                                result.SubEmotions.Add(new SubSegmentEmotion
                                {
                                    PartIndex = 1,
                                    Text = part1,
                                    Emotion = qual1,
                                    BaseEmotion = eval1.Emotion,
                                    Modifier = mod1,
                                    Emoji = eval1.Emoji,
                                    RelativeStartIndex = 0,
                                    Length = part1.Length,
                                    Score = eval1.Score
                                });

                                result.SubEmotions.Add(new SubSegmentEmotion
                                {
                                    PartIndex = 2,
                                    Text = part2,
                                    Emotion = qual2,
                                    BaseEmotion = eval2.Emotion,
                                    Modifier = mod2,
                                    Emoji = eval2.Emoji,
                                    RelativeStartIndex = splitIndex + splitLength,
                                    Length = part2.Length,
                                    Score = eval2.Score
                                });

                                return result;
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Evalúa un texto y retorna la emoción base con mayor puntuación acumulada.
        /// </summary>
        public static (string Emotion, string Emoji, double Score) EvaluateEmotion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return ("Neutral", "😐", 0.0);

            string normalized = Normalize(text);
            var scores = new Dictionary<CompiledEmotionRule, double>();

            bool hasMultipleExclamations = text.Contains("!!") || text.Contains("¡¡");
            bool hasMultipleQuestions = text.Contains("??") || text.Contains("¿¿");
            bool hasEllipsis = text.Contains("...") || text.Contains("…");
            bool isAllUpper = text.Length > 6 && text.Where(char.IsLetter).All(char.IsUpper);

            List<CompiledEmotionRule> rules;
            EmotionModelConfig model;

            lock (_syncLock)
            {
                rules = new List<CompiledEmotionRule>(_compiledRules);
                model = _currentModel;
            }

            foreach (var rule in rules)
            {
                double score = 0.0;

                foreach (var phrase in rule.Phrases)
                {
                    if (normalized.Contains(phrase))
                    {
                        score += 3.5 * rule.Weight;
                    }
                }

                foreach (var stem in rule.Stems)
                {
                    if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(stem)}\w*", RegexOptions.IgnoreCase))
                    {
                        score += 1.5 * rule.Weight;
                    }
                }

                if (score > 0)
                {
                    if (hasMultipleExclamations)
                    {
                        if (rule.Name is "Furia" or "Panico" or "SobreConfianza" or "Exigencia" or "Sorpresa" or "Alegria" or "Alegría")
                            score *= 1.35;
                    }

                    if (hasMultipleQuestions)
                    {
                        if (rule.Name is "Duda" or "Intriga" or "Sorpresa")
                            score *= 1.35;
                    }

                    if (hasEllipsis)
                    {
                        if (rule.Name is "Duda" or "Intriga" or "Cansancio" or "Tristeza" or "Decepcion" or "Decepción")
                            score *= 1.25;
                    }

                    if (isAllUpper)
                    {
                        if (rule.Name is "Furia" or "Panico" or "Exigencia")
                            score *= 1.4;
                    }

                    scores[rule] = score;
                }
            }

            // Aplicación dinámica de reglas de negación configuradas en el modelo
            if (model.NegationRules != null)
            {
                foreach (var neg in model.NegationRules)
                {
                    bool triggered = neg.TriggerPhrases.Any(tp => normalized.Contains(Normalize(tp)));
                    if (triggered)
                    {
                        if (!string.IsNullOrEmpty(neg.RemoveEmotion))
                        {
                            var targetRemove = rules.FirstOrDefault(r => r.Name.Equals(neg.RemoveEmotion, StringComparison.OrdinalIgnoreCase));
                            if (targetRemove != null && scores.ContainsKey(targetRemove))
                                scores.Remove(targetRemove);
                        }

                        if (!string.IsNullOrEmpty(neg.AddEmotion))
                        {
                            var targetAdd = rules.FirstOrDefault(r => r.Name.Equals(neg.AddEmotion, StringComparison.OrdinalIgnoreCase));
                            if (targetAdd != null)
                            {
                                scores[targetAdd] = scores.GetValueOrDefault(targetAdd) + (neg.BonusScore > 0 ? neg.BonusScore : 3.0);
                            }
                        }
                    }
                }
            }

            if (scores.Count == 0)
            {
                if (hasMultipleExclamations || text.Contains("¡") || text.Contains("!"))
                    return ("Sorpresa", "😲", 0.5);
                if (hasMultipleQuestions || text.Contains("¿") || text.Contains("?"))
                    return ("Duda", "🤨", 0.5);

                return ("Neutral", "😐", 0.0);
            }

            var top = scores.OrderByDescending(kv => kv.Value).First();
            return (top.Key.Name, top.Key.Emoji, top.Value);
        }

        /// <summary>
        /// Normaliza texto a minúsculas y remueve acentos/diacríticos para comparación rápida.
        /// Mantiene la letra 'ñ'.
        /// </summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string normalizedString = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (char c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
                else if (c == '\u0303') // Tilde de la eñe
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] == 'n')
                    {
                        sb.Remove(sb.Length - 1, 1);
                        sb.Append('ñ');
                    }
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
