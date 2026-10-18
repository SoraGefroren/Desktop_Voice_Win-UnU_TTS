using System;
using System.Collections.Generic;

namespace VtuberTts.Core
{
    public class TextSegment
    {
        public string Text { get; set; } = string.Empty;
        public int StartIndex { get; set; }
        public int Length { get; set; }
        public string Emotion { get; set; } = "Neutral";
        public string Emoji { get; set; } = "😐";
    }

    public static class EmotionAnalyzer
    {
        public static List<TextSegment> SegmentAndAnalyze(string fullText)
        {
            var segments = new List<TextSegment>();
            if (string.IsNullOrWhiteSpace(fullText)) return segments;

            char[] punctuation = { '.', '!', '?' };
            int i = 0;
            int n = fullText.Length;

            while (i < n)
            {
                // Saltar espacios y saltos de línea en blanco antes de la siguiente oración
                while (i < n && char.IsWhiteSpace(fullText[i]))
                {
                    i++;
                }

                if (i >= n) break;

                int segmentStart = i;

                // Avanzar mientras no encontremos puntuación terminal (., !, ?) ni salto de línea (\n)
                while (i < n && Array.IndexOf(punctuation, fullText[i]) < 0 && fullText[i] != '\n')
                {
                    i++;
                }

                // Si encontramos puntuación terminal (., !, ?), incluirla (incluyendo signos múltiples: "...", "?!", "!!")
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
                    var evaluated = EvaluateEmotion(part);
                    segments.Add(new TextSegment
                    {
                        Text = part,
                        StartIndex = segmentStart,
                        Length = length,
                        Emotion = evaluated.Emotion,
                        Emoji = evaluated.Emoji
                    });
                }
            }

            if (segments.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
            {
                var evaluated = EvaluateEmotion(fullText.Trim());
                segments.Add(new TextSegment
                {
                    Text = fullText.Trim(),
                    StartIndex = 0,
                    Length = fullText.Length,
                    Emotion = evaluated.Emotion,
                    Emoji = evaluated.Emoji
                });
            }

            return segments;
        }

        private static (string Emotion, string Emoji) EvaluateEmotion(string text)
        {
            string lower = text.ToLower();
            if (lower.Contains("bienvenido") || lower.Contains("feliz") || lower.Contains("gracias") || lower.Contains("genial") || lower.Contains("experiencia") || lower.Contains("pruébalo"))
                return ("Alegría", "😊");
            if (lower.Contains("cuidado") || lower.Contains("alerta") || lower.Contains("sorpresa") || lower.Contains("¡"))
                return ("Sorpresa", "😲");
            if (lower.Contains("triste") || lower.Contains("mal") || lower.Contains("lo siento"))
                return ("Tristeza", "😢");
            if (lower.Contains("enojo") || lower.Contains("molesto"))
                return ("Enojo", "😡");

            return ("Neutral", "😐");
        }
    }
}