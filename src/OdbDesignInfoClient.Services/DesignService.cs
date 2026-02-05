using Microsoft.Extensions.Logging;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Implementation of the design data service.
/// </summary>
public class DesignService : IDesignService
{
    private readonly ILogger<DesignService>? _logger;
    private readonly IConnectionService _connectionService;

    /// <summary>
    /// Initializes a new instance of the DesignService.
    /// </summary>
    public DesignService(IConnectionService connectionService, ILogger<DesignService>? logger = null)
    {
        _connectionService = connectionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Design>> GetDesignsAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting designs from server");
        
        // TODO: Implement actual API call using Refit
        // This is a placeholder implementation
        return Task.FromResult<IReadOnlyList<Design>>(new List<Design>());
    }

    /// <inheritdoc />
    public Task<Design?> GetDesignAsync(string designId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting design {DesignId} from server", designId);
        
        // TODO: Implement actual API call
        return Task.FromResult<Design?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Component>> GetComponentsAsync(string designId, string stepName, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting components for design {DesignId}, step {StepName}", designId, stepName);
        
        // TODO: Implement actual API call (gRPC for bulk data)
        return Task.FromResult<IReadOnlyList<Component>>(new List<Component>());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Net>> GetNetsAsync(string designId, string stepName, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting nets for design {DesignId}, step {StepName}", designId, stepName);
        
        // TODO: Implement actual API call (gRPC for bulk data)
        return Task.FromResult<IReadOnlyList<Net>>(new List<Net>());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Layer>> GetStackupAsync(string designId, string stepName, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Getting stackup for design {DesignId}, step {StepName}", designId, stepName);
        
        // TODO: Implement actual API call
        return Task.FromResult<IReadOnlyList<Layer>>(new List<Layer>());
    }
}
