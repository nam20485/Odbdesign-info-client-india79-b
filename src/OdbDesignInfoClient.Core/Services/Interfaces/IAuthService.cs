namespace OdbDesignInfoClient.Core.Services.Interfaces;

/// <summary>
/// Provides authentication services for API requests.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Gets whether authentication credentials are configured.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the username.
    /// </summary>
    string? Username { get; }

    /// <summary>
    /// Gets the Base64 encoded credentials (username:password).
    /// </summary>
    string? GetBase64Credentials();

    /// <summary>
    /// Sets the authentication credentials.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    void SetCredentials(string username, string password);

    /// <summary>
    /// Clears the authentication credentials.
    /// </summary>
    void ClearCredentials();
}
