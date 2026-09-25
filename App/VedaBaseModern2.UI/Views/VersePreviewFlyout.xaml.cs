using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VedaBaseModern.Core.Models;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class VersePreviewFlyout : Flyout
    {
        private string? _currentRecordKey;
        public event Action<string>? OpenVerseRequested;

        public VersePreviewFlyout()
        {
            this.InitializeComponent();
        }

        public async Task LoadVerseAsync(string referenceOrKey)
        {
            _currentRecordKey = null;
            ReferenceText.Text = referenceOrKey;
            BookTitleText.Text = "Loading passage...";
            TranslitText.Text = string.Empty;
            TranslationText.Text = string.Empty;

            try
            {
                string? recordKey = await App.Current.ReferenceService.TryResolveExactAsync(referenceOrKey);
                if (string.IsNullOrEmpty(recordKey)) recordKey = referenceOrKey;

                var record = await App.Current.Repository.GetRecordAsync(recordKey);
                if (record != null)
                {
                    _currentRecordKey = record.RecordKey;
                    ReferenceText.Text = record.Reference ?? record.RecordKey;
                    BookTitleText.Text = App.Current.Repository.GetCanonicalChapterHeader(record.BookKey, record.Reference);
                    TranslitText.Text = record.Transliteration ?? string.Empty;
                    TranslationText.Text = record.CleanTranslation ?? record.Translation ?? string.Empty;
                }
                else
                {
                    BookTitleText.Text = "Passage not found in corpus";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VersePreviewFlyout] Error: {ex}");
                BookTitleText.Text = "Could not load preview";
            }
        }

        private void OpenVerseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentRecordKey))
            {
                this.Hide();
                OpenVerseRequested?.Invoke(_currentRecordKey);
            }
        }
    }
}
