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

        // Assert
        Assert.Equal("localhost", config.Host);
        Assert.Equal(8888, config.RestPort);
        Assert.Equal(50051, config.GrpcPort);
        Assert.Equal(30, config.TimeoutSeconds);
        Assert.False(config.UseHttps);
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
