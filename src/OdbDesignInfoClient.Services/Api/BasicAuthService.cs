using System.Text;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Services.Api;

/// <summary>
/// Implementation of Basic Authentication service.
/// </summary>
public class BasicAuthService : IAuthService
{
    private string? _username;
    private string? _password;
    private string? _cachedCredentials;

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
