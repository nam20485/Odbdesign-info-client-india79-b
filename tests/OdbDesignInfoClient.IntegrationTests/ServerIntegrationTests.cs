using Xunit;

namespace OdbDesignInfoClient.IntegrationTests;

/// <summary>
/// Placeholder integration tests using TestContainers.
/// These tests will spin up OdbDesignServer in Docker for API contract validation.
/// </summary>
public class ServerIntegrationTests
{
    /// <summary>
    /// Placeholder test - will be implemented when Docker setup is complete.
    /// </summary>
    [Fact(Skip = "Requires OdbDesignServer Docker image")]
    public async Task HealthEndpoint_ReturnsSuccess_WhenServerIsRunning()
    {
        // TODO: Use TestContainers to spin up OdbDesignServer
        // and validate the health endpoint
        await Task.CompletedTask;
    }

    /// <summary>
    /// Placeholder test - will be implemented when Docker setup is complete.
    /// </summary>
    [Fact(Skip = "Requires OdbDesignServer Docker image")]
    public async Task GetDesigns_ReturnsDesignList_WhenDesignsLoaded()
    {
        // TODO: Use TestContainers with a test ODB++ file
        // and validate the designs endpoint
        await Task.CompletedTask;
    }
}
