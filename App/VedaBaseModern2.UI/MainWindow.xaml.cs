using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VedaBaseModern_UI;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        RootGrid.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;

        // Milestone 7: sensible initial size and a minimum usable size. Neither
        // was set before - WinUI's unconfigured default window size is small
        // enough to make Reading View and Settings feel cramped on first launch.
        // 1280x800 comfortably fits the widest Reading Width setting (1000px
        // content column) plus the NavigationView pane; 900x600 is the smallest
        // size at which the NavigationView pane, Reading View header
        // (Back/Reference/Previous/Next), and Settings cards all still lay out
        // without clipping (verified by code/layout review - see
        // MILESTONE_7_VALIDATION.md for what could and couldn't be confirmed
        // interactively in this environment).
        AppWindow.Resize(new SizeInt32(1280, 800));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 900;
            presenter.PreferredMinimumHeight = 600;
        }

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    /// <summary>
    /// Applies a theme to the whole window by setting RequestedTheme on the
    /// root element - WinUI cascades this to every {ThemeResource ...} lookup
    /// beneath it (i.e. every page, since all of them already use theme
    /// resources exclusively). See MILESTONE_6_SETTINGS_ARCHITECTURE.md.
    /// </summary>
    /// <summary>
    /// HWND handle for WinRT window interop (e.g. FileOpenPicker / FileSavePicker on unpackaged WinUI 3).
    /// </summary>
    public nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public void ApplyTheme(Microsoft.UI.Xaml.ElementTheme theme)
    {
        RootGrid.RequestedTheme = theme;
    }

    public void ApplyCustomWindowChrome(Microsoft.UI.Xaml.Media.Brush background, Microsoft.UI.Xaml.Media.Brush foreground)
    {
        RootGrid.Background = background;
        AppTitleBar.Background = background;
        AppTitleBar.Foreground = foreground;
    }

    public void RestoreWindowChrome()
    {
        RootGrid.ClearValue(Microsoft.UI.Xaml.Controls.Grid.BackgroundProperty);
        AppTitleBar.ClearValue(Microsoft.UI.Xaml.Controls.TitleBar.BackgroundProperty);
        AppTitleBar.ClearValue(Microsoft.UI.Xaml.Controls.TitleBar.ForegroundProperty);
    }
}
