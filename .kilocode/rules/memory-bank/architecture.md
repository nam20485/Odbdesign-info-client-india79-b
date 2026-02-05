# Architecture

## System Overview
The solution follows the **Clean Architecture** pattern, separating the user interface, core business logic, and external service implementations.

## Source Code Structure
- **`src/OdbDesignInfoClient` (UI)**: The executable "head" of the application. Contains Avalonia XAML Views, `App.axaml`, and platform-specific entry points. It has no direct dependencies on external libraries like Refit or gRPC; it only depends on the `Core` and `Services` projects.
- **`src/OdbDesignInfoClient.Core` (Domain)**: The heart of the application. Contains:
  - **Models**: Rich domain objects (observable properties).
  - **ViewModels**: `CommunityToolkit.Mvvm` implementations managing state (e.g., `MainViewModel`, `ComponentsTabViewModel`).
  - **Service Interfaces**: Contracts for `IConnectionService`, `IDesignService`, etc.
- **`src/OdbDesignInfoClient.Services` (Infrastructure)**: Implements the interfaces defined in Core.
  - **API Clients**: Refit (REST) and Grpc.Net.Client (gRPC) generated code.
  - **IPC**: `CrossProbeService` using `System.IO.Pipes`.

## Key Technical Decisions
### Hybrid Connectivity
The application uses a dual-protocol strategy to optimize performance:
- **REST (Refit)**: Used for control plane operations (fetching design lists, layers, steps) and metadata.
- **gRPC (Protobuf)**: Used for data plane operations (streaming large lists of components, nets, and geometry) where JSON serialization overhead is prohibitive.

### Hierarchical Data Grid
**TreeDataGrid** from Avalonia is used instead of the standard DataGrid.
- **Why**: Standard DataGrids struggle with nested hierarchies and massive virtualization. TreeDataGrid is designed for high-performance rendering of deep structures (Component -> Nets -> Pins).

### Cross-Probing (IPC)
Communication with the 3D Viewer is handled via **Named Pipes** (`System.IO.Pipes`).
- **Why**: Local IPC avoids network stack overhead, ensuring that "click-to-zoom" operations feel instantaneous (< 50ms).

## Component Relationships
1. **User Action**: User clicks a Component in `ComponentsTabView`.
2. **ViewModel**: `ComponentsTabViewModel` handles the selection.
3. **CrossProbeService**: Sends a JSON `Select` command via Named Pipe to the 3D Viewer.
4. **NavigationService**: (If it's a hyperlink click) Switches the active tab to `Nets` and filters the view.

## Critical Implementation Paths
- **Data Loading**: `DesignService` acts as the facade. It decides whether to fetch data via REST or gRPC and maps the DTOs to Observable Models.
- **State Management**: `MainViewModel` orchestrates the current design context. Changing a design triggers a global reload of all child ViewModels.
