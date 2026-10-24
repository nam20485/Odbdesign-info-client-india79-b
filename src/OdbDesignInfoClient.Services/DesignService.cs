using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Odb.Lib.Protobuf;
using OdbDesign.ProductModel;
using OdbDesign.ProductModel.Models;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services.Api;
using ProtoDesign = Odb.Lib.Protobuf.ProductModel.Design;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Implementation of the design data service. Design reads (matrix, step headers,
/// EDA data, symbols, components/nets/packages/parts/drill tools) go over gRPC via
/// the design's <c>FileModel</c>; REST carries the discovery plane only (design and
/// step name lists, health).
/// </summary>
/// <remarks>
/// Transport split (OdbDesign connectivity handoff §5b, decision D5): the server's
/// derived-data contract ships gRPC-only and REST is frozen at security fixes, so the
/// gRPC <c>GetDesign</c> response is the single content source. The per-resource REST
/// projections (<c>/designs/{name}/components|nets|packages|parts</c>) were deleted
/// rather than kept as fallbacks: they served strictly worse data (no placement
/// coordinates, no pin net names, no via counts, no usage rows), can never carry the
/// gRPC-only Connectivity contract, and required hand-mirrored DTOs to stay in sync.
/// When gRPC is unavailable the content tabs are honestly empty and the log says why.
/// </remarks>
public class DesignService : IDesignService
{
    private readonly ILogger<DesignService>? _logger;
    private readonly IOdbDesignGrpcClient _grpcClient;
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
    private readonly ConcurrentDictionary<string, Lazy<Task<DesignProductModel?>>> _productModelFetches = new();
    private readonly ConcurrentDictionary<string, ProtoDesign> _designArchiveCache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<ProtoDesign?>>> _designArchiveFetches = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<StepSummary>> _stepSummaryCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<SymbolSummary>> _symbolCache = new();
    private readonly ConcurrentDictionary<string, EdaDataSummary> _edaDataCache = new();
    private readonly ProductModelReader _productModelReader;

    /// <summary>
    /// Bumped by <see cref="ClearCache"/>. Load generations capture it before their
    /// awaits and must not write caches afterwards when it changed — a download that
    /// races a refresh must not re-populate the just-cleared caches as fresh for
    /// another TTL window.
    /// </summary>
    private int _cacheGeneration;

    private DateTime _designCacheRefresh = DateTime.MinValue;
    private DateTime _componentCacheRefresh = DateTime.MinValue;
    private DateTime _netCacheRefresh = DateTime.MinValue;
    private DateTime _stackupCacheRefresh = DateTime.MinValue;
    private DateTime _productModelCacheRefresh = DateTime.MinValue;
    private DateTime _designArchiveCacheRefresh = DateTime.MinValue;
    private DateTime _stepSummaryCacheRefresh = DateTime.MinValue;
    private DateTime _symbolCacheRefresh = DateTime.MinValue;
    private DateTime _edaDataCacheRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the DesignService.
    /// </summary>
    public DesignService(
        IOdbDesignGrpcClient grpcClient,
        IOdbDesignRestApi restApi,
        ILogger<DesignService>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _grpcClient = grpcClient;
        _restApi = restApi;
        _logger = logger;
        // ILogger<T> categories are not interchangeable (a cast between closed
        // generic logger types always yields null), so the product-model reader
        // builds its loggers from a factory.
        _productModelReader = new ProductModelReader(loggerFactory ?? NullLoggerFactory.Instance);
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

    /// <summary>
    /// Fetches (and caches) one design's full gRPC <see cref="Design"/> message — the
    /// file archive every content read derives from (matrix, step headers, EDA data,
    /// symbols, per-step product models). Returns null when gRPC is unavailable or the
    /// fetch fails, so callers can degrade honestly instead of throwing.
    /// </summary>
    /// <remarks>
    /// The request leaves <c>include_normalized_lists</c> unset (false): the server prunes the
    /// normalized nets/components/packages/parts collections to save bandwidth. The reader
    /// takes pin/net relationships from the response's always-on <c>connectivity</c> block
    /// (server-owned truth, D4 — no request flag exists) and placement/attribute data from
    /// the <c>FileModel</c> (EDA data + per-layer component and tools files); it joins
    /// nothing itself.
    /// A refresh fans out over every product-model-backed tab in parallel; they all miss
    /// the cache at once, so share one in-flight download per design (which also serves
    /// every step of that design) instead of issuing one GetDesign per tab. The download
    /// deliberately runs on CancellationToken.None — its result feeds the 5-minute cache
    /// regardless of which load generation asked first, so a cancelled generation must
    /// not abort it for the tabs that still want it. A <see cref="ClearCache"/> that
    /// races the download evicts the single-flight entry and bumps the cache
    /// generation, so the superseded snapshot is neither adopted by post-clear callers
    /// nor written back into the cache.
    /// </remarks>
    private async Task<ProtoDesign?> GetDesignArchiveAsync(string designId)
    {
        if (_designArchiveCache.TryGetValue(designId, out var cached) && !IsDesignArchiveCacheExpired())
        {
            return cached;
        }

        if (!_grpcClient.IsAvailable)
        {
            return null;
        }

        var generation = Volatile.Read(ref _cacheGeneration);

        // Lazy keeps ConcurrentDictionary.GetOrAdd honest: it may run the value
        // factory more than once under contention, and a bare task factory would
        // download the full design in every discarded invocation.
        var fetch = _designArchiveFetches.GetOrAdd(
            designId,
            _ => new Lazy<Task<ProtoDesign?>>(
                () => FetchDesignArchiveAsync(designId, CancellationToken.None, generation)));
        try
        {
            return await fetch.Value.ConfigureAwait(false);
        }
        finally
        {
            // Drop the completed entry (success or failure) so a later call after
            // the TTL expiry fetches fresh data instead of awaiting a terminal task.
            _designArchiveFetches.TryRemove(KeyValuePair.Create(designId, fetch));
        }
    }

    private async Task<ProtoDesign?> FetchDesignArchiveAsync(
        string designId,
        CancellationToken cancellationToken,
        int generation)
    {
        try
        {
            var design = await _grpcClient.GetDesignAsync(designId, cancellationToken);
            if (design?.FileModel is null)
            {
                _logger?.LogWarning(
                    "gRPC design response carried no file model for {DesignId}", designId);
                return null;
            }

            if (Volatile.Read(ref _cacheGeneration) == generation)
            {
                _designArchiveCache[designId] = design;
                _designArchiveCacheRefresh = DateTime.Now;
            }

            return design;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "gRPC design fetch failed for {DesignId}", designId);
            return null;
        }
    }

    /// <summary>
    /// Builds (and caches) the per-step product model from the design's cached gRPC
    /// file archive. Returns null when the archive is unavailable; an empty model is
    /// returned as-is (the transport owns no richer projection to fall back to).
    /// </summary>
    /// <remarks>
    /// The derive (a full FileModel parse) is single-flighted per (design, step):
    /// a refresh fans out over every product-model-backed tab at once and each
    /// would otherwise run the same parse for the same key.
    /// </remarks>
    private async Task<DesignProductModel?> GetProductModelAsync(string designId, string stepName)
    {
        var cacheKey = $"{designId}:{stepName}";
        if (_productModelCache.TryGetValue(cacheKey, out var cached) && !IsProductModelCacheExpired())
        {
            return cached;
        }

        var fetch = _productModelFetches.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<DesignProductModel?>>(
                () => BuildProductModelAsync(designId, stepName, cacheKey)));
        try
        {
            return await fetch.Value.ConfigureAwait(false);
        }
        finally
        {
            // Drop the completed entry (success or failure) so a later call after
            // the TTL expiry re-derives instead of awaiting a terminal task.
            _productModelFetches.TryRemove(KeyValuePair.Create(cacheKey, fetch));
        }
    }

    private async Task<DesignProductModel?> BuildProductModelAsync(
        string designId,
        string stepName,
        string cacheKey)
    {
        var generation = Volatile.Read(ref _cacheGeneration);
        var archive = await GetDesignArchiveAsync(designId);
        if (archive is null)
        {
            return null;
        }

        var model = _productModelReader.Read(archive, stepName);
        if (Volatile.Read(ref _cacheGeneration) == generation)
        {
            _productModelCache[cacheKey] = model;
            _productModelCacheRefresh = DateTime.Now;
        }

        return model;
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
            var generation = Volatile.Read(ref _cacheGeneration);
            var model = await GetProductModelAsync(designId, stepName);
            var components = (model?.Components ?? []).Select(MapComponentDetail).ToList();

            // Cache only real results: a degraded read (gRPC down) stays uncached so
            // the tab recovers on the next load once gRPC comes back, and a
            // superseded load generation must not re-populate cleared caches.
            if (model is not null && Volatile.Read(ref _cacheGeneration) == generation)
            {
                _componentCache[cacheKey] = components;
                _componentCacheRefresh = DateTime.Now;
            }

            if (model is null)
            {
                _logger?.LogInformation(
                    "Components unavailable for {DesignId}/{StepName} (gRPC design archive not available)",
                    designId, stepName);
            }
            else
            {
                _logger?.LogInformation(
                    "Loaded {Count} components via gRPC product model for {DesignId}/{StepName}",
                    components.Count, designId, stepName);
            }

            return components;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get components for design {DesignId}, step {StepName}", designId, stepName);
            throw;
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
            // Same identifier shape the pin join produced historically
            // (ComponentRef-pin), so the Feature Id column stays consistent.
            Id = $"{c.ComponentName}-{c.PinNumber}",
            ComponentRef = c.ComponentName,
        }).ToList(),
    };

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
            var generation = Volatile.Read(ref _cacheGeneration);
            var model = await GetProductModelAsync(designId, stepName);
            var nets = (model?.Nets ?? []).Select(MapNetDetail).ToList();

            // Cache only real results (see GetComponentsAsync).
            if (model is not null && Volatile.Read(ref _cacheGeneration) == generation)
            {
                _netCache[cacheKey] = nets;
                _netCacheRefresh = DateTime.Now;
            }

            if (model is null)
            {
                _logger?.LogInformation(
                    "Nets unavailable for {DesignId}/{StepName} (gRPC design archive not available)",
                    designId, stepName);
            }
            else
            {
                _logger?.LogInformation(
                    "Loaded {Count} nets via gRPC product model for {DesignId}/{StepName}",
                    nets.Count, designId, stepName);
            }

            return nets;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get nets for design {DesignId}, step {StepName}", designId, stepName);
            throw;
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
            var generation = Volatile.Read(ref _cacheGeneration);
            var fileModel = (await GetDesignArchiveAsync(designId))?.FileModel;

            var matrix = fileModel?.MatrixFile;
            if (matrix is not null && matrix.Layers.Count > 0)
            {
                var layers = MapMatrixLayers(matrix);
                if (Volatile.Read(ref _cacheGeneration) == generation)
                {
                    _stackupCache[cacheKey] = layers;
                    _stackupCacheRefresh = DateTime.Now;
                }

                return layers;
            }

            // Fallback: no matrix in the archive — build a minimal stackup from the
            // step's layer-name list (type guessed from name; no color/stack-order/drill span).
            if (fileModel is not null && fileModel.StepsByName.TryGetValue(stepName, out var stepDir))
            {
                var layerNames = stepDir.LayersByName.Keys
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
                if (layerNames.Count > 0)
                {
                    var fallbackLayers = layerNames.Select((name, index) => new Layer
                    {
                        Id = index,
                        StackOrder = index + 1,
                        Name = name,
                        Type = DetermineLayerType(name),
                        ColorHex = ColorForLayerType(DetermineLayerType(name)),
                    }).ToList();

                    if (Volatile.Read(ref _cacheGeneration) == generation)
                    {
                        _stackupCache[cacheKey] = fallbackLayers;
                        _stackupCacheRefresh = DateTime.Now;
                    }

                    return fallbackLayers;
                }
            }

            _logger?.LogWarning(
                "Stackup unavailable for design {DesignId}, step {StepName} (no matrix and no gRPC design archive)",
                designId, stepName);
            return [];
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get stackup for design {DesignId}, step {StepName}", designId, stepName);
            throw;
        }
    }

    /// <summary>
    /// Maps the gRPC <see cref="MatrixFile"/> layer rows into <see cref="Layer"/> records,
    /// sorting by the physical stack row and carrying the server's authoritative type,
    /// display color, and drill-span boundaries.
    /// </summary>
    /// <param name="matrix">The design's matrix file from the gRPC file model.</param>
    /// <returns>The ordered stackup layers; empty when the matrix has no layers.</returns>
    private List<Layer> MapMatrixLayers(MatrixFile matrix)
    {
        var layers = new List<Layer>(matrix.Layers.Count);
        foreach (var row in matrix.Layers.OrderBy(l => l.Row))
        {
            var type = LayerTypeName(row.Type);
            layers.Add(new Layer
            {
                Id = (int)row.Row,
                StackOrder = (int)row.Row,
                Name = row.Name,
                Type = type,
                ColorHex = ResolveLayerColor(row.Color, type),
                // The proto uses "" for an absent span (non-drill layers); the
                // domain keeps null for "no value".
                StartLayer = string.IsNullOrEmpty(row.StartName) ? null : row.StartName,
                EndLayer = string.IsNullOrEmpty(row.EndName) ? null : row.EndName,
            });
        }

        _logger?.LogInformation("Parsed {Count} stackup layers from the design matrix", layers.Count);
        return layers;
    }

    /// <summary>
    /// Converts a matrix layer-type enum to its display name. The proto enum's default
    /// (0) is <c>Signal</c>, matching the JSON projection that omitted the field for
    /// signal layers.
    /// </summary>
    private static string LayerTypeName(MatrixFile.Types.LayerRecord.Types.Type type) => type switch
    {
        MatrixFile.Types.LayerRecord.Types.Type.PowerGround => "PowerGround",
        MatrixFile.Types.LayerRecord.Types.Type.Dielectric => "Dielectric",
        MatrixFile.Types.LayerRecord.Types.Type.Mixed => "Mixed",
        MatrixFile.Types.LayerRecord.Types.Type.SolderMask => "SolderMask",
        MatrixFile.Types.LayerRecord.Types.Type.SolderPaste => "SolderPaste",
        MatrixFile.Types.LayerRecord.Types.Type.SilkScreen => "SilkScreen",
        MatrixFile.Types.LayerRecord.Types.Type.Drill => "Drill",
        MatrixFile.Types.LayerRecord.Types.Type.Rout => "Rout",
        MatrixFile.Types.LayerRecord.Types.Type.Document => "Document",
        MatrixFile.Types.LayerRecord.Types.Type.Component => "Component",
        MatrixFile.Types.LayerRecord.Types.Type.Mask => "Mask",
        MatrixFile.Types.LayerRecord.Types.Type.ConductivePaste => "ConductivePaste",
        _ => "Signal",
    };

    /// <summary>
    /// Converts a matrix layer color to a display hex string, falling back to a type-based
    /// default when the design declares no preferred color.
    /// </summary>
    /// <param name="color">The color from the matrix layer record (null when absent).</param>
    /// <param name="layerType">The resolved layer type used for the fallback color.</param>
    /// <returns>A <c>#RRGGBB</c> string.</returns>
    private static string ResolveLayerColor(Color? color, string layerType)
    {
        if (color is null || color.NoPreference)
        {
            return ColorForLayerType(layerType);
        }

        return FormattableString.Invariant($"#{ToHex(color.Red)}{ToHex(color.Green)}{ToHex(color.Blue)}");
    }

    private static string ToHex(double channel) => ((uint)Math.Clamp(channel, 0d, 255d)).ToString("X2");

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
    /// Handles HTTP error responses from the REST API.
    /// Note: 404 (Not Found) is handled gracefully by callers before reaching this method.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="designId">The design identifier.</param>
    /// <param name="resourceType">The type of resource being fetched (e.g., "steps").</param>
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
    private bool IsProductModelCacheExpired() => DateTime.Now - _productModelCacheRefresh > _cacheExpiration;
    private bool IsDesignArchiveCacheExpired() => DateTime.Now - _designArchiveCacheRefresh > _cacheExpiration;
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
        var model = await GetProductModelAsync(designId, stepName);
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

        var model = await GetProductModelAsync(designId, stepName);
        var packages = (model?.Packages ?? [])
            .Select(p => MapPackageDetail(p, model?.Components ?? []))
            .ToList();

        if (model is null)
        {
            _logger?.LogInformation(
                "Packages unavailable for {DesignId}/{StepName} (gRPC design archive not available)",
                designId, stepName);
        }

        return packages;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Part>> GetPartsAsync(
        string designId,
        string stepName,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting parts for design {DesignId}, step {StepName}", designId, stepName);

        var model = await GetProductModelAsync(designId, stepName);
        var parts = (model?.Parts ?? [])
            .Select(p => MapPartInfo(p, model?.Components ?? []))
            .ToList();

        if (model is null)
        {
            _logger?.LogInformation(
                "Parts unavailable for {DesignId}/{StepName} (gRPC design archive not available)",
                designId, stepName);
        }

        return parts;
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

        // Step names and headers both live in the gRPC file archive: one cached
        // download serves every step's summary.
        var generation = Volatile.Read(ref _cacheGeneration);
        var fileModel = (await GetDesignArchiveAsync(designId))?.FileModel;
        if (fileModel is not null)
        {
            // Map-field iteration order is wire order (the server's sorted map);
            // sort explicitly so the summary order never depends on it.
            var summaries = fileModel.StepsByName.Keys
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => MapStepSummary(
                    name,
                    fileModel.StepsByName.TryGetValue(name, out var stepDir) ? stepDir.Stephdrfile : null))
                .ToList();

            if (Volatile.Read(ref _cacheGeneration) == generation)
            {
                _stepSummaryCache[designId] = summaries;
                _stepSummaryCacheRefresh = DateTime.Now;
            }

            return summaries;
        }

        // Degraded mode (gRPC unavailable): the REST discovery steps route still
        // names the steps; the stephdr projection is gone with the REST read path,
        // so rows degrade to bare names instead of failing the tab.
        var stepsResponse = await _restApi.GetStepsAsync(designId, cancellationToken);
        if (stepsResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!stepsResponse.IsSuccessStatusCode)
        {
            HandleHttpError(stepsResponse.StatusCode ?? HttpStatusCode.InternalServerError, designId, "steps");
        }

        if (string.IsNullOrWhiteSpace(stepsResponse.Content))
        {
            return [];
        }

        var degraded = ParseStepsFromResponse(stepsResponse.Content, designId)
            .Select(name => MapStepSummary(name, stepHeader: null))
            .ToList();

        // Degraded rows stay uncached (matching the components/nets policy): once
        // gRPC recovers, the next load serves real headers instead of header-less
        // rows until the TTL expires.
        return degraded;
    }

    /// <summary>
    /// Maps one step's header file into a <see cref="StepSummary"/>. Every header field
    /// is optional; absent fields stay null rather than being defaulted to fabricated values.
    /// </summary>
    private static StepSummary MapStepSummary(string name, StepHdrFile? stepHeader) => new()
    {
        Name = name,
        Id = stepHeader?.HasId == true ? stepHeader.Id : null,
        XOrigin = stepHeader?.HasXOrigin == true ? stepHeader.XOrigin : null,
        YOrigin = stepHeader?.HasYOrigin == true ? stepHeader.YOrigin : null,
        XDatum = stepHeader?.HasXDatum == true ? stepHeader.XDatum : null,
        YDatum = stepHeader?.HasYDatum == true ? stepHeader.YDatum : null,
        RepeatCount = stepHeader?.StepRepeatRecords.Count ?? 0,
    };

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

        // The symbol library is design-wide: its names are the keys of the file
        // archive's symbols map (the server's REST route listed the same keys).
        var generation = Volatile.Read(ref _cacheGeneration);
        var fileModel = (await GetDesignArchiveAsync(designId))?.FileModel;
        if (fileModel is null)
        {
            _logger?.LogInformation(
                "Symbols unavailable for design {DesignId} (gRPC design archive not available)", designId);
            return [];
        }

        var symbols = fileModel.SymbolsDirectoriesByName.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new SymbolSummary { Name = name })
            .ToList();

        if (Volatile.Read(ref _cacheGeneration) == generation)
        {
            _symbolCache[designId] = symbols;
            _symbolCacheRefresh = DateTime.Now;
        }

        return symbols;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ViaSummary>> GetViaSummariesAsync(
        string designId,
        string stepName,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting via summaries for design {DesignId}, step {StepName}", designId, stepName);

        // Via subnets live in the EDA net records inside the gRPC FileModel. Without
        // the product model the list is honestly empty rather than fabricated.
        var model = await GetProductModelAsync(designId, stepName);
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

        // The step's EDA data file rides the gRPC file archive (the same message the
        // product-model reader parses); the REST eda_data route is gone.
        var generation = Volatile.Read(ref _cacheGeneration);
        var fileModel = (await GetDesignArchiveAsync(designId))?.FileModel;
        if (fileModel is null || !fileModel.StepsByName.TryGetValue(stepName, out var stepDir) ||
            stepDir.Edadatafile is null)
        {
            _logger?.LogInformation("No EDA data for {DesignId}/{StepName}", designId, stepName);
            return null;
        }

        var summary = MapEdaDataSummary(stepDir.Edadatafile);
        if (Volatile.Read(ref _cacheGeneration) == generation)
        {
            _edaDataCache[cacheKey] = summary;
            _edaDataCacheRefresh = DateTime.Now;
        }

        return summary;
    }

    /// <summary>
    /// Summarizes the step's <see cref="EdaDataFile"/> into header fields and per-net
    /// subnet/attribute counts. Defensive: optional protobuf fields are absent on the
    /// message and every section degrades to empty when missing.
    /// </summary>
    private static EdaDataSummary MapEdaDataSummary(EdaDataFile edaData)
    {
        var nets = new List<EdaNetSummary>(edaData.NetRecords.Count);
        foreach (var net in edaData.NetRecords)
        {
            var toeprints = 0;
            var traces = 0;
            var vias = 0;
            var planes = 0;
            foreach (var subnet in net.SubnetRecords)
            {
                switch (subnet.Type)
                {
                    case EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Toeprint: toeprints++; break;
                    case EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Trace: traces++; break;
                    case EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Via: vias++; break;
                    case EdaDataFile.Types.NetRecord.Types.SubnetRecord.Types.Type.Plane: planes++; break;
                }
            }

            nets.Add(new EdaNetSummary
            {
                // Same fallback convention as the product-model reader: unnamed
                // nets display as "#Index" instead of an empty cell.
                Name = string.IsNullOrEmpty(net.Name) ? FormattableString.Invariant($"#{net.Index}") : net.Name,
                Index = net.Index,
                ToeprintCount = toeprints,
                TraceCount = traces,
                ViaCount = vias,
                PlaneCount = planes,
                AttributeCount = net.PropertyRecords.Count + net.AttributeLookupTable.Count,
            });
        }

        return new EdaDataSummary
        {
            Units = edaData.Units,
            Source = edaData.Source,
            Path = edaData.Path,
            LayerCount = edaData.LayerNames.Count,
            AttributeNames = edaData.AttributeNames
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList(),
            Nets = nets,
        };
    }

    /// <summary>
    /// Maps a product-model <see cref="PackageDetail"/> to the domain <see cref="Package"/>,
    /// joining the placed components that reference the package into usage rows.
    /// </summary>
    private static Package MapPackageDetail(PackageDetail detail, IReadOnlyList<ComponentDetail> components) => new()
    {
        Name = detail.Name,
        // Null pitch stays null: an unavailable value must not surface as an
        // authoritative 0.000 mm.
        Pitch = detail.Pitch,
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
        // The reader groups part names case-insensitively, so the usage join must
        // too — otherwise UsageCount can exceed the rows rendered beneath it.
        Usages = components
            .Where(c => string.Equals(c.PartName, info.Name, StringComparison.OrdinalIgnoreCase))
            .Select(c => new EntityUsage
            {
                ComponentRefDes = c.Name,
            })
            .ToList(),
    };

    /// <summary>
    /// Clears all cached data.
    /// </summary>
    public void ClearCache()
    {
        // Bump the generation and evict the in-flight downloads so a refresh that
        // races a GetDesign never adopts or re-caches the superseded snapshot:
        // post-clear callers start a fresh download, and the old fetch's completion
        // (on CancellationToken.None) fails its generation check and writes nothing.
        Interlocked.Increment(ref _cacheGeneration);
        _designArchiveFetches.Clear();
        _productModelFetches.Clear();

        _designCache.Clear();
        _componentCache.Clear();
        _netCache.Clear();
        _stackupCache.Clear();
        _productModelCache.Clear();
        _designArchiveCache.Clear();
        _stepSummaryCache.Clear();
        _symbolCache.Clear();
        _edaDataCache.Clear();
        _designCacheRefresh = DateTime.MinValue;
        _componentCacheRefresh = DateTime.MinValue;
        _netCacheRefresh = DateTime.MinValue;
        _stackupCacheRefresh = DateTime.MinValue;
        _productModelCacheRefresh = DateTime.MinValue;
        _designArchiveCacheRefresh = DateTime.MinValue;
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
}
