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

            char[] delimiters = { '.', '!', '?', '\n' };
            int lastIndex = 0;

            for (int i = 0; i < fullText.Length; i++)
            {
                if (Array.IndexOf(delimiters, fullText[i]) >= 0 || i == fullText.Length - 1)
                {
                    int length = (i - lastIndex) + (i == fullText.Length - 1 ? 1 : 0);
                    string part = fullText.Substring(lastIndex, length);
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        var evaluated = EvaluateEmotion(part.Trim());
                        segments.Add(new TextSegment
                        {
                            Text = part,
                            StartIndex = lastIndex,
                            Length = length,
                            Emotion = evaluated.Emotion,
                            Emoji = evaluated.Emoji
                        });
                    }
                    lastIndex = i + 1;
                }
            }

            if (segments.Count == 0)
            {
                segments.Add(new TextSegment { Text = fullText, StartIndex = 0, Length = fullText.Length, Emotion = "Neutral", Emoji = "😐" });
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