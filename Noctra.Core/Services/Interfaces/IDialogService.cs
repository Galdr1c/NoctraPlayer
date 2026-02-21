using Noctra.Models;
using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Service for displaying system dialogs, alerts and specific windows
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a simple message dialog with an OK button
    /// </summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>
    /// Shows an error dialog with optional exception details
    /// </summary>
    Task ShowErrorAsync(string title, string message, Exception? ex = null);

    /// <summary>
    /// Shows a confirmation dialog with Yes/No or OK/Cancel options
    /// </summary>
    /// <returns>True if confirmed, false otherwise</returns>
    Task<bool> ShowConfirmationAsync(string title, string message);

    /// <summary>
    /// Shows the premium/upsell window
    /// </summary>
    Task ShowUpsellAsync();

    /// <summary>
    /// Shows the window for adding a new profile
    /// </summary>
    /// <returns>True if a profile was added</returns>
    Task<bool> ShowAddProfileAsync();

    /// <summary>
    /// Shows the window for editing an existing profile
    /// </summary>
    /// <param name="profile">Profile to edit</param>
    /// <returns>True if the profile was updated</returns>
    Task<bool> ShowEditProfileAsync(Profile profile);

    /// <summary>
    /// Shows the global settings window
    /// </summary>
    Task ShowGlobalSettingsAsync();

    /// <summary>
    /// Shows the avatar picker window
    /// </summary>
    /// <param name="currentAvatar">Currently selected avatar</param>
    /// <returns>The path/URL of the selected avatar, or null if cancelled</returns>
    Task<string?> ShowAvatarPickerAsync(string? currentAvatar);
}

