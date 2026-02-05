using Microsoft.Extensions.Logging;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Implementation of the connection service.
/// </summary>
public class ConnectionService : IConnectionService
{
    private readonly ILogger<ConnectionService>? _logger;
    private readonly HttpClient _httpClient;
    private ConnectionState _state = ConnectionState.Disconnected;
    private ServerConnectionConfig _configuration = new();

    /// <inheritdoc />
    public ConnectionState State => _state;

    /// <inheritdoc />
    public ServerConnectionConfig Configuration => _configuration;

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? StateChanged;

    /// <summary>
    /// Initializes a new instance of the ConnectionService.
    /// </summary>
    public ConnectionService(ILogger<ConnectionService>? logger = null)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(ServerConnectionConfig config, CancellationToken cancellationToken = default)
    {
        _configuration = config;
        SetState(ConnectionState.Connecting);

        try
        {
            _httpClient.BaseAddress = config.BaseUri;
            _httpClient.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

            var isHealthy = await CheckHealthAsync(cancellationToken);
            if (isHealthy)
            {
                SetState(ConnectionState.Connected);
                _logger?.LogInformation("Connected to server at {BaseUri}", config.BaseUri);
                return true;
            }

            SetState(ConnectionState.Disconnected);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to server at {BaseUri}", config.BaseUri);
            SetState(ConnectionState.Disconnected);
            return false;
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        SetState(ConnectionState.Disconnected);
        _logger?.LogInformation("Disconnected from server");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Health check failed");
            return false;
        }
    }

    private void SetState(ConnectionState newState)
    {
        if (_state != newState)
        {
            _state = newState;
            StateChanged?.Invoke(this, newState);
        }
    }
}
