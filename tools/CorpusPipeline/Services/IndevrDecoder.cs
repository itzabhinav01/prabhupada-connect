using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace VedaBaseModern.CorpusPipeline.Services
{
    public static class IndevrDecoder
    {
        public static readonly Dictionary<string, string> VerifiedMappings = new Dictionary<string, string>
        {
            // Complex / Multi-char
            { "\\'ed\\", "ष्ट्र" }, { "C^\\", "च्छ्र" },
            { "\\'e0", "न्न" }, { "\\'dc", "द्व" },
            { "\\'e1", "प्त" }, { "\\'f1", "स्त्र" }, { "\\'a2-", "क्त" }, { "\\'a2", "क्त्" },
            { "\\'d6", "द्ध" }, { "\\'e4", "ध्य" }, { "\\'f4", "हृ" }, { "\\'ec", "श्व" },
            { "\\'ef", "ष्ठि" }, { "\\'f3", "स्र" }, { "\\'f9", "ह्य" }, { "\\'f2", "त्स्न" },
            { "\\'aa", "ङ्क" }, { "\\'ab", "ङ्ख" }, { "\\'bf", "ङ्क्ष" }, { "\\'c4", "ञ्च" },
            { "\\'c0", "च्च" }, { "\\'e6", "रून्" }, { "\\'9b", "ॠ" }, { "\\'84", "" }, { "\\'d9", "दृ" }, { "\\'a5", "ग्म" }, { "\\'c9", "ठ" },
            { "(r)", "ङ्ग" }, { "\\'c5", "ञ्ज" }, { "\\'ea", "श्च" }, { "\\'e9", "श्" },
            { "\\'ed", "ष्ट" }, { "\\'ee", "ष्ट्व" }, { "\\'e5", "रु" }, { "\\'a7", "क्र" },
            { "\\'c2", "ज्ज" }, { "\\'a4-", "क्त्व" }, { "\\'e3", "ष्ठ" }, { "\\'d4", "द्भ" },
            { "\\'a3", "ग्घ" }, { "\\'db", "द्य" }, { "\\'de", "द्य" }, { "\\'df", "द्र" },
            { "\\'cb", "ज्र" }, { "\\'c6", "ञ्च" }, { "\\'e8", "श्र" }, { "\\'e7", "श्ल" },
            { "\\'c8", "ट्ट" },

            // Vowels & Modifiers
            { "A", "अ" }, { "W", "ए" }, { "o", "उ" }, { "e", "े" }, { "E", "ै" },
            { "u", "ु" }, { "U", "ू" }, { "i", "ि" }, { "q", "ी" }, { "*", "ृ" }, 
            { "&", "ं" }, { "\"", "ः" }, { "_", "ऽ" }, { "=", "्" }, 
            { "[", "्र" }, { "]", "्र" }, { "R", "र्" }, { "|", "र्थं" },

            // Consonants (Stem based)
            { "k-a", "का" }, { "k-", "क" }, { "k", "क्" },
            { "l/a", "ला" }, { "l/", "ल" }, { "l", "ल्" },

            // Consonants (Non-Stem, full by default in Indevr)
            { "da", "दा" }, { "d", "द" },
            { "ra", "रा" }, { "r", "र" },
            { "$a", "टा" }, { "$", "ट" },
            { "!a", "ढा" }, { "!", "ढ" },
            { "@a", "डा" }, { "@", "ड" },

            // Consonants (Standard assembly: C + a + a = Cā, C + a = Ca, C = C-halanta)
            { "%aa", "खा" }, { "%a", "ख" }, { "%", "ख्" },
            { "gaa", "गा" }, { "ga", "ग" }, { "g", "ग्" },
            { "Gaa", "घा" }, { "Ga", "घ" }, { "G", "घ्" },
            { "caa", "चा" }, { "ca", "च" }, { "c", "च्" },
            { "Caa", "छा" }, { "Ca", "छ" }, { "C", "छ्" },
            { "Jaa", "जा" }, { "Ja", "ज" }, { "J", "ज्" },
            { "<aa", "णा" }, { "<a", "ण" }, { "<", "ण्" },
            { "Taa", "ता" }, { "Ta", "त" }, { "T", "त्" },
            { "Qaa", "था" }, { "Qa", "थ" }, { "Q", "थ्" },
            { "Daa", "धा" }, { "Da", "ध" }, { "D", "ध्" },
            { "Naa", "ना" }, { "Na", "न" }, { "N", "न्" },
            { "Paa", "पा" }, { "Pa", "प" }, { "P", "प्" },
            { "Faa", "फा" }, { "Fa", "फ" }, { "F", "फ्" },
            { "baa", "बा" }, { "ba", "ब" }, { "b", "ब्" },
            { ">aa", "भा" }, { ">a", "भ" }, { ">", "भ्" },
            { "Maa", "मा" }, { "Ma", "म" }, { "M", "म्" },
            { "Yaa", "या" }, { "Ya", "य" }, { "Y", "य्" },
            { "v", "व" }, { "V", "व्" }, { "va", "वा" },
            { "Xaa", "शा" }, { "Xa", "श" }, { "X", "श्" },
            { "Zaa", "षा" }, { "Za", "ष" }, { "Z", "ष्" },
            { "Saa", "सा" }, { "Sa", "स" }, { "S", "स्" },
            { "h\"a", "हा" }, { "h\"", "ह" }, { "h", "ह्" },
            { "la", "ला" },
            { "+aa", "क्षा" }, { "+a", "क्ष" }, { "+", "क्ष्" },
            { "J\\aa", "ज्ञा" }, { "J\\a", "ज्ञ" }, { "J\\", "ज्ञ्" },

            // Conjuncts
            { "}", "त्र" }, { "pt", "प्त" }, { "tM", "त्म" },
            { "tn", "त्न" }, { "tva", "त्व" }, { "tv", "त्व्" },
            { "db", "द्भ" }, { "dm", "द्म" }, { "dy", "द्य" },
            { "dv", "द्व" }, { "dd", "द्द" }, { "dH", "द्ध" },
            { "nd", "न्द" }, { "nD", "न्ध" }, { "nt", "न्त" },
            { "nQ", "न्थ" }, { "nb", "न्ब" }, { "n>", "न्भ" },
            { "nm", "न्म" }, { "ny", "न्य" }, { "nv", "न्व" },
            { "ns", "न्स" }, { "st", "स्त" }, { "sT", "स्थ" },
            { "sp", "स्प" }, { "sm", "स्म" }, { "sy", "स्य" },
            { "sv", "स्व" }, { "sn", "स्न" }, { "by", "ब्य" },
            { "vy", "व्य" }, { "ly", "ल्य" }, { "py", "प्य" },
            { "my", "म्य" }, { "Gy", "घ्य" }, { "Cy", "छ्य" },
            { "Jy", "ज्य" }, { "Ty", "त्य" }, { "Qy", "थ्य" },
            { "Dy", "ध्य" }, { ">y", "भ्य" }, { "Xy", "श्य" },
            { "Zy", "ष्य" }, { "hy", "ह्य" }, { "hm", "ह्म" },
            { "hl", "ह्ल" }, { "hv", "ह्व" }, { "hn", "ह्न" },
            { "hN", "ह्ण" },

            { "O", "ऊ" }, { "H", "ञ्" }, { "m", "ँ" },
            { "\\'82", " - " }, { "\\'f8", "ह्म" }, { "\\'89", "ऋ" },
            { "\\'d5", "द्द" }, { "\\'8c", "ैर्" }, { "\\'fb", "ह्व" },
            { "\\'d2", "त्न" }, { "\\'eb", "श्ल" }, { "\\'81", "्र" },
            { "\\'da", "द्म" }, { "\\'d3", "द्ग" }, { "\\'a4", "क्त्व" },
            { "\\'f6", "ह्न" }, { "\\'f5", "ह्ण" }, { "\\'bb", "ङ्क्त" },
            { "\\'d8", "द्ब" }, { "\\'e2", "प्ल" }, { "\\'ba", "ङ्घ" },
            { "\\'87", "झ" }, { "\\'fa", "ह्ल" }, { "\\'b0", "ङ्ग्र" },
            { "\\'97", "ऽ" }, { "\\'83", "।" }, { "\\'ae", "ङ्ग" },
            { "\\'a1-", "क्क" }, { "\\'a1", "क्क" }, { "\\'cd", "ड्" },
            { "\\'c1", "च्ञ" }, { "\\'9c", "लृ" }, { "\\'cc", "ड्भ" },
            { "\\'ce", "ढ्व" }, { "\\'a9-", "क्ल" }, { "\\'a9", "क्ल" },
            { "\\'80", "ज्ञ्" }, { "\\'8a", "र्ऋ" }, { "\\'8b", "ध्र्य" }
        };

        public static string Decode(string indevr)
        {
            if (string.IsNullOrEmpty(indevr)) return string.Empty;

            string result = indevr;

            // Pass 1: Decode RTF hex escapes safely BEFORE single-letter rules run.
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\\'([0-9a-fA-F]{2})", m =>
            {
                string hex = m.Value.ToLower();
                if (VerifiedMappings.TryGetValue(hex, out var repl)) return repl;
                return $"\uE001{hex}\uE002";
            });

            // Pass 2: Longest-first replacement for non-hex keys
            var nonHexKeys = VerifiedMappings.Keys
                .Where(k => !k.StartsWith("\\'") && k != "\\")
                .OrderByDescending(k => k.Length)
                .ToList();

            foreach (var key in nonHexKeys)
            {
                result = result.Replace(key, VerifiedMappings[key]);
            }

            // Restore unmapped hex placeholders
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\uE001(\\'[0-9a-fA-F]{2})\uE002", m => m.Groups[1].Value);

            return result;
        }

        public static string ExtractIndevrText(string part)
        {
            if (string.IsNullOrEmpty(part)) return string.Empty;
            string text = part.Replace("\\line", "\n").Replace("\\par", "");
            const string BACKSLASH_PLACEHOLDER = "\uE000";
            text = text.Replace("\\\\", BACKSLASH_PLACEHOLDER);

            text = System.Text.RegularExpressions.Regex.Replace(text, "\\\\\\*[a-zA-Z]+(\\d+)?", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\\\[a-zA-Z]+(-?[0-9]+)? ?", "");
            text = text.Replace("{", "").Replace("}", "");

            text = text.Replace(BACKSLASH_PLACEHOLDER, "\\");
            return text.Trim();
        }
    }
}
