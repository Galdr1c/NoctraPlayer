// ============================================================
// Noctra.Tests/OverlayFocusControllerTests.cs
//
// OverlayFocusController için kapsamlı senaryo testleri.
// Tüm Win32/Avalonia bağımlılıkları delegate ile simüle edilir.
// Headless ortamda çalışır, UI thread gerektirmez.
//
// TEST GRUPLARI:
//   A. Başlangıç durumu                              (3 test)
//   B. Temel show/hide davranışı                     (6 test)
//   C. Odak kaybı (Alt-Tab) senaryoları              (7 test)
//   D. KRİTİK REGRESSION: Kalıcı kaybolma bug'ı      (6 test)
//   E. Layout geçiş senaryoları                      (7 test)
//   F. Topmost yönetimi                              (6 test)
//   G. Çift çağrı koruması (idempotency)             (5 test)
//   H. Hızlı ardışık geçişler (rapid-fire)           (5 test)
//   I. Kombine senaryo (alt-tab + layout + rapid)    (5 test)
//   J. LAYOUT FIX: Timer durunca anlık gizleme       (6 test)
//
// TOPLAM: 56 test
// ============================================================

using Xunit;
using System.Collections.Generic;
using System.Linq;
using Noctra.Avalonia.Controls;

namespace Noctra.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Test Altyapısı
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// OverlayFocusController'ın tüm dış bağımlılıklarını simüle eden
/// test yardımcısı. Her test kendi <see cref="OverlayHarness"/> 
/// örneğini oluşturur.
/// </summary>
internal sealed class OverlayHarness
{
    // ── Simüle Edilen Ortam ───────────────────────────────────────────────────

    /// <summary>Bu process foreground'da mı? Win32 GetForegroundWindow simülasyonu.</summary>
    public bool OurProcessIsActive { get; set; } = true;

    /// <summary>Avalonia IsEffectivelyVisible simülasyonu.</summary>
    public bool EffectivelyVisible { get; set; } = true;

    /// <summary>Overlay penceresinin gerçek görünürlük durumu.</summary>
    public bool OverlayVisible { get; private set; } = false;

    // ── Çağrı Kaydı ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sıralı çağrı geçmişi. Her girdi: "SHOW", "HIDE", "TOPMOST:TRUE", "TOPMOST:FALSE".
    /// Sıra önemli testlerde (E4 gibi) kullanılır.
    /// </summary>
    public List<string> Log { get; } = new();

    public int LoggedShowCount     => Log.Count(e => e == "SHOW");
    public int LoggedHideCount     => Log.Count(e => e == "HIDE");
    public int LoggedTopmostTrue   => Log.Count(e => e == "TOPMOST:TRUE");
    public int LoggedTopmostFalse  => Log.Count(e => e == "TOPMOST:FALSE");

    // ── Controller ───────────────────────────────────────────────────────────

    public OverlayFocusController Controller { get; }

    public OverlayHarness()
    {
        Controller = new OverlayFocusController(
            isOurProcessActive:        () => OurProcessIsActive,
            isEffectivelyVisible:      () => EffectivelyVisible,
            isOverlayCurrentlyVisible: () => OverlayVisible,
            showOverlay: () =>
            {
                OverlayVisible = true;
                Log.Add("SHOW");
            },
            hideOverlay: () =>
            {
                OverlayVisible = false;
                Log.Add("HIDE");
            },
            setTopmost: v => Log.Add(v ? "TOPMOST:TRUE" : "TOPMOST:FALSE")
        );
    }

    // ── Kısayol Metotları ────────────────────────────────────────────────────

    /// <summary>Timer tick simülasyonu.</summary>
    public void Tick(int count = 1)
    {
        for (int i = 0; i < count; i++)
            Controller.OnTimerTick();
    }

    /// <summary>Layout güncelleme simülasyonu.</summary>
    public void Layout(bool visible)
    {
        EffectivelyVisible = visible;
        Controller.OnLayoutChanged(visible);
    }

    /// <summary>Başka uygulamaya geçiş (Alt-Tab). Bir tick atar.</summary>
    public void LoseFocus()
    {
        OurProcessIsActive = false;
        Tick();
    }

    /// <summary>Noctra'ya geri dönüş. Bir tick atar.</summary>
    public void GainFocus()
    {
        OurProcessIsActive = true;
        Tick();
    }

    /// <summary>Çağrı kaydını temizler.</summary>
    public void ClearLog() => Log.Clear();

    /// <summary>
    /// Overlay'i dışarıdan gizler (layout veya başka bir kod yolu
    /// Hide() çağırdı simülasyonu). IsRootActive/IsLayoutVisible dokunulmaz.
    /// </summary>
    public void SimulateExternalHide() => OverlayVisible = false;

    /// <summary>
    /// Overlay'i dışarıdan gösterir (zorla Show simülasyonu).
    /// </summary>
    public void SimulateExternalShow() => OverlayVisible = true;
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM A: Başlangıç Durumu
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_A_InitialState
{
    [Fact]
    public void A1_IsRootActive_DefaultsToTrue()
    {
        var h = new OverlayHarness();
        Assert.True(h.Controller.IsRootActive);
    }

    [Fact]
    public void A2_IsLayoutVisible_DefaultsToTrue()
    {
        var h = new OverlayHarness();
        Assert.True(h.Controller.IsLayoutVisible);
    }

    [Fact]
    public void A3_OverlayIsNotShown_BeforeFirstTick()
    {
        var h = new OverlayHarness();
        // Tick atılmadan overlay show edilmemiş olmalı
        Assert.False(h.OverlayVisible);
        Assert.Empty(h.Log);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM B: Temel Show / Hide Davranışı
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_B_BasicShowHide
{
    [Fact]
    public void B1_FirstTick_ProcessActive_LayoutVisible_ShouldShow()
    {
        var h = new OverlayHarness();

        h.Tick();

        Assert.True(h.OverlayVisible);
        Assert.Contains("SHOW", h.Log);
    }

    [Fact]
    public void B2_FirstTick_ProcessNotActive_ShouldNotShow()
    {
        var h = new OverlayHarness { OurProcessIsActive = false };

        h.Tick();

        Assert.False(h.OverlayVisible);
        Assert.DoesNotContain("SHOW", h.Log);
    }

    [Fact]
    public void B3_FirstTick_LayoutNotVisible_ShouldNotShow()
    {
        var h = new OverlayHarness { EffectivelyVisible = false };

        h.Tick();

        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void B4_ProcessActive_OverlayAlreadyVisible_ShouldNotCallShowAgain()
    {
        var h = new OverlayHarness();
        h.Tick(); // ilk show
        h.ClearLog();

        h.Tick(); // overlay zaten görünür

        Assert.DoesNotContain("SHOW", h.Log);
    }

    [Fact]
    public void B5_ProcessNotActive_OverlayVisible_ShouldHide()
    {
        var h = new OverlayHarness();
        h.Tick(); // aç

        h.LoseFocus();

        Assert.False(h.OverlayVisible);
        Assert.Contains("HIDE", h.Log);
    }

    [Fact]
    public void B6_ProcessNotActive_OverlayAlreadyHidden_ShouldNotCallHideAgain()
    {
        var h = new OverlayHarness { OurProcessIsActive = false };
        // Overlay zaten kapalı başlıyor
        h.ClearLog();

        h.Tick(5);

        Assert.DoesNotContain("HIDE", h.Log);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM C: Odak Kaybı (Alt-Tab) Senaryoları
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_C_FocusLossAndReturn
{
    [Fact]
    public void C1_LoseFocus_OverlayHidden()
    {
        var h = new OverlayHarness();
        h.Tick();

        h.LoseFocus();

        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void C2_LoseFocus_IsRootActive_BecomesFalse()
    {
        var h = new OverlayHarness();
        h.Tick();

        h.LoseFocus();

        Assert.False(h.Controller.IsRootActive);
    }

    [Fact]
    public void C3_GainFocus_OverlayRestored()
    {
        var h = new OverlayHarness();
        h.Tick();
        h.LoseFocus();

        h.GainFocus();

        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void C4_GainFocus_IsRootActive_BecomesTrue()
    {
        var h = new OverlayHarness();
        h.LoseFocus();

        h.GainFocus();

        Assert.True(h.Controller.IsRootActive);
    }

    [Fact]
    public void C5_RepeatAltTab_5Times_AlwaysRestoresCorrectly()
    {
        var h = new OverlayHarness();
        h.Tick();

        for (int i = 0; i < 5; i++)
        {
            h.LoseFocus();
            Assert.False(h.OverlayVisible);

            h.GainFocus();
            Assert.True(h.OverlayVisible);
        }
    }

    [Fact]
    public void C6_LongAbsence_50Ticks_ThenReturn_OverlayRestored()
    {
        var h = new OverlayHarness();
        h.Tick();
        h.OurProcessIsActive = false;

        h.Tick(50); // 50 tick başka uygulamada

        h.OurProcessIsActive = true;
        h.Tick();

        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void C7_LoseFocusWhileLayoutHidden_OverlayRemainsHidden()
    {
        var h = new OverlayHarness();
        h.Tick();
        h.Layout(false);       // layout gizli
        h.OurProcessIsActive = false;

        h.Tick();

        Assert.False(h.OverlayVisible);
        Assert.False(h.Controller.IsRootActive);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM D: KRİTİK REGRESSION — Kalıcı Kaybolma Bug'ı
//
// ESKİ BUG:
//   FocusCheckTimer_Tick sadece _isRootActive false→true GEÇİŞİNDE
//   Show() çağırıyordu. Layout güncellemesi overlay.Hide() tetikledikten
//   Sonra IsRootActive zaten true kaldığı için geçiş oluşmuyordu.
//   Sonuç: overlay kalıcı kayboluyordu. Düzeltme için kullanıcının
//   Alt-Tab yapıp geri gelmesi gerekiyordu.
//
// YENİ DAVRANIŞ:
//   Her tick'te overlay gizliyse ve koşullar uygunsa Show() çağrılır.
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_D_PersistentHideBugRegression
{
    [Fact]
    public void D1_OverlayHiddenExternally_WhileRootActive_TimerMustRestore()
    {
        // ESKİ BUG'ı doğrudan test eder.
        var h = new OverlayHarness();
        h.Tick(); // Show

        h.SimulateExternalHide(); // Layout veya başka bir yol overlay'i gizledi
        Assert.True(h.Controller.IsRootActive);   // IsRootActive hâlâ true
        Assert.False(h.OverlayVisible);            // ama overlay gizli

        h.ClearLog();
        h.Tick(); // YENİ KOD: gizli + aktif → Show çağırılmalı

        Assert.True(h.OverlayVisible);
        Assert.Contains("SHOW", h.Log);
    }

    [Fact]
    public void D2_NoAltTabRequired_TimerAloneRestores()
    {
        // Kullanıcının Alt-Tab yapıp geri gelmesine gerek yok.
        var h = new OverlayHarness();
        h.Tick();
        h.SimulateExternalHide();

        // Alt-Tab YAPILMIYOR, sadece timer çalışıyor
        h.ClearLog();
        h.Tick();

        Assert.Contains("SHOW", h.Log);
    }

    [Fact]
    public void D3_IsRootActiveRemainsTrue_AfterLayoutHide()
    {
        // Layout geçişi IsRootActive'i etkilememelidir
        var h = new OverlayHarness();
        h.Tick();

        h.Layout(false); // anlık layout güncellemesi

        Assert.True(h.Controller.IsRootActive);
    }

    [Fact]
    public void D4_MultipleLayoutUpdates_TimerStillRestores()
    {
        var h = new OverlayHarness();
        h.Tick();

        // Ardışık layout toggle'ları
        for (int i = 0; i < 10; i++)
        {
            h.Layout(false);
            h.Layout(true);
        }

        h.SimulateExternalHide(); // son durumda overlay kayboldu
        h.Tick(); // timer restore etmeli

        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void D5_ThreeTicksAfterExternalHide_ShowCalledExactlyOnce()
    {
        // Restore edildikten sonra Show tekrar çağrılmamalı
        var h = new OverlayHarness();
        h.Tick();
        h.SimulateExternalHide();
        h.ClearLog();

        h.Tick(); // SHOW çağrılır
        h.Tick(); // overlay zaten görünür, SHOW çağrılmaz
        h.Tick(); // overlay zaten görünür, SHOW çağrılmaz

        Assert.Equal(1, h.LoggedShowCount);
    }

    [Fact]
    public void D6_OldBehavior_WouldHaveFailed_NewBehavior_Passes()
    {
        // Bu test ESKİ kodun neden başarısız olduğunu belgeler.
        // IsRootActive zaten true → false→true geçişi yok → ESKİ KOD Show çağırmıyordu.
        var h = new OverlayHarness();
        h.Tick(); // IsRootActive=true ayarlandı

        h.SimulateExternalHide();
        h.ClearLog();

        // IsRootActive değişmedi (hâlâ true), sadece overlay gizli
        // YENİ KOD: bu durumu yakalayıp Show çağırmalı
        h.Tick();

        Assert.Contains("SHOW", h.Log);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM E: Layout Geçiş Senaryoları
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_E_LayoutTransitions
{
    [Fact]
    public void E1_LayoutFalse_WhileRootActive_ShouldHideImmediately()
    {
        // YENİ DAVRANIŞ (CPU optimizasyonu fix'i):
        // IsRootActive=true iken layout=false → Hemen Hide çağrılır.
        // Eskiden timer tick'ine güveniyorduk, ancak timer artık layout
        // görünmezken durdurulduğu için overlay sonsuza kadar görünür kalıyordu.
        var h = new OverlayHarness();
        h.Tick();
        h.ClearLog();

        h.Layout(false); // IsRootActive hâlâ true

        // Yeni davranış: timer durduğu için hemen gizlenmeli
        Assert.Contains("HIDE", h.Log);
        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void E2_LayoutFalse_WhileRootNotActive_ShouldHide()
    {
        var h = new OverlayHarness();
        h.Tick();
        h.LoseFocus(); // IsRootActive=false
        h.SimulateExternalShow(); // zorla görünür yap
        h.ClearLog();

        h.Layout(false); // Root aktif değil + layout gizli → Hide

        Assert.Contains("HIDE", h.Log);
        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void E3_LayoutTrue_WithActiveRoot_TimerShows()
    {
        var h = new OverlayHarness { EffectivelyVisible = false };
        h.ClearLog();

        h.Layout(true);
        h.Tick();

        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void E4_LayoutFalse_IsLayoutVisible_UpdatedCorrectly()
    {
        var h = new OverlayHarness();
        h.Layout(false);
        Assert.False(h.Controller.IsLayoutVisible);

        h.Layout(true);
        Assert.True(h.Controller.IsLayoutVisible);
    }

    [Fact]
    public void E5_RapidLayoutToggle_FinalStateConsistent()
    {
        var h = new OverlayHarness();
        h.Tick();

        for (int i = 0; i < 20; i++)
            h.Layout(i % 2 == 0); // true/false/true/false...

        // Son değer: i=19 -> 19 % 2 == 0 -> false -> Layout(false)
        h.Tick();

        Assert.False(h.Controller.IsLayoutVisible);
        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void E6_LayoutFalseAndProcessLosesFocus_OverlayStaysHidden()
    {
        var h = new OverlayHarness();
        h.Tick();
        h.Layout(false);
        h.LoseFocus();

        h.Tick(5);

        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void E7_LayoutFalse_ProcessGainsFocus_ShouldNotShowYet()
    {
        // Layout=false iken process aktif olsa bile overlay gösterilmemeli
        var h = new OverlayHarness { EffectivelyVisible = false, OurProcessIsActive = false };

        h.OurProcessIsActive = true;
        h.Tick(); // IsLayoutVisible=false olduğu için Show olmamalı

        Assert.False(h.OverlayVisible);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM F: Topmost Yönetimi
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_F_Topmost
{
    [Fact]
    public void F1_ProcessActive_LayoutVisible_SetsTopmostTrue()
    {
        var h = new OverlayHarness();
        h.Tick();
        Assert.Contains("TOPMOST:TRUE", h.Log);
    }

    [Fact]
    public void F2_ProcessLosesFocus_SetsTopmostFalse()
    {
        var h = new OverlayHarness();
        h.Tick();
        h.ClearLog();

        h.LoseFocus();

        Assert.Contains("TOPMOST:FALSE", h.Log);
    }

    [Fact]
    public void F3_TopmostTrue_SetEveryTickWhenActive()
    {
        // Topmost her tick garanti altında olmalı
        var h = new OverlayHarness();
        h.Tick(5); // 5 tick

        Assert.Equal(5, h.LoggedTopmostTrue);
    }

    [Fact]
    public void F4_TopmostFalse_BeforeHide_OnFocusLoss()
    {
        // TOPMOST:FALSE, HIDE'dan önce gelmelidir
        var h = new OverlayHarness();
        h.Tick();
        h.ClearLog();

        h.LoseFocus();

        int topmostIdx = h.Log.IndexOf("TOPMOST:FALSE");
        int hideIdx    = h.Log.IndexOf("HIDE");

        Assert.True(topmostIdx >= 0 && hideIdx >= 0);
        Assert.True(topmostIdx < hideIdx);
    }

    [Fact]
    public void F5_LayoutNotVisible_ProcessActive_TopmostNotSet()
    {
        var h = new OverlayHarness { EffectivelyVisible = false };
        h.ClearLog();

        h.Tick();

        Assert.DoesNotContain("TOPMOST:TRUE", h.Log);
    }

    [Fact]
    public void F6_ProcessNotActive_TopmostFalseNotRepeated_WhenOverlayAlreadyHidden()
    {
        // Overlay zaten gizliyken odak da kayıpsa TOPMOST:FALSE tekrar çağrılmamalı
        var h = new OverlayHarness { OurProcessIsActive = false };
        // Overlay baştan gizli
        h.ClearLog();

        h.Tick(5);

        Assert.DoesNotContain("TOPMOST:FALSE", h.Log);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM G: Çift Çağrı Koruması (Idempotency)
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_G_NoDuplicateCalls
{
    [Fact]
    public void G1_Show_CalledOnce_OnFirstVisibleTick()
    {
        var h = new OverlayHarness();
        h.Tick();
        Assert.Equal(1, h.LoggedShowCount);
    }

    [Fact]
    public void G2_Hide_CalledOnce_OnFirstInactiveTick()
    {
        var h = new OverlayHarness();
        h.Tick();
        h.ClearLog();

        h.LoseFocus();

        Assert.Equal(1, h.LoggedHideCount);
    }

    [Fact]
    public void G3_Show_NotCalledAgain_When100TicksWhileVisible()
    {
        var h = new OverlayHarness();
        h.Tick(); // ilk show
        h.ClearLog();

        h.Tick(100); // overlay zaten görünür

        Assert.Equal(0, h.LoggedShowCount);
    }

    [Fact]
    public void G4_Hide_NotCalledAgain_When100TicksWhileHidden()
    {
        var h = new OverlayHarness { OurProcessIsActive = false };
        // Baştan gizli
        h.ClearLog();

        h.Tick(100);

        Assert.Equal(0, h.LoggedHideCount);
    }

    [Fact]
    public void G5_ShowAndHide_EachCalledExactlyOnce_PerTransition()
    {
        var h = new OverlayHarness();
        h.Tick();        // SHOW ×1
        h.LoseFocus();   // HIDE ×1
        h.GainFocus();   // SHOW ×2
        h.LoseFocus();   // HIDE ×2

        Assert.Equal(2, h.LoggedShowCount);
        Assert.Equal(2, h.LoggedHideCount);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM H: Hızlı Ardışık Geçişler (Rapid-Fire)
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_H_RapidFire
{
    [Fact]
    public void H1_RapidFocusLossAndReturn_FinalStateMatchesLastAction()
    {
        var h = new OverlayHarness();
        h.Tick();

        // 10 hızlı geçiş: 0=lose, 1=gain, 2=lose, ...
        for (int i = 0; i < 10; i++)
        {
            h.OurProcessIsActive = (i % 2 != 0); // 0,2,4,6,8=false; 1,3,5,7,9=true
            h.Tick();
        }
        // Son i=9: 9%2!=0 → true → process aktif
        Assert.True(h.OverlayVisible);
        Assert.True(h.Controller.IsRootActive);
    }

    [Fact]
    public void H2_RapidFire_LastActionGain_OverlayVisible()
    {
        var h = new OverlayHarness();
        h.Tick();

        for (int i = 0; i < 8; i++)
        {
            h.OurProcessIsActive = (i % 2 == 0);
            h.Tick();
        }
        h.OurProcessIsActive = true;
        h.Tick(); // son eylem: gain

        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void H3_RapidExternalHides_TimerAlwaysRestores()
    {
        var h = new OverlayHarness();
        h.Tick();

        for (int i = 0; i < 10; i++)
        {
            h.SimulateExternalHide(); // overlay gizlendi
            h.Tick(); // timer restore etmeli
            Assert.True(h.OverlayVisible);
        }
    }

    [Fact]
    public void H4_RapidLayoutToggle_ProcessAlwaysActive_EndsCorrectly()
    {
        // YENİ DAVRANIŞ: Layout(false) artık hemen Hide çağırır.
        // Test, hızlı toggle sonrası final state'in tutarlı olduğunu doğrular.
        var h = new OverlayHarness();
        h.Tick();

        // Hızlı layout toggle, process hep aktif
        for (int i = 0; i < 20; i++)
            h.Layout(i % 2 == 0);

        // Layout=true ile bitir
        h.Layout(true);
        h.Tick();

        // Son durum: overlay görünür ve root/layout aktif
        Assert.True(h.OverlayVisible);
        Assert.True(h.Controller.IsRootActive);
        Assert.True(h.Controller.IsLayoutVisible);
    }

    [Fact]
    public void H5_ImmediateLossThenGain_SingleHideAndSingleShow()
    {
        var h = new OverlayHarness();
        h.Tick();
        h.ClearLog();

        h.LoseFocus(); // HIDE ×1
        h.GainFocus(); // SHOW ×1

        Assert.Equal(1, h.LoggedShowCount);
        Assert.Equal(1, h.LoggedHideCount);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM I: Kombine Senaryolar
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_I_CombinedScenarios
{
    [Fact]
    public void I1_LayoutHide_ThenAltTab_ThenReturn_OverlayRestored()
    {
        // Layout geçişi + Alt-Tab + geri dönüş
        var h = new OverlayHarness();
        h.Tick();

        h.SimulateExternalHide(); // layout nedeniyle overlay gizlendi
        h.LoseFocus();            // alt-tab
        h.GainFocus();            // geri dön

        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void I2_Fullscreen_Simulation_LayoutChanges_OverlayStable()
    {
        // Tam ekran senaryosu: layout değişimleri var, odak hep Noctra'da
        var h = new OverlayHarness();
        h.Tick();

        // Tam ekrana geçiş simülasyonu (layout geçici değişiyor)
        h.Layout(false);
        h.Layout(true);

        h.Tick(); // timer restore

        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void I3_WindowMinimize_FocusLoss_Restore()
    {
        // Pencere küçültme: layout=false + odak kaybı
        var h = new OverlayHarness();
        h.Tick();

        h.Layout(false);
        h.LoseFocus(); // pencere küçültüldü ve odak kaybedildi

        h.GainFocus(); // geri gelindi ama layout hâlâ false
        Assert.False(h.OverlayVisible);

        h.Layout(true); // pencere büyütüldü
        h.Tick();
        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void I4_PiP_Mode_Simulation_MultipleWindowSwitches()
    {
        // PiP modu: sık pencere geçişleri
        var h = new OverlayHarness();
        h.Tick();

        // 3 farklı uygulamaya geçip geri dön
        for (int i = 0; i < 3; i++)
        {
            h.LoseFocus();
            Assert.False(h.OverlayVisible);
            h.GainFocus();
            Assert.True(h.OverlayVisible);
        }

        // Ara sıra layout değişimleri de olsun
        h.SimulateExternalHide();
        h.Tick(); // restore

        Assert.True(h.OverlayVisible);
    }

    [Fact]
    public void I5_CounterAccuracy_FullLifecycle()
    {
        // Tam yaşam döngüsünde sayaçlar doğru olmalı
        var h = new OverlayHarness();

        h.Tick();            // Show ×1, TopmostTrue ×1
        h.Tick();            // TopmostTrue ×2 (zaten görünür)
        h.LoseFocus();       // TopmostFalse ×1, Hide ×1
        h.GainFocus();       // Show ×2, TopmostTrue ×3
        h.SimulateExternalHide();
        h.Tick();            // Show ×3, TopmostTrue ×4

        Assert.Equal(3, h.Controller.ShowCallCount);
        Assert.Equal(1, h.Controller.HideCallCount);
        Assert.Equal(4, h.Controller.TopmostTrueCount);
        Assert.Equal(1, h.Controller.TopmostFalseCount);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BÖLÜM J: Layout Gizliyken Timer Durdurulduğunda Anlık Gizleme
//
// SORUN:
//   CPU optimizasyonu kapsamında 200ms focus timer, layout görünmezken
//   durduruluyordu. OnLayoutChanged ise eski kodda yalnızca 
//   `!IsLayoutVisible && !IsRootActive` durumunda Hide çağırıyordu.
//   `IsRootActive=true` iken layout=false olduğunda Hide çağrılmıyor,
//   timer da durduğu için overlay sonsuza kadar görünür kalıyordu.
//
// ÇÖZÜM:
//   OnLayoutChanged artık `!IsLayoutVisible` olduğunda, root aktifliğine
//   bakmaksızın overlay'i hemen gizler.
// ─────────────────────────────────────────────────────────────────────────────

public class OverlayFocusControllerTests_J_LayoutHiddenTimerStopped
{
    [Fact]
    public void J1_LayoutFalse_RootActive_OverlayVisible_HidesImmediately()
    {
        // Çekirdek fix: Root aktif ve overlay görünürken layout kapanırsa
        // timer tick'i beklenmeden hemen gizlenmeli.
        var h = new OverlayHarness();
        h.Tick(); // overlay göster

        h.Layout(false); // layout gizleniyor (örn. kullanıcı geri tuşuna bastı)

        Assert.False(h.OverlayVisible);
        Assert.Contains("HIDE", h.Log);
    }

    [Fact]
    public void J2_LayoutFalse_RootActive_OverlayAlreadyHidden_NoExtraHide()
    {
        // Overlay zaten gizliyken layout kapanırsa ek Hide çağrılmamalı (idempotency)
        var h = new OverlayHarness();
        h.Tick(); // show
        h.Layout(false); // hide
        h.ClearLog();

        // Aynı layout(false) tekrar çağrılsa (Avalonia layout event'i tekrarlayabilir)
        h.Layout(false);

        Assert.DoesNotContain("HIDE", h.Log);
        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void J3_LayoutFalse_ThenTrue_OverlayRestoresImmediately()
    {
        // Layout kapanıp hemen açılırsa overlay geri gelmeli (OnLayoutChanged ile)
        var h = new OverlayHarness();
        h.Tick(); // show
        h.ClearLog();

        h.Layout(false); // hide (OnLayoutChanged ile)
        Assert.False(h.OverlayVisible);

        h.Layout(true); // layout geri geldi, root aktif → show (OnLayoutChanged ile)
        Assert.True(h.OverlayVisible);
        Assert.Contains("SHOW", h.Log);
    }

    [Fact]
    public void J4_NoTimerTickNeeded_AfterLayoutFalse()
    {
        // Timer durduğu için tick atılmasa bile overlay gizlenmeli
        var h = new OverlayHarness();
        h.Tick(); // show
        h.ClearLog();

        h.Layout(false); // OnLayoutChanged ile gizlenir

        // Hiç tick atılmadan overlay gizli kalmalı
        Assert.False(h.OverlayVisible);

        // Tick atılsa bile durum değişmez (gizli kalır)
        h.Tick(5);
        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void J5_ClosePlayer_Simulation_FullFlow()
    {
        // Orijinal bug senaryosu: Kullanıcı ESC/Back ile player'dan çıkıyor
        var h = new OverlayHarness();
        h.Tick(); // Player açıldı, overlay gösteriliyor

        // Kullanıcı geri tuşuna basıyor → PlayerArea.IsVisible = false
        // MemoryVideoView.OnLayoutUpdated → OnLayoutChanged(false)
        h.Layout(false);

        // Timer durduruldu (MemoryVideoView timer'ı stop eder)
        // Ama OnLayoutChanged hemen hide yaptığı için overlay gizlendi
        Assert.False(h.OverlayVisible);
        Assert.True(h.Controller.IsRootActive);   // Uygulama hala aktif
        Assert.False(h.Controller.IsLayoutVisible); // Ama layout gizli

        // Timer tick'leri atılsa bile overlay gizli kalır
        h.Tick(3);
        Assert.False(h.OverlayVisible);
    }

    [Fact]
    public void J6_LayoutFalse_RootActive_ThenFocusLoss_OverlayStaysHidden()
    {
        // Layout false + root active → overlay gizli
        // Sonra odak kaybı ve geri dönüş: layout hala false olduğu için overlay gizli kalmalı
        var h = new OverlayHarness();
        h.Tick();
        h.Layout(false);

        h.LoseFocus(); // Alt-Tab (overlay zaten gizli)
        h.GainFocus(); // Geri dön (root aktif, ama layout hala false)

        // Layout hala false olduğu için overlay gizli kalmalı
        Assert.False(h.OverlayVisible);

        // Layout true olunca overlay geri gelmeli
        h.Layout(true);
        Assert.True(h.OverlayVisible);
    }
}

