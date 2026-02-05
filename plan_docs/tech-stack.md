# OdbDesign Client Technology Stack

## Overview
This document outlines the technology stack for the OdbDesign Client application, a cross-platform desktop workstation for ODB++ PCB design visualization and analysis.

## Core Runtime

| Component | Technology | Version | Notes |
|-----------|------------|---------|-------|
| **Runtime Framework** | .NET | 10.0 | Latest stable with long-term support |
| **Language** | C# | 14/15 | Primary constructors, raw string literals, pattern matching |
| **SDK Configuration** | global.json | 10.0.0 | RollForward: latestFeature |

## UI Framework

| Component | Technology | Purpose |
|-----------|------------|---------|
| **UI Framework** | Avalonia UI | Cross-platform Skia-based rendering |
| **Theme** | Avalonia.Themes.Fluent | Modern Windows 11-style design |
| **Data Grid** | Avalonia.Controls.TreeDataGrid | High-performance hierarchical grids |
| **MVVM** | CommunityToolkit.Mvvm | Source-generator based MVVM |

### Avalonia Rationale
- **Cross-Platform Consistency**: Unlike MAUI which wraps native controls, Avalonia renders pixels via Skia ensuring identical visuals on Windows, Linux, and macOS
- **TreeDataGrid Performance**: Supports UI virtualization for 10,000+ row grids
- **Complex Control Support**: Better handling of nested data grids compared to native controls

## Networking Layer

### REST API (Control Plane)

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Client Generator** | Refit | Auto-generate typed clients from OpenAPI/Swagger |
| **API Spec** | swagger/odbdesign-server-0.9-swagger.yaml | Source of truth for REST endpoints |

### gRPC (Data Plane)

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Client** | Grpc.Net.Client | High-performance binary transport |
| **Serialization** | Google.Protobuf | Binary message serialization |
| **Code Generator** | Grpc.Tools | Proto compilation to C# |
| **Proto Files** | OdbDesignLib/protoc/*.proto | gRPC service definitions |

### Hybrid Strategy Rationale
| Protocol | Use Case | Example Operations |
|----------|----------|-------------------|
| **REST** | Control Plane, lightweight metadata | Design list, health checks, layer info |
| **gRPC** | Data Plane, bulk streaming | Components, nets, geometry data |

## Infrastructure

### Dependency Injection

| Component | Technology | Purpose |
|-----------|------------|---------|
| **DI Container** | Microsoft.Extensions.DependencyInjection | Service lifetime management |
| **Service Lifetimes** | Singleton/Transient | ViewModels (Transient), Services (Singleton) |

### Logging & Observability

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Logging Framework** | Serilog | Structured logging |
| **Hosting Integration** | Serilog.Extensions.Hosting | .NET Generic Host integration |
| **File Sink** | Serilog.Sinks.File | Rolling file logs |
| **Log Location** | %AppData%/OdbDesignClient/logs/ | 7-day retention |

### Resiliency

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Retry Policies** | Polly | Transient error handling |
| **Connection State** | State Machine Pattern | Disconnected -> Connecting -> Connected -> Reconnecting |

## Inter-Process Communication (IPC)

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Transport** | System.IO.Pipes | Named Pipes for local IPC |
| **Protocol** | JSON | Message serialization |
| **Pipe Name** | OdbDesignViewerPipe | Cross-probing with 3D viewer |

### IPC Message Types
- `Select { entityType, name }` - Select component/net
- `Zoom { x, y, zoomLevel }` - Camera control
- `Highlight { netName, color }` - Visual highlighting

## Testing Stack

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Test Framework** | xUnit | Unit and integration tests |
| **Mocking** | Moq / NSubstitute | Service mocking |
| **Container Testing** | TestContainers | Docker-based integration tests |
| **Server Container** | OdbDesignServer Docker | API contract validation |

## Build & Distribution

### CI/CD

| Component | Technology | Purpose |
|-----------|------------|---------|
| **CI Platform** | GitHub Actions | Cross-platform builds |
| **Build Runners** | windows-latest, ubuntu-latest, macos-latest | Multi-OS validation |
| **Workflow** | .github/workflows/build-client.yml | Build, test, artifact |

### Packaging

| Platform | Output | Notes |
|----------|--------|-------|
| **Windows** | OdbDesignClient.exe | Self-contained, trimmed |
| **Linux** | OdbDesignClient (ELF) | Self-contained |
| **macOS** | OdbDesignClient.app | Bundle, ad-hoc signed |

## Performance Requirements

| Metric | Target |
|--------|--------|
| **Grid Rendering** | 10,000+ rows @ 60fps (16ms latency) |
| **Startup Time** | < 2 seconds cold start |
| **Memory Baseline** | < 200MB idle |
| **IPC Latency** | < 50ms perceived |

## Security

| Component | Implementation |
|-----------|----------------|
| **Authentication** | HTTP Basic Auth |
| **Credential Storage** | Secure, user-configurable |
| **gRPC Auth** | CallCredentials with metadata |

## Code Quality

| Component | Technology |
|-----------|------------|
| **Style Enforcement** | StyleCop |
| **Coverage Target** | 80%+ for Core project |
| **Documentation** | XML triple-slash comments |

## Project Dependencies Summary

```xml
<!-- Key NuGet Packages -->
<PackageReference Include="Avalonia" Version="*" />
<PackageReference Include="Avalonia.Controls.TreeDataGrid" Version="*" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="*" />
<PackageReference Include="Refit" Version="*" />
<PackageReference Include="Grpc.Net.Client" Version="*" />
<PackageReference Include="Google.Protobuf" Version="*" />
<PackageReference Include="Grpc.Tools" Version="*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="*" />
<PackageReference Include="Serilog" Version="*" />
<PackageReference Include="Serilog.Extensions.Hosting" Version="*" />
<PackageReference Include="Serilog.Sinks.File" Version="*" />
<PackageReference Include="Polly" Version="*" />
<PackageReference Include="xunit" Version="*" />
<PackageReference Include="Moq" Version="*" />
<PackageReference Include="Testcontainers" Version="*" />
```
