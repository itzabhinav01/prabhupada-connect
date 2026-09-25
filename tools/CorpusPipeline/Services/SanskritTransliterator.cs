using System;
using System.Text;
using System.Collections.Generic;

namespace VedaBaseModern.CorpusPipeline.Services
{
    public static class SanskritTransliterator
    {
        private static readonly Dictionary<string, string> IndependentVowels = new(StringComparer.Ordinal)
        {
            { "ai", "ऐ" }, { "au", "औ" },
            { "a", "अ" }, { "ā", "आ" },
            { "i", "इ" }, { "ī", "ई" },
            { "u", "उ" }, { "ū", "ऊ" },
            { "ṛ", "ऋ" }, { "ṝ", "ॠ" },
            { "ḷ", "ऌ" }, { "e", "ए" },
            { "o", "ओ" }
        };

        private static readonly Dictionary<string, string> DependentVowels = new(StringComparer.Ordinal)
        {
            { "ai", "ै" }, { "au", "ौ" },
            { "a", "" },   { "ā", "ा" },
            { "i", "ि" }, { "ī", "ी" },
            { "u", "ु" }, { "ū", "ू" },
            { "ṛ", "ृ" }, { "ṝ", "ॄ" },
            { "ḷ", "ॢ" }, { "e", "े" },
            { "o", "ो" }
        };

        private static readonly Dictionary<string, string> Consonants = new(StringComparer.Ordinal)
        {
            { "kh", "ख्" }, { "gh", "घ्" },
            { "ch", "छ्" }, { "jh", "झ्" },
            { "ṭh", "ठ्" }, { "ḍh", "ढ्" },
            { "th", "थ्" }, { "dh", "ध्" },
            { "ph", "फ्" }, { "bh", "भ्" },
            { "k", "क्" },  { "g", "ग्" }, { "ṅ", "ङ्" },
            { "c", "च्" },  { "j", "ज्" }, { "ñ", "ञ्" },
            { "ṭ", "ट्" },  { "ḍ", "ड्" }, { "ṇ", "ण्" },
            { "t", "त्" },  { "d", "द्" }, { "n", "न्" },
            { "p", "प्" },  { "b", "ब्" }, { "m", "म्" },
            { "y", "य्" },  { "r", "र्" }, { "l", "ल्" }, { "v", "व्" },
            { "ś", "श्" },  { "ṣ", "ष्" }, { "s", "स्" }, { "h", "ह्" }
        };

        public static string IastToDevanagari(string iast)
        {
            if (string.IsNullOrWhiteSpace(iast)) return string.Empty;

            var sb = new StringBuilder();
            int len = iast.Length;
            int i = 0;
            bool lastWasConsonantWithVirama = false;

            while (i < len)
            {
                // Double danda / single danda
                if (i + 1 < len && iast[i] == '|' && iast[i + 1] == '|')
                {
                    sb.Append("॥");
                    i += 2;
                    lastWasConsonantWithVirama = false;
                    continue;
                }
                if (iast[i] == '|')
                {
                    sb.Append("।");
                    i++;
                    lastWasConsonantWithVirama = false;
                    continue;
                }

                // Avagraha
                if (iast[i] == '\'' || iast[i] == '’')
                {
                    sb.Append("ऽ");
                    i++;
                    lastWasConsonantWithVirama = false;
                    continue;
                }

                // Anusvara
                if (iast[i] == 'ṁ' || iast[i] == 'ṁ')
                {
                    if (lastWasConsonantWithVirama && sb.Length > 0 && sb[^1] == '्')
                    {
                        // Inherent 'a' before anusvara if directly after consonant (e.g. "km" -> k + a + m)
                        sb.Length--; // remove virama
                    }
                    sb.Append("ं");
                    i++;
                    lastWasConsonantWithVirama = false;
                    continue;
                }

                // Visarga
                if (iast[i] == 'ḥ')
                {
                    sb.Append("ः");
                    i++;
                    lastWasConsonantWithVirama = false;
                    continue;
                }

                // Check 2-char consonants first
                string twoChar = (i + 1 < len) ? iast.Substring(i, 2).ToLowerInvariant() : "";
                string oneChar = iast.Substring(i, 1).ToLowerInvariant();

                // Check consonants
                if (!string.IsNullOrEmpty(twoChar) && Consonants.TryGetValue(twoChar, out var c2))
                {
                    sb.Append(c2);
                    i += 2;
                    lastWasConsonantWithVirama = true;
                    continue;
                }
                if (Consonants.TryGetValue(oneChar, out var c1))
                {
                    sb.Append(c1);
                    i++;
                    lastWasConsonantWithVirama = true;
                    continue;
                }

                // Check vowels
                if (!string.IsNullOrEmpty(twoChar) && (lastWasConsonantWithVirama ? DependentVowels : IndependentVowels).TryGetValue(twoChar, out var v2))
                {
                    if (lastWasConsonantWithVirama)
                    {
                        if (sb.Length > 0 && sb[^1] == '्') sb.Length--; // Remove virama
                        sb.Append(v2);
                    }
                    else
                    {
                        sb.Append(v2);
                    }
                    i += 2;
                    lastWasConsonantWithVirama = false;
                    continue;
                }
                if ((lastWasConsonantWithVirama ? DependentVowels : IndependentVowels).TryGetValue(oneChar, out var v1))
                {
                    if (lastWasConsonantWithVirama)
                    {
                        if (sb.Length > 0 && sb[^1] == '्') sb.Length--; // Remove virama
                        sb.Append(v1);
                    }
                    else
                    {
                        sb.Append(v1);
                    }
                    i++;
                    lastWasConsonantWithVirama = false;
                    continue;
                }

                // Hyphen within Sanskrit compound words: skip hyphen and preserve conjunct/vowel state
                if (iast[i] == '-')
                {
                    i++;
                    continue;
                }

                // Whitespace, punctuation, numbers, newlines
                if (lastWasConsonantWithVirama && char.IsWhiteSpace(iast[i]) && sb.Length > 0 && sb[^1] == '्')
                {
                    // Word-ending consonant without trailing vowel: keep virama (halanta)
                }

                sb.Append(iast[i]);
                i++;
                lastWasConsonantWithVirama = false;
            }

            return sb.ToString();
        }
    }
}
