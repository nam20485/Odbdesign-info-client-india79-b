using Avalonia;
using System;

namespace OdbDesignInfoClient;

/// <summary>
/// Main entry point for the OdbDesignInfoClient application.
/// </summary>
internal sealed class Program
{
    /// <summary>
    /// The main entry point. Initialization code. 
    /// Don't use any Avalonia, third-party APIs or any SynchronizationContext-reliant code before AppMain is called.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Avalonia configuration. Don't remove; used by visual designer.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
