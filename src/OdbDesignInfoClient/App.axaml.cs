using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Core.ViewModels;
using OdbDesignInfoClient.Services;
using OdbDesignInfoClient.Views;
using Serilog;
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

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(logPath, 
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("OdbDesignInfoClient starting...");

        // Read server configuration
        var serverHost = configuration["Server:Host"] ?? "localhost";
        var serverPort = configuration.GetValue<int>("Server:RestPort", 8888);
        var grpcPort = configuration.GetValue<int>("Server:GrpcPort", 50051);
        var useHttps = configuration.GetValue<bool>("Server:UseHttps", false);
        var protocol = useHttps ? "https" : "http";
        var restBaseUrl = $"{protocol}://{serverHost}:{serverPort}";

        Log.Information("Configuring REST client for {BaseUrl}", restBaseUrl);
        Log.Information("Configuring gRPC client for {Protocol}://{Host}:{Port}", protocol, serverHost, grpcPort);

        // Create server configuration
        var serverConfig = new ServerConnectionConfig
        {
            Host = serverHost,
            RestPort = serverPort,
            GrpcPort = grpcPort,
            UseHttps = useHttps,
            TimeoutSeconds = configuration.GetValue<int>("Server:TimeoutSeconds", 30)
        };

        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services, configuration, serverConfig, restBaseUrl);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Store command line arguments
            CommandLineArgs = desktop.Args ?? [];

            // Avoid duplicate validations from both Avalonia and CommunityToolkit.
            BindingPlugins.DataValidators.RemoveAt(0);

            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            // Check for auto-connect argument
            var autoConnect = CommandLineArgs.Any(arg => 
                arg.Equals("--auto-connect", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-ac", StringComparison.OrdinalIgnoreCase));

            if (autoConnect)
            {
                Log.Information("Auto-connect enabled via CLI argument");
            }

            // Initialize the view model after window is created
            _ = Task.Run(async () =>
            {
                try
                {
                    await mainViewModel.InitializeAsync(autoConnect);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize MainViewModel");
                }
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, ServerConnectionConfig serverConfig, string restBaseUrl)
    {
        // Register configuration
        services.AddSingleton(configuration);
        services.AddSingleton(serverConfig);

        // Register all OdbDesignInfoClient services with configured base URL
        services.AddOdbDesignInfoClientServices(restBaseUrl);

        // Register Tab ViewModels as Transient (new instance per request)
        services.AddTransient<ComponentsTabViewModel>();
        services.AddTransient<NetsTabViewModel>();
        services.AddTransient<StackupTabViewModel>();
        services.AddTransient<DrillToolsTabViewModel>();
        services.AddTransient<PackagesTabViewModel>();
        services.AddTransient<PartsTabViewModel>();

        // Register Main ViewModel
        services.AddTransient<MainViewModel>();

        // Add logging
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
    }
}
