using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Android-safe replacement for desktop shell Process.Start operations.
/// Browser/file-manager actions are routed through intents and never throw back
/// into the ViewModel if the device has no matching app.
/// </summary>
public sealed class AndroidPlatformActionService : IPlatformActionService
{
    private const string ActionViewDownloads = "android.intent.action.VIEW_DOWNLOADS";

    private readonly Context _context;
    private readonly AndroidActivityProvider _activityProvider;

    public AndroidPlatformActionService(Context context, AndroidActivityProvider activityProvider)
    {
        _context = context.ApplicationContext ?? context;
        _activityProvider = activityProvider;
    }

    public Task<bool> OpenUrlAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(false);
        }

        if (!System.Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != System.Uri.UriSchemeHttp && uri.Scheme != System.Uri.UriSchemeHttps && uri.Scheme != System.Uri.UriSchemeMailto))
        {
            return Task.FromResult(false);
        }

        var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri.AbsoluteUri));
        intent.AddCategory(Intent.CategoryBrowsable);
        return Task.FromResult(TryStartActivity(intent));
    }

    public Task<bool> OpenDirectoryAsync(string? directoryPath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            try
            {
                Directory.CreateDirectory(directoryPath);
            }
            catch
            {
                // The button should still try to open the system downloads UI.
            }
        }

        // Most Android file managers understand this system action and will open
        // the user's Downloads surface without exposing file:// paths.
        if (TryStartActivity(new Intent(ActionViewDownloads)))
        {
            return Task.FromResult(true);
        }

        // Fallback: let the user pick/open a directory in the Android documents UI.
        if (OperatingSystem.IsAndroidVersionAtLeast(21))
        {
            var treeIntent = new Intent(Intent.ActionOpenDocumentTree);
            treeIntent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
            if (TryStartActivity(treeIntent))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    private bool TryStartActivity(Intent intent)
    {
        try
        {
            var activity = _activityProvider.CurrentActivity;
            var startContext = (Context?)activity ?? _context;
            if (activity is null)
            {
                intent.AddFlags(ActivityFlags.NewTask);
            }

            startContext.StartActivity(intent);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
