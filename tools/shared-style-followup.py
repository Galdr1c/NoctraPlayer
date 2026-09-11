from pathlib import Path
import re

ROOT = Path('.')


def read(path):
    return Path(path).read_text(encoding='utf-8')


def write(path, text):
    Path(path).write_text(text.replace('\r\n', '\n'), encoding='utf-8')

# 1) Mobile source-of-truth for sort action: desktop Downloads uses the same
# SortIconButton class instead of the generic IconButton theme.
downloads_path = 'Noctra.Avalonia/Views/DownloadsView.axaml'
downloads = read(downloads_path)
downloads = downloads.replace(
    'Theme="{StaticResource IconButton}" Width="44" Height="44"',
    'Classes="SortIconButton"',
)
downloads = downloads.replace(
    'Theme="{StaticResource IconButton}"\n                        Click="OpenDownloadSortSheet_Click"',
    'Classes="SortIconButton"\n                        Click="OpenDownloadSortSheet_Click"',
)
write(downloads_path, downloads)

# 2) Desktop scrollbar thickness is genuinely host-specific. Do not let these
# generic selectors leak into the mobile shared style system.
common_path = 'Noctra.UI/Resources/CommonStyles.axaml'
common = read(common_path)
for selector in [
    'ScrollViewer /template/ ScrollBar:vertical',
    'ScrollViewer /template/ ScrollBar:horizontal',
]:
    pattern = re.compile(
        r'\s*<Style\s+Selector="' + re.escape(selector) + r'">.*?</Style>\s*',
        re.S,
    )
    common, count = pattern.subn('\n', common, count=1)
    if count != 1:
        raise RuntimeError(f'Expected one migrated desktop-only selector: {selector}')
write(common_path, common)

override_path = 'Noctra.Avalonia/Resources/DesktopStyleOverrides.axaml'
override = '''<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- Only behavior/metrics that are genuinely Windows-host specific belong here. -->
  <Style Selector="ScrollViewer /template/ ScrollBar:vertical">
    <Setter Property="Width" Value="8" />
  </Style>
  <Style Selector="ScrollViewer /template/ ScrollBar:horizontal">
    <Setter Property="Height" Value="8" />
  </Style>
</Styles>
'''
write(override_path, override)

app_path = 'Noctra.Avalonia/App.axaml'
app = read(app_path)
needle = '    <StyleInclude Source="avares://Noctra.UI/Resources/SettingsStyles.axaml" />'
addition = needle + '\n    <StyleInclude Source="avares://Noctra/Resources/DesktopStyleOverrides.axaml" />'
if 'DesktopStyleOverrides.axaml' not in app:
    if needle not in app:
        raise RuntimeError('Desktop SettingsStyles include not found')
    app = app.replace(needle, addition, 1)
write(app_path, app)

# 3) Stale tests from before the user's latest main changes must follow current main,
# not overwrite the user's PlayerIconHoverBrush / Resume radius decisions.
canonical_path = 'Noctra.Tests/SharedThemeCanonicalizationTests.cs'
canonical = read(canonical_path)
canonical = canonical.replace(
    'public void SharedThemes_PreserveMobilePlayerSheetPaletteAndCrossPlatformSemanticSurfaces()',
    'public void SharedThemes_PreserveMobilePlayerSheetPaletteAndCurrentSemanticBrushes()',
)
old_semantics = '''        foreach (var theme in new[] { dark, light })
        {
            Assert.Contains("x:Key=\\"SuccessSurfaceBrush\\"", theme, StringComparison.Ordinal);
            Assert.Contains("x:Key=\\"WarningSurfaceBrush\\"", theme, StringComparison.Ordinal);
            Assert.Contains("x:Key=\\"ErrorSurfaceBrush\\"", theme, StringComparison.Ordinal);
        }
'''
new_semantics = '''        foreach (var theme in new[] { dark, light })
        {
            Assert.Contains("x:Key=\\"PlayerIconHoverBrush\\"", theme, StringComparison.Ordinal);
            Assert.Contains("x:Key=\\"DangerBrush\\"", theme, StringComparison.Ordinal);
            Assert.Contains("x:Key=\\"InteractivePressedBrush\\"", theme, StringComparison.Ordinal);
        }
'''
if old_semantics not in canonical:
    raise RuntimeError('Old semantic brush assertions not found')
canonical = canonical.replace(old_semantics, new_semantics, 1)
canonical = canonical.replace(
    'public void DesktopResumeAction_UsesSharedPillRadiusToken()',
    'public void DesktopResumeAction_PreservesCurrentMobileFirstRadius()',
)
canonical = canonical.replace(
    'Assert.Contains("<Setter Property=\\"CornerRadius\\" Value=\\"{DynamicResource RadiusPill}\\" />", mainWindow, StringComparison.Ordinal);\n        Assert.DoesNotContain("<Setter Property=\\"CornerRadius\\" Value=\\"81\\" />", mainWindow, StringComparison.Ordinal);',
    'Assert.Contains("<Setter Property=\\"CornerRadius\\" Value=\\"6\\" />", mainWindow, StringComparison.Ordinal);\n        Assert.DoesNotContain("<Setter Property=\\"CornerRadius\\" Value=\\"81\\" />", mainWindow, StringComparison.Ordinal);',
)
# Strengthen the new style-system test with the intentionally tiny platform override.
canonical = canonical.replace(
    'Assert.DoesNotContain("DesktopModernStyles.axaml", desktopApp, StringComparison.Ordinal);',
    'Assert.DoesNotContain("DesktopModernStyles.axaml", desktopApp, StringComparison.Ordinal);\n        Assert.Contains("avares://Noctra/Resources/DesktopStyleOverrides.axaml", desktopApp, StringComparison.Ordinal);',
)
write(canonical_path, canonical)

# 4) TextBox keyboard-hint contract now lives in shared CommonStyles, not Mobile App.
release_path = 'Noctra.Tests/MobileReleaseGuardTests.cs'
release = read(release_path)
method_pattern = re.compile(
    r'(public void MobileTextBoxes_ExposePurposeSpecificKeyboardHints\(\)\s*\{)(.*?)(\n    \}\n\n    \[Fact\])',
    re.S,
)
match = method_pattern.search(release)
if not match:
    raise RuntimeError('MobileTextBoxes_ExposePurposeSpecificKeyboardHints method not found')
body = match.group(2)
if 'var commonStyles = ReadProjectFile("Noctra.UI", "Resources", "CommonStyles.axaml");' not in body:
    body = body.replace(
        '        var app = ReadProjectFile("Noctra.Mobile", "App.axaml");',
        '        var app = ReadProjectFile("Noctra.Mobile", "App.axaml");\n        var commonStyles = ReadProjectFile("Noctra.UI", "Resources", "CommonStyles.axaml");',
        1,
    )
for text in [
    '<Style Selector=\\"TextBox.url\\">',
    'TextInputOptions.ContentType\\" Value=\\"Url\\"',
    '<Style Selector=\\"TextBox.search\\">',
    'TextInputOptions.ContentType\\" Value=\\"Search\\"',
    'TextInputOptions.ReturnKeyType\\" Value=\\"Search\\"',
    '<Style Selector=\\"TextBox.password\\">',
    'TextInputOptions.IsSensitive\\" Value=\\"True\\"',
    '<Style Selector=\\"TextBox.pin\\">',
    'TextInputOptions.ContentType\\" Value=\\"Digits\\"',
]:
    body = body.replace(f'Assert.Contains("{text}", app);', f'Assert.Contains("{text}", commonStyles);')
release = release[:match.start()] + match.group(1) + body + match.group(3) + release[match.end():]
write(release_path, release)

# 5) Desktop selection-sheet architecture now requires shared styles plus the tiny
# desktop-only override, rather than the removed DesktopModernStyles catalog.
selection_test_path = 'Noctra.Tests/DesktopSettingsSelectionSheetTests.cs'
selection_test = read(selection_test_path)
old_test = '''    [Fact]
    public void Application_IncludesDesktopModernStyles()
    {
        var document = LoadProjectXaml("Noctra.Avalonia", "App.axaml");

        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "StyleInclude" &&
                (string?)element.Attribute("Source") ==
                "avares://Noctra/Resources/DesktopModernStyles.axaml");
    }
'''
new_test = '''    [Fact]
    public void Application_UsesSharedStylesWithOnlyDesktopSpecificOverrides()
    {
        var document = LoadProjectXaml("Noctra.Avalonia", "App.axaml");
        var includes = document.Descendants()
            .Where(element => element.Name.LocalName == "StyleInclude")
            .Select(element => (string?)element.Attribute("Source"))
            .ToArray();

        Assert.Contains("avares://Noctra.UI/Resources/CommonStyles.axaml", includes);
        Assert.Contains("avares://Noctra/Resources/DesktopStyleOverrides.axaml", includes);
        Assert.DoesNotContain("avares://Noctra/Resources/DesktopModernStyles.axaml", includes);
    }
'''
if old_test not in selection_test:
    raise RuntimeError('DesktopModernStyles source-contract test not found')
selection_test = selection_test.replace(old_test, new_test, 1)
write(selection_test_path, selection_test)

print('Shared style follow-up fixes applied.')
