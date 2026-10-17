using Odb.Lib.Protobuf.ProductModel;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Read access to the gRPC design service owned by the connection.
/// </summary>
/// <remarks>
/// Design reads (matrix, step headers, EDA data, symbols, the FileModel product
/// model) go over gRPC: the server's derived-data contract is gRPC-only and REST is
/// frozen at security fixes (OdbDesign handoff §5b, decision D5). This seam decouples
/// <see cref="DesignService"/> from the concrete gRPC client held by
/// <see cref="ConnectionService"/> so the read path is unit-testable against
/// synthetic protobuf designs.
/// </remarks>
public interface IOdbDesignGrpcClient
{
    /// <summary>
    /// Gets a value indicating whether the gRPC design client is connected.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Fetches one design's full protobuf <see cref="Design"/> (its <c>FileModel</c>
    /// carries the matrix, step directories, symbols and per-layer files).
    /// </summary>
    /// <param name="designName">The design name (server key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The design message, or null when gRPC is unavailable.</returns>
    /// <exception cref="Grpc.Core.RpcException">Thrown when the call fails at transport level.</exception>
    Task<Design?> GetDesignAsync(string designName, CancellationToken cancellationToken = default);
}
