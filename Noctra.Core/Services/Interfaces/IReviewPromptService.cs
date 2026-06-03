namespace Noctra.Services.Interfaces;

public interface IReviewPromptService
{
    Task TryShowMainWindowPromptAsync(CancellationToken cancellationToken = default);
}
