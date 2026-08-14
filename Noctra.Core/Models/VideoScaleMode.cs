namespace Noctra.Models;

/// <summary>
/// Video ölçekleme modu. Anlamlar endüstri standardına uyacak şekilde:
/// <list type="bullet">
/// <item><b>Fit</b> — en-boy oranını korur, videoyu görünüme sığdırır (letterbox).</item>
/// <item><b>Fill</b> — en-boy oranını korur, görünümü tamamen kaplar (center-crop/cover).</item>
/// <item><b>Stretch</b> — oranı yok sayar, videoyu görünüme gerer (fill).</item>
/// </list>
/// </summary>
public enum VideoScaleMode
{
    Fit,
    Fill,
    Stretch
}
