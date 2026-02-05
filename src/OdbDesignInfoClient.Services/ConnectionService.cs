using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Odb.Grpc;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services.Api;
using Polly;
using Polly.Retry;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Implementation of the connection service with state machine pattern and health monitoring.
/// </summary>
public class ConnectionService : IConnectionService, IDisposable
{
    private readonly ILogger<ConnectionService>? _logger;
    private readonly IOdbDesignRestApi _restApi;
    private readonly IAuthService _authService;
    
    private ConnectionState _state = ConnectionState.Disconnected;
    private ServerConnectionConfig _configuration = new();
    private GrpcChannel? _grpcChannel;
    private OdbDesignService.OdbDesignServiceClient? _grpcClient;
    private CancellationTokenSource? _healthMonitorCts;
    private Task? _healthMonitorTask;
    private bool _grpcAvailable;
    private bool _disposed;

    private readonly AsyncRetryPolicy _retryPolicy;

    /// <inheritdoc />
    public ConnectionState State => _state;

    /// <inheritdoc />
    public ServerConnectionConfig Configuration => _configuration;

    /// <inheritdoc />
    public bool IsGrpcAvailable => _grpcAvailable;

    /// <summary>
    /// Gets the gRPC client for direct access (used by DesignService).
    /// </summary>
    public OdbDesignService.OdbDesignServiceClient? GrpcClient => _grpcClient;

    /// <inheritdoc />
    public event EventHandler<ConnectionState>? StateChanged;

    /// <summary>
    /// Initializes a new instance of the ConnectionService.
    /// </summary>
    public ConnectionService(
        IOdbDesignRestApi restApi,
        IAuthService authService,
        ILogger<ConnectionService>? logger = null)
    {
        _restApi = restApi;
        _authService = authService;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                {
                    var delay = Math.Pow(2, attempt);
                    var maxDelay = 10.0; // Cap at 10 seconds
                    return TimeSpan.FromSeconds(Math.Min(delay, maxDelay));
                },
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger?.LogWarning(exception, 
                        "Connection attempt {RetryCount} failed. Retrying in {Delay}s", 
                        retryCount, timeSpan.TotalSeconds);
                });
    }

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(ServerConnectionConfig config, CancellationToken cancellationToken = default)
    {
        if (_state == ConnectionState.Connected || _state == ConnectionState.Connecting)
        {
            _logger?.LogWarning("Already connected or connecting");
            return _state == ConnectionState.Connected;
        }

        _configuration = config;
        SetState(ConnectionState.Connecting);

        try
        {
            var success = await _retryPolicy.ExecuteAsync(async () =>
            {
                var isHealthy = await CheckHealthAsync(cancellationToken);
                if (!isHealthy)
                {
                    throw new InvalidOperationException("Server health check failed");
                }
                return true;
            });

            if (success)
            {
                await InitializeGrpcAsync(config, cancellationToken);
                SetState(ConnectionState.Connected);
                _logger?.LogInformation("Connected to server at {RestUrl}", config.RestBaseUrl);
                
                StartHealthMonitoring();
                return true;
            }

            SetState(ConnectionState.Disconnected);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to server at {RestUrl}", config.RestBaseUrl);
            SetState(ConnectionState.Disconnected);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        StopHealthMonitoring();
        
        if (_grpcChannel != null)
        {
            await _grpcChannel.ShutdownAsync();
            _grpcChannel.Dispose();
            _grpcChannel = null;
            _grpcClient = null;
        }

        _grpcAvailable = false;
        SetState(ConnectionState.Disconnected);
        _logger?.LogInformation("Disconnected from server");
    }

    /// <inheritdoc />
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _restApi.HealthReadyAsync(cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Health check failed");
            return false;
        }
    }

    private async Task InitializeGrpcAsync(ServerConnectionConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var grpcOptions = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 100 * 1024 * 1024, // 100MB for large designs
                MaxSendMessageSize = 10 * 1024 * 1024
            };

            _grpcChannel = GrpcChannel.ForAddress(config.GrpcBaseUrl, grpcOptions);
            _grpcClient = new OdbDesignService.OdbDesignServiceClient(_grpcChannel);

            var healthRequest = new HealthCheckRequest { Service = "OdbDesignService" };
            var healthResponse = await _grpcClient.HealthCheckAsync(healthRequest, cancellationToken: cancellationToken);
            
            _grpcAvailable = healthResponse.Status == HealthCheckResponse.Types.ServingStatus.Serving;
            _logger?.LogInformation("gRPC connection initialized. Available: {Available}", _grpcAvailable);
        }
        catch (RpcException ex)
        {
            _logger?.LogWarning(ex, "gRPC initialization failed. Falling back to REST-only mode");
            _grpcAvailable = false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "gRPC initialization failed. Falling back to REST-only mode");
            _grpcAvailable = false;
        }
    }

    private void StartHealthMonitoring()
    {
        StopHealthMonitoring();
        
        _healthMonitorCts = new CancellationTokenSource();
        _healthMonitorTask = MonitorHealthAsync(_healthMonitorCts.Token);
    }

    private void StopHealthMonitoring()
    {
        _healthMonitorCts?.Cancel();
        _healthMonitorCts?.Dispose();
        _healthMonitorCts = null;
        _healthMonitorTask = null;
    }

    private async Task MonitorHealthAsync(CancellationToken cancellationToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(5); // Initial delay before first reconnection
        var maxReconnectDelay = TimeSpan.FromSeconds(30);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                if (_state == ConnectionState.Connected)
                {
                    var isHealthy = await CheckHealthAsync(cancellationToken);
                    if (!isHealthy)
                    {
                        _logger?.LogWarning("Health check failed. Attempting to reconnect...");
                        SetState(ConnectionState.Reconnecting);
                        reconnectDelay = TimeSpan.FromSeconds(5); // Reset delay
                    }
                }
                else if (_state == ConnectionState.Reconnecting)
                {
                    // Initial delay before attempting reconnection
                    await Task.Delay(reconnectDelay, cancellationToken);
                    
                    var isHealthy = await CheckHealthAsync(cancellationToken);
                    if (isHealthy)
                    {
                        await InitializeGrpcAsync(_configuration, cancellationToken);
                        SetState(ConnectionState.Connected);
                        _logger?.LogInformation("Reconnected to server");
                        reconnectDelay = TimeSpan.FromSeconds(5);
                    }
                    else
                    {
                        reconnectDelay = TimeSpan.FromSeconds(
                            Math.Min(reconnectDelay.TotalSeconds * 2, maxReconnectDelay.TotalSeconds));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in health monitoring loop");
            }
        }
    }

    private void SetState(ConnectionState newState)
    {
        if (_state != newState)
        {
            var oldState = _state;
            _state = newState;
            _logger?.LogDebug("Connection state changed: {OldState} -> {NewState}", oldState, newState);
            StateChanged?.Invoke(this, newState);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        StopHealthMonitoring();
        _grpcChannel?.Dispose();
        GC.SuppressFinalize(this);
    }
}
