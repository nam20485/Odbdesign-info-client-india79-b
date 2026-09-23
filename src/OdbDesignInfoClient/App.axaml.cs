using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using OdbDesignInfoClient.Services;
using OdbDesignInfoClient.Views;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Linq;

namespace OdbDesignInfoClient;

/// <summary>
/// The main application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    /// <summary>
    /// Gets the command line arguments.
    /// </summary>
    public static string[] CommandLineArgs { get; private set; } = [];

    /// <summary>
    /// Initializes the application.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Called when the framework initialization is completed.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OdbDesignInfoClient",
            "logs",
            "log-.txt");

        var logLevel = configuration.GetValue("Logging:LogLevel:Default", "Information");
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLogLevel(logLevel))
            .WriteTo.Console()
            .WriteTo.File(logPath, 
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("OdbDesignInfoClient starting...");

        // Read server configuration. Precedence: environment variables > appsettings.json > defaults.
        var serverConfig = new ServerConnectionConfig
        {
            Host = configuration["Server:Host"] ?? "debian13vm.tail11ba79.ts.net",
            RestPort = configuration.GetValue("Server:RestPort", 443),
            GrpcPort = configuration.GetValue("Server:GrpcPort", 50051),
            UseHttps = configuration.GetValue<bool>("Server:UseHttps", true),
            GrpcUseTls = configuration.GetValue<bool>("Server:GrpcUseTls", false),
            TimeoutSeconds = configuration.GetValue("Server:TimeoutSeconds", 30),
            RestUrlOverride = Environment.GetEnvironmentVariable("ODBDESIGN_REST_URL"),
            GrpcUrlOverride = Environment.GetEnvironmentVariable("ODBDESIGN_GRPC_URL"),
        };
        var restBaseUrl = serverConfig.RestBaseUrl;

        Log.Information("Configuring REST client for {BaseUrl}", restBaseUrl);
        Log.Information("Configuring gRPC client for {GrpcBaseUrl}", serverConfig.GrpcBaseUrl);

        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services, configuration, serverConfig, restBaseUrl);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Store command line arguments
            CommandLineArgs = desktop.Args ?? [];

            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            // Auto-connect on launch by default (per spec acceptance criteria);
            // opt out with --no-auto-connect / -nac, legacy --auto-connect / -ac is a no-op
            var autoConnect = !CommandLineArgs.Any(arg =>
                arg.Equals("--no-auto-connect", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-nac", StringComparison.OrdinalIgnoreCase));

            Log.Information("Auto-connect: {AutoConnect}", autoConnect);

            // Initialize the view model after window is created. Run on the UI thread
            // so observable-property updates raised during init keep dispatcher affinity.
            _ = InitializeViewModelAsync(mainViewModel, autoConnect);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Runs view model initialization as a fire-and-forget task with top-level error logging.
    /// </summary>
    private static async Task InitializeViewModelAsync(MainViewModel mainViewModel, bool autoConnect)
    {
        try
        {
            await mainViewModel.InitializeAsync(autoConnect);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize MainViewModel");
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, ServerConnectionConfig serverConfig, string restBaseUrl)
    {
        // Register configuration
        services.AddSingleton(configuration);
        services.AddSingleton(serverConfig);

        // Register all OdbDesignInfoClient services with configured base URL and timeout
        services.AddOdbDesignInfoClientServices(restBaseUrl, serverConfig.TimeoutSeconds);

        // Register Tab ViewModels as Transient (new instance per request)
        services.AddTransient<ComponentsTabViewModel>();
        services.AddTransient<NetsTabViewModel>();
        services.AddTransient<StackupTabViewModel>();
        services.AddTransient<DrillToolsTabViewModel>();
        services.AddTransient<PackagesTabViewModel>();
        services.AddTransient<PartsTabViewModel>();
        services.AddTransient<PinsTabViewModel>();
        services.AddTransient<ViasTabViewModel>();
        services.AddTransient<StepHierarchyTabViewModel>();
        services.AddTransient<SymbolsTabViewModel>();
        services.AddTransient<EdaDataTabViewModel>();

        // Register Main ViewModel
        services.AddTransient<MainViewModel>();

        // Add logging
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
    }

    private static Serilog.Events.LogEventLevel ParseLogLevel(string level)   
    {
        return level.ToLowerInvariant() switch
        {
            "debug" => Serilog.Events.LogEventLevel.Debug,
            "information" => Serilog.Events.LogEventLevel.Information,
            "warning" => Serilog.Events.LogEventLevel.Warning,
            "error" => Serilog.Events.LogEventLevel.Error,
            "fatal" => Serilog.Events.LogEventLevel.Fatal,
            _ => Serilog.Events.LogEventLevel.Information
        };
    }
}
