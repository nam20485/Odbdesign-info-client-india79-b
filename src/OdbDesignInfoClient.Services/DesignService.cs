using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Odb.Grpc;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services.Api;
using OdbDesignInfoClient.Services.Api.Dtos;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Implementation of the design data service with hybrid REST/gRPC transport.
/// </summary>
public class DesignService : IDesignService
{
    private readonly ILogger<DesignService>? _logger;
    private readonly IConnectionService _connectionService;
    private readonly IOdbDesignRestApi _restApi;

    /// <summary>
    /// JSON serializer options configured for protobuf-generated JSON (camelCase).
    /// </summary>
    /// <remarks>
    /// PropertyNamingPolicy is set to CamelCase to match the protobuf MessageToJsonString() output format.
    /// This ensures consistent deserialization of REST API responses regardless of server configuration.
    /// Combined with PropertyNameCaseInsensitive=true for defensive parsing of case variations.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly ConcurrentDictionary<string, Design> _designCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<Component>> _componentCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<Net>> _netCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<Layer>> _stackupCache = new();
    
    private DateTime _designCacheRefresh = DateTime.MinValue;
    private DateTime _componentCacheRefresh = DateTime.MinValue;
    private DateTime _netCacheRefresh = DateTime.MinValue;
    private DateTime _stackupCacheRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the DesignService.
    /// </summary>
    public DesignService(
        IConnectionService connectionService,
        IOdbDesignRestApi restApi,
        ILogger<DesignService>? logger = null)
    {
        _connectionService = connectionService;
        _restApi = restApi;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Design>> GetDesignsAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting designs from server");

        try
        {
            var response = await _restApi.GetDesignNamesAsync(cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogError(
                    "Failed to get design names. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode,
                    response.Content);
                return [];
            }

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                _logger?.LogWarning("REST API returned empty content for design names");
                return [];
            }

            _logger?.LogDebug("Raw response from /filemodels: {Content}", response.Content);

            List<(string Name, bool Loaded)> designInfos;
            
            try
            {
                // Parse the response which has structure: { "filearchives": [...] }
                using var doc = JsonDocument.Parse(response.Content);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("filearchives", out var filearchivesArray))
                {
                    designInfos = filearchivesArray.EnumerateArray()
                        .Select(item =>
                        {
                            var name = string.Empty;
                            var loaded = false;
                            
                            if (item.TryGetProperty("name", out var nameElement))
                            {
                                name = nameElement.GetString() ?? string.Empty;
                            }
                            
                            if (item.TryGetProperty("loaded", out var loadedElement))
                            {
                                loaded = loadedElement.GetBoolean();
                            }
                            
                            return (Name: name, Loaded: loaded);
                        })
                        .Where(item => !string.IsNullOrEmpty(item.Name))
                        .ToList();
                }
                else
                {
                    _logger?.LogError("Unable to find 'filearchives' property in response");
                    return [];
                }
            }
            catch (JsonException ex)
            {
                _logger?.LogError(ex, "Failed to parse design names from response");
                return [];
            }

            if (designInfos.Count == 0)
            {
                _logger?.LogInformation("No designs found on server");
                return [];
            }

            var designs = new List<Design>();
            var failedDesigns = new List<string>();
            var skippedDesigns = new List<string>();

            foreach (var (name, loaded) in designInfos)
            {
                if (_designCache.TryGetValue(name, out var cached) && !IsDesignCacheExpired())
                {
                    designs.Add(cached);
                    continue;
                }

                // Skip designs that aren't loaded on the server
                if (!loaded)
                {
                    _logger?.LogDebug("Skipping design '{DesignName}' - not loaded on server", name);
                    skippedDesigns.Add(name);
                    continue;
                }

                try
                {
                    var stepsResponse = await _restApi.GetStepsAsync(name, cancellationToken);
                    
                    if (!stepsResponse.IsSuccessStatusCode)
                    {
                        _logger?.LogWarning(
                            "Failed to get steps for design '{DesignName}'. Status: {StatusCode}",
                            name,
                            stepsResponse.StatusCode);
                        failedDesigns.Add(name);
                        continue;
                    }

                    var steps = ParseStepsFromResponse(stepsResponse.Content, name);
                    
                    var design = new Design
                    {
                        Id = name,
                        Name = name,
                        Path = $"/filemodels/{name}",
                        LoadedDate = DateTime.Now,
                        Steps = steps
                    };

                    _designCache[name] = design;
                    designs.Add(design);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load design '{DesignName}', skipping", name);
                    failedDesigns.Add(name);
                    // Continue to next design instead of throwing
                }
            }

            _designCacheRefresh = DateTime.Now;
            
            if (failedDesigns.Count > 0 || skippedDesigns.Count > 0)
            {
                _logger?.LogInformation(
                    "Successfully loaded {SuccessCount}/{TotalCount} designs. Skipped {SkipCount} not loaded on server. {FailCount} failed: {FailedDesigns}",
                    designs.Count,
                    designInfos.Count,
                    skippedDesigns.Count,
                    failedDesigns.Count,
                    failedDesigns.Count > 0 ? string.Join(", ", failedDesigns) : "none");
            }
            else
            {
                _logger?.LogInformation("Successfully loaded {Count}/{Total} designs", designs.Count, designInfos.Count);
            }
            
            return designs;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get designs from server");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Design?> GetDesignAsync(string designId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting design {DesignId} from server", designId);

        if (_designCache.TryGetValue(designId, out var cached) && !IsDesignCacheExpired())
        {
            return cached;
        }

        try
        {
            var stepsResponse = await _restApi.GetStepsAsync(designId, cancellationToken);
            
            if (!stepsResponse.IsSuccessStatusCode)
            {
                _logger?.LogError(
                    "Failed to get steps for design '{DesignId}'. Status: {StatusCode}",
                    designId,
                    stepsResponse.StatusCode);
                return null;
            }

            var steps = ParseStepsFromResponse(stepsResponse.Content, designId);
            
            var design = new Design
            {
                Id = designId,
                Name = designId,
                Path = $"/filemodels/{designId}",
                LoadedDate = DateTime.Now,
                Steps = steps
            };

            _designCache[designId] = design;
            return design;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get design {DesignId}", designId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Component>> GetComponentsAsync(
        string designId, 
        string stepName, 
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting components for design {DesignId}, step {StepName}", designId, stepName);

        var cacheKey = $"{designId}:{stepName}:components";
        if (_componentCache.TryGetValue(cacheKey, out var cached) && !IsComponentCacheExpired())
        {
            return cached;
        }

        try
        {
            var components = await (_connectionService.IsGrpcAvailable
                ? GetComponentsViaGrpcAsync(designId, cancellationToken)
                : GetComponentsViaRestAsync(designId, cancellationToken));

            _componentCache[cacheKey] = components;
            return components;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get components for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    private async Task<List<Component>> GetComponentsViaGrpcAsync(string designId, CancellationToken cancellationToken)
    {
        var components = new List<Component>();
        
        if (!_connectionService.IsGrpcAvailable)
        {
            return await GetComponentsViaRestAsync(designId, cancellationToken);
        }
        
        if (_connectionService is not ConnectionService connectionServiceImpl)
        {
            throw new InvalidOperationException("ConnectionService implementation is required for gRPC access");
        }
        
        var grpcClient = connectionServiceImpl.GrpcClient;
        if (grpcClient == null)
        {
            return await GetComponentsViaRestAsync(designId, cancellationToken);
        }

        try
        {
            var request = new GetDesignRequest { DesignName = designId };
            var design = await grpcClient.GetDesignAsync(request, cancellationToken: cancellationToken);

            foreach (var comp in design.Components)
            {
                var component = MapProtobufComponent(comp);
                components.Add(component);
            }

            _logger?.LogInformation("Loaded {Count} components via gRPC", components.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "gRPC component fetch failed, falling back to REST");
            return await GetComponentsViaRestAsync(designId, cancellationToken);
        }

        return components;
    }

    /// <summary>
    /// Fetches components via REST API with JSON deserialization.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of components.</returns>
    /// <exception cref="InvalidOperationException">Thrown when design not found (404) or JSON parsing fails.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails (401).</exception>
    private async Task<List<Component>> GetComponentsViaRestAsync(string designId, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Fetching components via REST API for design: {DesignId}", designId);

        try
        {
            var response = await _restApi.GetComponentsAsync(designId, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger?.LogWarning(
                    "Design '{DesignId}' not found when fetching components via REST API — returning empty list",
                    designId);
                return [];
            }

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

    /// <inheritdoc />
    public async Task<IReadOnlyList<Net>> GetNetsAsync(
        string designId, 
        string stepName, 
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting nets for design {DesignId}, step {StepName}", designId, stepName);

        var cacheKey = $"{designId}:{stepName}:nets";
        if (_netCache.TryGetValue(cacheKey, out var cached) && !IsNetCacheExpired())
        {
            return cached;
        }

        try
        {
            List<Net> nets;

            nets = await (_connectionService.IsGrpcAvailable
                ? GetNetsViaGrpcAsync(designId, cancellationToken)
                : GetNetsViaRestAsync(designId, cancellationToken));

            _netCache[cacheKey] = nets;
            return nets;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get nets for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    private async Task<List<Net>> GetNetsViaGrpcAsync(string designId, CancellationToken cancellationToken)
    {
        var nets = new List<Net>();
        
        if (!_connectionService.IsGrpcAvailable)
        {
            return await GetNetsViaRestAsync(designId, cancellationToken);
        }
        
        if (_connectionService is not ConnectionService connectionServiceImpl)
        {
            throw new InvalidOperationException("ConnectionService implementation is required for gRPC access");
        }
        
        var grpcClient = connectionServiceImpl.GrpcClient;
        if (grpcClient == null)
        {
            return await GetNetsViaRestAsync(designId, cancellationToken);
        }

        try
        {
            var request = new GetDesignRequest { DesignName = designId };
            var design = await grpcClient.GetDesignAsync(request, cancellationToken: cancellationToken);

            foreach (var net in design.Nets)
            {
                var mappedNet = MapProtobufNet(net);
                nets.Add(mappedNet);
            }

            _logger?.LogInformation("Loaded {Count} nets via gRPC", nets.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "gRPC net fetch failed, falling back to REST");
            return await GetNetsViaRestAsync(designId, cancellationToken);
        }

        return nets;
    }

    /// <summary>
    /// Fetches nets via REST API with JSON deserialization.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of nets.</returns>
    /// <exception cref="InvalidOperationException">Thrown when design not found (404) or JSON parsing fails.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when authentication fails (401).</exception>
    private async Task<List<Net>> GetNetsViaRestAsync(string designId, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Fetching nets via REST API for design: {DesignId}", designId);

        try
        {
            var response = await _restApi.GetNetsAsync(designId, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger?.LogWarning(
                    "Design '{DesignId}' not found when fetching nets via REST API — returning empty list",
                    designId);
                return [];
            }

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

    /// <inheritdoc />
    public async Task<IReadOnlyList<Layer>> GetStackupAsync(
        string designId, 
        string stepName, 
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting stackup for design {DesignId}, step {StepName}", designId, stepName);

        var cacheKey = $"{designId}:{stepName}:stackup";
        if (_stackupCache.TryGetValue(cacheKey, out var cached) && !IsStackupCacheExpired())
        {
            return cached;
        }

        try
        {
            var response = await _restApi.GetLayerNamesAsync(designId, stepName, cancellationToken);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger?.LogWarning("Failed to get layer names for design {DesignId}, step {StepName}. Status: {StatusCode}", 
                    designId, stepName, response.StatusCode);
                return new List<Layer>();
            }

            var layerNames = JsonSerializer.Deserialize<List<string>>(response.Content, JsonOptions) ?? new List<string>();
            var layers = layerNames.Select((name, index) => new Layer
            {
                Id = index,
                Name = name,
                Type = DetermineLayerType(name),
                Polarity = "Positive"
            }).ToList();

            _stackupCache[cacheKey] = layers;
            return layers;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get stackup for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    private static Component MapProtobufComponent(Odb.Lib.Protobuf.ProductModel.Component proto)
    {
        // TODO: Get position and rotation from protobuf when available in schema
        // For now, using defaults as these properties don't exist in current protobuf definition
        // TODO: Map position and rotation from protobuf when available
        var rotation = 0.0;
        var x = 0.0;
        var y = 0.0;

        return new Component
        {
            RefDes = proto.RefDes ?? string.Empty,
            PartName = proto.PartName ?? string.Empty,
            Package = proto.Package?.Name ?? string.Empty,
            Side = proto.Side == Odb.Lib.Protobuf.BoardSide.Top ? "Top" : "Bottom",
            Rotation = rotation,
            X = x,
            Y = y,
            Pins = []  // TODO: Map pins from protobuf when pin data is available
        };
    }

    private static Net MapProtobufNet(Odb.Lib.Protobuf.ProductModel.Net proto)
    {
        var features = new List<NetFeature>();

        foreach (var pinConnection in proto.PinConnections)
        {
            features.Add(new NetFeature
            {
                FeatureType = "Pin",
                Id = pinConnection.Name ?? string.Empty,
                ComponentRef = pinConnection.Component?.RefDes ?? string.Empty
            });
        }

        return new Net
        {
            Name = proto.Name ?? string.Empty,
            PinCount = proto.PinConnections.Count,
            ViaCount = 0,
            Features = features
        };
    }

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
                Number = (int)(pinDto.Index ?? 0),
                NetName = string.Empty, // Not available in component response
                ElectricalType = string.Empty,
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
            Pins = pins,
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
                ComponentRef = pc.Component?.RefDes ?? string.Empty,
            })
            .ToList() ?? [];

        return new Net
        {
            Name = dto.Name ?? string.Empty,
            PinCount = dto.PinConnections?.Count ?? 0,
            ViaCount = 0, // Not available in current protobuf schema
            Features = features,
        };
    }

    /// <summary>
    /// Maps protobuf BoardSide enum string values to display-friendly strings.
    /// Uses case-insensitive matching to handle variations in protobuf JSON serialization.
    /// </summary>
    /// <param name="side">
    /// The BoardSide enum value as a string, which may be in different cases
    /// (e.g., "TOP", "Top", or "top") depending on the server's protobuf JSON serializer configuration.
    /// </param>
    /// <returns>
    /// A display-friendly string: "Top" for TOP, "Bottom" for BOTTOM, or empty string for BS_NONE/unknown values.
    /// </returns>
    /// <remarks>
    /// The ToUpperInvariant() normalization is necessary because protobuf-generated JSON may produce
    /// enum values in different cases depending on serializer settings (JsonStringEnumConverter,
    /// naming policies, etc.). This ensures consistent mapping regardless of server configuration.
    /// </remarks>
    private static string MapBoardSide(string? side)
    {
        return side?.ToUpperInvariant() switch
        {
            "TOP" => "Top",
            "BOTTOM" => "Bottom",
            "BS_NONE" => string.Empty,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Handles HTTP error responses from the REST API.
    /// Note: 404 (Not Found) is handled gracefully by callers before reaching this method.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="designId">The design identifier.</param>
    /// <param name="resourceType">The type of resource being fetched (e.g., "components", "nets").</param>
    /// <exception cref="InvalidOperationException">Thrown for server errors or unexpected status codes.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown for 401 (Unauthorized).</exception>
    [DoesNotReturn]
    private void HandleHttpError(HttpStatusCode statusCode, string designId, string resourceType)
    {
        switch (statusCode)
        {
            case HttpStatusCode.NotFound:
                _logger?.LogWarning(
                    "Design '{DesignId}' not found when fetching {ResourceType} via REST API — returning empty list",
                    designId,
                    resourceType);
                // This should never be reached since 404 is handled before calling this method
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

    private static string DetermineLayerType(string layerName)
    {
        var lowerName = layerName.ToLowerInvariant();
        if (lowerName.Contains("signal") || lowerName.Contains("copper"))
            return "Signal";
        if (lowerName.Contains("power") || lowerName.Contains("ground") || lowerName.Contains("gnd"))
            return "Power";
        if (lowerName.Contains("dielectric") || lowerName.Contains("prepreg") || lowerName.Contains("core"))
            return "Dielectric";
        if (lowerName.Contains("drill"))
            return "Drill";
        if (lowerName.Contains("mask") || lowerName.Contains("solder"))
            return "SolderMask";
        if (lowerName.Contains("silk") || lowerName.Contains("legend"))
            return "SilkScreen";
        return "Signal";
    }

    private bool IsDesignCacheExpired() => DateTime.Now - _designCacheRefresh > _cacheExpiration;
    private bool IsComponentCacheExpired() => DateTime.Now - _componentCacheRefresh > _cacheExpiration;
    private bool IsNetCacheExpired() => DateTime.Now - _netCacheRefresh > _cacheExpiration;
    private bool IsStackupCacheExpired() => DateTime.Now - _stackupCacheRefresh > _cacheExpiration;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DrillTool>> GetDrillToolsAsync(
        string designId, 
        string stepName, 
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting drill tools for design {DesignId}, step {StepName}", designId, stepName);

        try
        {
            // TODO: Implement when drill tools API is available
            return [];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get drill tools for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Package>> GetPackagesAsync(
        string designId, 
        string stepName, 
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting packages for design {DesignId}, step {StepName}", designId, stepName);

        try
        {
            // TODO: Implement when packages API is available
            return [];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get packages for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Part>> GetPartsAsync(
        string designId, 
        string stepName, 
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting parts for design {DesignId}, step {StepName}", designId, stepName);

        try
        {
            // TODO: Implement when parts API is available
            return [];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get parts for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    public void ClearCache()
    {
        _designCache.Clear();
        _componentCache.Clear();
        _netCache.Clear();
        _designCacheRefresh = DateTime.MinValue;
        _componentCacheRefresh = DateTime.MinValue;
        _netCacheRefresh = DateTime.MinValue;
    }

    /// <summary>
    /// Parses step names from API response content.
    /// </summary>
    /// <param name="content">The raw JSON content from the API response.</param>
    /// <param name="designName">The design name for logging purposes.</param>
    /// <returns>List of step names.</returns>
    private List<string> ParseStepsFromResponse(string? content, string designName)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(content ?? "[]", JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(content ?? "{}");
            var root = doc.RootElement;
            
            if (root.TryGetProperty("steps", out var stepsArray))
            {
                return stepsArray.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            
            _logger?.LogWarning("Unable to parse steps from response for design '{DesignName}'", designName);
            return [];
        }
    }
}
