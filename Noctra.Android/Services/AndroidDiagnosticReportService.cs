using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Noctra.Services;
using Noctra.Services.Interfaces;
using AndroidBuild = global::Android.OS.Build;

namespace Noctra.Android.Services;

/// <summary>
/// Android implementation that opens the user's mail/share client with a
/// pre-filled, editable diagnostic report. Nothing is sent automatically.
/// </summary>
public sealed class AndroidDiagnosticReportService : IDiagnosticReportService
{
    private const string TargetEmail = "kynora.studio@gmail.com";

    private readonly Context _context;
    private readonly AndroidActivityProvider _activityProvider;
    private readonly ILicenseService _licenseService;
    private readonly IAppVersionService _appVersionService;

    public AndroidDiagnosticReportService(
        Context context,
        AndroidActivityProvider activityProvider,
        ILicenseService licenseService,
        IAppVersionService appVersionService)
    {
        _context = context.ApplicationContext ?? context;
        _activityProvider = activityProvider;
        _licenseService = licenseService;
        _appVersionService = appVersionService;
    }

    public void OpenBugReport()
    {
        OpenReport("Noctra Android Bug Report", BuildBugBody());
    }

    public void OpenCrashReport(Exception ex, string context)
    {
        var subject = $"CRASH Android: {ex.GetType().Name} in {context}";
        OpenReport(subject, BuildCrashBody(ex, context));
    }

    private void OpenReport(string subject, string body)
    {
        var activity = _activityProvider.CurrentActivity;
        var startContext = (Context?)activity ?? _context;

        try
        {
            var mailIntent = new Intent(Intent.ActionSendto);
            mailIntent.SetData(global::Android.Net.Uri.Parse("mailto:"));
            mailIntent.PutExtra(Intent.ExtraEmail, new[] { TargetEmail });
            mailIntent.PutExtra(Intent.ExtraSubject, subject);
            mailIntent.PutExtra(Intent.ExtraText, body);

            var chooser = Intent.CreateChooser(mailIntent, "Send Noctra report");
            if (activity is null)
            {
                chooser.AddFlags(ActivityFlags.NewTask);
            }

            startContext.StartActivity(chooser);
        }
        catch
        {
            try
            {
                var shareIntent = new Intent(Intent.ActionSend);
                shareIntent.SetType("message/rfc822");
                shareIntent.PutExtra(Intent.ExtraEmail, new[] { TargetEmail });
                shareIntent.PutExtra(Intent.ExtraSubject, subject);
                shareIntent.PutExtra(Intent.ExtraText, body);

                var chooser = Intent.CreateChooser(shareIntent, "Share Noctra report");
                if (activity is null)
                {
                    chooser.AddFlags(ActivityFlags.NewTask);
                }

                startContext.StartActivity(chooser);
            }
            catch
            {
                // Keep this best-effort: reporting should never crash Settings.
            }
        }
    }

    private string BuildBugBody()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("------");
        sb.AppendLine($"Report Generated (UTC): {DateTime.UtcNow:O}");
        sb.Append(GetSystemDiagnostics());
        return sb.ToString();
    }

    private string BuildCrashBody(Exception ex, string context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Noctra Android encountered an unexpected error.");
        sb.AppendLine("This editable report can help developers fix the issue.");
        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine($"Time: {DateTime.UtcNow:O} (UTC)");
        sb.AppendLine($"Context: {context}");
        sb.AppendLine($"Error: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine();
        sb.AppendLine("--- Stack Trace ---");
        sb.AppendLine(ex.StackTrace);

        if (ex.InnerException is not null)
        {
            sb.AppendLine();
            sb.AppendLine("--- Inner Exception ---");
            sb.AppendLine($"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            sb.AppendLine(ex.InnerException.StackTrace);
        }

        sb.AppendLine();
        sb.AppendLine("------");
        sb.Append(GetSystemDiagnostics());
        return sb.ToString();
    }

    private string GetSystemDiagnostics()
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine($"User ID: {GetDeterministicUserId()}");
            // Güncel lisans durumu (kaynak/bitiş/trial/pending) — masaüstüyle
            // aynı üretilir; yalnızca "Premium/Free" değil.
            sb.Append(DiagnosticLicenseReport.BuildStatus(_licenseService));
            sb.AppendLine($"App Version: {_appVersionService.DisplayVersion}");
            sb.AppendLine($"App Build: {_appVersionService.BuildNumber}");
            sb.AppendLine($"Package: {_context.PackageName}");
            sb.AppendLine($"OS: Android {AndroidBuild.VERSION.Release} API {AndroidBuild.VERSION.SdkInt} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($"Device: {AndroidBuild.Manufacturer} {AndroidBuild.Model}");
            sb.AppendLine($"Locale: {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Diagnostic error: {ex.Message}");
        }

        return sb.ToString();
    }

    private string GetDeterministicUserId()
    {
        try
        {
            var androidId = global::Android.Provider.Settings.Secure.GetString(
                _context.ContentResolver,
                global::Android.Provider.Settings.Secure.AndroidId) ?? string.Empty;
            var input = _context.PackageName + ":" + androidId;
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = MD5.HashData(bytes);
            return new Guid(hash).ToString();
        }
        catch
        {
            return "unknown-android-user";
        }
    }
}
