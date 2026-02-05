using OdbDesignInfoClient.Core.Models;

namespace OdbDesignInfoClient.Core.Services.Interfaces;

/// <summary>
/// Provides access to ODB++ design data from the server.
/// </summary>
public interface IDesignService
{
    /// <summary>
    /// Gets the list of available designs on the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of design summaries.</returns>
    Task<IReadOnlyList<Design>> GetDesignsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific design by ID.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The design details.</returns>
    Task<Design?> GetDesignAsync(string designId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the components for a design.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of components.</returns>
    Task<IReadOnlyList<Component>> GetComponentsAsync(string designId, string stepName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the nets for a design.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of nets.</returns>
    Task<IReadOnlyList<Net>> GetNetsAsync(string designId, string stepName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the layer stackup for a design.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of layers in stackup order.</returns>
    Task<IReadOnlyList<Layer>> GetStackupAsync(string designId, string stepName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the drill tools for a design.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of drill tools.</returns>
    Task<IReadOnlyList<DrillTool>> GetDrillToolsAsync(string designId, string stepName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the packages for a design.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of packages.</returns>
    Task<IReadOnlyList<Package>> GetPackagesAsync(string designId, string stepName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the parts for a design.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of parts.</returns>
    Task<IReadOnlyList<Part>> GetPartsAsync(string designId, string stepName, CancellationToken cancellationToken = default);
}
