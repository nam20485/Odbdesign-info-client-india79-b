using System;
using System.Text;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Services.Api;

/// <summary>
/// Implementation of Basic Authentication service.
/// WARNING: Credentials are stored in plain text in memory. For production applications,
/// consider using secure credential storage mechanisms provided by the operating system:
/// - Windows: Windows Credential Manager
/// - macOS: Keychain
/// - Linux: Secret Service API / gnome-keyring
/// 
/// Credentials can be provided via:
/// 1. Environment Variables: ODB_AUTH_USERNAME and ODB_AUTH_PASSWORD (recommended for production)
/// 2. Programmatically via SetCredentials() method
/// 3. User input at runtime
/// </summary>
public class BasicAuthService : IAuthService
{
    private string? _username; // Stored in plain text - not encrypted at rest
    private string? _password; // Stored in plain text - not encrypted at rest
    private string? _cachedCredentials;

    /// <summary>
    /// Initializes a new instance of BasicAuthService.
    /// Attempts to load credentials from environment variables on construction.
    /// </summary>
    public BasicAuthService()
    {
        // Try to load from environment variables on startup
        var envUsername = Environment.GetEnvironmentVariable("ODB_AUTH_USERNAME");
        var envPassword = Environment.GetEnvironmentVariable("ODB_AUTH_PASSWORD");
        
        if (!string.IsNullOrEmpty(envUsername) && !string.IsNullOrEmpty(envPassword))
        {
            SetCredentials(envUsername, envPassword);
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => !string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password);

    /// <inheritdoc />
    public string? Username => _username;

    /// <inheritdoc />
    public string? GetBase64Credentials()
    {
        return _cachedCredentials;
    }

    /// <inheritdoc />
    public void SetCredentials(string username, string password)
    {
        _username = username;
        _password = password;
        _cachedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
    }

    /// <inheritdoc />
    public void ClearCredentials()
    {
        _username = null;
        _password = null;
        _cachedCredentials = null;
    }
}
