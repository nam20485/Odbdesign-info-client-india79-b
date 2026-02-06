# REST API Client Implementation Plan

## Issue Reference

**GitHub Issue:** [#10 - Implement REST API JSON Deserialization for Component and Net Data](https://github.com/nam20485/Odbdesign-info-client-india79-b/issues/10)

**Priority:** 🔴 High  
**Impact:** 🔴 Major - Breaks fallback architecture, limits deployment flexibility  
**Estimated Effort:** 2-3 days (once JSON schema validated)  
**Status:** Ready to implement

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Problem Statement](#problem-statement)
3. [Server Architecture Analysis](#server-architecture-analysis)
4. [JSON Schema Reference](#json-schema-reference)
5. [Implementation Plan](#implementation-plan)
6. [DTO Class Definitions](#dto-class-definitions)
7. [Deserialization Implementation](#deserialization-implementation)
8. [Error Handling Strategy](#error-handling-strategy)
9. [Unit Test Examples](#unit-test-examples)
10. [Verification Checklist](#verification-checklist)
11. [Appendix](#appendix)

---

## Executive Summary

The OdbDesignInfoClient uses a **hybrid gRPC/REST architecture** where gRPC is the primary communication method and REST serves as a fallback. Currently, the REST API client-side implementation is incomplete - while the HTTP client can successfully call the REST endpoints, it **lacks JSON deserialization logic** to parse responses into usable C# objects.

### Solution Overview

1. Create C# DTO classes matching the protobuf-generated JSON schema
2. Implement JSON deserialization in `GetComponentsViaRestAsync` and `GetNetsViaRestAsync`
3. Map DTOs to existing domain models
4. Add comprehensive error handling and logging
5. Write unit tests for all scenarios

---

## Problem Statement

### Current Behavior

When gRPC is unavailable, the application throws `NotImplementedException` instead of falling back to REST:

```csharp
// src/OdbDesignInfoClient.Services/DesignService.cs (Lines 134-141)
private async Task<List<Component>> GetComponentsViaRestAsync(string designId, CancellationToken cancellationToken)
{
    // REST API parsing not implemented yet
    _logger?.LogWarning("REST API for components is not implemented yet");
    throw new NotImplementedException("REST API component parsing not yet implemented. Use gRPC.");
}

// Lines 212-218
private async Task<List<Net>> GetNetsViaRestAsync(string designId, CancellationToken cancellationToken)
{
    // REST API parsing not implemented yet
    _logger?.LogWarning("REST API for nets is not implemented yet");
    throw new NotImplementedException("REST API net parsing not yet implemented. Use gRPC.");
}
```

### Root Cause

The Refit interface returns raw JSON strings that need manual parsing:

```csharp
// src/OdbDesignInfoClient.Services/Api/IOdbDesignRestApi.cs (Lines 40-56)
/// <summary>
/// Gets the components for a design.
/// </summary>
[Get("/designs/{name}/components")]
Task<ApiResponse<string>> GetComponentsAsync(string name, CancellationToken cancellationToken = default);

/// <summary>
/// Gets the nets for a design.
/// </summary>
[Get("/designs/{name}/nets")]
Task<ApiResponse<string>> GetNetsAsync(string name, CancellationToken cancellationToken = default);
```

---

## Server Architecture Analysis

### OdbDesignServer REST Endpoints

The server is implemented in C++ using the Crow HTTP framework. Key endpoints are defined in `DesignsController.cpp`:

| Method | Endpoint | Description | Returns |
|--------|----------|-------------|---------|
| GET | `/designs` | List all loaded designs | `string[]` |
| GET | `/designs/{name}` | Get full design details | `Design` JSON |
| GET | `/designs/{name}/components` | Get all components | `Component[]` JSON |
| GET | `/designs/{name}/nets` | Get all nets | `Net[]` JSON |
| GET | `/designs/{name}/parts` | Get all parts | `Part[]` JSON |
| GET | `/designs/{name}/packages` | Get all packages | `Package[]` JSON |

### JSON Serialization Path

The server uses Protocol Buffers for data modeling and converts to JSON:

```
C++ Model (Component, Net, etc.)
    → to_protobuf() [IProtoBuffable interface]
    → Protocol Buffer Message
    → google::protobuf::util::MessageToJsonString()
    → JSON String (camelCase field names)
    → crow::response
```

### Server Response Handler (Components)

```cpp
// OdbDesignServer/Controllers/DesignsController.cpp
crow::response DesignsController::designs_components_route_handler(
    std::string designName, const crow::request& req)
{
    auto designNameDecoded = UrlEncoding::decode(designName);
    if (designNameDecoded.empty())
    {
        return crow::response(crow::status::BAD_REQUEST, "design name not specified");
    }

    auto pDesign = m_serverApp.designs().GetDesign(designNameDecoded);
    if (pDesign == nullptr)
    {
        std::stringstream ss;
        ss << "design: \"" << designNameDecoded << "\" not found";
        return crow::response(crow::status::NOT_FOUND, ss.str());
    }

    std::vector<crow::json::rvalue> rvComponents;
    
    const auto& components = pDesign->GetComponents();
    for (const auto& component : components)
    {
        rvComponents.push_back(crow::json::load(component->to_json()));
    }

    crow::json::wvalue wv;
    wv = std::move(rvComponents);
    return crow::response(wv);
}
```

**Key Insight:** The server returns a **JSON array directly**, not wrapped in an object like `{"components": [...]}`.

---

## JSON Schema Reference

### Protobuf Definitions

#### Component (component.proto)

```protobuf
message Component {
    optional string refDes = 1;
    optional string partName = 2;
    optional Package package = 3;
    optional uint32 index = 4;
    optional BoardSide side = 5;  // enum: Top/Bottom/BsNone
    optional Part part = 6;
}

enum BoardSide {
    BsNone = 0;
    Top = 1;
    Bottom = 2;
}
```

#### Net (net.proto)

```protobuf
message Net {
    optional string name = 1;
    repeated PinConnection pinConnections = 2;
    optional uint32 index = 3;
}
```

#### PinConnection (pinconnection.proto)

```protobuf
message PinConnection {
    optional string name = 1;
    optional Component component = 2;
    optional Pin pin = 3;
}
```

#### Pin (pin.proto)

```protobuf
message Pin {
    optional string name = 1;
    optional uint32 index = 2;
    // Additional fields for geometry...
}
```

#### Package (package.proto)

```protobuf
message Package {
    optional string name = 1;
    optional float pitch = 2;
    optional float xMin = 3;
    optional float xMax = 4;
    optional float yMin = 5;
    optional float yMax = 6;
    repeated Pin pins = 7;
    optional ContourPolygon outline = 8;
}
```

#### Part (part.proto)

```protobuf
message Part {
    optional string name = 1;
    map<string, string> attributes = 2;
}
```

### Expected JSON Response Examples

#### Components Response (`GET /designs/{name}/components`)

```json
[
  {
    "refDes": "U1",
    "partName": "STM32F407VGT6",
    "package": {
      "name": "LQFP-100",
      "pitch": 0.5,
      "xMin": -7.0,
      "xMax": 7.0,
      "yMin": -7.0,
      "yMax": 7.0,
      "pins": [
        {
          "name": "1",
          "index": 0
        }
      ]
    },
    "index": 0,
    "side": "TOP",
    "part": {
      "name": "STM32F407VGT6",
      "attributes": {
        "VALUE": "STM32F407VGT6",
        "MANUFACTURER": "STMicroelectronics"
      }
    }
  },
  {
    "refDes": "R1",
    "partName": "10K",
    "package": {
      "name": "0402"
    },
    "index": 1,
    "side": "TOP",
    "part": {
      "name": "RES_10K"
    }
  },
  {
    "refDes": "C1",
    "partName": "100nF",
    "package": {
      "name": "0603"
    },
    "index": 2,
    "side": "BOTTOM"
  }
]
```

#### Nets Response (`GET /designs/{name}/nets`)

```json
[
  {
    "name": "GND",
    "index": 0,
    "pinConnections": [
      {
        "name": "U1-1-GND",
        "component": {
          "refDes": "U1"
        },
        "pin": {
          "name": "1",
          "index": 0
        }
      },
      {
        "name": "C1-2-GND",
        "component": {
          "refDes": "C1"
        },
        "pin": {
          "name": "2",
          "index": 1
        }
      }
    ]
  },
  {
    "name": "+3.3V",
    "index": 1,
    "pinConnections": [
      {
        "name": "U1-11-VDD",
        "component": {
          "refDes": "U1"
        },
        "pin": {
          "name": "11",
          "index": 10
        }
      }
    ]
  },
  {
    "name": "DDR_DQ0",
    "index": 42,
    "pinConnections": []
  }
]
```

### JSON Naming Convention

Protobuf's `MessageToJsonString()` uses **camelCase** field names derived from the protobuf field names:

| Protobuf Field | JSON Key |
|----------------|----------|
| `refDes` | `"refDes"` |
| `partName` | `"partName"` |
| `pinConnections` | `"pinConnections"` |
| `BoardSide.Top` | `"TOP"` |
| `BoardSide.Bottom` | `"BOTTOM"` |

---

## Implementation Plan

### Phase 1: Data Capture (Day 0.5)

1. Run PowerShell script to capture live JSON from server
2. Save responses to `api_responses/` directory
3. Validate schema against protobuf definitions
4. Document any discrepancies or edge cases

### Phase 2: DTO Creation (Day 0.5)

1. Create `src/OdbDesignInfoClient.Services/Api/Dtos/` directory
2. Implement DTO classes with `[JsonPropertyName]` attributes
3. Add XML documentation explaining protobuf mapping

### Phase 3: Deserialization Implementation (Day 1)

1. Implement `GetComponentsViaRestAsync` JSON parsing
2. Implement `GetNetsViaRestAsync` JSON parsing
3. Create mapping methods for DTO → Domain Model conversion
4. Add comprehensive logging

### Phase 4: Error Handling (Day 0.5)

1. Handle HTTP status codes (401, 404, 500)
2. Handle malformed JSON gracefully
3. Add retry logic (optional)

### Phase 5: Testing (Day 1)

1. Update unit tests to validate REST parsing
2. Add integration tests (if server available)
3. Manual testing with disabled gRPC

### Phase 6: Documentation (Day 0.5)

1. Update API Integration Guide
2. Add XML documentation to all new code
3. Update README configuration section

---

## DTO Class Definitions

### File Structure

```
src/OdbDesignInfoClient.Services/
└── Api/
    └── Dtos/
        ├── ComponentDto.cs
        ├── NetDto.cs
        ├── PackageDto.cs
        ├── PartDto.cs
        ├── PinDto.cs
        └── PinConnectionDto.cs
```

### ComponentDto.cs

```csharp
// src/OdbDesignInfoClient.Services/Api/Dtos/ComponentDto.cs
using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Component JSON responses from OdbDesignServer REST API.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Component
/// </summary>
/// <remarks>
/// JSON field names use camelCase as generated by protobuf's MessageToJsonString().
/// All fields are nullable to handle optional protobuf fields gracefully.
/// </remarks>
public sealed class ComponentDto
{
    /// <summary>
    /// Reference designator (e.g., "U1", "R102", "C1").
    /// Unique identifier for the component instance on the board.
    /// </summary>
    [JsonPropertyName("refDes")]
    public string? RefDes { get; init; }

    /// <summary>
    /// Part name/number (e.g., "STM32F407VGT6", "10K").
    /// Links to the Parts library.
    /// </summary>
    [JsonPropertyName("partName")]
    public string? PartName { get; init; }

    /// <summary>
    /// Package/footprint information.
    /// Contains physical dimensions and pin geometry.
    /// </summary>
    [JsonPropertyName("package")]
    public PackageDto? Package { get; init; }

    /// <summary>
    /// Zero-based index of the component in the design's component list.
    /// </summary>
    [JsonPropertyName("index")]
    public uint? Index { get; init; }

    /// <summary>
    /// Board side where component is placed.
    /// Values: "TOP", "BOTTOM", or "BS_NONE" (unspecified).
    /// </summary>
    [JsonPropertyName("side")]
    public string? Side { get; init; }

    /// <summary>
    /// Part library entry with attributes.
    /// Contains component value, manufacturer, etc.
    /// </summary>
    [JsonPropertyName("part")]
    public PartDto? Part { get; init; }
}
```

### NetDto.cs

```csharp
// src/OdbDesignInfoClient.Services/Api/Dtos/NetDto.cs
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Net JSON responses from OdbDesignServer REST API.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Net
/// </summary>
/// <remarks>
/// Represents an electrical signal connecting multiple component pins.
/// </remarks>
public sealed class NetDto
{
    /// <summary>
    /// Net name (e.g., "GND", "+3.3V", "DDR_DQ0").
    /// Unique identifier for the electrical signal.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Zero-based index of the net in the design's netlist.
    /// </summary>
    [JsonPropertyName("index")]
    public uint? Index { get; init; }

    /// <summary>
    /// List of pin connections belonging to this net.
    /// Each connection links a component pin to this net.
    /// </summary>
    [JsonPropertyName("pinConnections")]
    public List<PinConnectionDto>? PinConnections { get; init; }
}
```

### PinConnectionDto.cs

```csharp
// src/OdbDesignInfoClient.Services/Api/Dtos/PinConnectionDto.cs
using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for PinConnection JSON responses.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.PinConnection
/// </summary>
/// <remarks>
/// Represents a connection between a component pin and a net.
/// </remarks>
public sealed class PinConnectionDto
{
    /// <summary>
    /// Connection name/identifier (e.g., "U1-1-GND").
    /// Format: {RefDes}-{PinNumber}-{NetName}
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Reference to the parent component.
    /// May contain partial data (just refDes) or full component data.
    /// </summary>
    [JsonPropertyName("component")]
    public ComponentDto? Component { get; init; }

    /// <summary>
    /// Reference to the physical pin on the component.
    /// </summary>
    [JsonPropertyName("pin")]
    public PinDto? Pin { get; init; }
}
```

### PinDto.cs

```csharp
// src/OdbDesignInfoClient.Services/Api/Dtos/PinDto.cs
using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Pin JSON responses.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Pin
/// </summary>
public sealed class PinDto
{
    /// <summary>
    /// Pin name/number (e.g., "1", "A1", "GND").
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Zero-based index of the pin in the package.
    /// </summary>
    [JsonPropertyName("index")]
    public uint? Index { get; init; }

    /// <summary>
    /// X coordinate of pin center relative to component center.
    /// Units depend on design settings (typically mm or mils).
    /// </summary>
    [JsonPropertyName("x")]
    public float? X { get; init; }

    /// <summary>
    /// Y coordinate of pin center relative to component center.
    /// </summary>
    [JsonPropertyName("y")]
    public float? Y { get; init; }
}
```

### PackageDto.cs

```csharp
// src/OdbDesignInfoClient.Services/Api/Dtos/PackageDto.cs
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Package JSON responses.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Package
/// </summary>
/// <remarks>
/// Represents a component footprint/package definition from the library.
/// </remarks>
public sealed class PackageDto
{
    /// <summary>
    /// Package name (e.g., "LQFP-100", "0402", "BGA-256").
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Pin pitch in design units (typically mm).
    /// </summary>
    [JsonPropertyName("pitch")]
    public float? Pitch { get; init; }

    /// <summary>
    /// Bounding box minimum X coordinate.
    /// </summary>
    [JsonPropertyName("xMin")]
    public float? XMin { get; init; }

    /// <summary>
    /// Bounding box maximum X coordinate.
    /// </summary>
    [JsonPropertyName("xMax")]
    public float? XMax { get; init; }

    /// <summary>
    /// Bounding box minimum Y coordinate.
    /// </summary>
    [JsonPropertyName("yMin")]
    public float? YMin { get; init; }

    /// <summary>
    /// Bounding box maximum Y coordinate.
    /// </summary>
    [JsonPropertyName("yMax")]
    public float? YMax { get; init; }

    /// <summary>
    /// List of pins in this package.
    /// </summary>
    [JsonPropertyName("pins")]
    public List<PinDto>? Pins { get; init; }
}
```

### PartDto.cs

```csharp
// src/OdbDesignInfoClient.Services/Api/Dtos/PartDto.cs
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OdbDesignInfoClient.Services.Api.Dtos;

/// <summary>
/// Data Transfer Object for Part JSON responses.
/// Maps to protobuf message: Odb.Lib.Protobuf.ProductModel.Part
/// </summary>
/// <remarks>
/// Represents a part library entry with component attributes.
/// </remarks>
public sealed class PartDto
{
    /// <summary>
    /// Part name/number in the library.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Key-value attributes (e.g., VALUE, MANUFACTURER, TOLERANCE).
    /// </summary>
    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; init; }
}
```

---

## Deserialization Implementation

### JsonSerializerOptions Configuration

```csharp
// src/OdbDesignInfoClient.Services/DesignService.cs

/// <summary>
/// JSON serializer options configured for protobuf-generated JSON.
/// </summary>
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip
};
```

### GetComponentsViaRestAsync Implementation

```csharp
// src/OdbDesignInfoClient.Services/DesignService.cs
// Replace lines 134-141

/// <summary>
/// Fetches components via REST API with JSON deserialization.
/// </summary>
/// <param name="designId">The design identifier.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>List of components.</returns>
/// <exception cref="InvalidOperationException">Thrown when design not found (404).</exception>
/// <exception cref="UnauthorizedAccessException">Thrown when authentication fails (401).</exception>
private async Task<List<Component>> GetComponentsViaRestAsync(string designId, CancellationToken cancellationToken)
{
    _logger?.LogInformation("Fetching components via REST API for design: {DesignId}", designId);

    try
    {
        var response = await _restClient.GetComponentsAsync(designId, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            HandleHttpError(response.StatusCode, designId, "components");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            _logger?.LogWarning("REST API returned empty content for components of design: {DesignId}", designId);
            return [];
        }

        var componentDtos = JsonSerializer.Deserialize<List<ComponentDto>>(response.Content, JsonOptions);

        if (componentDtos is null)
        {
            _logger?.LogWarning("JSON deserialization returned null for components of design: {DesignId}", designId);
            return [];
        }

        _logger?.LogInformation(
            "Successfully deserialized {Count} components from REST API for design: {DesignId}",
            componentDtos.Count,
            designId);

        return componentDtos.Select(MapComponentDto).ToList();
    }
    catch (JsonException ex)
    {
        _logger?.LogError(
            ex,
            "Failed to deserialize components JSON for design: {DesignId}. JSON parsing error at position {Position}",
            designId,
            ex.BytePositionInLine);
        throw new InvalidOperationException($"Failed to parse components response: {ex.Message}", ex);
    }
}
```

### GetNetsViaRestAsync Implementation

```csharp
// src/OdbDesignInfoClient.Services/DesignService.cs
// Replace lines 212-218

/// <summary>
/// Fetches nets via REST API with JSON deserialization.
/// </summary>
/// <param name="designId">The design identifier.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>List of nets.</returns>
/// <exception cref="InvalidOperationException">Thrown when design not found (404).</exception>
/// <exception cref="UnauthorizedAccessException">Thrown when authentication fails (401).</exception>
private async Task<List<Net>> GetNetsViaRestAsync(string designId, CancellationToken cancellationToken)
{
    _logger?.LogInformation("Fetching nets via REST API for design: {DesignId}", designId);

    try
    {
        var response = await _restClient.GetNetsAsync(designId, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            HandleHttpError(response.StatusCode, designId, "nets");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            _logger?.LogWarning("REST API returned empty content for nets of design: {DesignId}", designId);
            return [];
        }

        var netDtos = JsonSerializer.Deserialize<List<NetDto>>(response.Content, JsonOptions);

        if (netDtos is null)
        {
            _logger?.LogWarning("JSON deserialization returned null for nets of design: {DesignId}", designId);
            return [];
        }

        _logger?.LogInformation(
            "Successfully deserialized {Count} nets from REST API for design: {DesignId}",
            netDtos.Count,
            designId);

        return netDtos.Select(MapNetDto).ToList();
    }
    catch (JsonException ex)
    {
        _logger?.LogError(
            ex,
            "Failed to deserialize nets JSON for design: {DesignId}. JSON parsing error at position {Position}",
            designId,
            ex.BytePositionInLine);
        throw new InvalidOperationException($"Failed to parse nets response: {ex.Message}", ex);
    }
}
```

### DTO to Domain Model Mapping Methods

```csharp
// src/OdbDesignInfoClient.Services/DesignService.cs
// Add after existing MapProtobufComponent method (around line 305)

/// <summary>
/// Maps a ComponentDto from REST API JSON to the domain Component model.
/// </summary>
/// <param name="dto">The component DTO from JSON deserialization.</param>
/// <returns>A Component domain model instance.</returns>
private static Component MapComponentDto(ComponentDto dto)
{
    // Map pins from package if available
    var pins = dto.Package?.Pins?
        .Select(pinDto => new Pin
        {
            Name = pinDto.Name ?? string.Empty,
            Number = pinDto.Index?.ToString() ?? string.Empty,
            NetName = string.Empty, // Not available in component response
            ElectricalType = string.Empty
        })
        .ToList() ?? [];

    return new Component
    {
        RefDes = dto.RefDes ?? string.Empty,
        PartName = dto.PartName ?? string.Empty,
        Package = dto.Package?.Name ?? string.Empty,
        Side = MapBoardSide(dto.Side),
        // Position and rotation not yet available in protobuf schema
        // See TODO in MapProtobufComponent
        Rotation = 0.0,
        X = 0.0,
        Y = 0.0,
        Pins = pins
    };
}

/// <summary>
/// Maps a NetDto from REST API JSON to the domain Net model.
/// </summary>
/// <param name="dto">The net DTO from JSON deserialization.</param>
/// <returns>A Net domain model instance.</returns>
private static Net MapNetDto(NetDto dto)
{
    var features = dto.PinConnections?
        .Select(pc => new NetFeature
        {
            FeatureType = "Pin",
            Id = pc.Name ?? string.Empty,
            ComponentRef = pc.Component?.RefDes ?? string.Empty
        })
        .ToList() ?? [];

    return new Net
    {
        Name = dto.Name ?? string.Empty,
        PinCount = dto.PinConnections?.Count ?? 0,
        ViaCount = 0, // Not available in current protobuf schema
        Features = features
    };
}

/// <summary>
/// Maps protobuf BoardSide enum string to display string.
/// </summary>
/// <param name="side">The side string from JSON ("TOP", "BOTTOM", "BS_NONE", or null).</param>
/// <returns>Display string ("Top", "Bottom", or empty).</returns>
private static string MapBoardSide(string? side)
{
    return side?.ToUpperInvariant() switch
    {
        "TOP" => "Top",
        "BOTTOM" => "Bottom",
        "BS_NONE" => string.Empty,
        _ => string.Empty
    };
}
```

### HTTP Error Handler

```csharp
// src/OdbDesignInfoClient.Services/DesignService.cs
// Add as a private helper method

/// <summary>
/// Handles HTTP error responses from the REST API.
/// </summary>
/// <param name="statusCode">The HTTP status code.</param>
/// <param name="designId">The design identifier.</param>
/// <param name="resourceType">The type of resource being fetched (e.g., "components", "nets").</param>
/// <exception cref="InvalidOperationException">Thrown for 404 (Not Found) or other errors.</exception>
/// <exception cref="UnauthorizedAccessException">Thrown for 401 (Unauthorized).</exception>
private void HandleHttpError(HttpStatusCode statusCode, string designId, string resourceType)
{
    switch (statusCode)
    {
        case HttpStatusCode.NotFound:
            _logger?.LogWarning(
                "Design '{DesignId}' not found when fetching {ResourceType} via REST API",
                designId,
                resourceType);
            throw new InvalidOperationException($"Design '{designId}' not found.");

        case HttpStatusCode.Unauthorized:
            _logger?.LogWarning(
                "Authentication failed when fetching {ResourceType} for design '{DesignId}'",
                resourceType,
                designId);
            throw new UnauthorizedAccessException(
                "Authentication failed. Please check your credentials.");

        case HttpStatusCode.InternalServerError:
            _logger?.LogError(
                "Server error when fetching {ResourceType} for design '{DesignId}'",
                resourceType,
                designId);
            throw new InvalidOperationException(
                $"Server error occurred while fetching {resourceType}. Please try again later.");

        default:
            _logger?.LogError(
                "Unexpected HTTP status {StatusCode} when fetching {ResourceType} for design '{DesignId}'",
                statusCode,
                resourceType,
                designId);
            throw new InvalidOperationException(
                $"Unexpected error (HTTP {(int)statusCode}) while fetching {resourceType}.");
    }
}
```

### Required Using Statements

Add these using statements at the top of `DesignService.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using OdbDesignInfoClient.Services.Api.Dtos;
```

---

## Error Handling Strategy

### Error Categories

| Error Type | HTTP Status | C# Exception | User Message |
|------------|-------------|--------------|--------------|
| Design not found | 404 | `InvalidOperationException` | "Design 'X' not found." |
| Auth failure | 401 | `UnauthorizedAccessException` | "Authentication failed. Please check your credentials." |
| Server error | 500 | `InvalidOperationException` | "Server error occurred. Please try again later." |
| JSON parse error | N/A | `InvalidOperationException` | "Failed to parse response: {details}" |
| Network error | N/A | `HttpRequestException` | Bubble up with context |

### Logging Strategy

```csharp
// Success path
_logger?.LogInformation("Fetching components via REST API for design: {DesignId}", designId);
_logger?.LogInformation("Successfully deserialized {Count} components from REST API", count);

// Warning path (recoverable)
_logger?.LogWarning("REST API returned empty content for design: {DesignId}", designId);

// Error path (unrecoverable)
_logger?.LogError(ex, "Failed to deserialize JSON for design: {DesignId}", designId);
```

### Retry Policy (Optional Enhancement)

```csharp
// Using Polly for resilience (add to DI configuration)
services.AddRefitClient<IOdbDesignRestApi>()
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(restUrl))
    .AddHttpMessageHandler<AuthHeaderHandler>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        onRetry: (outcome, timespan, attempt, context) =>
        {
            _logger?.LogWarning(
                "REST API retry attempt {Attempt} after {Delay}ms due to {Reason}",
                attempt,
                timespan.TotalMilliseconds,
                outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
        }));
```

---

## Unit Test Examples

### Test File Structure

```
tests/OdbDesignInfoClient.Tests/
└── Services/
    ├── DesignServiceTests.cs (existing, update)
    └── TestData/
        ├── components_sample.json
        └── nets_sample.json
```

### Sample Test Data: components_sample.json

```json
[
  {
    "refDes": "U1",
    "partName": "STM32F407VGT6",
    "package": {
      "name": "LQFP-100",
      "pins": [
        { "name": "1", "index": 0 },
        { "name": "2", "index": 1 }
      ]
    },
    "index": 0,
    "side": "TOP",
    "part": {
      "name": "STM32F407VGT6"
    }
  },
  {
    "refDes": "R1",
    "partName": "10K",
    "package": {
      "name": "0402"
    },
    "index": 1,
    "side": "BOTTOM"
  }
]
```

### Sample Test Data: nets_sample.json

```json
[
  {
    "name": "GND",
    "index": 0,
    "pinConnections": [
      {
        "name": "U1-1-GND",
        "component": { "refDes": "U1" },
        "pin": { "name": "1", "index": 0 }
      },
      {
        "name": "R1-2-GND",
        "component": { "refDes": "R1" },
        "pin": { "name": "2", "index": 1 }
      }
    ]
  },
  {
    "name": "+3.3V",
    "index": 1,
    "pinConnections": []
  }
]
```

### Updated DesignServiceTests.cs

```csharp
// tests/OdbDesignInfoClient.Tests/Services/DesignServiceTests.cs

using System.Net;
using System.Text.Json;
using Moq;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Services;
using OdbDesignInfoClient.Services.Api;
using Refit;
using Xunit;

namespace OdbDesignInfoClient.Tests.Services;

public class DesignServiceTests
{
    private readonly Mock<IOdbDesignRestApi> _mockRestClient;
    private readonly Mock<IConnectionService> _mockConnectionService;
    private readonly DesignService _sut;

    // Sample JSON test data
    private const string SampleComponentsJson = """
        [
          {
            "refDes": "U1",
            "partName": "STM32F407VGT6",
            "package": { "name": "LQFP-100" },
            "index": 0,
            "side": "TOP"
          },
          {
            "refDes": "R1",
            "partName": "10K",
            "package": { "name": "0402" },
            "index": 1,
            "side": "BOTTOM"
          }
        ]
        """;

    private const string SampleNetsJson = """
        [
          {
            "name": "GND",
            "index": 0,
            "pinConnections": [
              {
                "name": "U1-1-GND",
                "component": { "refDes": "U1" },
                "pin": { "name": "1", "index": 0 }
              }
            ]
          },
          {
            "name": "+3.3V",
            "index": 1,
            "pinConnections": []
          }
        ]
        """;

    public DesignServiceTests()
    {
        _mockRestClient = new Mock<IOdbDesignRestApi>();
        _mockConnectionService = new Mock<IConnectionService>();
        
        // Configure to use REST (gRPC unavailable)
        _mockConnectionService.Setup(x => x.IsGrpcAvailable).Returns(false);
        
        _sut = new DesignService(
            restClient: _mockRestClient.Object,
            grpcClient: null!, // gRPC unavailable
            connectionService: _mockConnectionService.Object,
            logger: null);
    }

    #region GetComponentsAsync Tests

    [Fact]
    public async Task GetComponentsAsync_DeserializesRestJson_WhenGrpcUnavailable()
    {
        // Arrange
        var response = CreateSuccessResponse(SampleComponentsJson);
        _mockRestClient
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "step-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        var u1 = result.First(c => c.RefDes == "U1");
        Assert.Equal("STM32F407VGT6", u1.PartName);
        Assert.Equal("LQFP-100", u1.Package);
        Assert.Equal("Top", u1.Side);
        
        var r1 = result.First(c => c.RefDes == "R1");
        Assert.Equal("10K", r1.PartName);
        Assert.Equal("0402", r1.Package);
        Assert.Equal("Bottom", r1.Side);
    }

    [Fact]
    public async Task GetComponentsAsync_ReturnsEmptyList_WhenResponseIsEmpty()
    {
        // Arrange
        var response = CreateSuccessResponse("[]");
        _mockRestClient
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "step-1");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetComponentsAsync_ThrowsInvalidOperationException_OnMalformedJson()
    {
        // Arrange
        var response = CreateSuccessResponse("{ invalid json }");
        _mockRestClient
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetComponentsAsync("design-1", "step-1"));
    }

    [Fact]
    public async Task GetComponentsAsync_ThrowsInvalidOperationException_WhenDesignNotFound()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.NotFound);
        _mockRestClient
            .Setup(x => x.GetComponentsAsync("unknown-design", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _sut.GetComponentsAsync("unknown-design", "step-1"));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task GetComponentsAsync_ThrowsUnauthorizedAccessException_OnAuthFailure()
    {
        // Arrange
        var response = CreateErrorResponse(HttpStatusCode.Unauthorized);
        _mockRestClient
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await _sut.GetComponentsAsync("design-1", "step-1"));
    }

    [Fact]
    public async Task GetComponentsAsync_HandlesMissingOptionalFields_Gracefully()
    {
        // Arrange - Component with minimal fields
        var minimalJson = """
            [
              {
                "refDes": "U1"
              }
            ]
            """;
        var response = CreateSuccessResponse(minimalJson);
        _mockRestClient
            .Setup(x => x.GetComponentsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetComponentsAsync("design-1", "step-1");

        // Assert
        Assert.Single(result);
        var component = result[0];
        Assert.Equal("U1", component.RefDes);
        Assert.Equal(string.Empty, component.PartName);
        Assert.Equal(string.Empty, component.Package);
        Assert.Equal(string.Empty, component.Side);
    }

    #endregion

    #region GetNetsAsync Tests

    [Fact]
    public async Task GetNetsAsync_DeserializesRestJson_WhenGrpcUnavailable()
    {
        // Arrange
        var response = CreateSuccessResponse(SampleNetsJson);
        _mockRestClient
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetNetsAsync("design-1", "step-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        
        var gnd = result.First(n => n.Name == "GND");
        Assert.Equal(1, gnd.PinCount);
        Assert.Single(gnd.Features);
        Assert.Equal("U1-1-GND", gnd.Features[0].Id);
        Assert.Equal("U1", gnd.Features[0].ComponentRef);
        
        var vcc = result.First(n => n.Name == "+3.3V");
        Assert.Equal(0, vcc.PinCount);
        Assert.Empty(vcc.Features);
    }

    [Fact]
    public async Task GetNetsAsync_ReturnsEmptyList_WhenResponseIsEmpty()
    {
        // Arrange
        var response = CreateSuccessResponse("[]");
        _mockRestClient
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetNetsAsync("design-1", "step-1");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetNetsAsync_HandlesManyPinConnections_Correctly()
    {
        // Arrange - Net with many connections
        var manyConnectionsJson = """
            [
              {
                "name": "GND",
                "index": 0,
                "pinConnections": [
                  { "name": "U1-1", "component": { "refDes": "U1" } },
                  { "name": "U2-1", "component": { "refDes": "U2" } },
                  { "name": "C1-2", "component": { "refDes": "C1" } },
                  { "name": "C2-2", "component": { "refDes": "C2" } },
                  { "name": "R1-1", "component": { "refDes": "R1" } }
                ]
              }
            ]
            """;
        var response = CreateSuccessResponse(manyConnectionsJson);
        _mockRestClient
            .Setup(x => x.GetNetsAsync("design-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _sut.GetNetsAsync("design-1", "step-1");

        // Assert
        Assert.Single(result);
        Assert.Equal(5, result[0].PinCount);
        Assert.Equal(5, result[0].Features.Count);
    }

    #endregion

    #region Helper Methods

    private static ApiResponse<string> CreateSuccessResponse(string content)
    {
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        };
        return new ApiResponse<string>(
            httpResponse,
            content,
            new RefitSettings());
    }

    private static ApiResponse<string> CreateErrorResponse(HttpStatusCode statusCode)
    {
        var httpResponse = new HttpResponseMessage(statusCode);
        return new ApiResponse<string>(
            httpResponse,
            null,
            new RefitSettings(),
            new ApiException(
                new HttpRequestMessage(),
                HttpMethod.Get,
                httpResponse,
                new RefitSettings()));
    }

    #endregion
}
```

---

## Verification Checklist

### Code Quality

- [ ] All DTO classes have complete XML documentation
- [ ] All new methods have XML documentation
- [ ] Code follows StyleCop rules
- [ ] No compiler warnings
- [ ] No nullable reference type warnings

### Unit Tests

- [ ] `GetComponentsAsync_DeserializesRestJson_WhenGrpcUnavailable` - PASS
- [ ] `GetComponentsAsync_ReturnsEmptyList_WhenResponseIsEmpty` - PASS
- [ ] `GetComponentsAsync_ThrowsInvalidOperationException_OnMalformedJson` - PASS
- [ ] `GetComponentsAsync_ThrowsInvalidOperationException_WhenDesignNotFound` - PASS
- [ ] `GetComponentsAsync_ThrowsUnauthorizedAccessException_OnAuthFailure` - PASS
- [ ] `GetComponentsAsync_HandlesMissingOptionalFields_Gracefully` - PASS
- [ ] `GetNetsAsync_DeserializesRestJson_WhenGrpcUnavailable` - PASS
- [ ] `GetNetsAsync_ReturnsEmptyList_WhenResponseIsEmpty` - PASS
- [ ] `GetNetsAsync_HandlesManyPinConnections_Correctly` - PASS
- [ ] Code coverage >80% for modified files

### Manual Testing

- [ ] Server running at http://PRECISION5820:8888
- [ ] Can fetch design list via REST
- [ ] Can fetch components via REST when gRPC disabled
- [ ] Can fetch nets via REST when gRPC disabled
- [ ] Data displays correctly in UI grids
- [ ] Error messages show correctly for 404/401/500 scenarios
- [ ] Logs show appropriate messages for REST fallback

### Integration Testing

- [ ] gRPC → REST automatic fallback works
- [ ] Data consistency: REST results match gRPC results (field values)
- [ ] Performance acceptable (<2s for 1000+ components)

---

## Appendix

### A. PowerShell Script to Capture Live JSON

Save as `scripts/fetch-api-responses.ps1`:

```powershell
# OdbDesignServer API Response Capture Script
# Fetches actual JSON from running server for schema validation

param(
    [string]$BaseUrl = "http://PRECISION5820:8888",
    [string]$OutputDir = ".\api_responses",
    [string]$Username = "",
    [string]$Password = ""
)

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
    Write-Host "Created output directory: $OutputDir" -ForegroundColor Green
}

# Build headers
$headers = @{
    "Content-Type" = "application/json"
}

if ($Username -and $Password) {
    $base64Auth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${Username}:${Password}"))
    $headers["Authorization"] = "Basic $base64Auth"
    Write-Host "Using Basic Authentication" -ForegroundColor Cyan
}

Write-Host "`nConnecting to OdbDesignServer at $BaseUrl..." -ForegroundColor Cyan

try {
    # 1. Get designs list
    Write-Host "`n[1/4] Fetching designs list..." -ForegroundColor Yellow
    $designsUrl = "$BaseUrl/designs"
    $designs = Invoke-RestMethod -Uri $designsUrl -Headers $headers -Method GET
    $designs | ConvertTo-Json -Depth 10 | Out-File "$OutputDir/designs.json" -Encoding UTF8
    Write-Host "✓ Saved designs list to designs.json" -ForegroundColor Green
    Write-Host "  Found $($designs.Count) designs" -ForegroundColor Gray

    if ($designs.Count -eq 0) {
        Write-Host "⚠ No designs found on server" -ForegroundColor Yellow
        exit
    }

    # Use first design
    $designName = $designs[0]
    Write-Host "`nUsing design: $designName" -ForegroundColor Magenta

    # 2. Get components
    Write-Host "`n[2/4] Fetching components..." -ForegroundColor Yellow
    $componentsUrl = "$BaseUrl/designs/$designName/components"
    $components = Invoke-RestMethod -Uri $componentsUrl -Headers $headers -Method GET
    $components | ConvertTo-Json -Depth 10 | Out-File "$OutputDir/components.json" -Encoding UTF8
    Write-Host "✓ Saved components to components.json" -ForegroundColor Green
    Write-Host "  Found $($components.Count) components" -ForegroundColor Gray

    # 3. Get nets
    Write-Host "`n[3/4] Fetching nets..." -ForegroundColor Yellow
    $netsUrl = "$BaseUrl/designs/$designName/nets"
    $nets = Invoke-RestMethod -Uri $netsUrl -Headers $headers -Method GET
    $nets | ConvertTo-Json -Depth 10 | Out-File "$OutputDir/nets.json" -Encoding UTF8
    Write-Host "✓ Saved nets to nets.json" -ForegroundColor Green
    Write-Host "  Found $($nets.Count) nets" -ForegroundColor Gray

    # 4. Get parts (bonus)
    Write-Host "`n[4/4] Fetching parts..." -ForegroundColor Yellow
    try {
        $partsUrl = "$BaseUrl/designs/$designName/parts"
        $parts = Invoke-RestMethod -Uri $partsUrl -Headers $headers -Method GET
        $parts | ConvertTo-Json -Depth 10 | Out-File "$OutputDir/parts.json" -Encoding UTF8
        Write-Host "✓ Saved parts to parts.json" -ForegroundColor Green
        Write-Host "  Found $($parts.Count) parts" -ForegroundColor Gray
    } catch {
        Write-Host "⚠ Parts endpoint not available" -ForegroundColor Yellow
    }

    # Summary
    Write-Host "`n" + ("=" * 60) -ForegroundColor Cyan
    Write-Host "Summary" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Get-ChildItem "$OutputDir/*.json" | ForEach-Object {
        $size = [math]::Round($_.Length / 1KB, 2)
        Write-Host "  $($_.Name): $size KB" -ForegroundColor White
    }
    Write-Host "`nAll files saved to: $OutputDir" -ForegroundColor Green

} catch {
    Write-Host "`n❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode
        Write-Host "HTTP Status: $statusCode" -ForegroundColor Red
        
        if ($statusCode -eq 401) {
            Write-Host "`nAuthentication required. Use -Username and -Password parameters." -ForegroundColor Yellow
        }
    }
}
```

**Usage:**

```powershell
# Without authentication
.\scripts\fetch-api-responses.ps1 -BaseUrl "http://PRECISION5820:8888"

# With authentication
.\scripts\fetch-api-responses.ps1 -BaseUrl "http://PRECISION5820:8888" -Username "admin" -Password "secret"
```

### B. Protobuf to C# Type Mapping

| Protobuf Type | JSON Type | C# Type |
|---------------|-----------|---------|
| `string` | `"text"` | `string?` |
| `uint32` | `123` | `uint?` |
| `int32` | `-123` | `int?` |
| `float` | `1.23` | `float?` |
| `double` | `1.23` | `double?` |
| `bool` | `true`/`false` | `bool?` |
| `enum` | `"ENUM_VALUE"` | `string?` (parse manually) |
| `message` | `{ ... }` | `ClassDto?` |
| `repeated T` | `[ ... ]` | `List<T>?` |
| `map<K,V>` | `{ "key": "value" }` | `Dictionary<K,V>?` |
| `optional T` | `null` or value | `T?` |

### C. Related Files

| File | Purpose |
|------|---------|
| [DesignService.cs](../src/OdbDesignInfoClient.Services/DesignService.cs) | Main service to modify |
| [IOdbDesignRestApi.cs](../src/OdbDesignInfoClient.Services/Api/IOdbDesignRestApi.cs) | Refit interface |
| [DesignModels.cs](../src/OdbDesignInfoClient.Core/Models/DesignModels.cs) | Domain models |
| [DesignServiceTests.cs](../tests/OdbDesignInfoClient.Tests/Services/DesignServiceTests.cs) | Unit tests |
| [component.proto](../protoc/component.proto) | Protobuf schema |
| [net.proto](../protoc/net.proto) | Protobuf schema |

### D. Server Source References

| File | URL |
|------|-----|
| DesignsController.h | https://github.com/nam20485/OdbDesign/blob/development/OdbDesignServer/Controllers/DesignsController.h |
| DesignsController.cpp | https://github.com/nam20485/OdbDesign/blob/development/OdbDesignServer/Controllers/DesignsController.cpp |
| Component.cpp | https://github.com/nam20485/OdbDesign/blob/development/OdbDesignLib/ProductModel/Component.cpp |
| Net.cpp | https://github.com/nam20485/OdbDesign/blob/development/OdbDesignLib/ProductModel/Net.cpp |

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-02-05 | AI Assistant | Initial comprehensive plan |

---

**Status:** Ready for implementation  
**Next Steps:** Run PowerShell script to capture live JSON, then begin Phase 2 (DTO Creation)
