# OdbDesignInfo Client

A cross-platform desktop application for viewing and analyzing ODB++ PCB design data, built with **Avalonia UI** on **.NET 10**.

## Project Overview

OdbDesignInfo Client is a professional-grade desktop workstation application for interrogating ODB++ printed circuit board designs. It connects to an [OdbDesignServer](https://github.com/OdbDesign/OdbDesign) backend via REST (metadata) and gRPC (bulk data streaming) and presents hierarchical, navigable, cross-probed design intelligence.

**Key features:** hierarchical data grids, bi-directional cross-probing with a 3D viewer via named pipes, Fluent Design UI with dark/light themes, and resilient REST/gRPC communication with Polly retry policies.

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET 10 | `net10.0` |
| UI Framework | Avalonia UI | 12.0.3 |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 |
| REST Client | Refit | 10.1.6 |
| gRPC Client | Grpc.Net.Client | 2.80.0 |
| DI Container | Microsoft.Extensions.DependencyInjection | 10.0.8 |
| Logging | Serilog | 4.3.1 |
| Resiliency | Polly | 8.6.6 |
| Testing | xUnit, Moq, TestContainers | — |

> **Note:** `Directory.Build.props` references older Avalonia 11.1.x version properties, but the actual `.csproj` files use Avalonia **12.0.3**. The `Directory.Build.props` version properties are unused/overridden.

## Architecture

Follows **Clean Architecture** with three projects:

```
OdbDesignInfoClient/
├── src/
│   ├── OdbDesignInfoClient/              # UI layer (Avalonia views, App.axaml)
│   │   ├── Views/                        # XAML views (MainWindow, tab views)
│   │   ├── Assets/                       # Icons, fonts, styles
│   │   ├── App.axaml.cs                  # DI container setup, Serilog config
│   │   ├── Program.cs                    # Entry point, AppBuilder
│   │   └── appsettings.json              # Server connection config
│   ├── OdbDesignInfoClient.Core/         # Core business logic (no UI deps)
│   │   ├── Models/                       # Domain records (Design, Component, Net, etc.)
│   │   ├── ViewModels/                   # MVVM ViewModels (source generators)
│   │   └── Services/Interfaces/          # IConnectionService, IDesignService, etc.
│   └── OdbDesignInfoClient.Services/     # Service implementations
│       ├── Api/                          # Refit REST API interfaces, auth handlers
│       ├── ConnectionService.cs          # Server connectivity & state machine
│       ├── DesignService.cs              # Design data access (REST + gRPC)
│       ├── CrossProbeService.cs          # Named-pipe IPC with 3D viewer
│       ├── NavigationService.cs          # Tab & entity navigation
│       └── ServiceCollectionExtensions.cs # DI registration
├── tests/
│   ├── OdbDesignInfoClient.Tests/        # Unit tests (xUnit + Moq)
│   └── OdbDesignInfoClient.IntegrationTests/ # Integration tests (Docker/TestContainers)
├── protoc/                               # gRPC .proto definitions (26 files)
├── docker/                               # Docker configuration
├── docs/                                 # Documentation
└── plan_docs/                            # Architecture & planning docs
```

### Dependency Flow

```
OdbDesignInfoClient (UI) → OdbDesignInfoClient.Core (Models/ViewModels/Interfaces)
                         → OdbDesignInfoClient.Services (depends on Core)
```

Core has **no dependency** on UI or Services. Services depends only on Core.

## Building and Running

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (v10.0.102+, per `global.json`)
- [OdbDesignServer](https://github.com/OdbDesign/OdbDesign) running locally (REST on port 8888, gRPC on port 50051)

### Commands

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run application
dotnet run --project src/OdbDesignInfoClient/OdbDesignInfoClient.csproj

# Run with auto-connect
dotnet run --project src/OdbDesignInfoClient/OdbDesignInfoClient.csproj -- --auto-connect

# Run all tests
dotnet test

# Run unit tests only
dotnet test tests/OdbDesignInfoClient.Tests/

# Run integration tests (requires Docker)
dotnet test tests/OdbDesignInfoClient.IntegrationTests/

# Build with style analysis
dotnet build /p:EnforceCodeStyleInBuild=true

# Publish for Windows
dotnet publish src/OdbDesignInfoClient -c Release -r win-x64 --self-contained

# Publish for Linux
dotnet publish src/OdbDesignInfoClient -c Release -r linux-x64 --self-contained

# Publish for macOS
dotnet publish src/OdbDesignInfoClient -c Release -r osx-x64 --self-contained
```

## Configuration

Connection settings are in `src/OdbDesignInfoClient/appsettings.json`:

```json
{
  "Server": {
    "Host": "localhost",
    "RestPort": 8888,
    "GrpcPort": 50051,
    "TimeoutSeconds": 30,
    "UseHttps": false
  },
  "CrossProbe": {
    "PipeName": "OdbDesignViewerPipe",
    "AutoConnect": true
  }
}
```

Logs are written to `%AppData%/OdbDesignInfoClient/logs/` (rolling daily, 7-day retention).

## Development Conventions

### MVVM Pattern

- ViewModels live in `Core/ViewModels/` and use **CommunityToolkit.Mvvm source generators** (`[ObservableProperty]`, `[RelayCommand]`, `partial class`).
- ViewModels are registered as **Transient** in DI (new instance per resolution).
- Core services (`IConnectionService`, `IDesignService`, `INavigationService`, `ICrossProbeService`) are registered as **Singleton**.

### Code Style

- C# 14/15 features enabled (`LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`).
- XML documentation required on all public members (`GenerateDocumentationFile=true`, `CS1591` suppressed).
- `nullable` warnings treated as errors (`WarningsAsErrors=nullable`).
- File-scoped namespaces (`namespace X;`) used throughout.
- Record types used for domain models.

### Adding a New Tab/View

1. Define models in `Core/Models/`
2. Create service interface in `Core/Services/Interfaces/`
3. Implement service in `Services/`
4. Create ViewModel in `Core/ViewModels/`
5. Create View (`.axaml` + `.axaml.cs`) in `Views/`
6. Register ViewModel in `App.axaml.cs` `ConfigureServices()`
7. Register service in `ServiceCollectionExtensions.AddOdbDesignInfoClientServices()`
8. Wire into `MainViewModel` tab properties and `LoadCurrentTabDataAsync()`

### gRPC Proto Files

Proto files live in `protoc/` with the gRPC service definition in `protoc/grpc/service.proto`. The Services project compiles all protos via `<Protobuf>` MSBuild items. Regenerate client code by rebuilding the Services project after modifying `.proto` files.

### Key Pitfalls (from recent fixes)

- **Avalonia 12 breaking change:** `BindingPlugins.DataValidators` is now **internal**. Do not use it in `App.axaml.cs`.
- **Avalonia 12 deprecation:** `TextBox.Watermark` is obsolete — use `PlaceholderText` instead.
- **TreeDataGrid licensing:** `Avalonia.Controls.TreeDataGrid` v12+ requires a commercial license key. Removed from the project as it was unused.

## Testing

- **Unit tests** (`tests/OdbDesignInfoClient.Tests/`): xUnit + Moq, covers Models and Services.
- **Integration tests** (`tests/OdbDesignInfoClient.IntegrationTests/`): xUnit + TestContainers for Docker-based server contract testing.
- Test projects reference both Core and Services projects.
