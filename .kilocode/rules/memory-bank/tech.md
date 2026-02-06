# Technology Stack

## Core Runtime
- **Framework**: .NET 10 (Preview)
- **Language**: C# 14/15
- **SDK Configuration**: `global.json` enforces latest feature roll-forward.

## UI Framework
- **Framework**: Avalonia UI 11.1.x
- **Theme**: Avalonia.Themes.Fluent (Dark/Light mode support)
- **Data Grid**: `Avalonia.Controls.TreeDataGrid` (Virtualization, Hierarchical data)
- **MVVM**: `CommunityToolkit.Mvvm` (Source Generators)

## Networking & Connectivity
- **REST Client**: Refit (Auto-generated from Swagger)
- **gRPC Client**: `Grpc.Net.Client` (Protobuf-based high-performance streaming)
- **IPC**: `System.IO.Pipes` (Named Pipes for local 3D viewer communication)

## Infrastructure
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`
- **Logging**: Serilog (Structured logging to console and rolling files)
- **Resiliency**: Polly (Retry policies for network requests)

## Testing
- **Framework**: xUnit
- **Mocking**: Moq
- **Integration**: TestContainers (Docker-based OdbDesignServer instance)

## Build & CI
- **Platform**: GitHub Actions (Windows, Ubuntu, macOS runners)
- **Distribution**: Single-file self-contained executables (`dotnet publish`)
