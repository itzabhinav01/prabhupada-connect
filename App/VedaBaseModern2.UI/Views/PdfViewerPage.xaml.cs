using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VedaBaseModern.Core.Models;
using VedaBaseModern.UI.Services;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views
{
    public sealed partial class PdfViewerPage : Page
    {
        private BookNode? _currentBook;
        private string? _pdfPath;

        public PdfViewerPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is BookNode book)
            {
                _currentBook = book;
                _pdfPath = book.PdfPath;
                BookTitleText.Text = book.Title;
                AuthorText.Text = book.Author;
            }
            else if (e.Parameter is string path && File.Exists(path))
            {
                _pdfPath = path;
                BookTitleText.Text = Path.GetFileNameWithoutExtension(path);
                AuthorText.Text = "PDF Document";
            }

            if (!string.IsNullOrWhiteSpace(_pdfPath) && File.Exists(_pdfPath))
            {
                await InitializePdfViewerAsync(_pdfPath);
            }
            else
            {
                LoadingRing.IsActive = false;
                BookTitleText.Text = "PDF file not found";
            }
        }

        private async Task InitializePdfViewerAsync(string pdfPath)
        {
            try
            {
                var env = await ReadingPage.GetSharedEnvironmentAsync();
                await PdfWebView.EnsureCoreWebView2Async(env);

                string folder = Path.GetDirectoryName(pdfPath) ?? "";
                string fileName = Path.GetFileName(pdfPath);

                PdfWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "pdfhost.local",
                    folder,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                PdfWebView.NavigationCompleted += (s, args) =>
                {
                    LoadingRing.IsActive = false;
                };

                // Navigate directly to virtual host PDF URL
                PdfWebView.CoreWebView2.Navigate($"https://pdfhost.local/{Uri.EscapeDataString(fileName)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdfViewerPage] Failed to initialize PDF: {ex}");
                LoadingRing.IsActive = false;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }

        private async void RemoveBookButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBook == null) return;

            var dialog = new ContentDialog
            {
                Title = "Remove PDF Book",
                Content = $"Are you sure you want to remove '{_currentBook.Title}' from your Library? This will remove the book and its file.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            CustomThemeService.SyncDialogTheme(dialog, this.XamlRoot);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (App.Current.BookImportService != null)
                {
                    await App.Current.BookImportService.DeleteBookAsync(_currentBook.BookKey);
                }

                if (MainPage.Current != null)
                {
                    await MainPage.Current.ViewModel.LoadBooksAsync(true);
                }

                if (this.Frame.CanGoBack)
                {
                    this.Frame.GoBack();
                }
            }
        }
    }
}
