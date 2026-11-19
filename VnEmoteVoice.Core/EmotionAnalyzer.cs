using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

    /// <summary>
    /// Motor avanzado de análisis emocional de texto en español para avatares y VTubers.
    /// Abarca un espectro emocional profundo de 27 categorías, modificadores por signos de puntuación (Emocion/Matiz)
    /// y detección de transiciones intra-segmento.
    /// </summary>
    public static class EmotionAnalyzer
    {
        // ─── Definición interna de una categoría emocional ───────────────────────────
        private sealed class EmotionRule
        {
            public string Name { get; }
            public string Emoji { get; }
            public string Group { get; }
            public string[] Phrases { get; }
            public string[] Stems { get; }
            public double Weight { get; }

            public EmotionRule(string name, string emoji, string group, string[] phrases, string[] stems, double weight = 1.0)
            {
                Name = name;
                Emoji = emoji;
                Group = group;
                Phrases = phrases.Select(Normalize).ToArray();
                Stems = stems.Select(Normalize).ToArray();
                Weight = weight;
            }
        }

        private static readonly List<EmotionRule> Rules = new();

        static EmotionAnalyzer()
        {
            InitRules();
        }

        private static void InitRules()
        {
            // 1. FURIA / IRA
            Rules.Add(new EmotionRule("Furia", "🤬", "Ira",
                new[] { "te matare", "te voy a destruir", "te odio", "maldito infeliz", "estupido idiota", "maldita sea", "hijos de perra", "los destruire", "callate de una vez", "muere de una vez" },
                new[] { "furia", "furios", "rabia", "rabios", "ira", "iracund", "coler", "coleric", "maldit", "estupid", "odio", "odiar", "desgraciad", "reviento", "revienta", "exploto", "infeliz", "pudran", "asqueros", "bastard" },
                1.4));

            // 2. INTRIGA / MISTERIO / SOSPECHA
            Rules.Add(new EmotionRule("Intriga", "🕵️", "Misterio",
                new[] { "que extrano", "algo no encaja", "no me cuadra", "que oculta", "quien sera", "que trama", "tengo mis sospechas", "algo huele mal", "no tiene sentido", "me pregunto que paso" },
                new[] { "intriga", "misteri", "enigmat", "curios", "sospech", "secret", "ocult", "acertij", "sombras", "susurr", "conspiracion", "pistas", "indicio", "investig" },
                1.1));

            // 3. MIEDO / TEMOR
            Rules.Add(new EmotionRule("Miedo", "😨", "Temor",
                new[] { "tengo miedo", "me da miedo", "no te acerques", "tiemblo de miedo", "que miedo", "estoy asustado", "me asusta", "siento escalofrios" },
                new[] { "miedo", "temor", "temer", "asustad", "susto", "tiemblo", "temblor", "amenaza", "escalofrio", "pavor", "temeros", "insegur" },
                1.2));

            // 4. TERROR / HORROR
            Rules.Add(new EmotionRule("Terror", "😱", "Temor Extremo",
                new[] { "sangre fria", "pesadilla viviente", "es una abominacion", "monstruo aterrador", "horror inimaginable" },
                new[] { "terror", "espant", "espantos", "atroz", "pesadilla", "monstruo", "abominacion", "siniestr", "macabr", "horror", "horrorizad", "petrificad", "demoni", "aterrador", "cadaver" },
                1.4));

            // 5. PÁNICO / DESESPERACIÓN
            Rules.Add(new EmotionRule("Panico", "🚨", "Crisis",
                new[] { "no hay salida", "salvese quien pueda", "estamos perdidos", "no respiro", "me ahogo", "no puedo salir", "alguien ayudeme", "por favor paren" },
                new[] { "panico", "corran", "escapen", "auxili", "socorr", "ayuda", "histeri", "atrapad", "desesperad", "desesperacion", "angustia", "asfixi" },
                1.3));

            // 6. BURLA / SARCASMO / MOFA
            Rules.Add(new EmotionRule("Burla", "😏", "Sarcasmo",
                new[] { "que patetico", "mirate nada mas", "como si pudieras", "que risa me das", "uy si claro", "pobrecito de ti", "no me hagas reir", "que chiste tan malo", "tan ingenuo como siempre" },
                new[] { "burla", "burlar", "mofa", "jaja", "jajaja", "jeje", "patetic", "ridicul", "payas", "perdedor", "pobrecit", "inutil", "ilus", "ingenu", "chiste" },
                1.2));

            // 7. LUJURIA / SEDUCCIÓN / SENSUALIDAD
            Rules.Add(new EmotionRule("Lujuria", "💋", "Seduccion",
                new[] { "ven aqui", "acercate mas", "tus labios", "besame ahora", "tan ardiente", "no te resistas", "delicioso placer", "piel con piel", "deseo tu cuerpo", "hazme tuyo", "hazme tuya" },
                new[] { "lujuri", "seducc", "sensual", "ardiente", "besame", "besos", "labios", "caricia", "deseo", "tentacion", "excit", "provocativ", "sexy", "pasion", "erot" },
                1.3));

            // 8. EGO / NARCISISMO / ORGULLO
            Rules.Add(new EmotionRule("Ego", "🦚", "Orgullo",
                new[] { "mi grandeza", "soy el mejor", "nadie como yo", "mi nombre sera recordado", "soy unico", "mi intelecto superior", "admiren mi poder", "arrodillense ante mi", "mi legado inmortal" },
                new[] { "ego", "narcis", "suprem", "perfecto", "perfecta", "inigualable", "adoren", "arrodillen", "inmortal", "magnific", "vanidad", "gloria", "dios", "reina", "rey" },
                1.2));

            // 9. SOBRECONFIANZA / JACTANCIA
            Rules.Add(new EmotionRule("SobreConfianza", "😎", "Orgullo",
                new[] { "pan comido", "demasiado facil", "nadie puede conmigo", "ni me despeine", "no tienen oportunidad", "juego de ninos", "no me duraron ni un segundo", "me sobra talento", "esta regalado" },
                new[] { "sobreconfianz", "facilisim", "invencibl", "imparabl", "invict", "sobradisim", "regalado", "impecabl", "arras", "arroll" },
                1.2));

            // 10. SOBERBIA / DESDÉN / ALTIVEZ
            Rules.Add(new EmotionRule("Soberbia", "💅", "Desden",
                new[] { "no estas a mi nivel", "como te atreves", "insecto insignificante", "no pierdas mi tiempo", "no me rebajare a tu nivel", "criatura miserable", "gusano repugnante" },
                new[] { "soberbi", "altiv", "insolent", "escoria", "insignificant", "mediocr", "vulgar", "plebey", "indign", "desden", "menospreci", "basura" },
                1.3));

            // 11. CONFIANZA / SEGURIDAD / CERTEZA
            Rules.Add(new EmotionRule("Confianza", "🤝", "Firmeza",
                new[] { "confia en mi", "lo lograremos", "tengo la certeza", "cuento contigo", "todo saldra bien", "seguro de mi mismo", "estamos listos", "se que podemos", "juntos venceremos" },
                new[] { "confianz", "confiar", "certez", "segur", "firme", "convicci", "creemos", "lograrem", "seguridad", "resuelt", "fe" },
                1.1));

            // 12. ARMONÍA / SERENIDAD / PAZ
            Rules.Add(new EmotionRule("Armonia", "🕊️", "Serenidad",
                new[] { "en paz", "respirar hondo", "todo esta en calma", "suave brisa", "paz profunda", "sosiego absoluto", "silencio apacible" },
                new[] { "armoni", "seren", "calma", "tranquil", "sosieg", "equilibri", "paz", "suave", "relajad", "apacibl", "tranquilidad", "bienestar", "repos" },
                1.1));

            // 13. PLENITUD / DICHA / GOZO
            Rules.Add(new EmotionRule("Plenitud", "🌟", "Gozo",
                new[] { "plenitud total", "dicha absoluta", "no me falta nada", "completamente feliz", "mi corazon esta lleno", "realizado en la vida", "bendecido por completo" },
                new[] { "plenitud", "plenament", "dicha", "dichos", "gozo", "gozos", "extasis", "realizad", "bendecid", "colmado", "radiant" },
                1.2));

            // 14. EXIGENCIA / MANDATO / AUTORIDAD
            Rules.Add(new EmotionRule("Exigencia", "⚡", "Autoridad",
                new[] { "ahora mismo", "de inmediato", "te lo ordeno", "hazlo ya", "te exijo", "no acepto excusas", "es una orden", "cumple con tu deber", "sin peros", "obedezcan inmediatamente" },
                new[] { "exig", "orden", "mandat", "obedezc", "deprisa", "rapido", "demand", "acat", "silencio", "imperativ" },
                1.3));

            // 15. DUDA / INCERTIDUMBRE / VACILACIÓN
            Rules.Add(new EmotionRule("Duda", "🤨", "Incertidumbre",
                new[] { "no estoy seguro", "tal vez sea", "quizas tengamos", "tengo mis dudas", "sera posible", "me pregunto si", "no lo se con certeza", "no se que pensar" },
                new[] { "duda", "dudar", "quizas", "inciert", "vacil", "desconciert", "confundi", "insegur", "acaso", "titube" },
                1.1));

            // 16. CANSANCIO / AGOTAMIENTO / FATIGA
            Rules.Add(new EmotionRule("Cansancio", "🥱", "Desgaste",
                new[] { "no doy mas", "no puedo mas", "necesito dormir", "me pesa el cuerpo", "sin energias", "estoy muerto de cansancio", "que fatiga tan grande" },
                new[] { "cansanc", "cansad", "agotad", "exhaust", "fatig", "sueno", "bostez", "deshech", "extenuad", "agotamiento", "desgaste" },
                1.2));

            // 17. IRRITACIÓN / FASTIDIO / MOLESTIA
            Rules.Add(new EmotionRule("Irritacion", "😒", "Molestia",
                new[] { "me molesta", "que fastidio", "dejame en paz", "no me molestes", "me tienes harto", "que pesadez", "ya basta de tonterias" },
                new[] { "irrita", "molest", "fastidi", "harto", "hartazgo", "insufribl", "insoportabl", "impertinent", "enojad", "molestia" },
                1.2));

            // 18. ALEGRÍA / ENTUSIASMO
            Rules.Add(new EmotionRule("Alegria", "😊", "Positivo",
                new[] { "que bien", "que emocion", "muchas felicidades", "estoy tan feliz", "es maravilloso", "experiencia increible", "bienvenido seas" },
                new[] { "alegr", "feliz", "felicid", "content", "sonris", "sonre", "genial", "maravillos", "estupend", "hurra", "jubil", "festej", "victoria" },
                1.0));

            // 19. TRISTEZA / MELANCOLÍA / DESOLACIÓN
            Rules.Add(new EmotionRule("Tristeza", "😢", "Negativo",
                new[] { "me duele tanto", "roto el corazon", "llorar en soledad", "que dolor tan hondo", "lo he perdido todo", "no quiero seguir asi" },
                new[] { "trist", "llor", "llanto", "duel", "dolor", "soledad", "desolad", "lagrim", "pena", "vacio", "lament", "desconsuel", "melancol" },
                1.2));

            // 20. SORPRESA / ASOMBRO
            Rules.Add(new EmotionRule("Sorpresa", "😲", "Impacto",
                new[] { "no me lo esperaba", "no puede ser verdad", "jamas crei que", "santo cielo", "que locura", "no me digas" },
                new[] { "sorpres", "asombr", "impact", "insolit", "inaudit", "wow", "cielos", "increibl", "pasmad", "inesperad" },
                1.1));

            // 21. TERNURA / AFECTO / AMOR
            Rules.Add(new EmotionRule("Ternura", "💖", "Afecto",
                new[] { "te quiero mucho", "te amo", "mi angelito", "corazon mio", "te llevo en mi alma", "abrazame fuerte", "me importas demasiado" },
                new[] { "ternur", "carin", "afect", "amor", "lindo", "linda", "dulce", "precios", "hermos", "angelical", "tiern", "adorabl" },
                1.2));

            // 22. DETERMINACIÓN / VALENTÍA
            Rules.Add(new EmotionRule("Determinacion", "✊", "Firmeza",
                new[] { "no me rendire", "luchare hasta el final", "seguire peleando", "jamas retrocedere", "venceremos a toda costa", "no pienso dar un paso atras" },
                new[] { "determinad", "rendir", "resist", "luch", "batall", "inquebrantabl", "valient", "persever", "decidid", "fuerza", "coraje" },
                1.3));

            // 23. ALIVIO / DESAHOGO
            Rules.Add(new EmotionRule("Alivio", "😌", "Calma",
                new[] { "por fin termino", "menos mal", "gracias al cielo", "respirar tranquilo", "ya paso el peligro", "a salvo al fin" },
                new[] { "alivi", "descans", "salvo", "salvad", "desahog", "respiro" },
                1.1));

            // 24. DECEPCIÓN / FRUSTRACIÓN
            Rules.Add(new EmotionRule("Decepcion", "😞", "Desilusion",
                new[] { "esperaba mas de ti", "me decepcionaste", "que desperdicio", "no sirvio de nada", "inutil esfuerzo", "otra desilusion mas" },
                new[] { "decepc", "frustra", "fracas", "desilusi", "vano", "fiasc" },
                1.2));

            // 25. CULPA / ARREPENTIMIENTO
            Rules.Add(new EmotionRule("Culpa", "😔", "Pesar",
                new[] { "fue mi culpa", "lo siento tanto", "me arrepiento de verdad", "perdoname por favor", "no debi haberlo hecho", "que verguenza siento" },
                new[] { "culpa", "perdon", "arrepient", "remordimient", "verguenz", "avergonzad", "disculpa" },
                1.2));

            // 26. ASCO / REPULSIÓN
            Rules.Add(new EmotionRule("Asco", "🤢", "Rechazo",
                new[] { "que asco", "me da nauseas", "fuera de aqui", "alejalo de mi", "que repugnante" },
                new[] { "asco", "repugnan", "nausea", "asqueros", "podrid", "repulsion", "nauseabund" },
                1.3));
        }

        // ─── MODIFICADORES DE ENTONACIÓN POR SIGNOS DE PUNTUACIÓN ─────────────────────

        /// <summary>
        /// Evalúa los signos ortográficos y tipográficos de la frase para complementar la emoción base,
        /// generando matices como "Alegria/Agitada", "Alegria/Exaltada", "Alegria/Gritando",
        /// "Tristeza/Preguntando", "Tristeza/Cuestionando", etc.
        /// </summary>
        public static (string QualifiedEmotion, string Modifier) DeterminePunctuationModifier(string baseEmotion, string text)
        {
            if (string.IsNullOrWhiteSpace(baseEmotion) || baseEmotion.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
                return (baseEmotion, string.Empty);

            string trimmed = text.Trim();

            // 1. Detección de combinaciones mixtas (? y ! en cualquier orden o simultáneos)
            bool hasBothExclAndQuest = (trimmed.Contains("?") || trimmed.Contains("¿")) && (trimmed.Contains("!") || trimmed.Contains("¡"));

            // 2. Múltiples exclamaciones (!! o ¡¡)
            bool hasMultipleExcl = trimmed.Contains("!!") || trimmed.Contains("¡¡");

            // 3. Exclamación simple (! o ¡)
            bool hasSingleExcl = trimmed.Contains("!") || trimmed.Contains("¡");

            // 4. Múltiples interrogaciones (?? o ¿¿)
            bool hasMultipleQuest = trimmed.Contains("??") || trimmed.Contains("¿¿");

            // 5. Interrogación simple (? o ¿)
            bool hasSingleQuest = trimmed.Contains("?") || trimmed.Contains("¿");

            // 6. Puntos suspensivos (... o …)
            bool hasEllipsis = trimmed.Contains("...") || trimmed.Contains("…");

            // 7. Todo mayúsculas (grito)
            bool isAllUpper = trimmed.Length > 4 && trimmed.Where(char.IsLetter).All(char.IsUpper);

            string modifier = string.Empty;

            // Prioridad 1: Mixto (interrogación + exclamación) => "Agitada"
            if (hasBothExclAndQuest)
            {
                modifier = "Agitada";
            }
            // Prioridad 2: Exclamación múltiple o mayúsculas sostenidas gritadas => "Gritando"
            else if (hasMultipleExcl || (isAllUpper && hasSingleExcl))
            {
                modifier = "Gritando";
            }
            // Prioridad 3: Exclamación simple => "Exaltada"
            else if (hasSingleExcl)
            {
                modifier = "Exaltada";
            }
            // Prioridad 4: Interrogación múltiple => "Cuestionando"
            else if (hasMultipleQuest)
            {
                modifier = "Cuestionando";
            }
            // Prioridad 5: Interrogación simple => "Preguntando"
            else if (hasSingleQuest)
            {
                modifier = "Preguntando";
            }
            // Prioridad 6: Puntos suspensivos => Matiz según la emoción base
            else if (hasEllipsis)
            {
                modifier = baseEmotion switch
                {
                    "Tristeza" => "Quebrada",
                    "Cansancio" => "Suspirando",
                    "Duda" => "Vacilante",
                    "Furia" => "Contenida",
                    "Miedo" => "Tembloroso",
                    "Intriga" => "Susurrante",
                    "Alegría" or "Alegria" => "Contenida",
                    _ => "Vacilante"
                };
            }
            // Prioridad 7: Todo en mayúsculas sin signos => "Gritando"
            else if (isAllUpper)
            {
                modifier = "Gritando";
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
                // Saltar espacios y saltos de línea iniciales
                while (i < n && char.IsWhiteSpace(fullText[i])) i++;
                if (i >= n) break;

                int segmentStart = i;

                // Avanzar mientras no sea puntuación terminal ni salto de línea
                while (i < n && Array.IndexOf(punctuation, fullText[i]) < 0 && fullText[i] != '\n')
                {
                    i++;
                }

                // Incluir signos terminales múltiples consecutivos (ej. "...", "?!", "!!!")
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

            // Si por formato no se detectaron cortes pero hay texto
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

            // 1. Buscar si existe un punto de pivote o conectores de transición emocional
            var transitionResult = DetectTransition(text);

            if (transitionResult.HasTransition)
            {
                seg.HasTransition = true;
                seg.Transition = transitionResult;

                // Obtener emoción base y modificador del polo de destino (final)
                string toFull = transitionResult.ToEmotion;
                string baseTo = toFull.Contains('/') ? toFull.Split('/')[0] : toFull;
                string modTo = toFull.Contains('/') ? toFull.Split('/')[1] : string.Empty;

                seg.PrimaryEmotion = toFull;
                seg.BaseEmotion = baseTo;
                seg.Modifier = modTo;
                seg.PrimaryEmoji = transitionResult.ToEmoji;
                seg.Emotion = transitionResult.Description; // ej: "Duda/Preguntando ➔ Furia/Gritando"
                seg.Emoji = transitionResult.CombinedEmoji;  // ej: "🤨➔🤬"
                seg.SubEmotions = transitionResult.SubEmotions;
                seg.Confidence = 0.95;
            }
            else
            {
                // Evaluar emoción dominante global del segmento con modificador por signos
                var (baseEmotion, emoji, score) = EvaluateEmotion(text);
                var (qualifiedEmotion, modifier) = DeterminePunctuationModifier(baseEmotion, text);

                seg.HasTransition = false;
                seg.BaseEmotion = baseEmotion;
                seg.Modifier = modifier;
                seg.PrimaryEmotion = qualifiedEmotion;
                seg.PrimaryEmoji = emoji;
                seg.Emotion = qualifiedEmotion; // ej: "Alegria/Exaltada", "Tristeza/Preguntando"
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

        // ─── LÓGICA DE DETECCIÓN DE TRANSICIONES ──────────────────────────────────────

        private class TransitionPivotPattern
        {
            public Regex Pattern { get; }
            public string Type { get; }

            public TransitionPivotPattern(string regexPattern, string type)
            {
                Pattern = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                Type = type;
            }
        }

        private static readonly List<TransitionPivotPattern> PivotPatterns = new()
        {
            // Pivotes de contraste explícito
            new TransitionPivotPattern(@"\b(sin\s+embargo|no\s+obstante|por\s+el\s+contrario|en\s+cambio|a\s+pesar\s+de\s+(?:todo|eso))\b", "Contraste"),
            new TransitionPivotPattern(@"(?:,\s*|\.\.\.\s*|\s+)\b(pero|aunque|mas)\b(?:\s+ahora)?", "Contraste"),
            
            // Pivotes temporales o giros súbitos
            new TransitionPivotPattern(@"\b(de\s+repente|de\s+pronto|subitamente|inesperadamente)\b", "Giro Súbito"),
            new TransitionPivotPattern(@"\b(pero\s+ahora|y\s+ahora|al\s+final|finalmente|de\s+golpe)\b", "Giro Temporal"),

            // Ruptura por puntuación dramática (puntos suspensivos, guiones largos, punto y coma)
            new TransitionPivotPattern(@"(?:\.{3,}|…|——|--|;)\s*", "Pausa Dramática"),

            // Cambio de pregunta reflexiva a exclamación
            new TransitionPivotPattern(@"\?\s*(?:¡|!)", "Ruptura Emocional")
        };

        private class DetailedTransitionResult : EmotionTransitionInfo
        {
            public List<SubSegmentEmotion> SubEmotions { get; set; } = new();
        }

        private static DetailedTransitionResult DetectTransition(string text)
        {
            var result = new DetailedTransitionResult();

            // Buscar la coincidencia de pivote más representativa que separe el texto en dos partes sustanciales
            foreach (var pivot in PivotPatterns)
            {
                var match = pivot.Pattern.Match(text);
                if (match.Success)
                {
                    int splitIndex = match.Index;
                    int splitLength = match.Length;

                    // Exigir que ambas partes tengan longitud suficiente (mínimo 4 caracteres de texto real)
                    string part1 = text.Substring(0, splitIndex).Trim();
                    string part2 = text.Substring(splitIndex + splitLength).Trim();

                    if (part1.Length >= 4 && part2.Length >= 4)
                    {
                        var eval1 = EvaluateEmotion(part1);
                        var eval2 = EvaluateEmotion(part2);

                        // Si la primera es una pregunta reflexiva pero no tenía palabras clave fuertes
                        if (eval1.Emotion == "Neutral" && (part1.Contains("?") || part1.Contains("¿")) && eval2.Emotion != "Neutral")
                        {
                            eval1 = ("Duda", "🤨", 1.0);
                        }

                        // Calificar ambas partes con los modificadores de puntuación según sus signos respectivos
                        var (qual1, mod1) = DeterminePunctuationModifier(eval1.Emotion, part1);
                        var (qual2, mod2) = DeterminePunctuationModifier(eval2.Emotion, part2);

                        // Se confirma transición si las emociones base o calificadas difieren y al menos una no es Neutral
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

        // ─── EVALUACIÓN LÉXICA Y PONDERADA DE EMOCIONES BASE ──────────────────────────

        /// <summary>
        /// Evalúa un texto y retorna la emoción base con mayor puntuación acumulada.
        /// </summary>
        public static (string Emotion, string Emoji, double Score) EvaluateEmotion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return ("Neutral", "😐", 0.0);

            string normalized = Normalize(text);
            var scores = new Dictionary<EmotionRule, double>();

            // Detectar modificadores de estilo y puntuación para el puntaje léxico
            bool hasMultipleExclamations = text.Contains("!!") || text.Contains("¡¡");
            bool hasMultipleQuestions = text.Contains("??") || text.Contains("¿¿");
            bool hasEllipsis = text.Contains("...") || text.Contains("…");
            bool isAllUpper = text.Length > 6 && text.Where(char.IsLetter).All(char.IsUpper);

            foreach (var rule in Rules)
            {
                double score = 0.0;

                // 1. Búsqueda de frases completas (mayor peso = 3.5 puntos por coincidencia)
                foreach (var phrase in rule.Phrases)
                {
                    if (normalized.Contains(phrase))
                    {
                        score += 3.5 * rule.Weight;
                    }
                }

                // 2. Búsqueda de raíces/lemas (stems) con límites de palabra (1.5 puntos)
                foreach (var stem in rule.Stems)
                {
                    if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(stem)}\w*", RegexOptions.IgnoreCase))
                    {
                        score += 1.5 * rule.Weight;
                    }
                }

                if (score > 0)
                {
                    // Modificadores de puntuación contextual
                    if (hasMultipleExclamations)
                    {
                        if (rule.Name is "Furia" or "Panico" or "SobreConfianza" or "Exigencia" or "Sorpresa" or "Alegria")
                            score *= 1.35;
                    }

                    if (hasMultipleQuestions)
                    {
                        if (rule.Name is "Duda" or "Intriga" or "Sorpresa")
                            score *= 1.35;
                    }

                    if (hasEllipsis)
                    {
                        if (rule.Name is "Duda" or "Intriga" or "Cansancio" or "Tristeza" or "Decepcion")
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

            // Manejo de negaciones directas (ej. "no tengo miedo" no debe puntuar Miedo)
            if (normalized.Contains("no tengo miedo") || normalized.Contains("sin miedo") || normalized.Contains("no temo"))
            {
                var fearRule = Rules.FirstOrDefault(r => r.Name == "Miedo");
                if (fearRule != null && scores.ContainsKey(fearRule)) scores.Remove(fearRule);

                var confRule = Rules.FirstOrDefault(r => r.Name == "Confianza");
                if (confRule != null) scores[confRule] = scores.GetValueOrDefault(confRule) + 3.0;
            }

            if (normalized.Contains("no estoy seguro") || normalized.Contains("no se si"))
            {
                var dudaRule = Rules.FirstOrDefault(r => r.Name == "Duda");
                if (dudaRule != null) scores[dudaRule] = scores.GetValueOrDefault(dudaRule) + 3.0;
            }

            if (normalized.Contains("no puedo mas") || normalized.Contains("no doy mas"))
            {
                var cansancioRule = Rules.FirstOrDefault(r => r.Name == "Cansancio");
                if (cansancioRule != null) scores[cansancioRule] = scores.GetValueOrDefault(cansancioRule) + 3.5;
            }

            if (scores.Count == 0)
            {
                // Fallback por signos gramaticales puros si no hay palabras clave explícitas
                if (hasMultipleExclamations || text.Contains("¡") || text.Contains("!"))
                    return ("Sorpresa", "😲", 0.5);
                if (hasMultipleQuestions || text.Contains("¿") || text.Contains("?"))
                    return ("Duda", "🤨", 0.5);

                return ("Neutral", "😐", 0.0);
            }

            // Seleccionar la regla con mayor puntuación acumulada
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
                    // Si el carácter anterior es 'n', mantenerlo como 'ñ'
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
