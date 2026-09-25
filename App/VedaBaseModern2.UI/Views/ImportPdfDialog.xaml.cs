using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class ImportPdfDialog : ContentDialog
    {
        public string SelectedFilePath => FilePathBox.Text;
        public string BookTitle => TitleBox.Text;
        public string Author => AuthorBox.Text;
        public string Category => CategoryBox.Text;

        public ImportPdfDialog()
        {
            this.InitializeComponent();
            this.PrimaryButtonClick += ImportPdfDialog_PrimaryButtonClick;
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".pdf");

                // Initialize picker with main window handle for WinUI 3
                if (App.Current.MainWindowInstance != null)
                {
                    IntPtr hwnd = App.Current.MainWindowInstance.WindowHandle;
                    InitializeWithWindow.Initialize(picker, hwnd);
                }

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    FilePathBox.Text = file.Path;
                    if (string.IsNullOrWhiteSpace(TitleBox.Text))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
                        // Clean up underscores and hyphens into spaces
                        string cleaned = nameWithoutExt.Replace('_', ' ').Replace('-', ' ').Trim();
                        TitleBox.Text = cleaned;
                    }
                    StatusMessageText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                StatusMessageText.Text = $"Error choosing file: {ex.Message}";
                StatusMessageText.Visibility = Visibility.Visible;
            }
        }

        private void ImportPdfDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(SelectedFilePath) || !File.Exists(SelectedFilePath))
            {
                StatusMessageText.Text = "Please browse and select a valid .pdf file.";
                StatusMessageText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(BookTitle))
            {
                StatusMessageText.Text = "Please provide a title for the book.";
                StatusMessageText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }
        }
    }
}
