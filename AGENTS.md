---
description: AI Agent Instructions for OdbDesignInfoClient Repository
scope: repository
role: Development Assistant
---

# OdbDesignInfo Client - AI Agent Guide

## Project Overview

This repository contains **OdbDesignInfo Client**, a professional-grade, cross-platform desktop application for viewing and analyzing ODB++ PCB design data. The application is built with **Avalonia UI** and follows **Clean Architecture** principles.

### Purpose

The OdbDesignInfo Client transforms raw ODB++ design data into structured, navigable intelligence for CAM engineers, PCB designers, and manufacturing specialists. It connects to an OdbDesignServer backend and provides:

- Hierarchical data visualization
- Cross-probing with 3D viewers
- Design validation and verification
- Component, net, layer, and stackup analysis

## Technology Stack

| Component | Technology | Purpose |
| --------- | --------- | ------- |
| **Runtime** | .NET 10 | Latest .NET with C# 14/15 features |
| **UI Framework** | Avalonia UI 11.1+ | Cross-platform desktop UI |
| **MVVM** | CommunityToolkit.Mvvm | Modern MVVM with source generators |
| **Data Grid** | TreeDataGrid | Hierarchical data visualization |
| **REST Client** | Refit | Type-safe REST API client |
| **gRPC Client** | Grpc.Net.Client | High-performance binary transport |
| **DI Container** | Microsoft.Extensions.DependencyInjection | Service lifetime management |
| **Logging** | Serilog | Structured logging |
| **Testing** | xUnit, Moq, TestContainers | Unit & integration testing |

## Project Structure

```text
OdbDesignInfoClient/
├── src/
│   ├── OdbDesignInfoClient/           # Main Avalonia UI application
│   │   ├── Views/                     # XAML views and windows
│   │   ├── Assets/                    # Icons, fonts, styles
│   │   └── App.axaml                  # Application entry point
│   ├── OdbDesignInfoClient.Core/      # Core business logic
│   │   ├── Models/                    # Domain entities
│   │   ├── ViewModels/                # MVVM ViewModels
│   │   └── Services/                  # Service interfaces
│   └── OdbDesignInfoClient.Services/  # Service implementations
├── tests/
│   ├── OdbDesignInfoClient.Tests/             # Unit tests
│   └── OdbDesignInfoClient.IntegrationTests/  # Integration tests
├── protoc/                            # gRPC protocol definitions
├── docker/                            # Docker configuration
├── docs/                              # Documentation
└── plan_docs/                         # Architecture & planning docs
```

## Key Architecture Principles

### Clean Architecture

The application follows **Clean Architecture** to separate concerns:

1. **Core Layer**: Contains all business logic, ViewModels, models, and service interfaces
   - No UI dependencies
   - Testable and portable
   - Framework-agnostic

2. **Services Layer**: Implements service interfaces
   - REST/gRPC clients
   - Inter-process communication (IPC)
   - External integrations

3. **UI Layer**: Avalonia-specific views and platform code
   - XAML views
   - Platform-specific adapters
   - Depends on Core layer only

### MVVM Pattern

- **Model**: DTOs from Protobuf/Swagger definitions
- **View**: XAML-based UI definitions
- **ViewModel**: Presentation logic, state management, commands

## AI Agent Guidelines

When working with this codebase, AI agents should:

### 1. Understand the Domain

- **ODB++**: An open format for PCB design data exchange
- **Components**: Electronic parts placed on PCBs (RefDes: R1, C2, etc.)
- **Nets**: Electrical connections between pins (e.g., GND, +5V, CLK)
- **Layers**: Physical layers in PCB stackup (signal, power, dielectric)
- **Via**: Plated hole connecting layers
- **Package/Footprint**: Physical shape of components

### 2. Code Style & Standards

- **StyleCop**: All code must pass StyleCop analysis
- **XML Documentation**: All public members require /// comments
- **Naming Conventions**: Follow C# naming conventions (PascalCase for public, camelCase for private)
- **Async/Await**: Use async patterns for I/O operations
- **Null Safety**: Use nullable reference types

### 3. Adding New Features

When adding new features, follow this workflow:

1. **Define Models** in `OdbDesignInfoClient.Core/Models/`
2. **Create Service Interface** in `Core/Services/Interfaces/`
3. **Implement Service** in `OdbDesignInfoClient.Services/`
4. **Create ViewModel** in `Core/ViewModels/`
5. **Create View** in `OdbDesignInfoClient/Views/`
6. **Register in DI** in `App.axaml.cs`
7. **Write Tests** in `Tests/` project

### 4. Working with Data Grids

The application uses **TreeDataGrid** for hierarchical data:

- Support master-detail relationships (e.g., Component → Pins → Nets)
- Implement UI virtualization for performance (10,000+ rows)
- Use `HierarchicalTreeDataGridSource<T>` for nested data
- Provide filtering and sorting capabilities

### 5. Communication Protocols

- **REST (Refit)**: For control plane operations
  - Fetching design lists
  - Health checks
  - Metadata operations
  
- **gRPC**: For bulk data streaming
  - Component lists
  - Net lists
  - Geometry data

- **Named Pipes**: For IPC with 3D Viewer
  - Cross-probing commands
  - Selection synchronization

### 6. Testing Requirements

- **Unit Tests**: For ViewModels and Services
  - Use Moq/NSubstitute for mocking
  - Test business logic independently from UI
  - Minimum 80% code coverage for Core project

- **Integration Tests**: For API communication
  - Use TestContainers for Docker-based tests
  - Verify REST/gRPC contracts
  - Test actual server communication

### 7. Platform Considerations

The application must run on:

- **Windows**: 10/11 (x64, ARM64)
- **Linux**: Ubuntu 22.04+, Fedora
- **macOS**: Monterey+ (Apple Silicon & Intel)

Consider:

- File path separators (use `Path.Combine()`)
- Platform-specific resources
- Cross-platform UI rendering (Avalonia ensures consistency)

### 8. Performance Requirements

- **Grid Rendering**: Handle 10,000+ rows with <16ms latency (60fps)
- **Startup Time**: Cold start under 2 seconds
- **Memory Footprint**: Baseline under 200MB when idle
- **IPC Latency**: Cross-probe commands under 50ms

### 9. Documentation Requirements

When making changes:

- Update XML documentation for public APIs
- Update README.md if user-facing features change
- Create ADRs (Architecture Decision Records) for major decisions
- Update plan_docs for architectural changes

### 10. Security Considerations

- **TruffleHog**: Scan for secrets before committing
- **Authentication**: Support HTTP Basic Auth for server connections
- **Credentials**: Never log or expose credentials
- **Input Validation**: Validate all user input and API responses

## Common Tasks for AI Agents

### Adding a New Tab/View

1. Define the data model in `Core/Models/`
2. Create the ViewModel in `Core/ViewModels/`
3. Create the XAML view in `Views/`
4. Add service methods for data fetching
5. Register the ViewModel in DI container
6. Add routing/navigation logic
7. Write unit tests for ViewModel

### Implementing a New Service

1. Define interface in `Core/Services/Interfaces/`
2. Implement in `Services/` project
3. Register in `App.axaml.cs` DI configuration
4. Add logging with Serilog
5. Handle errors and retries (consider Polly)
6. Write unit tests with mocked dependencies
7. Write integration tests if external communication is involved

### Adding gRPC/REST Endpoints

1. **REST**: Update Swagger YAML, regenerate Refit client
2. **gRPC**: Update `.proto` files, regenerate client code
3. Update service implementations to use new endpoints
4. Add response mapping to domain models
5. Write integration tests for new endpoints

### Debugging Issues

1. Check **Serilog logs** in `%AppData%/OdbDesignClient/logs/`
2. Use **StyleCop** to identify code style issues
3. Run **tests** with `dotnet test`
4. Use **Docker** for consistent test environments
5. Check **connection status** to OdbDesignServer

## Build & Run Commands

```powershell
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run application
dotnet run --project src/OdbDesignInfoClient/OdbDesignInfoClient.csproj

# Run all tests
dotnet test

# Run with style checking
dotnet build /p:EnforceCodeStyleInBuild=true

# Publish for Windows
dotnet publish src/OdbDesignInfoClient -c Release -r win-x64 --self-contained

# Publish for Linux
dotnet publish src/OdbDesignInfoClient -c Release -r linux-x64 --self-contained

# Publish for macOS
dotnet publish src/OdbDesignInfoClient -c Release -r osx-x64 --self-contained
```

## Related Documentation

- [README.md](README.md) - Project overview and quick start
- [Architecture Guide](plan_docs/OdbDesignInfoClient-Architecture.md) - Detailed architecture
- [Implementation Spec](plan_docs/Application-Implementation-Specification-OdbDesignInfo-Client.md) - Full specification
- [Testing Guide](docs/TESTING.md) - Testing strategies
- [Docker Integration](docker/README.md) - Docker setup

## References

- [Avalonia UI Documentation](https://docs.avaloniaui.net/)
- [ODB++ Specification](https://odbplusplus.com/)
- [Clean Architecture Principles](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [MVVM Pattern](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm)

## Repository Information

- **Repository**: [nam20485/Odbdesign-info-client-india79-b](https://github.com/nam20485/Odbdesign-info-client-india79-b)
- **Current Branch**: mn/droid-app
- **Default Branch**: main
- **Active PR**: #9 - Enhance OdbDesignInfoClient with new tabs and authentication features

---

Last Updated: February 5, 2026
