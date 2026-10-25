using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using AiraAssistant.Services.Ai;
using AiraAssistant.Services.Audio;
using AiraAssistant.Services.History;
using AiraAssistant.Services.Speech;
using AiraAssistant.Services.SystemControl;
using AiraAssistant.UI.ViewModels;
using AiraAssistant.UI.Views;
using AiraAssistant.Infrastructure;
using AiraAssistant.Infrastructure.Logging;

namespace AiraAssistant;

/// <summary>
/// WPF application entry. Wires the DI container, configures logging,
/// and starts the WPF message loop on the STA thread.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Surface unhandled exceptions instead of crashing silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Theme BEFORE any window exists so every brush is app-level.
            var config = _serviceProvider.GetRequiredService<Models.AppConfig>();
            ThemeEngine.Apply(config.Ui.Theme);

            var bootstrapper = _serviceProvider.GetRequiredService<AppBootstrapper>();
            await bootstrapper.InitializeAsync().ConfigureAwait(true);

            var tray = _serviceProvider.GetRequiredService<TrayIconService>();
            var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            tray.Initialize(mainViewModel);

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            // Last-ditch error reporting before services are even ready.
            var fallbackPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aira", "Logs", "startup-fatal.log");
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath)!);
            File.AppendAllText(fallbackPath, $"{DateTime.UtcNow:O} FATAL {ex}{Environment.NewLine}");

            MessageBox.Show(
                $"Aira failed to start: {ex.Message}\n\nSee log at:\n{fallbackPath}",
                "Aira",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _serviceProvider?.GetService<TrayIconService>()?.Dispose();
            (_serviceProvider?.GetService(typeof(AppBootstrapper)) as AppBootstrapper)?.Shutdown();
            (_serviceProvider as IDisposable)?.Dispose();
        }
        catch
        {
            // Swallow shutdown errors; nothing useful we can do.
        }
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Configuration: one shared AppConfig instance (read at startup,
        // mutated by the Settings dialog, persisted back to appsettings.json).
        var appConfig = AppConfigExtensions.LoadFromFile();
        services.AddSingleton(appConfig);
        services.AddSingleton<IAppConfig>(new AppConfigLoader(appConfig));

        // DPAPI-protected API keys (%LOCALAPPDATA%\Aira\secrets.dat).
        services.AddSingleton<SecretStore>();

        // Logging first so other services can log.
        services.AddSingleton<ILogger>(sp => FileLoggerFactory.CreateFromAppConfig());

        // HTTP clients used by AI providers.
        services.AddHttpClient();

        // Persistence.
        services.AddSingleton<HistoryStore>();

        // Infrastructure.
        services.AddSingleton<IAdminService, AdminService>();
        services.AddSingleton<FunctionRouter>();
        services.AddSingleton<IFunctionRouter>(sp => sp.GetRequiredService<FunctionRouter>());
        services.AddSingleton<ISystemControlService, SystemControlService>();

        // Audio + Speech.
        services.AddSingleton<IMicrophoneService, MicrophoneService>();
        services.AddSingleton<ISpeechToTextService, SystemSpeechSttService>();
        services.AddSingleton<ITextToSpeechService, SystemSpeechTtsService>();

        // AI.
        services.AddSingleton<IToolCatalog, ToolCatalog>();
        services.AddSingleton<IAiService>(sp => AiServiceFactory.Create(sp));

        // View models + views.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsDialog>();
        services.AddTransient<SessionsViewModel>();
        services.AddTransient<SessionsDialog>();

        // Bootstrapper + tray.
        services.AddSingleton<AppBootstrapper>();
        services.AddSingleton<TrayIconService>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _serviceProvider?.GetService<ILogger>()?.Error("UI dispatcher exception", e.Exception);
        e.Handled = true; // keep the app alive for recoverable UI errors
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _serviceProvider?.GetService<ILogger>()?.Error("AppDomain unhandled exception", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _serviceProvider?.GetService<ILogger>()?.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
