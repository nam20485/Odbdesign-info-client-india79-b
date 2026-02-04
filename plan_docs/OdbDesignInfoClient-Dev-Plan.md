# **OdbDesign Client Development Plan**

## **Phase 1: Foundation & Infrastructure**

**Goal**: Initialize the solution and ensure the build pipeline works.

1. **Project Initialization**:  
   * Create OdbDesignClient solution and projects.  
   * Add NuGet packages: Avalonia, Avalonia.Controls.TreeDataGrid, CommunityToolkit.Mvvm, Refit, Grpc.Net.Client, Serilog.  
2. **CI Setup**:  
   * Create .github/workflows/build-client.yml.  
   * Ensure build passes on Windows, Ubuntu, and macOS runners.  
3. **Basic Shell**:  
   * Implement MainWindow with a basic TabControl.  
   * Set up Dependency Injection (App.axaml.cs).

## **Phase 2: Connectivity Layer**

**Goal**: Establish communication with the OdbDesignServer.

1. **API Generation**:  
   * Configure Refit to generate the REST client from swagger/odbdesign-server-0.9-swagger.yaml.  
   * Configure Protobuf compilation for OdbDesignLib/protoc/\*.proto files.  
2. **Connection Service**:  
   * Implement ConnectionService to handle server URLs (default: localhost:5000).  
   * Add a visual "Connection Status" indicator in the UI.  
3. **Board Loading Control**:  
   * Create a REST endpoint wrapper to GET /designs.  
   * Build a UI control (ComboBox or List) to select an active design from the server.

## **Phase 3: The "Fancy" Grids (Core Feature)**

**Goal**: Implement the hierarchical data visualization using TreeDataGrid.

1. **Grid Strategy Implementation**:  
   * Create generic factories for HierarchicalTreeDataGridSource.  
2. **Components & Nets Tabs**:  
   * Implement Components tab with child Nets.  
   * Implement Nets tab with child Components/Pins.  
3. **Filtering & Sorting**:  
   * Enable built-in column sorting.  
   * Implement "Quick Filter" text box for real-time filtering.

## **Phase 4: Extended Data Visualization**

**Goal**: Complete the comprehensive set of ODB++ feature tabs.

1. **Implement Remaining Tabs**:  
   * **Pins**: Pin \-\> Net/Component hierarchy.  
   * **Parts & Packages**: Part \-\> Components, Package \-\> Parts.  
   * **Vias**: Via Def \-\> Usage Instances.  
   * **Stackup**: Layer Matrix visualization.  
   * **Drill Tools**: Tools \-\> Drill Hits.  
   * **Step Hierarchy**: Tree view of steps.  
   * **Symbols & EDA Data**: Reference lists.  
2. **Advanced UI**:  
   * Use Avalonia.Themes.Fluent for a modern look.  
   * Add Dark/Light mode toggle.

## **Phase 5: Cross-Probing & IPC**

**Goal**: Enable bidirectional communication with the 3D Viewer.

1. **IPC Service Implementation**:  
   * Create CrossProbeService wrapping System.IO.Pipes.  
   * Implement connection retry logic (Viewer might be started before or after Client).  
2. **Protocol Definition**:  
   * Define JSON message contracts for Select, Zoom, and Highlight commands.  
3. **Client \-\> Viewer**:  
   * Bind Grid selection events (Row Click) to CrossProbeService.SendAsync().  
4. **Viewer \-\> Client**:  
   * Implement a background listener loop in CrossProbeService.  
   * Dispatch received messages to the UI thread to update Grid selection/scroll.

## **Phase 6: Testing & Release**

**Goal**: Ensure stability and distribute.

1. **Unit Testing**: Write tests for MainViewModel (navigation) and DesignService (data parsing).  
2. **Integration Testing**:  
   * Test IPC with a dummy console app simulating the Viewer.  
3. **Performance Tuning**: Test with large ODB++ designs (virtualization check).  
4. **Packaging**: Use dotnet publish to create self-contained executables for all platforms.

## **Task Breakdown & Status**

| ID | Task | Priority | Status |
| :---- | :---- | :---- | :---- |
| 1.1 | Solution Setup & CI | High | Pending |
| 2.1 | Generate REST/gRPC Clients | High | Pending |
| 3.1 | Components & Nets Grids | High | Pending |
| 4.1 | Stackup & Vias Grids | Medium | Pending |
| 5.1 | IPC Named Pipes Implementation | High | Pending |
| 5.2 | Bidirectional Event Sync | High | Pending |
| 6.1 | Unit & Integration Tests | Medium | Pending |

