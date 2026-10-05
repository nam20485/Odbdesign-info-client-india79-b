using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Odb.Grpc;
using OdbDesign.ProductModel;
using OdbDesign.ProductModel.Models;
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
    private readonly ConcurrentDictionary<string, DesignProductModel> _productModelCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<StepSummary>> _stepSummaryCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<SymbolSummary>> _symbolCache = new();
    private readonly ConcurrentDictionary<string, EdaDataSummary> _edaDataCache = new();
    private readonly ProductModelReader _productModelReader;

    private DateTime _designCacheRefresh = DateTime.MinValue;
    private DateTime _componentCacheRefresh = DateTime.MinValue;
    private DateTime _netCacheRefresh = DateTime.MinValue;
    private DateTime _stackupCacheRefresh = DateTime.MinValue;
    private DateTime _stepSummaryCacheRefresh = DateTime.MinValue;
    private DateTime _symbolCacheRefresh = DateTime.MinValue;
    private DateTime _edaDataCacheRefresh = DateTime.MinValue;
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
        _productModelReader = new ProductModelReader(
            logger as ILogger<ProductModelReader>
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProductModelReader>.Instance);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Design>> GetDesignsAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting designs from server");

        try
        {
            var response = await _restApi.GetDesignNamesAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                // The server returns 204 when no designs are loaded - not an error
                _logger?.LogInformation("Server returned 204 No Content for design names - no designs available");
                return [];
            }

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
                using var doc = JsonDocument.Parse(response.Content);
                var root = doc.RootElement;
                
                // Try to parse as a simple string array first (documented REST API format)
                if (root.ValueKind == JsonValueKind.Array)
                {
                    designInfos = root.EnumerateArray()
                        .Select(item =>
                        {
                            // Handle both string array ["design1", "design2"] 
                            // and object array with name/loaded properties
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                return (Name: item.GetString() ?? string.Empty, Loaded: true);
                            }
                            else if (item.ValueKind == JsonValueKind.Object)
                            {
                                var name = string.Empty;
                                var loaded = true;
                                
                                if (item.TryGetProperty("name", out var nameElement))
                                {
                                    name = nameElement.GetString() ?? string.Empty;
                                }
                                
                                if (item.TryGetProperty("loaded", out var loadedElement))
                                {
                                    loaded = loadedElement.GetBoolean();
                                }
                                
                                return (Name: name, Loaded: loaded);
                            }
                            
                            return (Name: string.Empty, Loaded: false);
                        })
                        .Where(item => !string.IsNullOrEmpty(item.Name))
                        .ToList();
                }
                // Fallback to object format: { "filearchives": [...] }
                else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("filearchives", out var filearchivesArray))
                {
                    designInfos = filearchivesArray.EnumerateArray()
                        .Select(item =>
                        {
                            var name = string.Empty;
                            var loaded = true;
                            
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
                    _logger?.LogError("Unable to parse response - expected JSON array or object with 'filearchives' property");
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
                ? GetComponentsViaGrpcAsync(designId, stepName, cancellationToken)
                : GetComponentsViaRestAsync(designId, cancellationToken));

            _componentCache[cacheKey] = components;
            _componentCacheRefresh = DateTime.Now;
            return components;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get components for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    private async Task<List<Component>> GetComponentsViaGrpcAsync(string designId, string stepName, CancellationToken cancellationToken)
    {
        var model = await GetProductModelAsync(designId, stepName, cancellationToken);
        if (model is null)
        {
            return await GetComponentsViaRestAsync(designId, cancellationToken);
        }

        var components = model.Components.Select(MapComponentDetail).ToList();
        _logger?.LogInformation("Loaded {Count} components via gRPC product model for {DesignId}/{StepName}", components.Count, designId, stepName);
        return components;
    }

    /// <summary>
    /// Fetches (and caches) the full product model for a design/step over gRPC by reading
    /// the design's <c>FileModel</c>. Returns null when gRPC is unavailable or the model is
    /// empty, so callers can fall back to the REST control-plane endpoints.
    /// </summary>
    /// <remarks>
    /// The request leaves <c>include_normalized_lists</c> unset (false): the server prunes the
    /// normalized nets/components/packages/parts collections to save bandwidth, and this reader
    /// reconstructs them from the always-present <c>FileModel</c> (EDA data + per-layer component
    /// and tools files) exactly as the 3D client does.
    /// </remarks>
    private async Task<DesignProductModel?> GetProductModelAsync(string designId, string stepName, CancellationToken cancellationToken)
    {
        var cacheKey = $"{designId}:{stepName}";
        if (_productModelCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (!_connectionService.IsGrpcAvailable ||
            _connectionService is not ConnectionService connectionServiceImpl)
        {
            return null;
        }

        var grpcClient = connectionServiceImpl.GrpcClient;
        if (grpcClient is null)
        {
            return null;
        }

        try
        {
            var request = new GetDesignRequest { DesignName = designId };
            var design = await grpcClient.GetDesignAsync(request, cancellationToken: cancellationToken);
            var model = _productModelReader.Read(design, stepName);
            if (model.Components.Count == 0 && model.Nets.Count == 0)
            {
                _logger?.LogWarning(
                    "gRPC product model was empty for {DesignId}/{StepName}; falling back to REST",
                    designId, stepName);
                return null;
            }

            _productModelCache[cacheKey] = model;
            return model;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "gRPC product-model fetch failed for {DesignId}/{StepName}; falling back to REST", designId, stepName);
            return null;
        }
    }

    /// <summary>Maps a product-model <see cref="ComponentDetail"/> to the domain <see cref="Component"/>.</summary>
    private static Component MapComponentDetail(ComponentDetail detail) => new()
    {
        RefDes = detail.Name,
        PartName = detail.PartName ?? string.Empty,
        Package = detail.Package?.Name ?? detail.PkgRef ?? string.Empty,
        Side = detail.Side switch
        {
            Odb.Lib.Protobuf.BoardSide.Top => "Top",
            Odb.Lib.Protobuf.BoardSide.Bottom => "Bottom",
            _ => string.Empty,
        },
        Rotation = detail.Rotation,
        X = detail.PositionX,
        Y = detail.PositionY,
        Pins = detail.Pins.Select(p => new Pin
        {
            Name = p.Name ?? p.PinNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Number = (int)p.PinNumber,
            NetName = p.NetName ?? string.Empty,
            ElectricalType = string.Empty,
            X = p.X,
            Y = p.Y,
        }).ToList(),
    };

    /// <summary>Maps a product-model <see cref="NetDetail"/> to the domain <see cref="Net"/>.</summary>
    private static Net MapNetDetail(NetDetail detail) => new()
    {
        Name = detail.Name,
        PinCount = detail.Connections.Count,
        ViaCount = detail.ViaCount,
        Features = detail.Connections.Select(c => new NetFeature
        {
            FeatureType = "Pin",
            Id = c.PinNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ComponentRef = c.ComponentName,
        }).ToList(),
    };


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
                ? GetNetsViaGrpcAsync(designId, stepName, cancellationToken)
                : GetNetsViaRestAsync(designId, cancellationToken));

            _netCache[cacheKey] = nets;
            _netCacheRefresh = DateTime.Now;
            return nets;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get nets for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    private async Task<List<Net>> GetNetsViaGrpcAsync(string designId, string stepName, CancellationToken cancellationToken)
    {
        var model = await GetProductModelAsync(designId, stepName, cancellationToken);
        if (model is null)
        {
            return await GetNetsViaRestAsync(designId, cancellationToken);
        }

        var nets = model.Nets.Select(MapNetDetail).ToList();
        _logger?.LogInformation("Loaded {Count} nets via gRPC product model for {DesignId}/{StepName}", nets.Count, designId, stepName);
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
            var matrixResponse = await _restApi.GetMatrixAsync(designId, cancellationToken);
            if (matrixResponse.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(matrixResponse.Content))
            {
                var layers = ParseMatrixLayers(matrixResponse.Content, designId);
                if (layers.Count > 0)
                {
                    _stackupCache[cacheKey] = layers;
                    _stackupCacheRefresh = DateTime.Now;
                    return layers;
                }
            }
            else
            {
                _logger?.LogWarning(
                    "Matrix fetch failed for design {DesignId} (status {StatusCode}); falling back to layer names. Stackup detail will be limited.",
                    designId,
                    matrixResponse.StatusCode);
            }

            // Fallback: matrix route unavailable or empty — build a minimal stackup from the
            // per-step layer-name list (type guessed from name; no color/stack-order/drill span).
            var namesResponse = await _restApi.GetLayerNamesAsync(designId, stepName, cancellationToken);
            if (!namesResponse.IsSuccessStatusCode || string.IsNullOrWhiteSpace(namesResponse.Content))
            {
                _logger?.LogWarning("Failed to get layer names for design {DesignId}, step {StepName}. Status: {StatusCode}",
                    designId, stepName, namesResponse.StatusCode);
                return new List<Layer>();
            }

            var layerNames = ParseLayerNamesFromResponse(namesResponse.Content, designId, stepName);
            var fallbackLayers = layerNames.Select((name, index) => new Layer
            {
                Id = index,
                StackOrder = index + 1,
                Name = name,
                Type = DetermineLayerType(name),
                ColorHex = ColorForLayerType(DetermineLayerType(name))
            }).ToList();

            _stackupCache[cacheKey] = fallbackLayers;
            _stackupCacheRefresh = DateTime.Now;
            return fallbackLayers;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get stackup for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    /// <summary>
    /// Parses the design-matrix projection into <see cref="Layer"/> records, sorting by the
    /// physical stack row and carrying the server's authoritative type, display color, and
    /// drill-span boundaries.
    /// </summary>
    /// <param name="content">The raw matrix JSON from the API response.</param>
    /// <param name="designId">The design identifier for logging purposes.</param>
    /// <returns>The ordered stackup layers; empty when the matrix has no layers.</returns>
    private List<Layer> ParseMatrixLayers(string? content, string designId)
    {
        try
        {
            var matrix = JsonSerializer.Deserialize<MatrixDto>(content ?? "{}", JsonOptions);
            if (matrix?.Layers is null || matrix.Layers.Count == 0)
            {
                _logger?.LogWarning("Matrix response contained no layers for design {DesignId}", designId);
                return [];
            }

            var layers = new List<Layer>(matrix.Layers.Count);
            foreach (var row in matrix.Layers.OrderBy(l => l.Row))
            {
                var type = string.IsNullOrEmpty(row.Type) ? "Signal" : row.Type;
                layers.Add(new Layer
                {
                    Id = (int)row.Row,
                    StackOrder = (int)row.Row,
                    Name = row.Name ?? string.Empty,
                    Type = type,
                    ColorHex = ResolveLayerColor(row.Color, type),
                    StartLayer = row.StartName,
                    EndLayer = row.EndName,
                });
            }

            _logger?.LogInformation(
                "Parsed {Count} stackup layers from design matrix for {DesignId}",
                layers.Count,
                designId);
            return layers;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(
                ex,
                "Failed to parse design matrix JSON for {DesignId} at position {Position}",
                designId,
                ex.BytePositionInLine);
            throw new InvalidOperationException($"Failed to parse stackup matrix: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Converts a matrix layer color to a display hex string, falling back to a type-based
    /// default when the design declares no preferred color.
    /// </summary>
    /// <param name="color">The color from the matrix response (may be null).</param>
    /// <param name="layerType">The resolved layer type used for the fallback color.</param>
    /// <returns>A <c>#RRGGBB</c> string.</returns>
    private static string ResolveLayerColor(MatrixColorDto? color, string layerType)
    {
        if (color is null || color.NoPreference)
        {
            return ColorForLayerType(layerType);
        }

        return FormattableString.Invariant($"#{ToHex(color.Red)}{ToHex(color.Green)}{ToHex(color.Blue)}");
    }

    private static string ToHex(uint channel) => Math.Min(channel, 255u).ToString("X2");

    /// <summary>
    /// Returns a conventional display color for a layer type when the design specifies none.
    /// </summary>
    /// <param name="layerType">The normalized layer type.</param>
    /// <returns>A <c>#RRGGBB</c> string.</returns>
    private static string ColorForLayerType(string layerType) => layerType switch
    {
        "Signal" => "#4CAF50",
        "Mixed" => "#8BC34A",
        "PowerGround" => "#F44336",
        "Power" => "#F44336",
        "Dielectric" => "#FFC107",
        "Drill" => "#9C27B0",
        "Rout" => "#795548",
        "SolderMask" => "#2196F3",
        "SolderPaste" => "#03A9F4",
        "SilkScreen" => "#FFFFFF",
        "Component" => "#607D8B",
        "Document" => "#BDBDBD",
        _ => "#808080",
    };

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
            // The REST component projection omits placement/rotation; the gRPC
            // product-model path supplies real X/Y/rotation when available.
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
    private bool IsStepSummaryCacheExpired() => DateTime.Now - _stepSummaryCacheRefresh > _cacheExpiration;
    private bool IsSymbolCacheExpired() => DateTime.Now - _symbolCacheRefresh > _cacheExpiration;
    private bool IsEdaDataCacheExpired() => DateTime.Now - _edaDataCacheRefresh > _cacheExpiration;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DrillTool>> GetDrillToolsAsync(
        string designId,
        string stepName,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting drill tools for design {DesignId}, step {StepName}", designId, stepName);

        // Drill tools live in the step's tools file inside the gRPC FileModel — the server
        // exposes no REST route for them. Without gRPC the tab is intentionally empty.
        var model = await GetProductModelAsync(designId, stepName, cancellationToken);
        if (model is null)
        {
            _logger?.LogInformation(
                "Drill tools unavailable for {DesignId}/{StepName} (gRPC product model not available)",
                designId, stepName);
            return [];
        }

        return model.DrillTools
            .Select(t => new DrillTool
            {
                ToolNumber = t.ToolNumber,
                Diameter = t.DrillSizeMm,
                Shape = t.ToolType,
                IsPlated = t.IsPlated,
                HitCount = 0,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Package>> GetPackagesAsync(
        string designId,
        string stepName,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting packages for design {DesignId}, step {StepName}", designId, stepName);

        var model = await GetProductModelAsync(designId, stepName, cancellationToken);
        if (model is not null)
        {
            return model.Packages.Select(p => MapPackageDetail(p, model.Components)).ToList();
        }

        // REST fallback: the /designs/{name}/packages control-plane endpoint.
        try
        {
            var response = await _restApi.GetPackagesAsync(designId, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound || string.IsNullOrWhiteSpace(response.Content))
            {
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                HandleHttpError(response.StatusCode, designId, "packages");
            }

            var dtos = JsonSerializer.Deserialize<List<PackageDto>>(response.Content, JsonOptions) ?? [];
            return dtos.Select(MapPackageDto).ToList();
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException and not InvalidOperationException)
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

        var model = await GetProductModelAsync(designId, stepName, cancellationToken);
        if (model is not null)
        {
            return model.Parts
                .Select(p => MapPartInfo(p, model.Components))
                .ToList();
        }

        // REST fallback: the /designs/{name}/parts control-plane endpoint.
        try
        {
            var response = await _restApi.GetPartsAsync(designId, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound || string.IsNullOrWhiteSpace(response.Content))
            {
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                HandleHttpError(response.StatusCode, designId, "parts");
            }

            var dtos = JsonSerializer.Deserialize<List<PartDto>>(response.Content, JsonOptions) ?? [];
            return dtos.Select(MapPartDto).ToList();
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException and not InvalidOperationException)
        {
            _logger?.LogError(ex, "Failed to get parts for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StepSummary>> GetStepSummariesAsync(
        string designId,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting step summaries for design {DesignId}", designId);

        if (_stepSummaryCache.TryGetValue(designId, out var cached) && !IsStepSummaryCacheExpired())
        {
            return cached;
        }

        var stepsResponse = await _restApi.GetStepsAsync(designId, cancellationToken);
        if (stepsResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!stepsResponse.IsSuccessStatusCode)
        {
            HandleHttpError(stepsResponse.StatusCode, designId, "steps");
        }

        if (string.IsNullOrWhiteSpace(stepsResponse.Content))
        {
            return [];
        }

        var steps = ParseStepsFromResponse(stepsResponse.Content, designId);
        var summaries = new List<StepSummary>(steps.Count);
        foreach (var step in steps)
        {
            var header = await GetStepHeaderAsync(designId, step, cancellationToken);
            summaries.Add(new StepSummary
            {
                Name = step,
                Id = header?.Id,
                XOrigin = header?.XOrigin,
                YOrigin = header?.YOrigin,
                XDatum = header?.XDatum,
                YDatum = header?.YDatum,
                RepeatCount = header?.RepeatCount ?? 0,
            });
        }

        _stepSummaryCache[designId] = summaries;
        _stepSummaryCacheRefresh = DateTime.Now;
        return summaries;
    }

    /// <summary>
    /// Fetches and parses one step's header projection (stephdr route). Returns null
    /// when the route is unavailable or malformed — the header is metadata, so a
    /// missing header degrades the row to a bare step name instead of failing the tab.
    /// </summary>
    private async Task<StepHeaderData?> GetStepHeaderAsync(string designId, string stepName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _restApi.GetStepHdrAsync(designId, stepName, cancellationToken);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger?.LogDebug(
                    "Step header unavailable for {DesignId}/{StepName} (status {StatusCode})",
                    designId, stepName, response.StatusCode);
                return null;
            }

            return ParseStepHeader(response.Content);
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException and not InvalidOperationException)
        {
            _logger?.LogWarning(ex, "Failed to read step header for {DesignId}/{StepName}", designId, stepName);
            return null;
        }
    }

    /// <summary>Parsed step-header projection fields.</summary>
    private sealed record StepHeaderData(
        int? Id,
        double? XOrigin,
        double? YOrigin,
        double? XDatum,
        double? YDatum,
        int RepeatCount);

    /// <summary>
    /// Parses the stephdr JSON (protobuf camelCase) tolerantly: every field is optional
    /// and absent fields stay null rather than being defaulted to fabricated values.
    /// </summary>
    private static StepHeaderData? ParseStepHeader(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            return new StepHeaderData(
                GetNullableInt(root, "id"),
                GetNullableDouble(root, "xOrigin"),
                GetNullableDouble(root, "yOrigin"),
                GetNullableDouble(root, "xDatum"),
                GetNullableDouble(root, "yDatum"),
                root.TryGetProperty("stepRepeatRecords", out var repeats) && repeats.ValueKind == JsonValueKind.Array
                    ? repeats.GetArrayLength()
                    : 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? GetNullableInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static double? GetNullableDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SymbolSummary>> GetSymbolsAsync(
        string designId,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting symbols for design {DesignId}", designId);

        if (_symbolCache.TryGetValue(designId, out var cached) && !IsSymbolCacheExpired())
        {
            return cached;
        }

        var response = await _restApi.GetSymbolNamesAsync(designId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            HandleHttpError(response.StatusCode, designId, "symbols");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            return [];
        }

        var symbols = ParseSymbolNames(response.Content, designId);
        _symbolCache[designId] = symbols;
        _symbolCacheRefresh = DateTime.Now;
        return symbols;
    }

    /// <summary>
    /// Parses the symbols response. Accepts both the observed envelope format
    /// ({ "symbols": ["r10", …] }) and a bare string array, filtering empty names.
    /// </summary>
    private List<SymbolSummary> ParseSymbolNames(string? content, string designId)
    {
        try
        {
            using var doc = JsonDocument.Parse(content ?? "[]");
            var root = doc.RootElement;
            JsonElement? array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("symbols", out var symbols) && symbols.ValueKind == JsonValueKind.Array
                    ? symbols
                    : null;

            if (array is null)
            {
                _logger?.LogWarning("Unable to parse symbols from response for design '{DesignId}'", designId);
                return [];
            }

            return array.Value.EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(name => new SymbolSummary { Name = name })
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse symbols JSON for design {DesignId}", designId);
            throw new InvalidOperationException($"Failed to parse symbols response: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ViaSummary>> GetViaSummariesAsync(
        string designId,
        string stepName,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting via summaries for design {DesignId}, step {StepName}", designId, stepName);

        // Via subnets live in the EDA net records inside the gRPC FileModel; the REST
        // nets projection exposes no via information. Without the product model the
        // list is honestly empty rather than fabricated.
        var model = await GetProductModelAsync(designId, stepName, cancellationToken);
        if (model is null)
        {
            _logger?.LogInformation(
                "Via summaries unavailable for {DesignId}/{StepName} (gRPC product model not available)",
                designId, stepName);
            return [];
        }

        return model.Nets
            .Where(n => n.ViaCount > 0)
            .Select(n => new ViaSummary
            {
                NetName = n.Name,
                NetIndex = n.Index,
                ViaCount = n.ViaCount,
                PinCount = n.Connections.Count,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<EdaDataSummary?> GetEdaDataSummaryAsync(
        string designId,
        string stepName,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting EDA data summary for design {DesignId}, step {StepName}", designId, stepName);

        var cacheKey = $"{designId}:{stepName}:eda";
        if (_edaDataCache.TryGetValue(cacheKey, out var cached) && !IsEdaDataCacheExpired())
        {
            return cached;
        }

        var response = await _restApi.GetEdaDataAsync(designId, stepName, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.NoContent)
        {
            _logger?.LogInformation("No EDA data for {DesignId}/{StepName} (status {StatusCode})",
                designId, stepName, response.StatusCode);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            HandleHttpError(response.StatusCode, designId, "eda_data");
        }

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            _logger?.LogInformation("Empty EDA data content for {DesignId}/{StepName}", designId, stepName);
            return null;
        }

        var summary = ParseEdaDataSummary(response.Content, designId, stepName);
        if (summary is null)
        {
            return null;
        }

        _edaDataCache[cacheKey] = summary;
        _edaDataCacheRefresh = DateTime.Now;
        return summary;
    }

    /// <summary>
    /// Summarizes the (potentially multi-megabyte) eda_data JSON into header fields and
    /// per-net subnet/attribute counts. Defensive: optional protobuf fields are absent in
    /// the JSON and every section degrades to empty when missing.
    /// </summary>
    private EdaDataSummary? ParseEdaDataSummary(string? content, string designId, string stepName)
    {
        try
        {
            using var doc = JsonDocument.Parse(content ?? "{}");
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger?.LogWarning("Unexpected eda_data shape for {DesignId}/{StepName}", designId, stepName);
                return null;
            }

            var nets = new List<EdaNetSummary>();
            if (root.TryGetProperty("netRecords", out var netRecords) && netRecords.ValueKind == JsonValueKind.Array)
            {
                foreach (var net in netRecords.EnumerateArray())
                {
                    var toeprints = 0;
                    var traces = 0;
                    var vias = 0;
                    var planes = 0;
                    if (net.TryGetProperty("subnetRecords", out var subnets) && subnets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var subnet in subnets.EnumerateArray())
                        {
                            var type = subnet.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                            switch (type?.ToUpperInvariant())
                            {
                                case "TOEPRINT": toeprints++; break;
                                case "TRACE": traces++; break;
                                case "VIA": vias++; break;
                                case "PLANE": planes++; break;
                            }
                        }
                    }

                    var attributeCount = 0;
                    if (net.TryGetProperty("propertyRecords", out var properties) && properties.ValueKind == JsonValueKind.Array)
                    {
                        attributeCount += properties.GetArrayLength();
                    }

                    if (net.TryGetProperty("attributeLookupTable", out var lookup) && lookup.ValueKind == JsonValueKind.Object)
                    {
                        attributeCount += lookup.EnumerateObject().Count();
                    }

                    var name = net.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    var index = GetNullableUint(net, "index") ?? 0;

                    nets.Add(new EdaNetSummary
                    {
                        // Same fallback convention as the product-model reader: unnamed
                        // nets display as "#Index" instead of an empty cell.
                        Name = string.IsNullOrEmpty(name) ? $"#{index}" : name!,
                        Index = index,
                        ToeprintCount = toeprints,
                        TraceCount = traces,
                        ViaCount = vias,
                        PlaneCount = planes,
                        AttributeCount = attributeCount,
                    });
                }
            }

            var attributeNames = new List<string>();
            if (root.TryGetProperty("attributeNames", out var names) && names.ValueKind == JsonValueKind.Array)
            {
                attributeNames.AddRange(names.EnumerateArray().Select(n => n.GetString() ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)));
            }

            return new EdaDataSummary
            {
                Units = GetStringProperty(root, "units"),
                Source = GetStringProperty(root, "source"),
                Path = GetStringProperty(root, "path"),
                LayerCount = root.TryGetProperty("layerNames", out var layers) && layers.ValueKind == JsonValueKind.Array
                    ? layers.GetArrayLength()
                    : 0,
                AttributeNames = attributeNames,
                Nets = nets,
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse eda_data JSON for {DesignId}/{StepName}", designId, stepName);
            throw new InvalidOperationException($"Failed to parse eda_data response: {ex.Message}", ex);
        }
    }

    private static uint? GetNullableUint(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetUInt32()
            : null;

    private static string GetStringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Maps a product-model <see cref="PackageDetail"/> to the domain <see cref="Package"/>,
    /// joining the placed components that reference the package into usage rows.
    /// </summary>
    private static Package MapPackageDetail(PackageDetail detail, IReadOnlyList<ComponentDetail> components) => new()
    {
        Name = detail.Name,
        Pitch = detail.Pitch ?? 0d,
        PinCount = detail.PinCount,
        Width = detail.XMax - detail.XMin,
        Height = detail.YMax - detail.YMin,
        Usages = components
            .Where(c => c.Package?.Name == detail.Name)
            .Select(c => new EntityUsage
            {
                ComponentRefDes = c.Name,
                PartName = c.PartName,
            })
            .ToList(),
    };

    /// <summary>Maps a product-model <see cref="PartInfo"/> to the domain <see cref="Part"/>.</summary>
    private static Part MapPartInfo(PartInfo info, IReadOnlyList<ComponentDetail> components) => new()
    {
        PartNumber = info.Name,
        UsageCount = info.UsageCount,
        Usages = components
            .Where(c => c.PartName == info.Name)
            .Select(c => new EntityUsage
            {
                ComponentRefDes = c.Name,
            })
            .ToList(),
    };

    private static Package MapPackageDto(PackageDto dto) => new()
    {
        Name = dto.Name ?? string.Empty,
        Pitch = dto.Pitch ?? 0d,
        PinCount = dto.Pins?.Count ?? 0,
        Width = (dto.XMax ?? 0f) - (dto.XMin ?? 0f),
        Height = (dto.YMax ?? 0f) - (dto.YMin ?? 0f),
    };

    private static Part MapPartDto(PartDto dto)
    {
        var attrs = dto.Attributes;
        return new Part
        {
            PartNumber = dto.Name ?? string.Empty,
            Manufacturer = TryGetAttr(attrs, "MANUFACTURER", "MFR", "VENDOR"),
            Description = TryGetAttr(attrs, "DESCRIPTION", "DESC", "VALUE"),
            UsageCount = 0,
        };
    }

    private static string TryGetAttr(IDictionary<string, string>? attrs, params string[] keys)
    {
        if (attrs is null)
        {
            return string.Empty;
        }

        foreach (var key in keys)
        {
            foreach (var kvp in attrs)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    return kvp.Value;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    public void ClearCache()
    {
        _designCache.Clear();
        _componentCache.Clear();
        _netCache.Clear();
        _stackupCache.Clear();
        _productModelCache.Clear();
        _stepSummaryCache.Clear();
        _symbolCache.Clear();
        _edaDataCache.Clear();
        _designCacheRefresh = DateTime.MinValue;
        _componentCacheRefresh = DateTime.MinValue;
        _netCacheRefresh = DateTime.MinValue;
        _stackupCacheRefresh = DateTime.MinValue;
        _stepSummaryCacheRefresh = DateTime.MinValue;
        _symbolCacheRefresh = DateTime.MinValue;
        _edaDataCacheRefresh = DateTime.MinValue;
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

    /// <summary>
    /// Parses layer names from the layer-list API response.
    /// Accepts both the bare array format ("["layer-1", …]") and the
    /// envelope format ("{ "layers": ["layer-1", …] }") the server emits.
    /// </summary>
    /// <param name="content">The raw JSON content from the API response.</param>
    /// <param name="designId">The design identifier for logging purposes.</param>
    /// <param name="stepName">The step name for logging purposes.</param>
    /// <returns>List of layer names.</returns>
    private List<string> ParseLayerNamesFromResponse(string? content, string designId, string stepName)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(content ?? "[]", JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(content ?? "{}");
            var root = doc.RootElement;

            if (root.TryGetProperty("layers", out var layersArray))
            {
                return layersArray.EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }

            _logger?.LogWarning(
                "Unable to parse layer names from response for design '{DesignId}', step '{StepName}'",
                designId,
                stepName);
            return [];
        }
    }
}
