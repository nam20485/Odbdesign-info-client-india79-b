using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Implementation of cross-probing service using Named Pipes.
/// </summary>
public class CrossProbeService : ICrossProbeService, IDisposable
{
    private const string PipeName = "OdbDesignViewerPipe";
    
    private readonly ILogger<CrossProbeService>? _logger;
    private NamedPipeClientStream? _pipeClient;
    private CancellationTokenSource? _listenerCts;
    private bool _isConnected;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <inheritdoc />
    public event EventHandler<bool>? ConnectionChanged;

    /// <inheritdoc />
    public event EventHandler<CrossProbeEventArgs>? SelectionReceived;

    /// <summary>
    /// Initializes a new instance of the CrossProbeService.
    /// </summary>
    public CrossProbeService(ILogger<CrossProbeService>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            
            await _pipeClient.ConnectAsync(5000, cancellationToken);
            
            _isConnected = true;
            ConnectionChanged?.Invoke(this, true);
            
            // Start listening for incoming messages
            _listenerCts = new CancellationTokenSource();
            _ = ListenForMessagesAsync(_listenerCts.Token);
            
            _logger?.LogInformation("Connected to 3D viewer via Named Pipe");
            return true;
        }
        catch (TimeoutException)
        {
            _logger?.LogWarning("Timeout connecting to 3D viewer");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to 3D viewer");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        _listenerCts?.Cancel();
        
        if (_pipeClient != null)
        {
            await _pipeClient.DisposeAsync();
            _pipeClient = null;
        }
        
        _isConnected = false;
        ConnectionChanged?.Invoke(this, false);
        _logger?.LogInformation("Disconnected from 3D viewer");
    }

    /// <inheritdoc />
    public async Task SelectAsync(string entityType, string entityId, bool zoomToFit = true, CancellationToken cancellationToken = default)
    {
        var message = new
        {
            action = "select",
            entity_type = entityType,
            entity_id = entityId,
            zoom_to_fit = zoomToFit
        };

        await SendMessageAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public async Task HighlightNetAsync(string netName, string? color = null, CancellationToken cancellationToken = default)
    {
        var message = new
        {
            action = "highlight",
            net_name = netName,
            color = color ?? "#FF0000"
        };

        await SendMessageAsync(message, cancellationToken);
    }

    private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
    {
        if (_pipeClient == null || !_pipeClient.IsConnected)
        {
            _logger?.LogWarning("Cannot send message - pipe not connected");
            return;
        }

        var json = JsonSerializer.Serialize(message);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json + "\n");
        
        await _pipeClient.WriteAsync(bytes, cancellationToken);
        await _pipeClient.FlushAsync(cancellationToken);
        
        _logger?.LogDebug("Sent cross-probe message: {Message}", json);
    }

    private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
    {
        if (_pipeClient == null) return;

        var buffer = new byte[4096];
        var reader = new StreamReader(_pipeClient);

        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipeClient.IsConnected)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line != null)
                {
                    ProcessIncomingMessage(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error listening for cross-probe messages");
        }
    }

    private void ProcessIncomingMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var eventArgs = new CrossProbeEventArgs
            {
                EventType = root.TryGetProperty("event", out var evt) ? evt.GetString() ?? "" : "",
                EntityType = root.TryGetProperty("entity_type", out var et) ? et.GetString() ?? "" : "",
                EntityId = root.TryGetProperty("entity_id", out var ei) ? ei.GetString() ?? "" : ""
            };

            SelectionReceived?.Invoke(this, eventArgs);
            _logger?.LogDebug("Received cross-probe event: {EventType} {EntityType} {EntityId}", 
                eventArgs.EventType, eventArgs.EntityType, eventArgs.EntityId);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse cross-probe message: {Message}", json);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _pipeClient?.Dispose();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
