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

    /// <summary>
    /// Gets the steps of a design with the cheaply-available step-header metadata.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of step summaries, one per step, in server order.</returns>
    Task<IReadOnlyList<StepSummary>> GetStepSummariesAsync(string designId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the symbol names of a design's symbols library.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of symbol names; empty when the design declares no symbols.</returns>
    Task<IReadOnlyList<SymbolSummary>> GetSymbolsAsync(string designId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets per-net via counts for a design/step, derived from the EDA net records
    /// in the gRPC product model. Empty when the design has no VIA subnets or the
    /// product model is unavailable (REST nets expose no via information).
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of via summaries for nets with at least one via.</returns>
    Task<IReadOnlyList<ViaSummary>> GetViaSummariesAsync(string designId, string stepName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of the step's EDA data file (header fields plus per-net
    /// subnet/attribute counts). Null when the server exposes no eda_data for the step.
    /// </summary>
    /// <param name="designId">The design identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The EDA data summary, or null when unavailable.</returns>
    Task<EdaDataSummary?> GetEdaDataSummaryAsync(string designId, string stepName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all cached design data, forcing subsequent fetches to hit the server.
    /// Call on design change, step change, disconnect, reconnect, and explicit refresh.
    /// </summary>
    void ClearCache();
}
