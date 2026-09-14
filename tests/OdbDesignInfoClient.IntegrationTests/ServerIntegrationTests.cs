using System.Net;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services;
using OdbDesignInfoClient.Services.Api;
using Refit;
using Xunit;
using Xunit.Abstractions;

namespace OdbDesignInfoClient.IntegrationTests;

/// <summary>
/// Live integration tests against the deployed OdbDesignServer.
/// These exercise the production pipeline (env credentials → BasicAuthService →
/// AuthHeaderHandler → Refit → DesignService parsing) end to end.
/// They run only when credentials are available via ODBDESIGN_REST_USERNAME /
/// ODBDESIGN_REST_PASSWORD (or the legacy ODB_AUTH_* names) and the server is
/// reachable; otherwise each test returns early and reports why.
/// Override the target with ODBDESIGN_REST_URL (default: the standard deployment).
/// Docker/TestContainers-based contract tests remain a planned addition.
/// </summary>
public class ServerIntegrationTests
{
    private const string DefaultBaseUrl = "https://debian13vm.tail11ba79.ts.net";
    private const string DefaultGrpcUrl = "http://debian13vm.tail11ba79.ts.net:50051";

    private readonly ITestOutputHelper _output;
    private readonly string _baseUrl;
    private readonly string _grpcUrl;

    public ServerIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _baseUrl = Environment.GetEnvironmentVariable("ODBDESIGN_REST_URL") ?? DefaultBaseUrl;
        _grpcUrl = Environment.GetEnvironmentVariable("ODBDESIGN_GRPC_URL") ?? DefaultGrpcUrl;
    }

    private bool TryCreateClient(out DesignService designService, out string skipReason, bool withCredentials = true)
    {
        designService = null!;
        skipReason = string.Empty;

        if (!withCredentials)
        {
            // BasicAuthService reads env credentials in its constructor, so a truly
            // credential-free client needs an auth service with nothing configured
            designService = CreateDesignService(new EmptyAuthService());
            return true;
        }

        var auth = new BasicAuthService();
        if (!auth.IsAuthenticated)
        {
            skipReason = $"No credentials found. Set ODBDESIGN_REST_USERNAME / ODBDESIGN_REST_PASSWORD (or ODB_AUTH_USERNAME / ODB_AUTH_PASSWORD).";
            return false;
        }

        designService = CreateDesignService(auth);
        return true;
    }

    private DesignService CreateDesignService(IAuthService auth)
    {
        var handler = new AuthHeaderHandler(auth)
        {
            InnerHandler = new SocketsHttpHandler(),
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        var restApi = RestService.For<IOdbDesignRestApi>(httpClient);
        return new DesignService(new NoGrpcConnectionService(), restApi);
    }

    [Fact]
    public async Task Live_GetDesigns_ReturnsLoadedDesignsWithSteps()
    {
        if (!TryCreateClient(out var sut, out var skip))
        {
            _output.WriteLine($"SKIPPED: {skip}");
            return;
        }

        var designs = await sut.GetDesignsAsync();

        _output.WriteLine($"Designs loaded: {designs.Count} ({string.Join(", ", designs.Select(d => d.Name))})");
        Assert.NotEmpty(designs);
        var first = designs[0];
        Assert.False(string.IsNullOrEmpty(first.Id));
        Assert.NotEmpty(first.Steps);
        _output.WriteLine($"First design '{first.Name}' steps: {string.Join(", ", first.Steps)}");
    }

    [Fact]
    public async Task Live_GetComponents_ReturnsComponentsForLoadedDesign()
    {
        if (!TryCreateClient(out var sut, out var skip))
        {
            _output.WriteLine($"SKIPPED: {skip}");
            return;
        }

        var designs = await sut.GetDesignsAsync();
        var design = designs.FirstOrDefault(d => d.Name == "sample_design") ?? designs.FirstOrDefault();
        Assert.NotNull(design);

        var components = await sut.GetComponentsAsync(design!.Id, design.Steps[0]);

        _output.WriteLine($"Design '{design.Id}': {components.Count} components");
        Assert.NotEmpty(components);
        Assert.All(components.Take(5), c => Assert.False(string.IsNullOrEmpty(c.RefDes)));
        _output.WriteLine($"Sample RefDes: {string.Join(", ", components.Take(5).Select(c => c.RefDes))}");
    }

    [Fact]
    public async Task Live_GetStackup_ReturnsMatrixLayersWithAuthoritativeTypes()
    {
        if (!TryCreateClient(out var sut, out var skip))
        {
            _output.WriteLine($"SKIPPED: {skip}");
            return;
        }

        var designs = await sut.GetDesignsAsync();
        var design = designs.FirstOrDefault(d => d.Name == "sample_design") ?? designs.FirstOrDefault();
        Assert.NotNull(design);

        // The server's /matrix/matrix projection gives real layer types, stack order, and
        // display colors — this validates the authoritative parse end to end.
        var layers = await sut.GetStackupAsync(design!.Id, design.Steps[0]);

        _output.WriteLine($"Design '{design.Id}': {layers.Count} layers");
        Assert.NotEmpty(layers);
        Assert.All(layers, l => Assert.False(string.IsNullOrEmpty(l.Name)));

        // Types come from the server, not name heuristics: sample_design has Dielectric + Drill rows.
        Assert.Contains(layers, l => l.Type == "Dielectric");
        Assert.Contains(layers, l => l.Type == "Drill");

        // Stack order is 1-based and monotonic (top to bottom).
        Assert.Equal(1, layers.Min(l => l.StackOrder));
        Assert.True(layers.Max(l => l.StackOrder) >= layers.Count / 2);

        // Dielectric layers carry a real server color (not a type default).
        var dielectric = layers.First(l => l.Type == "Dielectric");
        Assert.StartsWith("#", dielectric.ColorHex);

        // A drill layer carries its span boundaries.
        var drill = layers.First(l => l.Type == "Drill");
        Assert.False(string.IsNullOrEmpty(drill.StartLayer));
        Assert.False(string.IsNullOrEmpty(drill.EndLayer));

        _output.WriteLine(
            $"Top 8: {string.Join(", ", layers.Take(8).Select(l => $"{l.StackOrder}:{l.Name}({l.Type})"))}");
        _output.WriteLine($"Drill span: {drill.StartLayer} → {drill.EndLayer}; dielectric color {dielectric.ColorHex}");
    }


    [Fact]
    public async Task Live_GetNets_ReturnsNetsForLoadedDesign()
    {
        if (!TryCreateClient(out var sut, out var skip))
        {
            _output.WriteLine($"SKIPPED: {skip}");
            return;
        }

        var designs = await sut.GetDesignsAsync();
        var design = designs.FirstOrDefault(d => d.Name == "sample_design") ?? designs.FirstOrDefault();
        Assert.NotNull(design);

        var nets = await sut.GetNetsAsync(design!.Id, design.Steps[0]);

        _output.WriteLine($"Design '{design.Id}': {nets.Count} nets");
        Assert.NotEmpty(nets);
        _output.WriteLine($"Sample nets: {string.Join(", ", nets.Take(5).Select(n => n.Name))}");
    }

    [Fact]
    public async Task Live_UnauthenticatedRequest_SurfacesUnauthorizedError()
    {
        if (!TryCreateClient(out var sut, out var skip, withCredentials: false))
        {
            _output.WriteLine($"SKIPPED: {skip}");
            return;
        }

        // Without credentials the server returns 401, which the service
        // must translate into a clear UnauthorizedAccessException
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.GetComponentsAsync("sample_design", "step"));

        _output.WriteLine($"Received expected auth error: {ex.Message}");
    }

    [Fact]
    public async Task Live_GrpcProductModel_PopulatesComponentsPackagesPartsAndDrills()
    {
        var (sut, skip) = await TryCreateGrpcClientAsync();
        if (sut is null)
        {
            _output.WriteLine($"SKIPPED: {skip}");
            return;
        }

        // One gRPC GetDesign (normalized lists pruned) → the shared ProductModelReader
        // reconstructs components/nets/pins/packages/parts/drill from the FileModel.
        var comps = await sut.GetComponentsAsync("sample_design", "step");
        var pkgs = await sut.GetPackagesAsync("sample_design", "step");
        var parts = await sut.GetPartsAsync("sample_design", "step");
        var drills = await sut.GetDrillToolsAsync("sample_design", "step");

        _output.WriteLine(
            $"gRPC product model: components={comps.Count} packages={pkgs.Count} parts={parts.Count} drills={drills.Count}");

        Assert.True(comps.Count > 100, $"expected the full component list, got {comps.Count}");
        // Real placement comes from the FileModel (the REST projection has X/Y = 0).
        Assert.Contains(comps, c => c.X != 0 || c.Y != 0);
        Assert.Contains(comps, c => c.Pins.Count > 0);
        Assert.NotEmpty(pkgs);
        Assert.NotEmpty(parts);
        Assert.Contains(parts, p => p.UsageCount > 0);
        Assert.NotEmpty(drills);

        var sample = comps.First(c => c.Pins.Count > 0);
        _output.WriteLine(
            $"sample component {sample.RefDes}: part={sample.PartName} pkg={sample.Package} " +
            $"x={sample.X:F3} y={sample.Y:F3} rot={sample.Rotation} pins={sample.Pins.Count}");
    }

    /// <summary>
    /// Builds a DesignService backed by a real gRPC connection (REST health check + gRPC
    /// channel), or returns a skip reason when credentials/gRPC are unavailable.
    /// </summary>
    private async Task<(DesignService? Service, string Skip)> TryCreateGrpcClientAsync()
    {
        var auth = new BasicAuthService();
        if (!auth.IsAuthenticated)
        {
            return (null, "No credentials found (set ODBDESIGN_REST_USERNAME / ODBDESIGN_REST_PASSWORD).");
        }

        var handler = new AuthHeaderHandler(auth) { InnerHandler = new SocketsHttpHandler() };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        var restApi = RestService.For<IOdbDesignRestApi>(httpClient);

        var connection = new ConnectionService(restApi, auth);
        var config = new ServerConnectionConfig
        {
            Host = new Uri(_baseUrl).Host,
            RestUrlOverride = _baseUrl,
            GrpcUrlOverride = _grpcUrl,
            GrpcUseTls = _grpcUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase),
        };

        var connected = await connection.ConnectAsync(config);
        if (!connected || !connection.IsGrpcAvailable)
        {
            return (null, $"gRPC unavailable (connected={connected}, grpc={connection.IsGrpcAvailable}) at {_grpcUrl}");
        }

        return (new DesignService(connection, restApi), string.Empty);
    }

    /// <summary>
    /// Minimal IConnectionService stand-in that reports gRPC unavailable,
    /// forcing the REST code path (what these tests exercise).
    /// </summary>
    private sealed class NoGrpcConnectionService : IConnectionService
    {
        public ConnectionState State => ConnectionState.Connected;

        public ServerConnectionConfig Configuration { get; } = new();

        public bool IsGrpcAvailable => false;

        public event EventHandler<ConnectionState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task<bool> ConnectAsync(ServerConnectionConfig config, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    /// <summary>
    /// Auth service with no credentials configured, for testing unauthenticated requests.
    /// </summary>
    private sealed class EmptyAuthService : IAuthService
    {
        public bool IsAuthenticated => false;

        public string? Username => null;

        public string? GetBase64Credentials() => null;

        public void SetCredentials(string username, string password)
        {
        }

        public void ClearCredentials()
        {
        }
    }
}
