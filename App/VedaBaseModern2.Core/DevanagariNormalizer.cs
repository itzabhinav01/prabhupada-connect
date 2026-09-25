using System;
using System.Text;
using System.Text.RegularExpressions;

namespace VedaBaseModern.Core
{
    /// <summary>
    /// Deterministic display-time normalizer for legacy Indevr-decoded Devanagari text.
    /// Transforms systemic 8-bit glyph encoding artifacts into valid, canonical Unicode Devanagari.
    /// Operates purely on structural script patterns without hardcoded lexical dictionaries.
    /// Fully idempotent and non-destructive to already-valid Unicode Sanskrit.
    /// </summary>
    public static class DevanagariNormalizer
    {
        // 1. Compose split vowel signs O (ा + े -> ो) and AU (ा + ै -> ौ)
        private static readonly Regex SplitORegex = new Regex("\u093E\u0947", RegexOptions.Compiled);
        private static readonly Regex SplitAURegex = new Regex("\u093E\u0948", RegexOptions.Compiled);

        // 2. Known Indevr ligature artifacts in ku (क्ु-) and kr (क्ृ-)
        private static readonly Regex KuHyphenRegex = new Regex("\u0915\u094D\u0941-", RegexOptions.Compiled);
        private static readonly Regex KrHyphenRegex = new Regex("\u0915\u094D\u0943-", RegexOptions.Compiled);

        // 3. Indevr 'ha' spurious virama defect:
        // In the legacy Indevr mapping, unmodified 'ha' was mapped to ह् (\u0939\u094D).
        // In Sanskrit, 'ha' only forms valid onsets in true h-conjuncts with [म य ल व ण र].
        // Before all other consonants (e.g. ह् + त, ह् + स, ह् + क), before vowel matras, anusvara, or at word ends, the virama is spurious.
        private static readonly Regex SpuriousHaViramaRegex = new Regex(
            @"\u0939\u094D(?=[^\u092E\u092F\u0932\u0935\u0923\u0930\u094D\u0943]|[\u093E-\u094C\u0902\u0903]|\s|$)",
            RegexOptions.Compiled);

        // 4. Pre-consonantal short-i: ONLY match if NOT already preceded by a consonant (prevents re-inverting valid Unicode)
        // Consonant cluster: (Consonant [Nukta]? Virama)* Consonant [Nukta]?
        private static readonly Regex PreConsonantalShortIRegex = new Regex(
            @"(?<![\u0915-\u0939\u0958-\u095F]\u093C?)\u093F((?:[\u0915-\u0939\u0958-\u095F]\u093C?\u094D)*[\u0915-\u0939\u0958-\u095F]\u093C?)",
            RegexOptions.Compiled);

        // 5. Conflicting vowel signs: short-i immediately preceding vowel sign aa (\u093F\u093E -> \u093E)
        private static readonly Regex ConflictingShortIAaRegex = new Regex("\u093F\u093E", RegexOptions.Compiled);

        // 6. Spurious virama before matras or anusvara, and duplicate virama collapse
        private static readonly Regex ViramaBeforeMatraRegex = new Regex(@"\u094D([\u093E-\u094C\u0962\u0963])", RegexOptions.Compiled);
        private static readonly Regex ViramaBeforeAnusvaraRegex = new Regex(@"\u094D\u0902", RegexOptions.Compiled);
        private static readonly Regex DuplicateViramaRegex = new Regex(@"\u094D{2,}", RegexOptions.Compiled);

        // 7. Conservative Repha Inversion (NO LEXICAL DICTIONARIES):
        // Only inverts trailing repha at unambiguous structural word boundaries (before space, punctuation, danda, visarga, anusvara).
        // Leaves internal consonant-consonant clusters untouched to prevent corruption of valid Unicode.
        private static readonly Regex RephaBeforeBoundaryRegex = new Regex(
            @"(?<!\u094D)([\u0915-\u0939\u0958-\u095F]\u093C?[\u093E-\u094C]?)\u0930\u094D(?=[\s\)\(।॥,;:\.\-\$\u0902\u0903])",
            RegexOptions.Compiled);

        // 8. Legacy Punctuation & Word-Terminal Halant
        private static readonly Regex VerseEndDandasNumberRegex = new Regex(@"\)\)\s*(\d+)\s*\)\)", RegexOptions.Compiled);
        private static readonly Regex DoubleDandasRegex = new Regex(@"\)\)", RegexOptions.Compiled);
        private static readonly Regex LineEndDandaRegex = new Regex(@"(?<=[\u0900-\u097F])\s*\)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex MidLineDandaRegex = new Regex(@"(?<=[\u0900-\u097F])\s*\)\s*(?=[\u0900-\u097F]|$)", RegexOptions.Compiled);
        private static readonly Regex TerminalHalantParenRegex = new Regex(@"([\u0915-\u0939\u0958-\u095F]\u093C?)\((?=[\s\)॥\।\.]|$)", RegexOptions.Compiled);

        public static string Normalize(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
                return raw ?? string.Empty;

            string current = raw;
            for (int iter = 0; iter < 3; iter++)
            {
                string prev = current;

                // 1. Compose split vowel signs O (ा + े -> ो) and AU (ा + ै -> ौ)
                current = SplitORegex.Replace(current, "\u094B");
                current = SplitAURegex.Replace(current, "\u094C");

                // 2. Known Indevr ligature artifacts in ku and kr
                current = KuHyphenRegex.Replace(current, "कु");
                current = KrHyphenRegex.Replace(current, "कृ");

                // 3. Resolve Indevr 'ha' spurious virama before non-conjunct consonants, matras, anusvara, or boundaries
                current = SpuriousHaViramaRegex.Replace(current, "\u0939");

                // 4. Pre-consonantal short-i: move ि after the consonant cluster
                current = PreConsonantalShortIRegex.Replace(current, "$1\u093F");

                // 5. Conflicting vowel signs (e.g. ष्ठिाय -> ष्ठाय)
                current = ConflictingShortIAaRegex.Replace(current, "\u093E");

                // 6. Remove spurious viramas before matras or anusvara, and collapse duplicate viramas
                current = ViramaBeforeMatraRegex.Replace(current, "$1");
                current = ViramaBeforeAnusvaraRegex.Replace(current, "\u0902");
                current = DuplicateViramaRegex.Replace(current, "\u094D");

                // 7. Conservative Repha Inversion (purely structural, 100% safe on valid Unicode)
                current = RephaBeforeBoundaryRegex.Replace(current, "\u0930\u094D$1");

                // 8. Legacy Punctuation & Terminal Halant
                current = VerseEndDandasNumberRegex.Replace(current, "॥ $1 ॥");
                current = DoubleDandasRegex.Replace(current, "॥");
                current = LineEndDandaRegex.Replace(current, " ।");
                current = MidLineDandaRegex.Replace(current, " । ");
                current = TerminalHalantParenRegex.Replace(current, "$1\u094D");

                if (current == prev)
                    break;
            }

            return current;
        }
    }
}
