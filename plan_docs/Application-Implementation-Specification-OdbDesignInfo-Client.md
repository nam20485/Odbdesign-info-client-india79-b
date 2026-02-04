# **New Application Implementation Specification**

## **App Title**

**OdbDesignInfo Client**

## **Development Plan**

The development is structured into six distinct, progressive phases. This phased approach allows for the validation of core architectural decisions—such as the hybrid connectivity model—before layering on complex UI logic.

* **Phase 1: Foundation & Infrastructure**  
  * **Solution Initialization:** Create a Clean Architecture solution structure (OdbDesignClient.sln) with separated concerns: Core (Business Logic/Interfaces), UI (Avalonia), and Tests. Define Directory.Build.props for consistent versioning across projects.  
  * **CI/CD Pipeline Setup:** Configure GitHub Actions to automatically build the solution on windows-latest, ubuntu-latest, and macos-latest agents. This ensures cross-platform compatibility is checked on every commit, catching OS-specific file path or UI rendering issues early.  
  * **Application Shell & DI:** Implement the MainWindow with a basic navigation shell (TabControl or sidebar). Configure the Microsoft.Extensions.DependencyInjection container in App.axaml.cs to manage the lifecycle of ViewModels (Transient) and Services (Singleton), ensuring a loosely coupled design.  
* **Phase 2: Connectivity Layer (The Hybrid Engine)**  
  * **API Client Generation:**  
    * **REST:** Use **Refit** to generate strong-typed IOdbDesignApi interfaces from the swagger/odbdesign-server-0.9-swagger.yaml definition. This handles all metadata and control plane operations.  
    * **gRPC:** Compile .proto files using Grpc.Tools to generate the high-performance OdbDesignService.OdbDesignServiceClient for streaming geometry data.  
  * **Resilient Connection Service:** Implement ConnectionService featuring a "State Machine" pattern (Disconnected \-\> Connecting \-\> Connected \-\> Reconnecting). Integrate **Polly** policies for transient error handling (e.g., automatic retries with exponential backoff if the server is temporarily unavailable).  
  * **Design Loading Workflow:** Create the "Open Design" user experience. This involves fetching the list of available designs via REST (GET /designs), parsing the response, and populating a selection UI. Include error handling for scenarios where the server is reachable but empty.  
* **Phase 3: The "Fancy" Grids (Core Visualization)**  
  * **TreeDataGrid Architecture:** Implement TreeDataGridFactory to abstract the complex setup of Avalonia's HierarchicalTreeDataGridSource. This factory will handle the mapping of flat DTOs into nested, expandable structures.  
  * **Master-Detail Views:**  
    * **Components View:** Configure columns for RefDes, Part Name, and Side. Implement the hierarchy where expanding a Component row triggers a lazy-load of its connected Nets and Pins.  
    * **Nets View:** Configure columns for Net Name and Total Pin Count. Expanding a Net row reveals the specific Pins, Vias, and TestPoints associated with it.  
  * **Interactive Features:** Implement client-side filtering (debounced text search) and multi-column sorting. Ensure that sorting a parent row does not break the integrity of its child rows.  
* **Phase 4: Extended Data Visualization**  
  * **Complete ODB++ Feature Set:** systematically implement the remaining tabs required for full design interrogation:  
    * **Stackup:** A visual matrix showing layer types (Signal, Power, Dielectric), thickness, and materials.  
    * **Drill Tools:** A list of drill definitions (Plated/Non-Plated) and their hit counts.  
    * **Step Hierarchy:** A tree view visualizing the ODB++ step structure (e.g., Panel \-\> Array \-\> Board).  
  * **Theming & Accessibility:** detailed implementation of Avalonia.Themes.Fluent. Create a robust ThemeService that detects the system OS theme (Dark/Light) and applies the corresponding Avalonia resource dictionary dynamically.  
* **Phase 5: Cross-Probing & IPC (Inter-Process Communication)**  
  * **Named Pipe Infrastructure:** Develop CrossProbeService using System.IO.Pipes. This service must act as both a client (sending commands) and a server (listening for events) to establish a full duplex link with the 3D Viewer.  
  * **Protocol Definition:** Define a rigid JSON contract for commands.  
    * Select { entityType, name }: Selects an item.  
    * Zoom { x, y, zoomLevel }: Pans the camera.  
    * Highlight { netName, color }: Overlays a highlight color.  
  * **Event Binding:** Wire up the SelectionChanged events of the DataGrids to trigger CrossProbeService.SendAsync(). Conversely, implement a background Task loop to listen for incoming JSON from the Viewer and marshal those events to the UI thread to update the Grid selection.  
* **Phase 6: Testing, Optimization & Release**  
  * **Comprehensive Testing:** Write unit tests for all ViewModels, ensuring navigation logic and state changes are correct. Use **TestContainers** to spin up a Dockerized OdbDesignServer for integration tests that verify the actual API contracts.  
  * **Performance Tuning:** Profile the application with large datasets (e.g., a motherboard design with 20k+ nets). Verify that TreeDataGrid virtualization is active and that UI thread blocking is minimized during data loading.  
  * **Packaging:** Configure dotnet publish profiles to produce single-file, self-contained executables (.exe, ELF, Mach-O) that do not require the user to have the .NET Runtime pre-installed.

## **Description**

The **OdbDesign Client** is a professional-grade, cross-platform desktop workstation application tailored for the detailed interrogation of ODB++ printed circuit board (PCB) designs. Unlike simple file viewers that merely render static images, this client acts as an intelligent analytics dashboard. It consumes hierarchical design data served by the **OdbDesignServer** and transforms it into structured, navigable, and interconnected intelligence.

It is specifically engineered for CAM (Computer-Aided Manufacturing) engineers, PCB layout designers, and fabrication specialists who need to verify "Design Intent." This includes validating component placement, checking electrical connectivity (nets), and verifying layer stackup configurations before the board moves to physical production. By operating in a "connected" mode, the client remains lightweight and responsive, delegating the heavy lifting of parsing complex ODB++ geometry to the high-performance backend server.

## **Overview**

* **Architecture:** The system utilizes a **Client-Server** topology.  
  * **The Client:** A "Thin Client" in terms of logic but "Thick" in terms of UI capabilities. It handles presentation, user interaction, filtering, and cross-probing logic.  
  * **The Server:** The OdbDesignServer acts as the engine, parsing raw files and serving normalized data objects.  
* **Target Audience:**  
  * **CAM Engineers:** To verify drill charts and layer stackups.  
  * **PCB Designers:** To cross-probe specific nets and verify component footprints.  
  * **Test Engineers:** To locate test points and verify net continuity.  
* **Core Value Proposition:**  
  * **Validation:** Quickly find discrepancies between the schematic intent and the layout implementation.  
  * **Efficiency:** Instant search and filtering across thousands of nets and components, which is often slow in traditional heavy CAD tools.  
  * **Connectivity:** Seamless synchronization with 3D visualization tools allows for a "Spatial vs. Logical" verification workflow.  
* **Connectivity Strategy (Hybrid):** The application employs a dual-protocol approach to maximize performance:  
  * **REST (HTTP/1.1):** Used for "Chatty" operations—fetching lists of designs, steps, layers, and health checks. It is simple to debug and easy to implement.  
  * **gRPC (HTTP/2):** Used for "Bulky" operations—streaming thousands of net records or component instances. Protobuf binary serialization ensures minimal network overhead and fast parsing speed.  
* **UI Paradigm:** The application is built on the **Model-View-ViewModel (MVVM)** pattern using **Avalonia UI**.  
  * **Model:** Data Transfer Objects (DTOs) generated from Protobuf/Swagger.  
  * **ViewModel:** Handles the presentation logic, state management, and commands (e.g., CommunityToolkit.Mvvm).  
  * **View:** XAML-based definitions that bind to the ViewModels.  
  * **Rationale:** Avalonia provides pixel-perfect rendering consistency across Windows, Linux, and macOS, which is critical for complex data grids that native controls (like those in MAUI) struggle to handle efficiently.

## **Document Links**

* [Architecture Guide](./OdbDesign\ Client\ Architecture.md)  
* ![][image2]  
* ![][image3]

## **Requirements**

* **Runtime Environment:** .NET 10 (Standard Support).  
* **Operating System Support:**  
  * **Windows:** 10/11 (x64, arm64).  
  * **Linux:** Ubuntu 22.04+, Fedora (x64).  
  * **macOS:** Monterey+ (Apple Silicon M1/M2/M3 & Intel).  
* **Performance Metrics:**  
  * **Grid Rendering:** Must handle loading and scrolling a grid with **10,000+ rows** with a UI latency of \< 16ms (60fps) using virtualization.  
  * **Startup Time:** Application cold start should be under 2 seconds.  
  * **Memory Footprint:** Should maintain a baseline memory usage under 200MB when idle.  
* **Connectivity Requirements:**  
  * Support for **HTTP Basic Authentication** for secured servers.  
  * Automatic network resiliency: The app must detect disconnection events and enter a "Reconnecting" state without crashing.  
  * Graceful degradation: If gRPC ports are blocked by a firewall, the app should notify the user (or fallback if feasible).  
* **Inter-Process Communication (IPC):**  
  * Must implement a robust Named Pipe client/server.  
  * Latency for cross-probing commands (Client click \-\> Viewer update) must be perceived as "instant" (\< 50ms).  
* **Visualization Capabilities:**  
  * Support for N-level hierarchical data (Master-Detail) within grids.  
  * Dynamic column visibility (users can hide/show columns).

## **Features**

1. **Intelligent Hybrid Data Connectivity:**  
   * **Auto-Discovery:** The client automatically queries the server capabilities endpoint to determine available protocols.  
   * **Health Monitoring:** A persistent status indicator (Green/Red dot) in the footer showing real-time connection health.  
   * **Smart Reconnect:** If the server is restarted, the client automatically re-establishes the session and refreshes the current view.  
2. **Advanced Hierarchical Data Grids (TreeDataGrid):**  
   * **Components Tab:**  
     * **Level 1 (Master):** Component Instances (RefDes, Part Name, Package, Side, Rotation).  
     * **Level 2 (Detail):** List of Pins belonging to that component, including the Net they are connected to.  
   * **Nets Tab:**  
     * **Level 1 (Master):** Net Name, Color, Total Pin Count, Total Via Count.  
     * **Level 2 (Detail):** List of all physical entities (Pins, Vias, Surfaces) attached to that net.  
   * **Stackup Tab:**  
     * Visual representation of the board cross-section. Columns for Layer Name, Type (Signal/Power/Dielectric), Polarity, and Thickness.  
   * **Drill Tools:**  
     * Summary of all drill bits used, differentiated by Plated/Non-Plated status, with total hit counts.  
3. **Bi-Directional Cross-Probing (IPC):**  
   * **Sync to 3D Viewer:** Clicking a row (e.g., "C12") in the grid sends a JSON command to the 3D Viewer to zoom into and highlight "C12".  
   * **Sync from 3D Viewer:** Selecting a component in the 3D viewport sends a command to the Client, causing the grid to auto-scroll to the corresponding row and expand it.  
   * **Protocol:** Uses local Named Pipes (OdbDesignViewerPipe) to avoid network stack overhead for local IPC.  
4. **Deep Linking & Navigation:**  
   * **Hyperlink Cells:** "Net Name" cells in the Components tab act as hyperlinks.  
   * **Workflow:** Clicking "GND" in the Component view instantly switches the main tab to "Nets", applies a filter for "GND", and expands the "GND" row. This allows for fluid navigation through the design connectivity graph.  
5. **Modern, Adaptive UI:**  
   * **Fluent Design:** Implements the latest Microsoft Fluent Design principles (acrylic, rounding, depth).  
   * **Theme Awareness:** Automatically respects the OS-level Dark/Light mode setting or allows manual override.  
   * **Quick Filter:** A global filter bar that accepts regex or simple text to filter the currently active grid in real-time.

## **Test Cases**

* **Unit Tests (Business Logic):**  
  * MainViewModel: Verify that changing the SelectedTab property correctly triggers data loading for that specific tab (lazy loading).  
  * DesignService: Test the logic that maps raw Protobuf messages (e.g., NetRecord) into ViewModels.  
  * NavigationService: Verify that a request to navigate to a specific Net correctly changes the view state and applies the filter.  
* **Integration Tests (System):**  
  * **IPC Communication:** Use a "Dummy Viewer" console app to act as the pipe server. Send serialized JSON commands and assert they are received correctly. Verify deserialization of malformed JSON handles errors gracefully.  
  * **API Contract:** Spin up a **Dockerized** instance of OdbDesignServer using TestContainers. Execute the Refit and gRPC clients against it to ensure the C\# DTOs match the actual server response format.  
* **Performance Tests:**  
  * **Load Testing:** Load a "Stress Test" ODB++ design (e.g., high-density interconnect board).  
  * **Scroll Performance:** Automate scrolling the grid from top to bottom and measure frame drop.  
  * **Search Latency:** Measure the time taken to filter a 20,000-row Netlist down to 1 row.

## **Logging**

* **Library:** Serilog is the standard for structured logging.  
* **Sinks Strategy:**  
  * **Console:** Enriched logging (colors) for developer debugging.  
  * **Rolling File:** Writes to %AppData%/OdbDesignClient/logs/log-.txt. Retains logs for 7 days. Critical for diagnosing crashes on user machines.  
* **Scope & Levels:**  
  * Information: App lifecycle (Startup, Shutdown), Navigation events ("User switched to Nets tab").  
  * Warning: API timeouts, retries, or recoverable IPC connection failures.  
  * Error: Unhandled exceptions, API 500 responses, or JSON parsing failures.  
  * **Performance Metrics:** Log the duration of specific operations (e.g., Log.Information("Loaded {Count} nets in {Duration}ms")).

## **Containerization: Docker**

* **Usage:** Docker is explicitly used for **Integration Testing infrastructure**, not for distributing the client app itself.  
* **Strategy:**  
  * The solution includes a docker-compose.test.yml or utilizes the TestContainers C\# library.  
  * During the CI build, a container running OdbDesignServer is started.  
  * The Integration Test suite runs against this ephemeral container to validate the API clients.  
  * This ensures that client code never drifts from the server implementation.

## **Containerization: Docker Compose**

* Not applicable for the production Client application (which is a native Desktop artifact), but may be used in the repo to orchestrate the test environment.

## **Swagger/OpenAPI**

* **Source:** The swagger/odbdesign-server-0.9-swagger.yaml file is the source of truth for the REST API.  
* **Usage:**  
  * **Refit:** The build process parses this YAML to auto-generate the IOdbDesignApi C\# interface.  
  * **Validation:** Any changes to the server API must be reflected here first, or the client build will fail, ensuring type safety.  
* **Coverage:** Covers all Control Plane operations: Retrieving the Design List, Step hierarchy, Layer definitions, File Upload endpoints, and Server Health status.

## **Documentation**

* **Code Level:**  
  * **XML Documentation:** All public members of Services and ViewModels must have triple-slash /// comments. These appear in IDE tooltips.  
  * **Architecture Decision Records (ADRs):** Major decisions (e.g., choosing Avalonia over MAUI) should be documented in the /docs folder.  
* **User Level:**  
  * **Connection Guide:** A clear guide on how to configure the client to connect to a remote OdbDesignServer (IP/Port configuration).  
  * **Feature Manual:** Explanations of how to use the Filtering syntax and Cross-Probing features.

## **Acceptance Criteria**

* **Build & Deploy:**  
  * The solution must build successfully on Windows, Linux, and macOS without compilation warnings.  
  * dotnet publish must yield a functional executable for each platform.  
* **Connectivity & Resilience:**  
  * The application defaults to localhost:5000 and successfully connects upon launch.  
  * If the connection drops, the UI must display a prominent "Reconnecting..." overlay.  
  * When the connection is restored, the overlay must disappear, and data should refresh automatically.  
* **Visualization:**  
  * Loading a complex design must populate the Components and Nets grids.  
  * The UI thread must remain responsive (no "Not Responding" freeze) during data fetch.  
* **Interactivity:**  
  * Clicking a Component row must send a correctly formatted JSON payload to the OdbDesignViewerPipe.  
  * The application must correctly handle incoming IPC messages from the Viewer and update the UI selection state.  
* **Standards:**  
  * All new code must pass the defined StyleCop rules.  
  * Unit test coverage for the Core project must exceed 80%.

## **Language**

* **C\#** (Selected for its strong typing, rich ecosystem, and performance).

## **Language Version**

* **.NET v10.0** (Preview/Latest Stable).  
  * Utilizing features like Primary Constructors, Raw String Literals, and generic math support.

## **Include global.json?**

* Yes  
* **SDK:** 10.0.0  
* **RollForward:** latestFeature (Ensures the build environment uses the exact specified .NET SDK version to prevent "works on my machine" issues).

## **Frameworks, Tools, Packages**

* **UI Framework:** Avalonia (Latest Stable).  
  * Chosen for its Skia-based rendering engine which provides identical visuals across all OSs.  
* **Themes:** Avalonia.Themes.Fluent.  
  * Provides the modern Windows 11-style look and feel.  
* **MVVM:** CommunityToolkit.Mvvm.  
  * Utilizes C\# Source Generators to reduce boilerplate code (e.g., \[ObservableProperty\]).  
* **Data Grid:** Avalonia.Controls.TreeDataGrid.  
  * The only control capable of high-performance, hierarchical data rendering in Avalonia.  
* **REST Client:** Refit.  
  * turns the Swagger definition into a declarative, type-safe C\# interface.  
* **gRPC Client:** Grpc.Net.Client, Google.Protobuf, Grpc.Tools.  
  * Essential for the high-performance binary data streaming requirements.  
* **Dependency Injection:** Microsoft.Extensions.DependencyInjection.  
  * Standard .NET DI container for managing service lifetimes.  
* **Logging:** Serilog, Serilog.Extensions.Hosting, Serilog.Sinks.File.  
  * Provides structured logging capabilities.  
* **Testing:** xUnit (Runner), Moq (Mocking), TestContainers (Docker integration).

## **Project Structure/Package System**

The solution follows a strict **Clean Architecture** layout to ensure maintainability:

* **Solution:** OdbDesignClient.sln  
* **Projects:**  
  * **src/OdbDesignClient**: The Executable Project. Contains Views, Windows, Styles, and UI-specific adapters. It depends on OdbDesignClient.Core.  
  * **src/OdbDesignClient.Core**: The Class Library. Contains ViewModels, Models (DTOs), Service Interfaces, and Business Logic. It has *no* dependency on Avalonia, making it testable and portable.  
  * **src/OdbDesignClient.Tests**: The xUnit Test Project. Contains all unit and integration tests.  
* **Assets & Config:**  
  * swagger/: Contains the OpenAPI YAML definitions.  
  * OdbDesignLib/protoc/: Contains the shared .proto definitions for gRPC.  
  * Directory.Build.props: Centralized package version management.

## **GitHub**

* **Repo:** https://github.com/intel-agency/OdbDesignClient  
* **Branch:** develop (Main development branch; main is for stable releases).

## **Deliverables**

1. **Source Code:** A complete, compilable .sln repository adhering to the specified architecture and passing all linter checks.  
2. **Binaries:**  
   * **Windows:** OdbDesignClient.exe (Self-contained, trimmed).  
   * **Linux:** OdbDesignClient binary (Self-contained, tested on Ubuntu).  
   * **macOS:** OdbDesignClient.app bundle (Signed/Ad-hoc).  
3. **CI Pipeline:** A fully functional .github/workflows/build-client.yml that builds, tests, and artifacts the binaries for all three platforms.  
4. **Test Report:** A comprehensive test report showing passing status for Unit and Integration tests.

[image1]: <data:image/png;base64,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>

[image2]: <data:image/png;base64,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>

[image3]: <data:image/png;base64,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>
