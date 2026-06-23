using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Desktop implementation that preserves the current shell integration.
/// </summary>
public sealed class DesktopPlatformActionService : IPlatformActionService
{
    public Task<bool> OpenUrlAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(false);
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeMailto))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(StartShell(uri.AbsoluteUri));
    }

    public Task<bool> OpenDirectoryAsync(string? directoryPath, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(directoryPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            Directory.CreateDirectory(directoryPath);
            return Task.FromResult(StartShell(directoryPath));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static bool StartShell(string fileName)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true,
                Verb = "open"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
