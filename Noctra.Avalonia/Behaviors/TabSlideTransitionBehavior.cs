using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Noctra.Avalonia.Behaviors;

/// <summary>
/// TabControl'e attach edilir. Sekme değiştiğinde aktif içeriği
/// soldan veya sağdan kayarak getirir — yön, sekme indeksine göre belirlenir.
///
/// Kullanım (SettingsWindow.axaml):
///   <TabControl behaviors:TabSlideTransitionBehavior.IsEnabled="True" ...>
/// </summary>
public static class TabSlideTransitionBehavior
{
    // ── Public Attached Property ─────────────────────────────────────────────
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TabControl, bool>(
            "IsEnabled",
            typeof(TabSlideTransitionBehavior));

    public static bool GetIsEnabled(TabControl element) =>
        element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TabControl element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    // ── Private state ────────────────────────────────────────────────────────
    private static readonly AttachedProperty<int> PreviousIndexProperty =
        AvaloniaProperty.RegisterAttached<TabControl, int>(
            "_prevIdx",
            typeof(TabSlideTransitionBehavior),
            defaultValue: -1);

    private static readonly AttachedProperty<CancellationTokenSource?> CtsProperty =
        AvaloniaProperty.RegisterAttached<TabControl, CancellationTokenSource?>(
            "_cts",
            typeof(TabSlideTransitionBehavior));

    // ── Static ctor — property change handler ───────────────────────────────
    static TabSlideTransitionBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<TabControl>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(TabControl tabControl, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            tabControl.SelectionChanged += OnSelectionChanged;
        else
            tabControl.SelectionChanged -= OnSelectionChanged;
    }

    // ── Selection changed ────────────────────────────────────────────────────
    private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        int newIndex = tabControl.SelectedIndex;
        int oldIndex = tabControl.GetValue(PreviousIndexProperty);

        // Başlangıç — ilk yüklemede animasyon yok
        if (oldIndex == -1)
        {
            tabControl.SetValue(PreviousIndexProperty, newIndex);
            return;
        }

        tabControl.SetValue(PreviousIndexProperty, newIndex);

        // İçerik presenter'ı bul
        var presenter = FindContentPresenter(tabControl);
        if (presenter == null) return;

        // Mevcut animasyonu iptal et
        var prev = tabControl.GetValue(CtsProperty);
        prev?.Cancel();
        prev?.Dispose();

        var cts = new CancellationTokenSource();
        tabControl.SetValue(CtsProperty, cts);

        // Yön: yeni sekme eskisinin sağındaysa → içerik sağdan gelir
        double slideDistance = 32.0;
        double startX = newIndex > oldIndex ? slideDistance : -slideDistance;

        // TranslateTransform — temiz, sadece X ekseninde
        presenter.RenderTransform = new TranslateTransform { X = startX };
        presenter.Opacity = 0;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(320),
            Easing   = new SplineEasing(0.25, 1.0, 0.3, 1.0), // iOS tarzı easing
            FillMode = FillMode.Forward,
            Children =
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

        _ = RunAsync(animation, presenter, cts.Token);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Avalonia TabControl şablonundaki aktif içerik presenter'ını bulur.
    /// Yanlışlıkla TabItem başlıklarını (Header) bulmaması için sadece "PART_SelectedContentHost"
    /// isimli ana içerik çerçevesini arar.
    /// </summary>
    private static Control? FindContentPresenter(TabControl tabControl)
    {
        foreach (var child in tabControl.GetVisualDescendants())
        {
            if (child is ContentPresenter cp && cp.Name == "PART_SelectedContentHost")
            {
                return cp;
            }
            // Bazı özel temalarda Carousel kullanılabilir
            else if (child is Carousel carousel)
            {
                return carousel;
            }
        }

        return null;
    }

    private static async System.Threading.Tasks.Task RunAsync(
        Animation animation, Control target, CancellationToken token)
    {
        try
        {
            await animation.RunAsync(target, token);

            if (!token.IsCancellationRequested)
            {
                target.Opacity         = 1.0;
                target.RenderTransform = null;
            }
        }
        catch (OperationCanceledException) { /* yeni animasyon başladı */ }
    }
}
