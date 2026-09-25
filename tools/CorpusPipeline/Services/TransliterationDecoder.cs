using System.Text.RegularExpressions;

namespace VedaBaseModern.CorpusPipeline.Services
{
    public static class TransliterationDecoder
    {
        public static string Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            text = text.Replace("\\'e4", "ā").Replace("\\'c4", "Ā")
                       .Replace("\\'e5", "ṛ").Replace("\\'c5", "Ṛ")
                       .Replace("\\'e7", "ś").Replace("\\'c7", "Ś")
                       .Replace("\\'e9", "ī").Replace("\\'c9", "Ī")
                       .Replace("\\'eb", "ṇ").Replace("\\'cb", "Ṇ")
                       .Replace("\\'ef", "ñ").Replace("\\'cf", "Ñ")
                       .Replace("\\'f1", "ṣ").Replace("\\'d1", "Ṣ")
                       .Replace("\\'f2", "ḍ").Replace("\\'d2", "Ḍ")
                       .Replace("\\'f6", "ṭ").Replace("\\'d6", "Ṭ")
                       .Replace("\\'f9", "ḥ").Replace("\\'d9", "Ḥ")
                       .Replace("\\'fc", "ū").Replace("\\'dc", "Ū")
                       .Replace("\\'e0", "ṁ").Replace("\\'c0", "Ṁ")
                       .Replace("\\'e1", "ṁ")
                       .Replace("\\'e6", "ṝ").Replace("\\'c6", "Ṝ")
                       .Replace("\\'ec", "ṅ").Replace("\\'cc", "Ṅ")
                       .Replace("\\'ee", "ḷ").Replace("\\'ce", "Ḷ")
                       .Replace("\\'97", "—")
                       .Replace("\\'96", "–")
                       .Replace("\\'91", "‘").Replace("\\'92", "’")
                       .Replace("\\'93", "\"").Replace("\\'94", "\"");

            text = text.Replace("\\line", "\n").Replace("\\par", "");
            text = Regex.Replace(text, "\\\\\\*[a-zA-Z]+(\\d+)?", ""); 
            text = Regex.Replace(text, "\\\\[a-zA-Z]+(-?[0-9]+)? ?", ""); 
            text = Regex.Replace(text, "\\\\'[0-9a-fA-F]{2}", ""); 
            text = text.Replace("{", "").Replace("}", "");

            return text.Trim();
        }
    }
}
