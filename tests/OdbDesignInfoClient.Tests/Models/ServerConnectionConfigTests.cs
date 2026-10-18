using OdbDesignInfoClient.Core.Models;
using Xunit;

namespace OdbDesignInfoClient.Tests.Models;

/// <summary>
/// Unit tests for ServerConnectionConfig.
/// </summary>
public class ServerConnectionConfigTests
{
    [Fact]
    public void DefaultConfig_HasCorrectDefaultValues()
    {
        // Arrange & Act
        var config = new ServerConnectionConfig();

        // Assert - defaults target the standard deployment:
        // HTTPS REST via ingress on 443, plaintext gRPC on 50051
        Assert.Equal("localhost", config.Host);
        Assert.Equal(443, config.RestPort);
        Assert.Equal(50051, config.GrpcPort);
        Assert.Equal(30, config.TimeoutSeconds);
        Assert.True(config.UseHttps);
        Assert.False(config.GrpcUseTls);
        Assert.Null(config.RestUrlOverride);
        Assert.Null(config.GrpcUrlOverride);
    }

    [Fact]
    public void RestBaseUrl_ReturnsHttpUrl_WhenUseHttpsIsFalse()
    {
        // Arrange
        var config = new ServerConnectionConfig
        {
            Host = "example.com",
            RestPort = 8080,
            UseHttps = false
        };

        // Act
        var url = config.RestBaseUrl;

        // Assert
        Assert.Equal("http://example.com:8080", url);
    }

    [Fact]
    public void GrpcBaseUrl_ReturnsCorrectUrl()
    {
        // Arrange
        var config = new ServerConnectionConfig
        {
            Host = "server.local",
            GrpcPort = 50051,
            UseHttps = false
        };

        // Act
        var url = config.GrpcBaseUrl;

        // Assert
        Assert.Equal("http://server.local:50051", url);
    }

    [Fact]
    public void GrpcBaseUrl_StaysPlaintext_WhenOnlyRestUsesHttps()
    {
        // Arrange - standard deployment: HTTPS REST at the ingress,
        // plaintext h2c gRPC on a separate port
        var config = new ServerConnectionConfig
        {
            Host = "debian13vm.tail11ba79.ts.net",
            RestPort = 443,
            GrpcPort = 50051,
            UseHttps = true,
            GrpcUseTls = false,
        };

        // Act & Assert
        Assert.Equal("https://debian13vm.tail11ba79.ts.net:443", config.RestBaseUrl);
        Assert.Equal("http://debian13vm.tail11ba79.ts.net:50051", config.GrpcBaseUrl);
    }

    [Fact]
    public void GrpcBaseUrl_ReturnsHttpsUrl_WhenGrpcUseTlsIsTrue()
    {
        // Arrange
        var config = new ServerConnectionConfig
        {
            Host = "server.local",
            GrpcPort = 50051,
            GrpcUseTls = true,
        };

        // Act
        var url = config.GrpcBaseUrl;

        // Assert
        Assert.Equal("https://server.local:50051", url);
    }

    [Fact]
    public void UrlOverrides_TakePrecedenceOverHostPortSettings()
    {
        // Arrange - env-var supplied full URLs win over host/port/scheme
        var config = new ServerConnectionConfig
        {
            Host = "ignored.local",
            RestPort = 1234,
            GrpcPort = 5678,
            UseHttps = false,
            RestUrlOverride = "https://override.example.com",
            GrpcUrlOverride = "http://grpc-override.example.com:50051",
        };

        // Act & Assert
        Assert.Equal("https://override.example.com", config.RestBaseUrl);
        Assert.Equal("http://grpc-override.example.com:50051", config.GrpcBaseUrl);
    }

    [Fact]
    public void BaseUri_ReturnsHttpsUri_WhenUseHttpsIsTrue()
    {
        // Arrange
        var config = new ServerConnectionConfig
        {
            Host = "secure.example.com",
            RestPort = 443,
            UseHttps = true
        };

        // Act
        var uri = config.BaseUri;

        // Assert - Default HTTPS port 443 may be omitted by Uri
        Assert.StartsWith("https://secure.example.com", uri.ToString());
    }

    [Theory]
    [InlineData(ConnectionState.Disconnected)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.Connected)]
    [InlineData(ConnectionState.Reconnecting)]
    public void ConnectionState_HasAllExpectedValues(ConnectionState state)
    {
        // Assert
        Assert.True(Enum.IsDefined(typeof(ConnectionState), state));
    }
}
