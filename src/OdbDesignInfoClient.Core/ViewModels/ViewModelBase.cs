using CommunityToolkit.Mvvm.ComponentModel;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// Base class for all ViewModels.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Gets whether there is an error.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Clears the current error message.
    /// </summary>
    protected void ClearError()
    {
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Sets an error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    protected void SetError(string message)
    {
        ErrorMessage = message;
    }
}
