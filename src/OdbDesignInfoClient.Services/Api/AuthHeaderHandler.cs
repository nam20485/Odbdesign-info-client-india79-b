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
        
        // InnerHandler must remain null when used with HttpClientFactory.
        // The factory will set up the handler chain automatically.
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
