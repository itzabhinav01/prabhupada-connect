using System;
using System.Collections.Generic;
using System.Text;

namespace VedaBaseModern.Core.Services
{
    /// <summary>
    /// Formats raw corpus prose (Translation and Purports) by collapsing Folio's
    /// fixed-width 70-column line-wrap artifacts into continuous flowing paragraphs.
    /// Handles mid-word split rejoining (e.g. "re\r\nsult" -> "result", "P\r\nāṇḍu" -> "Pāṇḍu")
    /// and word-boundary spacing (e.g. "King\r\nDrupada" -> "King Drupada").
    /// </summary>
    public static class ProseFormatter
    {
        private static bool IsWrapBoundaryChar(char c) =>
            char.IsWhiteSpace(c) || c == '-' || c == '‐' || c == '‑' || c == '–' || c == '—';

        private static bool IsClauseEndingPunctuation(char c) =>
            c is ';' or ',' or ':' or ')' or ']' or '!' or '?';

        private static bool IsLineBreakAt(string s, int i, out int length)
        {
            if (i < s.Length && s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n') { length = 2; return true; }
            if (i < s.Length && (s[i] == '\n' || s[i] == '\r')) { length = 1; return true; }
            length = 0;
            return false;
        }

        private static bool IsSanskritDiacriticLetter(char c) =>
            c is 'ā' or 'Ā' or 'ī' or 'Ī' or 'ū' or 'Ū' or 'ṛ' or 'Ṛ' or 'ṝ' or 'Ṝ'
              or 'ḷ' or 'Ḷ' or 'ḹ' or 'Ḹ' or 'ñ' or 'Ñ' or 'ṅ' or 'Ṅ'
              or 'ṇ' or 'Ṇ' or 'ṭ' or 'Ṭ' or 'ḍ' or 'Ḍ' or 'ṣ' or 'Ṣ' or 'ś' or 'Ś'
              or 'ḥ' or 'Ḥ' or 'ṁ' or 'Ṁ';

        private static string ExtractTrailingFragment(StringBuilder sb)
        {
            int start = sb.Length;
            while (start > 0 && !IsWrapBoundaryChar(sb[start - 1])) start--;
            return sb.ToString(start, sb.Length - start);
        }

        private static readonly HashSet<string> CommonEnglishWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "a","an","the","this","that","these","those","some","any","all","both","each","every","either","neither","no","none",
            "i","you","he","she","it","we","they","him","her","them","his","its","their","my","your","our","who","whom","whose","which","what",
            "am","is","are","was","were","be","been","being","has","have","had","do","does","did","done",
            "can","could","will","would","shall","should","may","might","must",
            "and","or","but","nor","so","yet","if","unless","because","although","though","while","when","where","as","than","that",
            "in","on","at","by","to","of","for","from","with","without","into","onto","upon","over","under","between","among","through",
            "during","before","after","above","below","near","about","against","across","along","around","behind","beside","beyond",
            "not","also","only","even","still","again","then","therefore","however","thus","hence","naturally","actually","certainly",
            "indeed","simply","clearly","especially","particularly","generally","usually","always","never","sometimes","often","already",
            "just","very","quite","rather","more","most","less","least","much","many","few","little","such","other","another","same",
            "one","two","three","first","second","third","last","next","own","new","old","great","high","low","good","bad","true","false",
            "real","pure","whole","entire","complete","perfect","supreme","different","various","several","certain",
            "person","persons","people","man","men","woman","king","kings","god","godhead","lord","personality",
            "soul","souls","body","bodies","mind","minds","life","lives","world","material","spiritual","devotee","devotees",
            "service","knowledge","activity","activities","consciousness","nature","natures","position","positions","class","classes",
            "society","circumstance","circumstances","theory","theories","thread","threads","family","families","sacrifice","sacrifices",
            "scripture","scriptures","evidence","example","examples","birthright","caste","castes","statement","statements","stated",
            "states","state","book","books","guide","guides","word","words","time","times","place","places","thing","things","way","ways",
            "part","parts","point","points","process","processes","principle","principles","practice","practices","duty","duties",
            "member","members","fact","facts","idea","ideas","proof","proofs","reason","reasons","result","results","cause","causes",
            "effect","effects","form","forms","force","forces","power","powers","truth","truths","quality","qualities",
            "called","named","known","said","says","say","claim","claims","claimed","accept","accepts","accepted","think","thinks",
            "thought","know","knows","see","sees","seen","come","comes","came","go","goes","went","gone","give","gives","given",
            "take","takes","taken","make","makes","made","find","finds","found","become","becomes","became","receive","receives"
        };

        /// <summary>
        /// Cleans a prose block by removing fixed-width hard-wrap artifacts while preserving
        /// true paragraph breaks (double newlines).
        /// </summary>
        public static string CleanProse(string? original)
        {
            if (string.IsNullOrWhiteSpace(original)) return string.Empty;

            var sb = new StringBuilder(original.Length);
            int i = 0;

            while (i < original.Length)
            {
                if (IsLineBreakAt(original, i, out int len))
                {
                    bool isParagraphBreak = IsLineBreakAt(original, i + len, out _);
                    if (isParagraphBreak)
                    {
                        // Preserve paragraph breaks (emit double newline)
                        sb.Append("\n\n");
                        while (i < original.Length && (original[i] == '\r' || original[i] == '\n' || original[i] == ' ' || original[i] == '\t'))
                        {
                            i++;
                        }
                        continue;
                    }

                    // Single line break: inspect adjacent characters
                    bool spaceAlreadyBefore = sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]);
                    int afterIdx = i + len;
                    bool spaceAlreadyAfter = afterIdx < original.Length && char.IsWhiteSpace(original[afterIdx]);

                    if (spaceAlreadyBefore || spaceAlreadyAfter)
                    {
                        i += len;
                        continue;
                    }

                    char prevChar = sb.Length > 0 ? sb[sb.Length - 1] : '\0';
                    bool hasClausePunctuation = IsClauseEndingPunctuation(prevChar);
                    bool hasPeriodWordEnd = prevChar == '.' && (afterIdx >= original.Length || !char.IsDigit(original[afterIdx]));

                    string trailingFragment = ExtractTrailingFragment(sb);
                    bool trailingHasDiacritic = false;
                    foreach (char ch in trailingFragment)
                    {
                        if (IsSanskritDiacriticLetter(ch)) { trailingHasDiacritic = true; break; }
                    }

                    bool trailingIsRecognizedWord = !trailingHasDiacritic && CommonEnglishWords.Contains(trailingFragment);

                    if (hasClausePunctuation || hasPeriodWordEnd || trailingIsRecognizedWord)
                    {
                        sb.Append(' ');
                    }

                    i += len;
                }
                else
                {
                    sb.Append(original[i]);
                    i++;
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Splits purports into individual clean paragraphs for UI display.
        /// </summary>
        public static IReadOnlyList<string> GetCleanParagraphs(string? original)
        {
            if (string.IsNullOrWhiteSpace(original)) return Array.Empty<string>();

            // First split by multi-line breaks to isolate distinct paragraphs
            var rawParagraphs = original.Split(new[] { "\r\n\r\n", "\n\n", "\r\r" }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>(rawParagraphs.Length);

            foreach (var raw in rawParagraphs)
            {
                var cleaned = CleanProse(raw);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    result.Add(cleaned);
                }
            }

            return result;
        }
    }
}
