# OdbDesignInfo Client

A cross-platform desktop application for viewing and analyzing ODB++ PCB design data, built with Avalonia UI.

## Overview

The OdbDesignInfo Client is a professional-grade desktop workstation application tailored for the detailed interrogation of ODB++ printed circuit board (PCB) designs. Unlike simple file viewers, this client acts as an intelligent analytics dashboard that consumes hierarchical design data served by the OdbDesignServer and transforms it into structured, navigable, and interconnected intelligence.

### Key Features

- **Cross-Platform**: Runs on Windows, macOS, and Linux with identical visuals
- **Hierarchical Data Grids**: Advanced TreeDataGrid for components, nets, layers, and more
- **Bi-Directional Cross-Probing**: Sync selections between the client and 3D viewer
- **Hybrid Connectivity**: REST for metadata, gRPC for bulk data streaming
- **Modern UI**: Fluent Design with dark/light theme support

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (Preview)
- [OdbDesignServer](https://github.com/OdbDesign/OdbDesign) running locally or remotely

## Quick Start

### Clone the Repository

```bash
git clone https://github.com/nam20485/Odbdesign-info-client.git
cd Odbdesign-info-client
```

### Build

```bash
dotnet restore
dotnet build
```

### Run

```bash
dotnet run --project src/OdbDesignInfoClient/OdbDesignInfoClient.csproj
```

### Test

```bash
dotnet test
```

## Project Structure

```
OdbDesignInfoClient/
├── src/
│   ├── OdbDesignInfoClient/           # Main Avalonia UI application
│   │   ├── Assets/                    # Icons, fonts, styles
│   │   ├── Views/                     # XAML views and windows
│   │   ├── App.axaml                  # Application entry point
│   │   └── Program.cs                 # Main entry
│   ├── OdbDesignInfoClient.Core/      # Core business logic
│   │   ├── Models/                    # Domain entities
│   │   ├── ViewModels/                # MVVM ViewModels
│   │   └── Services/                  # Service interfaces
│   └── OdbDesignInfoClient.Services/  # Service implementations
│       ├── ConnectionService.cs       # Server connectivity
│       ├── DesignService.cs           # Design data access
│       └── CrossProbeService.cs       # IPC with 3D viewer
├── tests/
│   ├── OdbDesignInfoClient.Tests/             # Unit tests
│   └── OdbDesignInfoClient.IntegrationTests/  # Integration tests
├── docs/                              # Documentation
├── plan_docs/                         # Architecture and planning docs
└── docker/                            # Docker configuration
```

## Architecture

The application follows **Clean Architecture** principles:

- **Core**: Contains all business logic, ViewModels, and service interfaces (no UI dependencies)
- **Services**: Implements service interfaces with REST/gRPC clients and IPC
- **UI (OdbDesignInfoClient)**: Avalonia-specific views and platform code

### Key Technologies

| Component | Technology |
|-----------|------------|
| UI Framework | Avalonia UI 11.1.x |
| MVVM | CommunityToolkit.Mvvm |
| REST Client | Refit |
| gRPC Client | Grpc.Net.Client |
| Logging | Serilog |
| Testing | xUnit, Moq, TestContainers |

## Configuration

The application connects to `localhost:5000` by default. Configure the connection in the UI or modify `appsettings.json`.

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ODBDESIGN_SERVER_HOST` | Server hostname | `localhost` |
| `ODBDESIGN_SERVER_PORT` | Server port | `5000` |

## Development

### Code Style

The project uses StyleCop for code style enforcement. Run analysis with:

```bash
dotnet build /p:EnforceCodeStyleInBuild=true
```

### Adding New Features

1. Define models in `OdbDesignInfoClient.Core/Models/`
2. Create service interface in `Services/Interfaces/`
3. Implement service in `OdbDesignInfoClient.Services/`
4. Create ViewModel in `ViewModels/`
5. Create View in `OdbDesignInfoClient/Views/`
6. Register in DI container in `App.axaml.cs`

## Testing

### Unit Tests

```bash
dotnet test tests/OdbDesignInfoClient.Tests/
```

### Integration Tests

Integration tests require Docker and OdbDesignServer image:

```bash
dotnet test tests/OdbDesignInfoClient.IntegrationTests/
```

## Building for Distribution

### Windows

```bash
dotnet publish src/OdbDesignInfoClient -c Release -r win-x64 --self-contained
```

### Linux

```bash
dotnet publish src/OdbDesignInfoClient -c Release -r linux-x64 --self-contained
```

### macOS

```bash
dotnet publish src/OdbDesignInfoClient -c Release -r osx-x64 --self-contained
```

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Related Projects

- [OdbDesign](https://github.com/OdbDesign/OdbDesign) - The ODB++ parsing server
- [ODB++ Specification](https://odbplusplus.com/) - Official ODB++ format documentation
