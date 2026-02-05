using System.Net.Http.Headers;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Services.Api;

/// <summary>
/// HTTP message handler that injects Basic Authentication headers into requests.
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly IAuthService _authService;

    /// <summary>
    /// Initializes a new instance of AuthHeaderHandler.
    /// </summary>
    public AuthHeaderHandler(IAuthService authService)
    {
        _authService = authService;
        
        // Ensure there is always a terminal handler in the pipeline when this handler
        // is instantiated directly, while still allowing DI/HttpClientFactory to
        // overwrite InnerHandler when it builds the handler chain.
        if (InnerHandler == null)
        {
            InnerHandler = new HttpClientHandler();
        }
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        if (_authService.IsAuthenticated)
        {
            var credentials = _authService.GetBase64Credentials();
            if (!string.IsNullOrEmpty(credentials))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
