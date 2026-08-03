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
            !IsSupportedUriScheme(uri.Scheme))
        {
            return Task.FromResult(false);
        }

        var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri.AbsoluteUri));
        intent.AddCategory(Intent.CategoryBrowsable);
        return Task.FromResult(TryStartActivity(intent));
    }


    private static bool IsSupportedUriScheme(string scheme) =>
        string.Equals(scheme, System.Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, System.Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, System.Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scheme, "market", StringComparison.OrdinalIgnoreCase);

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
                return Task.FromResult(false);
            }
        }

        // Android 11+ (scoped storage): system file managers cannot browse the
        // app-private directory (Android/data/<package>/files/Download), so there
        // is no reliable way to open the real folder. Report failure instead of
        // silently opening an unrelated surface like the system Downloads screen.
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
