using Noctra.Models;
using System.Threading.Tasks;

namespace Noctra.Services.Interfaces;

public interface IDialogService
{
    Task ShowMessageAsync(string title, string message);
    Task ShowErrorAsync(string title, string message, Exception? ex = null);
    Task<bool> ShowConfirmationAsync(string title, string message);
    Task ShowUpsellAsync();
    Task<bool> ShowAddProfileAsync();
    Task<bool> ShowEditProfileAsync(Profile profile);
}

