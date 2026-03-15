using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Noctra.Avalonia.Behaviors;

/// <summary>
/// Bağlı olduğu kontrolün içeriği değiştiğinde, yön bilgisine göre
/// sağdan-sola veya soldan-sağa kayma + opacity animasyonu uygular.
/// Kullanım: behaviors:SlideTransitionBehavior.TriggerValue="{Binding SelectedSeason}"
/// </summary>
public class SlideTransitionBehavior : AvaloniaObject
{
    // ── Public Attached Property ────────────────────────────────────────────
    public static readonly AttachedProperty<object?> TriggerValueProperty =
        AvaloniaProperty.RegisterAttached<SlideTransitionBehavior, Control, object?>("TriggerValue");

    public static object? GetTriggerValue(Control element) => element.GetValue(TriggerValueProperty);
    public static void SetTriggerValue(Control element, object? value) => element.SetValue(TriggerValueProperty, value);

    // ── Private Attached Property (animasyon iptali için) ───────────────────
    private static readonly AttachedProperty<CancellationTokenSource?> CtsProperty =
        AvaloniaProperty.RegisterAttached<SlideTransitionBehavior, Control, CancellationTokenSource?>("_cts");

    // ── Static Ctor ─────────────────────────────────────────────────────────
    static SlideTransitionBehavior()
    {
        TriggerValueProperty.Changed.AddClassHandler<Control>(OnTriggerValueChanged);
    }

    // ── Sezon numarası çıkar (reflection-free, interface tabanlı) ───────────
    private static int GetSeasonOrder(object? obj)
    {
        if (obj is null) return 0;

        // Önce hızlı yol: SeasonNumber property var mı?
        var prop = obj.GetType().GetProperty("SeasonNumber");
        if (prop?.PropertyType == typeof(int))
            return (int)(prop.GetValue(obj) ?? 0);

        return 0;
    }

    // ── Ana handler ─────────────────────────────────────────────────────────
    private static void OnTriggerValueChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // Aynı değer — animasyon yok
        if (Equals(e.OldValue, e.NewValue)) return;

        // Yeni değer null ise temizle, animasyon yapma
        if (e.NewValue is null)
        {
            ResetTransform(control);
            return;
        }

        // Çalışan animasyonu iptal et
        var prev = control.GetValue(CtsProperty);
        prev?.Cancel();
        prev?.Dispose();

        var cts = new CancellationTokenSource();
        control.SetValue(CtsProperty, cts);

        // Yön: yeni sezon eskisinin sağındaysa içerik sağdan (pozitif X) gelir
        int oldOrder = GetSeasonOrder(e.OldValue);
        int newOrder = GetSeasonOrder(e.NewValue);

        // İlk açılışta veya bilinmeyen yönde hafif bir fade-in
        double slideDistance = e.OldValue is null ? 12.0 : 32.0; // Mesafeyi TabSlideTransitionBehavior ile eşitledik (32.0)
        double startX = (e.OldValue is null || newOrder >= oldOrder)
            ? slideDistance    // sağdan geliyor → sola kayar
            : -slideDistance;  // soldan geliyor → sağa kayar

        // TranslateTransform'u her seferinde temiz oluştur
        // (TransformGroup içine KOYMUYORUZ — Avalonia setter'ları bunu hedefleyemez)
        var translate = new TranslateTransform { X = startX };
        control.RenderTransform = translate;
        control.Opacity = 0;

        // ── Animasyon tanımı ────────────────────────────────────────────────
        // Easing: iOS/Apple TV tarzı — hızlı giriş, çok yumuşak çıkış
        var animation = new Animation
        {
            Duration    = TimeSpan.FromMilliseconds(320), // Süreyi TabSlideTransitionBehavior ile eşitledik (320ms)
            Easing      = new SplineEasing(0.25, 1.0, 0.3, 1.0),
            FillMode    = FillMode.Forward,
            Children    =
            {
                new KeyFrame
                {
                    Cue     = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty,       0.0d),
                        new Setter(TranslateTransform.XProperty, startX),
                    }
                },
                // %35'te opacity tamamlanır — içerik çabuk "snap" eder,
                // kalan sürede sadece kayma usulca biter
                new KeyFrame
                {
                    Cue     = new Cue(0.35d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1.0d),
                    }
                },
                new KeyFrame
                {
                    Cue     = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty,       1.0d),
                        new Setter(TranslateTransform.XProperty, 0.0d),
                    }
                }
            }
        };

        _ = RunAnimationAsync(animation, control, cts.Token);
    }

    private static async System.Threading.Tasks.Task RunAnimationAsync(
        Animation animation, Control control, CancellationToken token)
    {
        try
        {
            await animation.RunAsync(control, token);

            // Animasyon iptal edilmemişse son durumu kalıcı hale getir
            if (!token.IsCancellationRequested)
                ResetTransform(control);
        }
        catch (OperationCanceledException)
        {
            // Yeni animasyon başladı, eski state temizlenecek — ignore
        }
    }

    private static void ResetTransform(Control control)
    {
        control.Opacity         = 1.0;
        control.RenderTransform = null;
    }
}