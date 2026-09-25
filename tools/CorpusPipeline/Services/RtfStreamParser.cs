using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VedaBaseModern.CorpusPipeline.Services
{
    public class ParsedVerseData
    {
        public string Reference { get; set; } = string.Empty;
        public string RecordKey { get; set; } = string.Empty;
        public string RawDevanagari { get; set; } = string.Empty;
        public string RawTransliteration { get; set; } = string.Empty;
        public string RawSynonyms { get; set; } = string.Empty;
        public string RawTranslation { get; set; } = string.Empty;
        public List<string> RawPurportParagraphs { get; set; } = new();

        public string DecodedDevanagari { get; set; } = string.Empty;
        public string DecodedTransliteration { get; set; } = string.Empty;
        public string DecodedSynonyms { get; set; } = string.Empty;
        public string DecodedTranslation { get; set; } = string.Empty;
        public string DecodedPurports { get; set; } = string.Empty;
    }

    public static class RtfStreamParser
    {
        public static IEnumerable<ParsedVerseData> ParseVersesFromRtf(string rtfPath)
        {
            using var reader = new StreamReader(rtfPath, Encoding.Default);
            var sb = new StringBuilder();
            string currentStyle = "";

            ParsedVerseData? current = null;
            string currentSection = ""; // SYNONYMS, TRANSLATION, PURPORT
            bool inVerse = false;

            int b;
            while ((b = reader.Read()) != -1)
            {
                char c = (char)b;
                sb.Append(c);

                if (sb.Length >= 8 &&
                    sb[sb.Length - 8] == '\\' &&
                    sb[sb.Length - 7] == 'p' &&
                    sb[sb.Length - 6] == 'a' &&
                    sb[sb.Length - 5] == 'r' &&
                    sb[sb.Length - 4] == 'd' &&
                    sb[sb.Length - 3] == ' ' &&
                    sb[sb.Length - 2] == '\\' &&
                    sb[sb.Length - 1] == 's')
                {
                    string styleNum = "";
                    while ((b = reader.Read()) != -1)
                    {
                        char d = (char)b;
                        if (char.IsDigit(d))
                        {
                            styleNum += d;
                        }
                        else
                        {
                            string blockText = sb.ToString(0, sb.Length - 8);
                            if (!string.IsNullOrEmpty(currentStyle))
                            {
                                var processed = ProcessBlock(currentStyle, blockText, ref current, ref currentSection, ref inVerse);
                                if (processed != null)
                                {
                                    FinalizeVerse(processed);
                                    yield return processed;
                                }
                            }

                            currentStyle = styleNum;
                            sb.Clear();
                            sb.Append(d);
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentStyle) && sb.Length > 0)
            {
                var processed = ProcessBlock(currentStyle, sb.ToString(), ref current, ref currentSection, ref inVerse);
                if (processed != null)
                {
                    FinalizeVerse(processed);
                    yield return processed;
                }
            }

            if (current != null && !string.IsNullOrEmpty(current.Reference))
            {
                FinalizeVerse(current);
                yield return current;
            }
        }

        private static ParsedVerseData? ProcessBlock(
            string style,
            string contentBlock,
            ref ParsedVerseData? current,
            ref string currentSection,
            ref bool inVerse)
        {
            ParsedVerseData? finishedVerse = null;

            // Verse reference banner (\s2354)
            if (style == "2354")
            {
                string rawText = ExtractRawText(contentBlock).Trim();
                if (IsVerseReference(rawText))
                {
                    if (current != null && !string.IsNullOrEmpty(current.Reference))
                    {
                        finishedVerse = current;
                    }

                    current = new ParsedVerseData
                    {
                        Reference = rawText
                    };
                    currentSection = "";
                    inVerse = true;
                    return finishedVerse;
                }
            }

            if (current == null || !inVerse) return null;

            // Section markers
            if (style == "2043" || style == "1970")
            {
                string header = ExtractRawText(contentBlock).Trim();
                if (header.Contains("SYNONYMS")) currentSection = "SYNONYMS";
                else if (header.Contains("TRANSLATION")) currentSection = "TRANSLATION";
                else if (header.Contains("PURPORT")) currentSection = "PURPORT";
                return null;
            }

            // TEXT title (e.g. \s2014 "TEXT 6")
            if (style == "2014" || style == "2015")
            {
                return null;
            }

            // Section break / Chapter title styles signal end of verse
            if (style == "3" || style == "4" || style == "5" || style == "6" || style == "8")
            {
                if (current != null && !string.IsNullOrEmpty(current.Reference))
                {
                    finishedVerse = current;
                    current = null;
                    inVerse = false;
                    currentSection = "";
                    return finishedVerse;
                }
            }

            // Route content depending on section
            if (currentSection == "SYNONYMS")
            {
                if (style == "1962")
                {
                    current.RawSynonyms += contentBlock + " ";
                }
            }
            else if (currentSection == "TRANSLATION")
            {
                if (style == "2087")
                {
                    current.RawTranslation += contentBlock + " ";
                }
            }
            else if (currentSection == "PURPORT")
            {
                // In PURPORT section: capture ALL paragraph styles!
                // Exclude empty whitespace or hidden control blocks
                string plain = ExtractRawText(contentBlock).Trim();
                if (!string.IsNullOrEmpty(plain))
                {
                    current.RawPurportParagraphs.Add(contentBlock);
                }
            }
            else // Before SYNONYMS: Verse script & Transliteration
            {
                // Devanagari styles: 696 (standard), 1450 (prose dev), or contains \f48 (Indevr)
                if (style == "696" || style == "1450" || contentBlock.Contains("\\f48"))
                {
                    current.RawDevanagari += contentBlock + " ";
                }
                // Transliteration styles: 9 (standard), 2304 (uvaca), 1451 (ProseVerse), 1452 (Prose Verse)
                else if (style == "9" || style == "2304" || style == "1451" || style == "1452")
                {
                    current.RawTransliteration += contentBlock + " ";
                }
                // Generic italic block before SYNONYMS is transliteration
                else if (contentBlock.Contains("\\i ") || contentBlock.Contains("\\i\\"))
                {
                    string plain = ExtractRawText(contentBlock).Trim();
                    if (!string.IsNullOrEmpty(plain) && !plain.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase))
                    {
                        current.RawTransliteration += contentBlock + " ";
                    }
                }
            }

            return null;
        }

        private static void FinalizeVerse(ParsedVerseData v)
        {
            if (!string.IsNullOrEmpty(v.RawDevanagari))
            {
                string stripped = IndevrDecoder.ExtractIndevrText(v.RawDevanagari);
                v.DecodedDevanagari = IndevrDecoder.Decode(stripped).Trim();
            }

            if (!string.IsNullOrEmpty(v.RawTransliteration))
            {
                string decoded = TransliterationDecoder.Decode(v.RawTransliteration).Trim();
                // Strip trailing Audio bookmark/link present in bg.rtf
                decoded = Regex.Replace(decoded, @"\s+Audio\s*$", "", RegexOptions.IgnoreCase).Trim();
                v.DecodedTransliteration = decoded;
            }

            if (!string.IsNullOrEmpty(v.RawSynonyms))
            {
                v.DecodedSynonyms = TransliterationDecoder.Decode(v.RawSynonyms).Trim();
            }

            if (!string.IsNullOrEmpty(v.RawTranslation))
            {
                v.DecodedTranslation = TransliterationDecoder.Decode(v.RawTranslation).Trim();
            }

            if (v.RawPurportParagraphs.Count > 0)
            {
                var purpSb = new StringBuilder();
                foreach (var p in v.RawPurportParagraphs)
                {
                    string decoded = TransliterationDecoder.Decode(p).Trim();
                    if (!string.IsNullOrEmpty(decoded))
                    {
                        if (purpSb.Length > 0) purpSb.Append("\n\n");
                        purpSb.Append(decoded);
                    }
                }
                v.DecodedPurports = purpSb.ToString();
            }
        }

        private static bool IsVerseReference(string rawText)
        {
            if (string.IsNullOrEmpty(rawText)) return false;

            return rawText.StartsWith("SB ", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("Bg ", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("Cc ", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("Ādi ", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("Ädi ", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("Adi ", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("Madhya ", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("Antya ", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("NoI", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("Iso", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("TLK", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("TQK", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("MM", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("NBS", StringComparison.OrdinalIgnoreCase) ||
                   rawText.StartsWith("mantra", StringComparison.OrdinalIgnoreCase) ||
                   rawText.Equals("Invocation", StringComparison.OrdinalIgnoreCase);
        }

        public static string ExtractRawText(string block)
        {
            if (string.IsNullOrEmpty(block)) return string.Empty;
            string text = block.Replace("\\'c4", "Ā").Replace("\\'e4", "ā");
            text = text.Replace("\\line", " ").Replace("\\par", " ");
            text = Regex.Replace(text, "\\\\\\*[a-zA-Z]+(\\d+)?", "");
            text = Regex.Replace(text, "\\\\[a-zA-Z]+(-?[0-9]+)? ?", "");
            text = Regex.Replace(text, "\\\\'[0-9a-fA-F]{2}", " ");
            text = text.Replace("{", "").Replace("}", "");
            return text.Trim();
        }
    }
}
