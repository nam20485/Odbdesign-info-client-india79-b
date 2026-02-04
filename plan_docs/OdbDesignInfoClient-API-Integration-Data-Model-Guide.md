# **OdbDesign Client: API Integration & Data Model Guide**

**Version:** 2.0 (Expanded)

**Target Audience:** Client Development Team

**Scope:** OdbDesignServer v0.9+ Integration

## **1\. Integration Architecture**

The OdbDesign Client utilizes a **Hybrid Connectivity Strategy** to balance ease of management with high-performance data throughput.

| Protocol | Implementation | Role | Typical Use Cases |
| :---- | :---- | :---- | :---- |
| **REST (HTTP/1.1)** | Refit | **Control Plane** | Design discovery, session management, file upload, health checks, lightweight metadata (Steps/Layers lists), quick attribute lookups. |
| **gRPC (HTTP/2)** | Grpc.Net.Client | **Data Plane** | Bulk geometry transfer, full netlist streaming, component catalog retrieval, heavy analytical queries, streaming large datasets. |

### **1.1 Connection Parameters**

* **Default REST Port:** 8888 (HTTP)  
* **Default gRPC Port:** 50051 (HTTP/2)  
* **Timeouts:**  
  * REST: 30s default, 5m for uploads.  
  * gRPC: Infinite (streams can last minutes).

## **2\. Authentication & Security**

The server implements **Basic Authentication**. All requests (REST and gRPC) must include valid credentials if authentication is enabled on the server.

### **2.1 Mechanism**

* **Type:** HTTP Basic Auth  
* **Encoding:** Base64 encoded username:password string.  
* **Header Key:** Authorization  
* **Header Value:** Basic \<Base64String\>

### **2.2 Server Configuration**

The server validates credentials via BasicRequestAuthentication.

* **Default User:** (Check config.json or server environment variables)  
* **Anonymous Access:** If the server is configured to allow anonymous access, the header may be omitted, but the client **should** support sending it to be compatible with secure deployments.

## **3\. Data Model Reference**

The OdbDesign data model transforms the raw ODB++ file structure into a queryable object graph.

### **3.1 Entity Hierarchy**

Design (Root)  
├── Steps (e.g., "odb\_job", "panel")  
│   ├── Layers (Physical & EDA)  
│   │   ├── Name, Type (Signal/Dielectric), Polarity  
│   │   └── AttrList (Attributes)  
│   ├── Netlist  
│   │   └── Nets (e.g., "DDR4\_DQS")  
│   │       ├── PinConnections (Refs to Component Pins)  
│   │       └── FeatureConnections (Refs to Vias/Traces)  
│   ├── EDA Data  
│   │   ├── Components (Instances)  
│   │   │   ├── RefDes, PartName, PackageName  
│   │   │   └── Pins (Geometry & Nets)  
│   │   ├── Parts (Library)  
│   │   │   └── Attributes (e.g., "Value", "Tolerance")  
│   │   └── Packages (Footprints)  
│   │       ├── Outline (ContourPolygon)  
│   │       └── Pin Geometry  
│   └── Profile (Board Outline)  
└── FileModel (Raw Archive Access)

### **3.2 Key Entities & Fields**

#### **Component (Instance)**

Represents a physical part placed on the board.

* **RefDes**: Unique identifier (e.g., U1, R102).  
* **PartName**: Key to lookup in the Parts library.  
* **PackageName**: Key to lookup in the Packages library.  
* **Side**: Top (0) or Bottom (1).  
* **Transformation**: Rotation (degrees), Mirror (bool), Translation (![][image1]).  
* **Attributes**: Key-value pairs (e.g., placed: true).

#### **Net (Connection)**

Represents an electrical signal.

* **Name**: Unique name (e.g., \+5V, GND).  
* **PinConnections**: List of attached component pins.  
* **Side**: The side of the board where the net is primarily routed (if applicable).  
* **Color**: Assigned highlight color (R, G, B).

#### **Pin (Connection Point)**

* **Name**: Pin name/number (e.g., 1, A1).  
* **Index**: Zero-based index in the package.  
* **X, Y**: Center coordinates relative to the component center.

## **4\. REST API Reference (Control Plane)**

The REST API corresponds to the OdbDesignServer controllers.

### **4.1 Designs Controller (/api/designs)**

| Method | Endpoint | Description | Returns |
| :---- | :---- | :---- | :---- |
| GET | /api/designs | List all loaded designs. | List\<string\> |
| GET | /api/designs/{design}/steps | List steps in design. | List\<string\> |
| GET | /api/designs/{design}/steps/{step}/layers | Get layer matrix. | List\<LayerModel\> |
| GET | /api/designs/{design}/steps/{step}/netlist | Get all nets (lightweight). | List\<string\> |
| GET | /api/designs/{design}/steps/{step}/netlist/{net} | Get specific net details. | NetModel |
| GET | /api/designs/{design}/steps/{step}/parts | Get all parts. | List\<PartModel\> |
| GET | /api/designs/{design}/steps/{step}/packages | Get all packages. | List\<PackageModel\> |

### **4.2 File Upload (/api/upload)**

| Method | Endpoint | Description | Payload |
| :---- | :---- | :---- | :---- |
| POST | /api/upload | Upload .tgz or .zip ODB++ archive. | Multipart/Form-Data |

### **4.3 Health (/api/health)**

| Method | Endpoint | Description | Returns |
| :---- | :---- | :---- | :---- |
| GET | /api/health | Server status check. | HealthStatus (JSON) |

## **5\. gRPC API Reference (Data Plane)**

Defined in service.proto and imported files. The client uses OdbDesignService.

### **5.1 Service Definition**

service OdbDesignService {  
    // Check if design is available  
    rpc IsDesignPresent (DesignRequest) returns (BooleanResponse);

    // Stream ALL components for a step (High Performance)  
    rpc GetComponents (StepRequest) returns (stream ComponentMessage);

    // Stream ALL nets with full connectivity info  
    rpc GetNets (StepRequest) returns (stream NetMessage);

    // Get a specific package geometry  
    rpc GetPackage (PackageRequest) returns (PackageMessage);

    // Get a specific part definition  
    rpc GetPart (PartRequest) returns (PartMessage);  
      
    // Get full EDA data hierarchy (use with caution on large designs)  
    rpc GetEdaData (StepRequest) returns (EdaDataMessage);  
}

### **5.2 Message Structures**

**ComponentMessage**

message ComponentMessage {  
    string refDes \= 1;  
    string partName \= 2;  
    string packageName \= 3;  
    bool topSide \= 4;  
    TransformationMessage transform \= 5;  
    repeated PinMessage pins \= 6;  
    map\<string, string\> attributes \= 7;  
}

**NetMessage**

message NetMessage {  
    string name \= 1;  
    repeated PinConnectionMessage pinConnections \= 2;  
    // ... feature connections  
}

**PackageMessage**

message PackageMessage {  
    string name \= 1;  
    float pitch \= 2;  
    float xMin \= 3;  
    float xMax \= 4;  
    float yMin \= 5;  
    float yMax \= 6;  
    repeated PinMessage pins \= 7;  
    ContourPolygonMessage outline \= 8;  
}

## **6\. Client Implementation Strategy (C\#)**

The client must handle both protocols and authentication seamlessly.

### **6.1 Dependency Injection & Auth Setup**

We use Refit with a custom DelegatingHandler for REST auth, and CallCredentials for gRPC auth.

**1\. Define the Auth Service Interface**

public interface IAuthService  
{  
    string? GetBase64Credentials(); // Returns "dXNlcm5hbWU6cGFzc3dvcmQ="  
    bool IsAuthenticated { get; }  
}

**2\. Create REST Auth Handler**

public class AuthHeaderHandler : DelegatingHandler  
{  
    private readonly IAuthService \_authService;  
    public AuthHeaderHandler(IAuthService authService) \=\> \_authService \= authService;

    protected override async Task\<HttpResponseMessage\> SendAsync(HttpRequestMessage request, CancellationToken ct)  
    {  
        if (\_authService.IsAuthenticated)  
        {  
            request.Headers.Authorization \= new AuthenticationHeaderValue("Basic", \_authService.GetBase64Credentials());  
        }  
        return await base.SendAsync(request, ct);  
    }  
}

**3\. Configure Services in App.axaml.cs**

public void ConfigureServices(IServiceCollection services)  
{  
    string restUrl \= "http://localhost:8888";  
    string grpcUrl \= "http://localhost:50051";

    services.AddSingleton\<IAuthService, BasicAuthService\>();  
    services.AddTransient\<AuthHeaderHandler\>();

    // \--- REST Client Setup \---  
    services.AddRefitClient\<IOdbDesignApi\>()  
            .ConfigureHttpClient(c \=\> c.BaseAddress \= new Uri(restUrl))  
            .AddHttpMessageHandler\<AuthHeaderHandler\>(); // Injects Auth Header

    // \--- gRPC Client Setup \---  
    services.AddSingleton(sp \=\>   
    {  
        var authService \= sp.GetRequiredService\<IAuthService\>();  
          
        // Create credentials that inject the metadata per-call  
        var credentials \= CallCredentials.FromInterceptor((context, metadata) \=\>  
        {  
            if (authService.IsAuthenticated)  
            {  
                metadata.Add("Authorization", $"Basic {authService.GetBase64Credentials()}");  
            }  
            return Task.CompletedTask;  
        });

        // Combine channel with credentials  
        var channel \= GrpcChannel.ForAddress(grpcUrl);  
        var invoker \= channel.Intercept(credentials);  
          
        return new OdbDesignService.OdbDesignServiceClient(invoker);  
    });

    services.AddSingleton\<IDesignService, DesignService\>();  
}

### **6.2 The Data Service Implementation**

The DesignService abstracts the complexity of fetching data. It decides when to use REST and when to use gRPC.

public class DesignService : IDesignService  
{  
    private readonly IOdbDesignApi \_rest;  
    private readonly OdbDesignService.OdbDesignServiceClient \_grpc;

    public DesignService(IOdbDesignApi rest, OdbDesignService.OdbDesignServiceClient grpc)  
    {  
        \_rest \= rest;  
        \_grpc \= grpc;  
    }

    // Use Case: Populating the "Components" DataGrid  
    public async Task\<List\<ComponentViewModel\>\> GetComponentsAsync(string design, string step)  
    {  
        var result \= new List\<ComponentViewModel\>();  
        var request \= new StepRequest { DesignName \= design, StepName \= step };

        // Use gRPC streaming for performance  
        using var call \= \_grpc.GetComponents(request);  
        await foreach (var msg in call.ResponseStream.ReadAllAsync())  
        {  
            // Map Protobuf \-\> ViewModel  
            var vm \= new ComponentViewModel  
            {  
                RefDes \= msg.RefDes,  
                PartName \= msg.PartName,  
                PackageName \= msg.PackageName,  
                Side \= msg.TopSide ? "Top" : "Bottom",  
                X \= msg.Transform?.X ?? 0,  
                Y \= msg.Transform?.Y ?? 0,  
                Rotation \= msg.Transform?.Rotation ?? 0  
            };  
            result.Add(vm);  
        }  
        return result;  
    }

    // Use Case: Populating the "Nets" DataGrid  
    public async Task\<List\<NetViewModel\>\> GetNetsAsync(string design, string step)  
    {  
        var result \= new List\<NetViewModel\>();  
        var request \= new StepRequest { DesignName \= design, StepName \= step };

        using var call \= \_grpc.GetNets(request);  
        await foreach (var msg in call.ResponseStream.ReadAllAsync())  
        {  
            result.Add(new NetViewModel   
            {  
                Name \= msg.Name,  
                PinCount \= msg.PinConnections.Count  
            });  
        }  
        return result;  
    }

    // Use Case: Fetching Stackup (Metadata \-\> REST)  
    public async Task\<List\<LayerModel\>\> GetStackupAsync(string design, string step)  
    {  
        // Use REST for simple lists where streaming isn't needed  
        return await \_rest.GetLayers(design, step);  
    }  
}

### **6.3 Performance Considerations**

1. **Streaming:** Always use ReadAllAsync() for gRPC streams. Never try to load the entire stream into a List before processing if you can avoid it (though for TreeDataGrid we often need the full list for sorting).  
2. **Caching:** The DesignService should cache the Step list and Layer matrix to avoid hitting the REST API on every tab switch.  
3. **Error Handling:** Wrap all gRPC calls in try/catch (RpcException ex).  
   * StatusCode.Unavailable: Server is down \-\> Show "Reconnecting".  
   * StatusCode.Unauthenticated: Credentials rejected \-\> Show Login Dialog.

[image1]: <data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACoAAAAZCAYAAABHLbxYAAACF0lEQVR4Xu1WzytEURR+YyYkJfRK8+u9eTMbE1kJSdmIkpWF0thZKHvCQpFipSixsrUhxcZOlGxspWzs/RF8nzlTd47Bmyuh5qvTu/ec735z7j33xzhODTX8L0R93x+FbXme9wy7hc2nUqksg/yivy6xE9hALpdr0CKVAJ4L3SnYFcY9pdPpBdigyUGsg36J7+M7CZ9vcsqQTCbbQDqmsW3GMG4M1odmxPSHBcbOcaJIZETHCOojfqn9HwLkWS2IpLvRPzV51QJV6YLuCyuDbtSMQTtPfX5N/1eIiuANBgawI7THHcuVNCG6ZYsgZS84NvoUE9E1a5EK4OSpi+Q22Xddtxn9PTRjihoOXnGfvs3esRWpAOjtiO41ujEkvCSJ2kEEKPjiqP30HfAkM0nqckux7JpTDSIQOIDYrggGmmCLfD5fD71t0a3q4GhEIDLNmeKUDrH0EJzRJFuYiTq2lYLAIATOzZl6xavqCYn3m1xbZDKZHujd03QsNJDgBRIaVr5A9uq7u89EPB5v4mppvwarI3pnOvYZ6lDeXgxexcBHllr9WCybzaZEmHtqEdbKcQaHq75S4ph+E7yGEolEOzh3wl0OgqDFCXPtsZylHxDj3TlQiiOpCRWnnemnFb5Ov/iOP+DbaMZK4AnXWvAdcgKa++NgwmHK/6vgPylfXpu/jAjKWMCKbujAn4IcuL3vvjI11PBf8QrddZU5zc2GawAAAABJRU5ErkJggg==>