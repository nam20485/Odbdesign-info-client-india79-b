# **OdbDesign Client Architecture**

## **1\. Overview and Scope**

The **OdbDesign Client** is a sophisticated, cross-platform desktop application engineered to visualize, interrogate, and interact with ODB++ design data served by the OdbDesignServer.

Unlike a simple file viewer, this application serves as an analytical client that transforms raw, hierarchical ODB++ data into structured, navigable information. It is designed for CAM engineers, PCB designers, and manufacturing specialists who need to verify design intent, check component connectivity, and validate stackup configurations.

The application operates in a "connected" mode, relying on the OdbDesignServer to parse and serve disparate ODB++ artifacts (files, archives, directories) via a unified REST and gRPC API. This separation allows the client to remain lightweight while the server handles the heavy lifting of geometry parsing and file extraction.

## **2\. Technology Stack & Rationale**

* **Runtime Framework**: **.NET 10**  
  * *Rationale*: Leveraging the latest .NET release ensures access to the highest performance garbage collector, newest language features (C\# 14/15), and long-term support. Performance is critical when rendering grids with thousands of rows.  
* **UI Framework**: [**Avalonia UI**](https://avaloniaui.net/) **(Latest Stable)**  
  * *Rationale*: Avalonia is the only viable option for a true "write once, run anywhere" desktop experience on .NET. Unlike MAUI (which wraps native controls), Avalonia renders its own pixels using Skia, guaranteeing that the complex DataGrids and custom drawing look identical on Windows, Linux, and macOS. This consistency is vital for engineering tools where visual precision is paramount.  
* **MVVM Toolkit**: [**CommunityToolkit.Mvvm**](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)  
  * *Rationale*: This library provides a modern, source-generator-based approach to MVVM. It eliminates boilerplate code for INotifyPropertyChanged and ICommand, allowing developers to focus on business logic. It is more lightweight and performant than older frameworks like Prism or ReactiveUI for this specific use case.  
* **Data Grid Control**: **Avalonia.Controls.TreeDataGrid**  
  * *Rationale*: The application's core requirement is to display "fancy child rows" and master-detail hierarchies (e.g., Component \-\> Nets). The standard DataGrid struggles with nesting. TreeDataGrid is designed specifically for hierarchical data, supporting high-performance UI virtualization (recycling elements during scrolling), which is non-negotiable for PCB designs containing 10,000+ components.  
* **Networking Layer**:  
  * **REST**: **Refit** is used to auto-generate type-safe clients from the swagger/odbdesign-server-0.9-swagger.yaml file. This prevents "magic string" errors in URL construction and ensures the client stays synchronized with the server API.  
  * **gRPC**: **Grpc.Net.Client** processes OdbDesignLib/protoc/\*.proto files to generate highly efficient binary transport clients. This is used for fetching bulk lists (like netlists or vector data) where JSON overhead would be prohibitive.  
* **IPC (Cross-Probing)**: **System.IO.Pipes (Named Pipes)**  
  * *Rationale*: To communicate with the companion 3D Viewer application, HTTP is unnecessary overhead. Named Pipes provide a secure, low-latency, OS-level mechanism for inter-process communication on the same machine. This ensures selection events (clicking a pin) feel instantaneous in the 3D view.  
* **Dependency Injection**: **Microsoft.Extensions.DependencyInjection**  
  * *Rationale*: The industry-standard container for managing service lifetimes (Singleton vs. Transient) and decoupling ViewModels from Service implementations.  
* **Logging**: **Serilog**  
  * *Rationale*: Structured logging is essential for diagnosing issues in the field. Serilog integrates seamlessly with the .NET generic host and can write to local rolling files or console output for debugging.

## **3\. Project Structure**

The solution OdbDesignClient.sln follows the **Clean Architecture** principles to separate UI concerns from business logic:

/src  
  /OdbDesignClient          \# Main Executable (Desktop Head)  
    /Assets                 \# Icons, Fonts, Styles, Theme Dictionaries  
    /Views                  \# Avalonia Windows, UserControls, and DataTemplates  
    /App.axaml              \# Application Entry Point and Global Styles  
    /Program.cs             \# Main Entry (Bootstrapper)

  /OdbDesignClient.Core     \# Application Logic (Net Standard / .NET 10 Class Lib)  
    /Models                 \# Domain Entities (Rich objects mapped from DTOs)  
    /ViewModels             \# Application State (MainVM, ComponentGridVM, etc.)  
    /Services               \# Business Logic Contracts & Implementations  
      /Interfaces           \# IConnectionService, IDesignService, INavigationService, ICrossProbeService  
      /Implementations      \# Concrete Logic (Network calls, IPC handling)  
    /Api                    \# Generated API Clients  
      /Rest                 \# Refit interfaces  
      /Grpc                 \# Protobuf generated classes

/tests  
  /OdbDesignClient.Tests    \# Unit Tests (xUnit) ensuring logic correctness

## **4\. Key System Components**

### **4.1 Connection Service (IConnectionService)**

* **Role**: The gatekeeper for all external communication. It does not just "connect"; it manages the *health* of the connection.  
* **Behaviors**:  
  * **Auto-Discovery/Defaulting**: Defaults to localhost:5000 but allows user override.  
  * **Health Monitoring**: Periodically polls the /health endpoint. If the server goes down, the UI updates to a "Reconnecting..." state, disabling data-dependent tabs to prevent crashes.  
  * **Protocol Negotiation**: Determines if the server supports gRPC. If firewalls or proxies block gRPC, it can degrade gracefully to REST-only (if endpoints exist), or alert the user.

### **4.2 Design Data Service (IDesignService)**

* **Role**: The primary data provider for ViewModels. It abstracts the underlying transport (REST vs gRPC).  
* **Behaviors**:  
  * **Abstraction**: A ViewModel calls GetComponentsAsync(designName). The service decides whether to call the REST endpoint or the gRPC service based on configuration and data size.  
  * **Caching**: Implements a short-lived memory cache. If the user switches tabs from "Components" to "Nets" and back, the data is not re-fetched immediately, preserving the scroll position and expanded state.  
  * **DTO Mapping**: Converts raw API DTOs (Data Transfer Objects) into rich Observable models used by the UI.

### **4.3 Data Grid Strategy (TreeDataGridFactory)**

* **Role**: A centralized factory to generate complex grid definitions. Since standard XAML definition for TreeDataGrid can be verbose, this factory allows defining columns in C\# code.  
* **Architecture**:  
  * Uses HierarchicalTreeDataGridSource\<T\> to support N-level depth.  
  * Implements generic column builders (e.g., .AddTextColumn(), .AddHyperlinkColumn()) to ensure consistent styling across all tabs.  
  * **Performance**: Configures the grid for virtualization. Rows are only instantiated when they scroll into view.

### **4.4 Cross-Probe Service (ICrossProbeService)**

* **Role**: Manages the bidirectional link with the 3D Board Viewer.  
* **Architecture**:  
  * **Transport**: Uses NamedPipeClientStream (Client) and NamedPipeServerStream (Viewer).  
  * **Lifecycle**:  
    1. **Discovery**: Checks if the named pipe OdbDesignViewerPipe exists.  
    2. **Launch**: If not found, it attempts to launch the Viewer executable as a child process with the current design path as an argument.  
    3. **Connection**: Establishes the stream.  
  * **Message Protocol**: Communicates via a lightweight JSON protocol.  
    * **Request (Client \-\> Viewer)**:  
      { "action": "select", "entity\_type": "component", "entity\_id": "R201", "zoom\_to\_fit": true }

    * **Event (Viewer \-\> Client)**:  
      { "event": "selection\_changed", "entity\_type": "net", "entity\_id": "GND" }

  * **Thread Safety**: Incoming messages from the pipe (background thread) are marshaled to the UI thread using Dispatcher.UIThread to update the grid selection safely.

## **5\. UI/UX Design & Feature Breakdown**

### **5.1 Main Layout**

The application layout is divided into three primary zones:

1. **Command Bar (Top)**:  
   * **Connection Indicator**: A colored badge (Green/Red) showing server status.  
   * **Design Selector**: A standardized ComboBox that lists loaded designs. Changing this triggers a global "Context Change" event, clearing all grids and reloading data for the new design.  
   * **Refresh**: Force-reloads data from the server.  
2. **Workspace (Center)**:  
   * A TabControl hosting the various feature grids. Each tab is lazy-loaded; data is fetched only when the tab is first activated.  
3. **Status Bar (Bottom)**:  
   * Displays operational messages ("Loaded 15,000 components in 200ms"), active IPC status ("Viewer Connected"), and server version info.

### **5.2 Feature Tabs & Hierarchy**

Each tab provides a specialized view of the design data, utilizing the master-detail capabilities of the TreeDataGrid.

1. **Components Tab**:  
   * **Root Level**: Component Instance (RefDes, Part Name, Side (Top/Bottom), Layer, Rotation).  
   * **Child Level**: **Connected Nets**. Lists every net connected to this component, including the specific Pin Name and Pin Number.  
2. **Nets Tab**:  
   * **Root Level**: Net Name (e.g., "+5V", "GND", "DDR\_CLK").  
   * **Child Level**: **Connected Features**. Lists every Component Pin, Via, and Test Point associated with this net.  
3. **Pins Tab**:  
   * **Root Level**: Pin Definition (Pin Name, Index, Electrical Type).  
   * **Child Level**:  
     * **Component Context**: Links back to the Component RefDes holding this pin.  
     * **Net Context**: Links to the Net connected to this pin.  
4. **Parts Tab**:  
   * **Root Level**: Part Definition (Vendor Part Number, Manufacturer, Description).  
   * **Child Level**: **Usage List**. A list of every Component RefDes on the board that utilizes this specific part definition.  
5. **Packages Tab**:  
   * **Root Level**: Package/Footprint Name (e.g., "0402", "SOIC-8").  
   * **Child Level**: **Usage List**. Lists all Part definitions and Component instances utilizing this footprint.  
6. **Vias Tab**:  
   * **Root Level**: Via Definition (Name, Drill Size, Plating Status, Start Layer, End Layer).  
   * **Child Level**: **Instance Locations**. A list of X/Y coordinates for every instance of this via on the board, allowing the user to jump to specific locations.  
7. **Stackup / Layers Tab**:  
   * **Root Level**: Layer Definition (ID, Name, Type (Signal/Dielectric/Drill), Polarity, Thickness).  
   * **Child Level**: **Extended Attributes**. Detail views for dielectric constants, material names, and specific EDA attributes mapped to the layer.  
8. **Step Hierarchy Tab**:  
   * **Structure**: A Tree View (rather than a grid) visualizing the nested ODB++ steps.  
   * **Levels**: Job \-\> Step (PCB) \-\> Array/Panel \-\> Repeat steps. Columns show "Repeat Count" and transform matrices.  
9. **Drill Tools Tab**:  
   * **Root Level**: Tool Definition (Tool Number, Diameter, Shape, Plated/Non-Plated).  
   * **Child Level**: **Hit List**. Every drill hit location (X, Y) associated with the tool.  
10. **Symbols Library Tab**:  
    * **Root Level**: Symbol Name, Type (Standard, Round, Square, Custom), Dimensions.  
    * **Child Level**: **Usage List**. References to pads or features using this symbol.  
11. **EDA Data Tab**:  
    * **View**: A flat or grouped grid showing raw Key-Value pairs extracted from eda/data files, useful for debugging CAD metadata import issues.

### **5.3 Cross-Tab Navigation (Hyperlinks)**

To make the data actionable, the grid supports "Deep Linking":

* **Concept**: Any cell containing a reference to another entity (e.g., a "Net Name" cell inside the "Components" tab) is rendered as a clickable hyperlink.  
* **Behavior**:  
  1. User clicks "GND" in the Components tab.  
  2. NavigationService intercepts the click.  
  3. The active tab switches to **Nets**.  
  4. The grid in the Nets tab filters or scrolls to the "GND" row.  
  5. The row is highlighted (flashed) to draw attention.  
  6. Simultaneously, an IPC message is sent to the 3D Viewer to highlight the entire GND net.

## **6\. Testing Strategy**

* **Unit Tests**:  
  * Focus on ViewModels and Services.  
  * Use **Moq** or **NSubstitute** to mock IConnectionService and IDesignService. This allows testing UI logic (e.g., "Does clicking a hyperlink trigger the NavigationService?") without a running server.  
* **Integration Tests**:  
  * Spin up a local Docker container of OdbDesignServer using **TestContainers**.  
  * Run the actual Refit and gRPC clients against this container to verify parsing accuracy and API contract compatibility.  
* **IPC Testing**:  
  * Create a simple "Dummy Viewer" console app that acts as a Named Pipe server.  
  * The test suite launches the client and the dummy viewer, sends commands, and asserts that the dummy viewer received the correct JSON payloads.  
* **CI/CD Pipeline**:  
  * Use GitHub Actions to build the solution on Windows, Ubuntu, and macOS agents.  
  * Ensure dotnet test passes on all platforms to catch OS-specific pathing or networking issues early.