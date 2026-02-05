using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Odb.Grpc;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services.Api;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Implementation of the design data service with hybrid REST/gRPC transport.
/// </summary>
public class DesignService : IDesignService
{
    private readonly ILogger<DesignService>? _logger;
    private readonly IConnectionService _connectionService;
    private readonly IOdbDesignRestApi _restApi;

    private readonly ConcurrentDictionary<string, Design> _designCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<Component>> _componentCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<Net>> _netCache = new();
    
    private DateTime _designCacheRefresh = DateTime.MinValue;
    private DateTime _componentCacheRefresh = DateTime.MinValue;
    private DateTime _netCacheRefresh = DateTime.MinValue;
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
            var designNames = await _restApi.GetDesignNamesAsync(cancellationToken);
            var designs = new List<Design>();

            foreach (var name in designNames)
            {
                if (_designCache.TryGetValue(name, out var cached) && !IsDesignCacheExpired())
                {
                    designs.Add(cached);
                    continue;
                }

                var steps = await _restApi.GetStepsAsync(name, cancellationToken);
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

            _designCacheRefresh = DateTime.Now;
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
            var steps = await _restApi.GetStepsAsync(designId, cancellationToken);
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
            List<Component> components;

            if (_connectionService.IsGrpcAvailable)
            {
                components = await GetComponentsViaGrpcAsync(designId, cancellationToken);
            }
            else
            {
                components = await GetComponentsViaRestAsync(designId, cancellationToken);
            }

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

    private async Task<List<Component>> GetComponentsViaRestAsync(string designId, CancellationToken cancellationToken)
    {
        // REST API parsing not implemented yet
        _logger?.LogWarning("REST API for components is not implemented yet");
        throw new NotImplementedException("REST API component parsing not yet implemented. Use gRPC.");
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

            if (_connectionService.IsGrpcAvailable)
            {
                nets = await GetNetsViaGrpcAsync(designId, cancellationToken);
            }
            else
            {
                nets = await GetNetsViaRestAsync(designId, cancellationToken);
            }

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

    private async Task<List<Net>> GetNetsViaRestAsync(string designId, CancellationToken cancellationToken)
    {
        // REST API parsing not implemented yet
        _logger?.LogWarning("REST API for nets is not implemented yet");
        throw new NotImplementedException("REST API net parsing not yet implemented. Use gRPC.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Layer>> GetStackupAsync(
        string designId, 
        string stepName, 
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting stackup for design {DesignId}, step {StepName}", designId, stepName);

        try
        {
            var layerNames = await _restApi.GetLayerNamesAsync(designId, stepName, cancellationToken);
            var layers = layerNames.Select((name, index) => new Layer
            {
                Id = index,
                Name = name,
                Type = DetermineLayerType(name),
                Polarity = "Positive"
            }).ToList();

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
        // Map position and rotation from protobuf if available, falling back to 0 when missing
        var rotation = proto.Rotation;
        var x = proto.CenterPoint?.X ?? 0;
        var y = proto.CenterPoint?.Y ?? 0;
        var pins = new List<Pin>();

        return new Component
        {
            RefDes = proto.RefDes ?? string.Empty,
            PartName = proto.PartName ?? string.Empty,
            Package = proto.Package?.Name ?? string.Empty,
            Side = proto.Side == Odb.Lib.Protobuf.BoardSide.Top ? "Top" : "Bottom",
            Rotation = rotation,
            X = x,
            Y = y,
            Pins = pins
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
}
