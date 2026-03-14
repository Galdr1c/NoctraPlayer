using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class DiagnosticReportService : IDiagnosticReportService
{
    private const string TargetEmail = "support@noctra.app";
    private readonly ILicenseService _licenseService;

    public DiagnosticReportService(ILicenseService licenseService)
    {
        _licenseService = licenseService;
    }

    public void OpenBugReport()
    {
        SendMail("Noctra Bug Report", BuildBugBody());
    }

    public void OpenCrashReport(Exception ex, string context)
    {
        var subject = $"CRASH: {ex.GetType().Name} in {context}";
        SendMail(subject, BuildCrashBody(ex, context));
    }

    private void SendMail(string subject, string body)
    {
        try
        {
            // mailto URI encoding refinement: Use %20 for spaces instead of +
            string escapedSubject = Uri.EscapeDataString(subject);
            string escapedBody = Uri.EscapeDataString(body);
            
            var url = $"mailto:{TargetEmail}?subject={escapedSubject}&body={escapedBody}";
            
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private string BuildBugBody()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("------");
        sb.Append(GetSystemDiagnostics());
        return sb.ToString();
    }

    private string BuildCrashBody(Exception ex, string context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("We're sorry, Noctra encountered an unexpected error.");
        sb.AppendLine("This report will help developers fix the issue.");
        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine($"Time: {DateTime.UtcNow} (UTC)");
        sb.AppendLine($"Context: {context}");
        sb.AppendLine($"Error: {ex.GetType().FullName}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine();
        sb.AppendLine("--- Stack Trace ---");
        sb.AppendLine(ex.StackTrace);

        if (ex.InnerException != null)
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
            var appVersion = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0";
            
            sb.AppendLine($"User ID: {GetDeterministicUserId()}");
            sb.AppendLine($"Status: {(_licenseService.IsPremium ? "Premium" : "Free")}");
            sb.AppendLine($"App Version: {appVersion}");
            sb.AppendLine($"OS: {GetFriendlyOSName()} ({RuntimeInformation.OSArchitecture})");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Diagnostic error: {ex.Message}");
        }
        return sb.ToString();
    }

    private string GetFriendlyOSName()
    {
        var desc = RuntimeInformation.OSDescription;
        if (desc.Contains("Windows"))
        {
            var parts = desc.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var version = parts.LastOrDefault();
            if (version != null && version.Contains('.'))
            {
                var build = version.Split('.').Last();
                bool isWin11 = int.TryParse(build, out int b) && b >= 22000;
                return $"Windows {(isWin11 ? "11" : "10")} Build {build}";
            }
        }
        return desc;
    }

    private string GetDeterministicUserId()
    {
        try
        {
            var input = Environment.MachineName + Environment.UserName;
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = MD5.HashData(bytes);
            return new Guid(hash).ToString();
        }
        catch
        {
            return "unknown-user";
        }
    }
}
