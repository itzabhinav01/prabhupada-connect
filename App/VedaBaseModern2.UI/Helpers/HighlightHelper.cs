using System;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace VedaBaseModern.UI.Helpers
{
    public static class HighlightHelper
    {
        public static readonly DependencyProperty FormattedTextProperty =
            DependencyProperty.RegisterAttached(
                "FormattedText",
                typeof(string),
                typeof(HighlightHelper),
                new PropertyMetadata(null, OnFormattedTextChanged));

        public static string GetFormattedText(DependencyObject obj) => (string)obj.GetValue(FormattedTextProperty);
        public static void SetFormattedText(DependencyObject obj, string value) => obj.SetValue(FormattedTextProperty, value);

        private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock)
            {
                textBlock.Inlines.Clear();
                string text = e.NewValue as string ?? "";
                if (string.IsNullOrEmpty(text)) return;

                // Split on sentinel tokens «...» or legacy brackets [...]
                var parts = Regex.Split(text, @"([«\[].*?[»\]])");
                foreach (var part in parts)
                {
                    if ((part.StartsWith("«") && part.EndsWith("»")) || (part.StartsWith("[") && part.EndsWith("]")))
                    {
                        string matchText = part.Length >= 2 ? part.Substring(1, part.Length - 2) : part;
                        textBlock.Inlines.Add(new Run
                        {
                            Text = matchText,
                            FontWeight = FontWeights.Bold
                        });
                    }
                    else if (!string.IsNullOrEmpty(part))
                    {
                        textBlock.Inlines.Add(new Run
                        {
                            Text = part
                        });
                    }
                }
            }
        }
    }
}
