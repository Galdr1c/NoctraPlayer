using System;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Sistem bilgilerini toplayan ve hata/bug raporu oluşturan servis
/// </summary>
public interface IDiagnosticReportService
{
    /// <summary>
    /// Kullanıcı için boş bir hata raporu mail şablonu açar
    /// </summary>
    void OpenBugReport();

    /// <summary>
    /// Kritik bir uygulama çökmesi (crash) durumunda teknik detayları içeren mail şablonu açar
    /// </summary>
    void OpenCrashReport(Exception ex, string context);
}
