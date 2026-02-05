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
        Assert.Equal(5000, config.Port);
        Assert.Equal(30, config.TimeoutSeconds);
        Assert.False(config.UseHttps);
    }

    [Fact]
    public void BaseUri_ReturnsHttpUri_WhenUseHttpsIsFalse()
    {
        // Arrange
        var config = new ServerConnectionConfig
        {
            Host = "example.com",
            Port = 8080,
            UseHttps = false
        };

        // Act
        var uri = config.BaseUri;

        // Assert
        Assert.Equal("http://example.com:8080/", uri.ToString());
    }

    [Fact]
    public void BaseUri_ReturnsHttpsUri_WhenUseHttpsIsTrue()
    {
        // Arrange
        var config = new ServerConnectionConfig
        {
            Host = "secure.example.com",
            Port = 443,
            UseHttps = true
        };

        // Act
        var uri = config.BaseUri;

        // Assert
        // Note: Default ports (443 for HTTPS, 80 for HTTP) are omitted by UriBuilder
        Assert.Equal("https://secure.example.com/", uri.ToString());
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
