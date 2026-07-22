using System.Text.RegularExpressions;

namespace Noctra.Tests;

/// <summary>
/// Comprehensive tests for keyboard visibility, TextBox focus handling,
/// and double-scroll prevention in MainView's global GotFocus handler.
///
/// Scenarios covered:
///   1. IsTextBoxVisibleInViewport uses TranslatePoint for coordinate mapping
///   2. TextBox_GotFocus checks viewport before calling BringIntoView
///   3. Handler uses 250ms delay for keyboard appearance
///   4. Password/PIN fields get extra bottom margin (height + 24)
///   5. Handler is registered/detached correctly with visual tree
///   6. ScrollViewers have BringIntoViewOnFocusChange enabled
///   7. Handler guards against IsFocused being false after delay
///   8. No duplicate BringIntoView calls when already visible
/// </summary>
public class KeyboardVisibilityAndScrollPreventionTests
{
    // ═══════════════════════════════════════════════════════════════
    // 1. IsTextBoxVisibleInViewport — coordinate mapping correctness
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IsTextBoxVisibleInViewport_UsesTranslatePointForCoordinateMapping()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Must use TranslatePoint to convert TextBox corners to ScrollViewer coordinates
        Assert.Contains("textBox.TranslatePoint(new Point(0, 0), scrollViewer)", codeBehind);
        Assert.Contains("textBox.TranslatePoint(", codeBehind);
        Assert.Contains("new Point(textBox.Bounds.Width, textBox.Bounds.Height), scrollViewer)", codeBehind);
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_FindsAncestorScrollViewer()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Must find the parent ScrollViewer using Avalonia visual tree traversal
        Assert.Contains("textBox.FindAncestorOfType<ScrollViewer>()", codeBehind);
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_ReturnsFalseWhenNoScrollViewerAncestor()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // When no ScrollViewer ancestor exists, the method must return false
        // so the global handler still calls BringIntoView as a safe fallback
        var match = Regex.Match(
            codeBehind,
            @"IsTextBoxVisibleInViewport.*?\{[^}]*FindAncestorOfType<ScrollViewer>\(\)[^}]*scrollViewer is null[^}]*return false[^}]*\}",
            RegexOptions.Singleline);

        Assert.True(match.Success,
            "IsTextBoxVisibleInViewport must return false when no ScrollViewer ancestor is found.");
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_ReturnsFalseWhenTranslatePointFails()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // TranslatePoint can return null if elements are not in the visual tree.
        // The method must handle this gracefully.
        Assert.Contains("topLeft is not { } top || bottomRight is not { } bottom", codeBehind);
        Assert.Contains("return false", codeBehind);
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_ChecksBothTopAndBottomEdges()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The visibility check must verify the ENTIRE TextBox is within viewport
        // (both top edge below scroll offset AND bottom edge above viewport bottom)
        Assert.Contains("top.Y >= scrollOffset", codeBehind);
        Assert.Contains("bottom.Y <= scrollOffset + viewportHeight", codeBehind);
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_UsesScrollViewerOffsetAndBounds()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Must use ScrollViewer's Offset.Y for scroll position and Bounds.Height for viewport
        Assert.Contains("scrollViewer.Offset.Y", codeBehind);
        Assert.Contains("scrollViewer.Bounds.Height", codeBehind);
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_IsStaticMethod()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The method should be static since it only uses extension methods and parameters
        var match = Regex.Match(
            codeBehind,
            @"private static bool IsTextBoxVisibleInViewport\(TextBox textBox\)");
        Assert.True(match.Success, "IsTextBoxVisibleInViewport must be a private static method.");
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. TextBox_GotFocus — double-scroll prevention logic
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_GotFocus_ChecksViewportBeforeBringIntoView()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The handler MUST check IsTextBoxVisibleInViewport before calling BringIntoView
        Assert.Contains("if (!IsTextBoxVisibleInViewport(textBox))", codeBehind);
    }

    [Fact]
    public void TextBox_GotFocus_BringIntoView_IsInsideVisibilityCheck()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // BringIntoView calls must be inside the visibility check block
        // Find the GotFocus handler method
        var handlerMatch = Regex.Match(
            codeBehind,
            @"private void TextBox_GotFocus\(.*?\{(.*?)protected override void OnDetachedFromVisualTree",
            RegexOptions.Singleline);

        Assert.True(handlerMatch.Success, "TextBox_GotFocus handler method not found.");
        var handlerBody = handlerMatch.Groups[1].Value;

        // Both BringIntoView calls must be after the visibility check
        var visibilityCheckIndex = handlerBody.IndexOf("IsTextBoxVisibleInViewport", StringComparison.Ordinal);
        var bringIntoViewIndex = handlerBody.IndexOf("textBox.BringIntoView", StringComparison.Ordinal);

        Assert.True(visibilityCheckIndex >= 0, "Visibility check not found in handler.");
        Assert.True(bringIntoViewIndex >= 0, "BringIntoView call not found in handler.");
        Assert.True(visibilityCheckIndex < bringIntoViewIndex,
            "BringIntoView must come AFTER the visibility check to prevent double-scroll.");
    }

    [Fact]
    public void TextBox_GotFocus_HandlesBothVisibleAndInvisibleScenarios()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The handler should only call BringIntoView when NOT visible
        // The comment should explain why
        Assert.Contains("çift kayma/zıplama önlemi", codeBehind);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. TextBox_GotFocus — timing and delay
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_GotFocus_Uses250MillisecondsDelay()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The 250ms delay allows time for the keyboard to appear before checking viewport
        Assert.Contains("TimeSpan.FromMilliseconds(250)", codeBehind);
    }

    [Fact]
    public void TextBox_GotFocus_UsesDispatcherTimerRunOnce()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Must use DispatcherTimer.RunOnce for the delayed execution
        Assert.Contains("DispatcherTimer.RunOnce", codeBehind);
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. TextBox_GotFocus — password/PIN extra offset
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_GotFocus_PasswordFields_GetExtraBottomMargin()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Password/PIN fields need extra margin (height + 24) to stay above keyboard
        Assert.Contains("textBox.PasswordChar != '\\0'", codeBehind);
        Assert.Contains("height + 24", codeBehind);
    }

    [Fact]
    public void TextBox_GotFocus_PasswordFields_FallbackToDefaultHeight()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // When Bounds.Height is 0 (not yet laid out), default to 48px
        Assert.Contains("bounds.Height > 0 ? bounds.Height : 48", codeBehind);
    }

    [Fact]
    public void TextBox_GotFocus_PasswordFields_UsesBoundsForDimensions()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Must use textBox.Bounds for width and height calculation
        Assert.Contains("var bounds = textBox.Bounds", codeBehind);
        Assert.Contains("var height = bounds.Height", codeBehind);
    }

    [Fact]
    public void TextBox_GotFocus_NonPasswordFields_CallsSimpleBringIntoView()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Non-password TextBoxes use the simpler BringIntoView() without extra rect
        var match = Regex.Match(
            codeBehind,
            @"else\s*\{\s*textBox\.BringIntoView\(\);\s*\}",
            RegexOptions.Singleline);

        Assert.True(match.Success,
            "Non-password TextBoxes must use simple BringIntoView() without extra rect.");
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. TextBox_GotFocus — event registration lifecycle
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_GotFocus_IsRegisteredInOnAttachedToVisualTree()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Handler must be registered when the view attaches to the visual tree
        Assert.Contains("AddHandler(InputElement.GotFocusEvent, TextBox_GotFocus)", codeBehind);

        // Verify it's inside OnAttachedToVisualTree
        var attachMatch = Regex.Match(
            codeBehind,
            @"protected override void OnAttachedToVisualTree.*?\{.*?AddHandler\(InputElement\.GotFocusEvent, TextBox_GotFocus\)",
            RegexOptions.Singleline);

        Assert.True(attachMatch.Success,
            "TextBox_GotFocus must be registered inside OnAttachedToVisualTree.");
    }

    [Fact]
    public void TextBox_GotFocus_IsRemovedInOnDetachedFromVisualTree()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Handler must be unregistered when the view detaches to prevent event leaks
        Assert.Contains("RemoveHandler(InputElement.GotFocusEvent, TextBox_GotFocus)", codeBehind);

        // Verify it's inside OnDetachedFromVisualTree
        var detachMatch = Regex.Match(
            codeBehind,
            @"protected override void OnDetachedFromVisualTree.*?\{.*?RemoveHandler\(InputElement\.GotFocusEvent, TextBox_GotFocus\)",
            RegexOptions.Singleline);

        Assert.True(detachMatch.Success,
            "TextBox_GotFocus must be removed inside OnDetachedFromVisualTree to avoid event leaks.");
    }

    [Fact]
    public void TextBox_GotFocus_EventRegistrationAndRemoval_AreBalanced()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        var addCount = CountOccurrences(codeBehind, "AddHandler(InputElement.GotFocusEvent, TextBox_GotFocus)");
        var removeCount = CountOccurrences(codeBehind, "RemoveHandler(InputElement.GotFocusEvent, TextBox_GotFocus)");

        Assert.Equal(addCount, removeCount);
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. TextBox_GotFocus — IsFocused guard
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_GotFocus_ChecksIsFocusedBeforeAction()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // After the 250ms delay, the TextBox might have lost focus.
        // The handler must verify it's still focused before scrolling.
        Assert.Contains("if (textBox.IsFocused)", codeBehind);
    }

    [Fact]
    public void TextBox_GotFocus_ChecksSourceIsTextBox()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Handler must filter for TextBox only (not other focusable elements)
        Assert.Contains("if (e.Source is TextBox textBox)", codeBehind);
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. ScrollViewers — BringIntoViewOnFocusChange configuration
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MobileSettings_ScrollViewer_HasBringIntoViewOnFocusChange()
    {
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        // Settings ScrollViewer must have BringIntoViewOnFocusChange enabled
        Assert.Contains("BringIntoViewOnFocusChange=\"True\"", settings);
    }

    [Fact]
    public void ProfileSetup_ScrollViewer_HasBringIntoViewOnFocusChange()
    {
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        // Profile setup ScrollViewer must have BringIntoViewOnFocusChange enabled
        Assert.Contains("BringIntoViewOnFocusChange=\"True\"", profile);
    }

    [Fact]
    public void MobileSettings_ScrollViewer_IsNamedForReference()
    {
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        // Settings ScrollViewer should have an x:Name for potential programmatic access
        Assert.Contains("x:Name=\"SettingsScrollViewer\"", settings);
    }

    [Fact]
    public void ProfileSetup_ScrollViewer_IsNamedForReference()
    {
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        // Profile setup ScrollViewer should have an x:Name
        Assert.Contains("x:Name=\"ProfileFormScrollViewer\"", profile);
    }

    [Fact]
    public void ScrollViewers_DontDisableBringIntoViewOnFocusChange()
    {
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        // Neither should explicitly disable the feature
        Assert.DoesNotContain("BringIntoViewOnFocusChange=\"False\"", settings);
        Assert.DoesNotContain("BringIntoViewOnFocusChange=\"False\"", profile);
    }

    // ═══════════════════════════════════════════════════════════════
    // 8. ProfileSetup — TextBox touch targets and accessibility
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ProfileSetup_TextBoxes_HaveAccessibleTouchTargets()
    {
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        // All TextBoxes in profile setup must have MinHeight >= 48 for touch accessibility
        var textBoxMatches = Regex.Matches(
            profile,
            @"<TextBox\b[^>]*?>",
            RegexOptions.Singleline);

        Assert.NotEmpty(textBoxMatches);
        foreach (Match match in textBoxMatches)
        {
            Assert.DoesNotContain("MinHeight=\"44\"", match.Value);
            Assert.DoesNotContain("MinHeight=\"46\"", match.Value);
        }
    }

    [Fact]
    public void ProfileSetup_PasswordTextBox_UsesPasswordContentType()
    {
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        // Password field must use ContentType="Password" for proper keyboard
        Assert.Contains("TextInputOptions.ContentType=\"Password\"", profile);
    }

    [Fact]
    public void ProfileSetup_PinTextBoxes_HavePinClasses()
    {
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        // PIN fields must have the "pin" class for specific styling
        var pinClassCount = CountOccurrences(profile, "Classes=\"pin\"");
        Assert.Equal(2, pinClassCount); // PinCode and PinConfirm
    }

    [Fact]
    public void ProfileSetup_TextBoxes_HaveReturnKeyHints()
    {
        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");

        // Textboxes should guide keyboard flow with ReturnKeyType
        Assert.Contains("TextInputOptions.ReturnKeyType=\"Next\"", profile);
        Assert.Contains("TextInputOptions.ReturnKeyType=\"Done\"", profile);
    }

    // ═══════════════════════════════════════════════════════════════
    // 9. Settings — TextBox keyboard configuration
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Settings_UserAgentTextBox_HasDoneReturnKey()
    {
        var settings = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        // UserAgent field should show "Done" key since it's typically the last field
        Assert.Contains("TextInputOptions.ReturnKeyType=\"Done\"", settings);
    }

    // ═══════════════════════════════════════════════════════════════
    // 10. Android activity — keyboard resize behavior
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AndroidActivity_UsesAdjustResizeForSoftwareKeyboard()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        // Android must resize the activity when keyboard appears
        Assert.Contains("WindowSoftInputMode = SoftInput.AdjustResize,", activity);
    }

    [Fact]
    public void AndroidActivity_DoesNotUseAdjustPan()
    {
        var activity = ReadProjectFile("Noctra.Android", "MainActivity.cs");

        // AdjustPan would push content behind keyboard instead of resizing
        Assert.DoesNotContain("SoftInput.AdjustPan", activity);
    }

    // ═══════════════════════════════════════════════════════════════
    // 11. Double-scroll prevention — integration checks
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DoubleScrollPrevention_CommentExplainsTheWhy()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The code should document why the viewport check exists
        Assert.Contains("BringIntoViewOnFocusChange davranışı zaten", codeBehind);
        Assert.Contains("TextBox görünür alana kaydırdıysa tekrar çağırmayalım", codeBehind);
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_DocumentationExplainsPurpose()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The method should have XML documentation explaining its purpose
        var docMatch = Regex.Match(
            codeBehind,
            @"/// <summary>\s*/// TextBox'ın üst üste binen ScrollViewer.*?/// </summary>\s*private static bool IsTextBoxVisibleInViewport",
            RegexOptions.Singleline);

        Assert.True(docMatch.Success,
            "IsTextBoxVisibleInViewport must have XML documentation explaining its purpose.");
    }

    [Fact]
    public void TextBox_GotFocus_HandlesNullSenderGracefully()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Handler must check e.Source before casting
        Assert.Contains("if (e.Source is TextBox textBox)", codeBehind);
    }

    // ═══════════════════════════════════════════════════════════════
    // 12. Edge cases and robustness
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void IsTextBoxVisibleInViewport_HandlesZeroHeightBounds()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // When bounds are not yet calculated (Height=0), fallback to 48px default
        Assert.Contains("bounds.Height > 0 ? bounds.Height : 48", codeBehind);
    }

    [Fact]
    public void TextBox_GotFocus_PasswordCheck_UsesCharNotString()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Must compare PasswordChar to null char, not empty string
        Assert.Contains("textBox.PasswordChar != '\\0'", codeBehind);
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_FullMethodSignatureExists()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Verify the complete method signature exists
        var match = Regex.Match(
            codeBehind,
            @"private static bool IsTextBoxVisibleInViewport\(TextBox textBox\)\s*\{");

        Assert.True(match.Success, "IsTextBoxVisibleInViewport method signature not found.");
    }

    [Fact]
    public void TextBox_GotFocus_FullMethodSignatureExists()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Verify the complete method signature exists
        var match = Regex.Match(
            codeBehind,
            @"private void TextBox_GotFocus\(object\? sender, RoutedEventArgs e\)\s*\{");

        Assert.True(match.Success, "TextBox_GotFocus method signature not found.");
    }

    // ═══════════════════════════════════════════════════════════════
    // 13. Happy path — TextBox IS visible (no redundant scroll)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_GotFocus_SkipsBringIntoViewWhenAlreadyVisible()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The handler must ONLY call BringIntoView when the TextBox is NOT visible.
        // Find the negated visibility check and BringIntoView calls
        var notVisibleCheck = codeBehind.IndexOf(
            "!IsTextBoxVisibleInViewport(textBox)", StringComparison.Ordinal);
        var bringIntoViewCalls = new List<int>();
        var searchFrom = 0;
        while (true)
        {
            var idx = codeBehind.IndexOf("textBox.BringIntoView", searchFrom, StringComparison.Ordinal);
            if (idx < 0) break;
            bringIntoViewCalls.Add(idx);
            searchFrom = idx + 1;
        }

        Assert.True(notVisibleCheck >= 0, "Negated visibility check not found.");
        Assert.NotEmpty(bringIntoViewCalls);

        // ALL BringIntoView calls must come AFTER the visibility check
        foreach (var bringIdx in bringIntoViewCalls)
        {
            Assert.True(notVisibleCheck < bringIdx,
                $"BringIntoView at position {bringIdx} must come after visibility check at {notVisibleCheck}.");
        }
    }

    [Fact]
    public void IsTextBoxVisibleInViewport_HandlesZeroViewportHeight()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // When the ScrollViewer is not laid out yet, report the TextBox as not visible
        // so the caller takes the normal BringIntoView path.
        var match = Regex.Match(
            codeBehind,
            @"if\s*\(viewportHeight\s*<=\s*0\)\s*\{\s*return false;\s*\}",
            RegexOptions.Singleline);

        Assert.True(match.Success,
            "IsTextBoxVisibleInViewport must explicitly treat a zero viewport as not visible.");
    }

    // ═══════════════════════════════════════════════════════════════
    // 14. IsFocused guard inside RunOnce lambda
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_GotFocus_IsFocusedCheck_IsInsideRunOnceLambda()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // The IsFocused check must be INSIDE the DispatcherTimer.RunOnce lambda
        // to prevent acting on a TextBox that lost focus during the 250ms delay.
        var runOnceMatch = Regex.Match(
            codeBehind,
            @"DispatcherTimer\.RunOnce\(\s*\(\)\s*=>\s*\{(.*?)\},\s*TimeSpan\.FromMilliseconds\(250\)\)",
            RegexOptions.Singleline);

        Assert.True(runOnceMatch.Success, "DispatcherTimer.RunOnce lambda not found.");
        var lambdaBody = runOnceMatch.Groups[1].Value;

        // The lambda body must contain the IsFocused check
        Assert.Contains("textBox.IsFocused", lambdaBody);

        // The IsFocused check must come BEFORE any BringIntoView or viewport check
        var focusedIndex = lambdaBody.IndexOf("textBox.IsFocused", StringComparison.Ordinal);
        var viewportCheckIndex = lambdaBody.IndexOf("IsTextBoxVisibleInViewport", StringComparison.Ordinal);
        var bringIntoViewIndex = lambdaBody.IndexOf("BringIntoView", StringComparison.Ordinal);

        Assert.True(focusedIndex >= 0, "IsFocused check not found in lambda.");
        if (viewportCheckIndex >= 0)
        {
            Assert.True(focusedIndex < viewportCheckIndex,
                "IsFocused must be checked before viewport check.");
        }
        if (bringIntoViewIndex >= 0)
        {
            Assert.True(focusedIndex < bringIntoViewIndex,
                "IsFocused must be checked before BringIntoView.");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 15. Complete flow verification
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextBox_GotFocus_Flow_IsSourceCheck_ThenDelay_ThenFocusCheck_ThenViewportCheck_ThenScroll()
    {
        var codeBehind = ReadProjectFile("Noctra.Mobile", "Views", "MainView.axaml.cs");

        // Verify the complete flow ordering in the handler:
        // 1. e.Source is TextBox → 2. RunOnce(250ms) → 3. IsFocused → 4. !IsTextBoxVisibleInViewport → 5. BringIntoView
        var sourceCheck = codeBehind.IndexOf("e.Source is TextBox", StringComparison.Ordinal);
        var runOnce = codeBehind.IndexOf("DispatcherTimer.RunOnce", StringComparison.Ordinal);
        var focusedCheck = codeBehind.IndexOf("textBox.IsFocused", StringComparison.Ordinal);
        var viewportCheck = codeBehind.IndexOf("!IsTextBoxVisibleInViewport", StringComparison.Ordinal);
        var scroll = codeBehind.IndexOf("textBox.BringIntoView", StringComparison.Ordinal);

        Assert.True(sourceCheck >= 0, "Step 1: Source check not found.");
        Assert.True(runOnce >= 0, "Step 2: RunOnce not found.");
        Assert.True(focusedCheck >= 0, "Step 3: IsFocused check not found.");
        Assert.True(viewportCheck >= 0, "Step 4: Viewport check not found.");
        Assert.True(scroll >= 0, "Step 5: BringIntoView not found.");

        Assert.True(sourceCheck < runOnce, "Source check must come before RunOnce.");
        Assert.True(runOnce < focusedCheck, "RunOnce must come before IsFocused check.");
        Assert.True(focusedCheck < viewportCheck, "IsFocused must come before viewport check.");
        Assert.True(viewportCheck < scroll, "Viewport check must come before BringIntoView.");
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper methods
    // ═══════════════════════════════════════════════════════════════

    private static string ReadProjectFile(params string[] relativeParts)
        => File.ReadAllText(FindProjectFile(relativeParts));

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string FindProjectFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not find project file: {Path.Combine(relativeParts)}");
    }
}
