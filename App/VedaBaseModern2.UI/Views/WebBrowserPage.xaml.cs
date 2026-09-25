using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using VedaBaseModern_UI;

namespace VedaBaseModern.UI.Views;

public sealed partial class WebBrowserPage : Page
{
    private string _currentUrl = "about:blank";
    private bool _isInitialized;

    public WebBrowserPage()
    {
        this.InitializeComponent();
        this.NavigationCacheMode = NavigationCacheMode.Required;
        this.Loaded += WebBrowserPage_Loaded;
    }

    private async void WebBrowserPage_Loaded(object sender, RoutedEventArgs e)
    {
        await EnsureBrowserInitializedAsync();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string url && !string.IsNullOrWhiteSpace(url))
        {
            _currentUrl = url;
            UrlTextBox.Text = url;
            await EnsureBrowserInitializedAsync();
            NavigateToUrl(url);
        }
    }

    private async Task EnsureBrowserInitializedAsync()
    {
        if (_isInitialized && BrowserWebView.CoreWebView2 != null)
            return;

        try
        {
            var env = await ReadingPage.GetSharedEnvironmentAsync();
            await BrowserWebView.EnsureCoreWebView2Async(env);

            var core = BrowserWebView.CoreWebView2;
            if (core != null && !_isInitialized)
            {
                _isInitialized = true;

                core.NavigationStarting += Core_NavigationStarting;
                core.NavigationCompleted += Core_NavigationCompleted;
                core.DocumentTitleChanged += Core_DocumentTitleChanged;
                core.NewWindowRequested += Core_NewWindowRequested;

                if (!string.IsNullOrWhiteSpace(_currentUrl) && _currentUrl != "about:blank")
                {
                    NavigateToUrl(_currentUrl);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowserPage] Initialization error: {ex}");
        }
    }

    private void NavigateToUrl(string input)
    {
        if (BrowserWebView.CoreWebView2 == null) return;

        string target = input.Trim();
        if (string.IsNullOrWhiteSpace(target)) return;

        if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            if (target.Contains(".") && !target.Contains(" "))
            {
                target = "https://" + target;
            }
            else
            {
                // Search query
                target = "https://www.google.com/search?q=" + Uri.EscapeDataString(target);
            }
        }

        _currentUrl = target;
        UrlTextBox.Text = target;
        BrowserWebView.CoreWebView2.Navigate(target);
    }

    private void Core_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        LoadingRing.IsActive = true;
        _currentUrl = args.Uri;
        UrlTextBox.Text = args.Uri;
    }

    private void Core_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingRing.IsActive = false;
        BackButton.IsEnabled = BrowserWebView.CanGoBack;
        ForwardButton.IsEnabled = BrowserWebView.CanGoForward;
    }

    private void Core_DocumentTitleChanged(CoreWebView2 sender, object args)
    {
        string title = sender.DocumentTitle;
        if (!string.IsNullOrWhiteSpace(title) && MainPage.Current != null)
        {
            // Truncate if title is very long for tab header
            string tabTitle = title.Length > 25 ? title.Substring(0, 22) + "..." : title;
            MainPage.Current.UpdateTabHeaderForContent(this, tabTitle, "\uE774");
        }
    }

    private void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (!string.IsNullOrWhiteSpace(args.Uri))
        {
            if (MainPage.Current != null)
            {
                string host = "Web";
                try { host = new Uri(args.Uri).Host; } catch { }
                MainPage.Current.CreateNewTab(host, "\uE774", typeof(WebBrowserPage), args.Uri);
            }
            else
            {
                NavigateToUrl(args.Uri);
            }
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserWebView.CanGoBack) BrowserWebView.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserWebView.CanGoForward) BrowserWebView.GoForward();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        BrowserWebView.Reload();
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            NavigateToUrl(UrlTextBox.Text);
        }
    }

    private async void ExternalButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Uri.TryCreate(_currentUrl, UriKind.Absolute, out var uri))
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebBrowserPage] External browser launch error: {ex}");
        }
    }
}
