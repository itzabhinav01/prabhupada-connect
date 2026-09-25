using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using VedaBaseModern.Core.Models;
using VedaBaseModern.Core.Registry;
using VedaBaseModern.Core.Repositories;
using VedaBaseModern.Core.Services;
using VedaBaseModern.UI.Services;

namespace VedaBaseModern_UI;

public partial class App : Application
{
    private Window? _window;

    public static new App Current => (App)Application.Current;
    public ICorpusRegistry CorpusRegistry { get; }
    public IBookRegistry BookRegistry { get; }
    public CorpusDescriptor CanonicalCorpusDescriptor { get; }
    public ICorpusRepository Repository { get; }
    public IUserRepository UserRepository { get; }
    public ISettingsService SettingsService { get; }
    public ISyncMetadataService SyncMetadataService { get; }
    public IUnifiedSearchService SearchService { get; }
    public DirectReferenceService ReferenceService { get; }
    public IResearchDataBackupService BackupService { get; }
    public ICredentialStorageService CredentialStorage { get; }
    public IResearchSyncService SyncService { get; }
    public CustomThemeService CustomThemeService { get; }
    public ReadingPreferencesService ReadingPreferencesService { get; }
    public ConcordanceService ConcordanceService { get; }
    public BookImportService BookImportService { get; }

    public AppTheme StartupTheme => _startupTheme;
    private AppTheme _startupTheme = AppTheme.System;

    public App()
    {
        this.UnhandledException += (sender, args) =>
        {
            try
            {
                System.IO.File.WriteAllText(@"C:\VedaBaseModern2\crash.txt", $"Xaml Unhandled: {args.Message}\n{args.Exception}\nStackTrace: {args.Exception?.StackTrace}");
            }
            catch { }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            try
            {
                System.IO.File.AppendAllText(@"C:\VedaBaseModern2\crash.txt", $"\nDomain Unhandled: {args.ExceptionObject}");
            }
            catch { }
        };
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            try
            {
                System.IO.File.AppendAllText(@"C:\VedaBaseModern2\crash.txt", $"\nTask Unhandled: {args.Exception}");
            }
            catch { }
        };

        this.InitializeComponent();

        // Auto-discover Database directory relative to execution location or fallback
        string baseDir = AppContext.BaseDirectory;
        string? dbDir = null;
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, "Database");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "prabhupada_corpus.db")))
            {
                dbDir = candidate;
                break;
            }
            current = current.Parent;
        }

        if (string.IsNullOrEmpty(dbDir))
        {
            string[] fallbacks = new[]
            {
                @"C:\vedabase versions\modern vedabase v2\Database",
                @"C:\vedabase versions\modern vedabase v2",
                @"C:\VedaBaseModern2\Database",
                @"C:\VedaBaseModern\Database"
            };
            foreach (var fb in fallbacks)
            {
                if (Directory.Exists(fb) && File.Exists(Path.Combine(fb, "prabhupada_corpus.db")))
                {
                    dbDir = fb;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(dbDir))
        {
            dbDir = @"C:\vedabase versions\modern vedabase v2\Database";
        }
        if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);

        string corpusDbPath = Path.Combine(dbDir, "prabhupada_corpus.db");
        string userDbPath = Path.Combine(dbDir, "user.db");

        var bookRegistry = new BookRegistry();
        BookRegistry = bookRegistry;

        var corpusRegistry = new CorpusRegistry();
        CorpusRegistry = corpusRegistry;

        var descriptor = Task.Run(async () => await corpusRegistry.InitializeAsync(corpusDbPath)).GetAwaiter().GetResult();
        CanonicalCorpusDescriptor = descriptor;

        if (descriptor.Status == CorpusStatus.Active)
        {
            Task.Run(async () => await bookRegistry.InitializeFromDatabaseAsync(descriptor.DatabasePath)).GetAwaiter().GetResult();
            Repository = new SqliteCorpusRepository(descriptor.DatabasePath, bookRegistry);
        }
        else
        {
            Repository = new SqliteCorpusRepository(descriptor.DatabasePath ?? "", bookRegistry);
        }

        UserRepository = new SqliteUserRepository(userDbPath);
        SettingsService = new SqliteSettingsService(userDbPath);
        SyncMetadataService = new SyncMetadataService(UserRepository);
        SearchService = new UnifiedSearchService(Repository, UserRepository);
        ReferenceService = new DirectReferenceService(Repository);
        BackupService = new ResearchDataBackupService(UserRepository, SettingsService, userDbPath);
        CredentialStorage = new WindowsCredentialStorageService();
        SyncService = new ResearchSyncService(UserRepository, BackupService, CredentialStorage);
        CustomThemeService = new CustomThemeService();
        ReadingPreferencesService = new ReadingPreferencesService();
        ConcordanceService = new ConcordanceService(descriptor.DatabasePath ?? corpusDbPath);
        BookImportService = new BookImportService(descriptor.DatabasePath ?? corpusDbPath, BookRegistry);

        try
        {
            Task.Run(async () => await ReadingPreferencesService.LoadPreferencesAsync()).Wait();
        }
        catch { }

        try
        {
            Task.Run(async () => await UserRepository.InitializeAsync()).Wait();
            var settings = Task.Run(async () => await SettingsService.GetSettingsAsync()).GetAwaiter().GetResult();
            _startupTheme = settings.Theme;
        }
        catch
        {
            _startupTheme = AppTheme.System;
        }
    }

    public static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        var mainWindow = (MainWindow)_window;
        MainWindowInstance = mainWindow;

        if (_startupTheme == AppTheme.Custom)
        {
            var customTheme = Task.Run(async () => await CustomThemeService.LoadThemeAsync()).GetAwaiter().GetResult();
            CustomThemeService.ApplyCustomTheme(customTheme, mainWindow);
        }
        else
        {
            mainWindow.ApplyTheme(ToElementTheme(_startupTheme));
        }

        _window.Activate();

        // Trigger automatic scheduled local research backup in background
        _ = Task.Run(async () =>
        {
            try
            {
                await BackupService.EnsureAutomaticLocalBackupAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Auto-backup on startup failed: {ex}");
            }
        });
    }

    public MainWindow? MainWindowInstance { get; private set; }

    public event Action<AppTheme>? ThemeChanged;

    public void NotifyThemeChanged(AppTheme theme)
    {
        ThemeChanged?.Invoke(theme);
    }

    public void ApplyAppTheme(AppTheme theme)
    {
        if (theme == AppTheme.Custom)
        {
            var customTheme = Task.Run(async () => await CustomThemeService.LoadThemeAsync()).GetAwaiter().GetResult();
            CustomThemeService.ApplyCustomTheme(customTheme, MainWindowInstance);
        }
        else
        {
            var elemTheme = ToElementTheme(theme);
            CustomThemeService.RemoveCustomTheme(elemTheme, MainWindowInstance);
            MainWindowInstance?.ApplyTheme(elemTheme);
        }
        ThemeChanged?.Invoke(theme);
    }

    public async Task<AppTheme> ToggleThemeAsync()
    {
        var settings = await SettingsService.GetSettingsAsync();
        bool isCurrentlyDark;
        if (settings.Theme == AppTheme.System)
        {
            isCurrentlyDark = (MainWindowInstance?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark
                || Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
        else if (settings.Theme == AppTheme.Custom)
        {
            isCurrentlyDark = !string.Equals(CustomThemeService.ActiveCustomTheme?.BaseTheme, "Light", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            isCurrentlyDark = settings.Theme == AppTheme.Dark;
        }

        var newTheme = isCurrentlyDark ? AppTheme.Light : AppTheme.Dark;
        await SettingsService.SetThemeAsync(newTheme);
        ApplyAppTheme(newTheme);
        return newTheme;
    }
}
