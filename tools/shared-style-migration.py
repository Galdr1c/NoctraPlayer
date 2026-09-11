from pathlib import Path
import re

ROOT = Path('.')

MOBILE_STYLES = ROOT / 'Noctra.Mobile/Resources/Styles.axaml'
DESKTOP_STYLES = ROOT / 'Noctra.Avalonia/Resources/Styles.axaml'
DESKTOP_MODERN = ROOT / 'Noctra.Avalonia/Resources/DesktopModernStyles.axaml'
SHARED_STYLES = ROOT / 'Noctra.UI/Resources/Styles.axaml'
COMMON_STYLES = ROOT / 'Noctra.UI/Resources/CommonStyles.axaml'
DESKTOP_TOKENS = ROOT / 'Noctra.Avalonia/Resources/DesktopAdaptiveTokens.axaml'


def read(path: Path) -> str:
    return path.read_text(encoding='utf-8')


def write(path: Path, text: str) -> None:
    path.write_text(text.replace('\r\n', '\n'), encoding='utf-8')


def theme_re(key: str) -> re.Pattern[str]:
    return re.compile(
        r'<ControlTheme\s+x:Key="' + re.escape(key) + r'"\s+TargetType="[^"]+">.*?</ControlTheme>',
        re.S,
    )


def get_theme(text: str, key: str) -> str:
    matches = list(theme_re(key).finditer(text))
    if len(matches) != 1:
        raise RuntimeError(f'Expected exactly one ControlTheme {key!r}, found {len(matches)}')
    return matches[0].group(0)


def replace_theme(text: str, key: str, replacement: str) -> str:
    pattern = theme_re(key)
    text, count = pattern.subn(lambda _: replacement, text, count=1)
    if count != 1:
        raise RuntimeError(f'Could not replace ControlTheme {key!r}')
    return text


def remove_theme(text: str, key: str) -> str:
    text, count = theme_re(key).subn('', text, count=1)
    if count != 1:
        raise RuntimeError(f'Could not remove ControlTheme {key!r}')
    return text


def add_before_closing(text: str, closing: str, content: str) -> str:
    index = text.rfind(closing)
    if index < 0:
        raise RuntimeError(f'Missing closing marker {closing!r}')
    return text[:index] + content.rstrip() + '\n\n' + text[index:]


def remove_style_selector(text: str, selector: str) -> str:
    pattern = re.compile(
        r'\s*<Style\s+Selector="' + re.escape(selector) + r'">.*?</Style>\s*',
        re.S,
    )
    text, count = pattern.subn('\n', text, count=1)
    if count != 1:
        raise RuntimeError(f'Could not remove selector style {selector!r}')
    return text


def enhance_mobile_theme(block: str, key: str) -> str:
    if key == 'PrimaryButtonStyle':
        marker = '    <Style Selector="^:pressed">'
        insertion = '''    <Style Selector="^:pointerover">\n      <Setter Property="Opacity" Value="0.92" />\n    </Style>\n'''
        block = block.replace(marker, insertion + marker, 1)
    elif key == 'SecondaryButton':
        marker = '    <Style Selector="^:pressed">'
        insertion = '''    <Style Selector="^:pointerover">\n      <Setter Property="Background" Value="{DynamicResource InteractiveHoverBrush}" />\n      <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />\n    </Style>\n'''
        block = block.replace(marker, insertion + marker, 1)
    elif key == 'PlainButton':
        marker = '    <Style Selector="^:pressed">'
        insertion = '''    <Style Selector="^:pointerover">\n      <Setter Property="Opacity" Value="0.85" />\n    </Style>\n'''
        block = block.replace(marker, insertion + marker, 1)
    elif key == 'IconButton':
        marker = '    <Style Selector="^:pressed">'
        insertion = '''    <Style Selector="^:pointerover">\n      <Setter Property="Background" Value="{DynamicResource InteractiveHoverBrush}" />\n    </Style>\n'''
        block = block.replace(marker, insertion + marker, 1)
    elif key == 'RevealBtn':
        marker = '      <Style Selector="^:pressed /template/ ContentPresenter">'
        insertion = '''      <Style Selector="^:pointerover /template/ ContentPresenter">\n        <Setter Property="Background" Value="{DynamicResource InteractiveHoverBrush}" />\n      </Style>\n'''
        block = block.replace(marker, insertion + marker, 1)
    return block


mobile = read(MOBILE_STYLES)
desktop = read(DESKTOP_STYLES)

# 1. Build one canonical ControlTheme dictionary. Desktop-only themes are retained,
# but every overlap is replaced by the mobile implementation. Pointer hover is then
# layered into those mobile-first themes where it is harmless on touch devices.
shared = desktop
for resource_key in [
    'RatioPortraitWidth',
    'RatioPortraitHeight',
    'RatioLandscapeWidth',
    'RatioLandscapeHeight',
    'RatioSquareSize',
    'DefaultRemoteImagePlaceholder',
]:
    shared = re.sub(
        r'\s*<x:(?:Double|String)\s+x:Key="' + re.escape(resource_key) + r'">.*?</x:(?:Double|String)>\s*',
        '\n',
        shared,
        count=1,
        flags=re.S,
    )

mobile_wins = [
    'PrimaryButtonStyle',
    'SecondaryButton',
    'DangerButtonStyle',
    'PlainButton',
    'IconButton',
    '{x:Type RadioButton}',
    '{x:Type ToggleSwitch}',
]
for key in mobile_wins:
    shared = replace_theme(shared, key, enhance_mobile_theme(get_theme(mobile, key), key))

# These already have canonical selector-class implementations in shared style files.
# Keeping a second ControlTheme with the same conceptual name would reintroduce drift.
for duplicate_key in ['UpgradeButton', 'SettingsActionButton']:
    shared = remove_theme(shared, duplicate_key)

mobile_only = [
    'Searchicon',
    'RevealBtn',
    '{x:Type CheckBox}',
    'SegmentedRadioButtonTransparent',
]
mobile_only_blocks = []
for key in mobile_only:
    if theme_re(key).search(shared):
        raise RuntimeError(f'Mobile-only theme unexpectedly already exists in desktop source: {key}')
    mobile_only_blocks.append(enhance_mobile_theme(get_theme(mobile, key), key))

shared = add_before_closing(
    shared,
    '</ResourceDictionary>',
    '\n  <!-- Mobile-first themes that previously existed only in Noctra.Mobile. -->\n  '
    + '\n\n  '.join(block.replace('\n', '\n  ') for block in mobile_only_blocks),
)
write(SHARED_STYLES, shared)

# 2. Keep only truly desktop layout/asset tokens in the desktop override dictionary.
desktop_tokens = read(DESKTOP_TOKENS)
for key in ['RatioPortraitWidth', 'RatioPortraitHeight', 'RatioLandscapeWidth', 'RatioLandscapeHeight', 'RatioSquareSize']:
    if f'x:Key="{key}"' in desktop_tokens:
        raise RuntimeError(f'Desktop adaptive token already contains {key}')
extra_tokens = '''
  <!-- Desktop-only card geometry / asset compatibility. Visual style remains shared. -->
  <x:Double x:Key="RatioPortraitWidth">160</x:Double>
  <x:Double x:Key="RatioPortraitHeight">240</x:Double>
  <x:Double x:Key="RatioLandscapeWidth">280</x:Double>
  <x:Double x:Key="RatioLandscapeHeight">158</x:Double>
  <x:Double x:Key="RatioSquareSize">120</x:Double>
  <x:String x:Key="DefaultRemoteImagePlaceholder">avares://Noctra/Assets/Square150x150Logo.png</x:String>
'''
desktop_tokens = add_before_closing(desktop_tokens, '</ResourceDictionary>', extra_tokens)
write(DESKTOP_TOKENS, desktop_tokens)

# 3. Merge selector styles into the shared Styles collection. Near-duplicates are
# removed and their desktop usages are redirected to the mobile-first shared names.
common = read(COMMON_STYLES)
desktop_modern = read(DESKTOP_MODERN)
match = re.search(r'<Styles\b[^>]*>(.*)</Styles>\s*$', desktop_modern, re.S)
if not match:
    raise RuntimeError('Could not extract DesktopModernStyles body')
desktop_modern_body = match.group(1)

for selector in [
    'TextBlock.DesktopPageTitle',
    'Button.DesktopIconButton',
    'Button.DesktopIconButton:pointerover /template/ ContentPresenter',
    'Button.DesktopIconButton:pressed /template/ ContentPresenter',
    'Button.DesktopSettingsSelectionButton',
    'Button.DesktopSettingsSelectionButton:pointerover /template/ ContentPresenter',
    'Button.DesktopSettingsSelectionButton:pressed /template/ ContentPresenter',
    'ListBox.DesktopSelectionList',
    'ListBox.DesktopCategoryList',
    'ListBox.DesktopSelectionList > ListBoxItem',
    'ListBox.DesktopCategoryList > ListBoxItem',
    'Button.DesktopSheetOption',
    'Button.DesktopSheetOption:pointerover /template/ ContentPresenter',
    'Button.DesktopSheetOption:pressed /template/ ContentPresenter',
    'Button.DesktopCategoryRow',
    'Button.DesktopCategoryRow:pointerover /template/ ContentPresenter',
    'Button.DesktopRevealButton',
    'Button.DesktopRevealButton:pointerover /template/ ContentPresenter',
]:
    desktop_modern_body = remove_style_selector(desktop_modern_body, selector)

shared_selector_styles = r'''

  <!-- Unified application interaction styles. Mobile values are canonical. -->
  <Style Selector="Button">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="Slider">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="ComboBox">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="ComboBox TextBlock">
    <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    <Setter Property="MaxLines" Value="1" />
  </Style>
  <Style Selector="ListBox">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="ToggleSwitch">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="CheckBox">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="RadioButton">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="ScrollBar">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="ScrollBar Thumb">
    <Setter Property="Cursor" Value="Hand" />
  </Style>
  <Style Selector="ScrollBar RepeatButton">
    <Setter Property="Cursor" Value="Hand" />
  </Style>

  <Style Selector="Control">
    <Setter Property="ToolTip.ShowDelay" Value="400" />
  </Style>
  <Style Selector="ToolTip">
    <Setter Property="Opacity" Value="0" />
    <Setter Property="Transitions">
      <Transitions>
        <DoubleTransition Property="Opacity" Duration="0:0:0.15" Easing="CubicEaseOut" />
      </Transitions>
    </Setter>
  </Style>
  <Style Selector="ToolTip:open">
    <Setter Property="Opacity" Value="1" />
  </Style>

  <!-- NoctraTextBox: one mobile-first input system for every host. -->
  <Style Selector="TextBox">
    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
    <Setter Property="Background" Value="{DynamicResource Surface1Brush}" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="Padding" Value="14,0" />
    <Setter Property="MinHeight" Value="54" />
    <Setter Property="FontSize" Value="{DynamicResource FBodyL}" />
    <Setter Property="TextWrapping" Value="NoWrap" />
    <Setter Property="AcceptsReturn" Value="False" />
    <Setter Property="VerticalContentAlignment" Value="Center" />
    <Setter Property="CaretBrush" Value="{DynamicResource AccentBrush}" />
    <Setter Property="SelectionBrush" Value="{DynamicResource TextSelectionBrush}" />
    <Setter Property="SelectionForegroundBrush" Value="{DynamicResource TextPrimaryBrush}" />
    <Setter Property="PlaceholderForeground" Value="{DynamicResource TextMutedBrush}" />
  </Style>
  <Style Selector="TextBox:focus /template/ Border#PART_BorderElement">
    <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
    <Setter Property="BorderThickness" Value="1.5" />
  </Style>
  <Style Selector="TextBox:not(:focus) /template/ Border#PART_BorderElement">
    <Setter Property="BorderBrush" Value="{DynamicResource TextDisabledBrush}" />
    <Setter Property="BorderThickness" Value="1.5" />
  </Style>
  <Style Selector="TextBox:disabled">
    <Setter Property="Opacity" Value="0.5" />
  </Style>
  <Style Selector="TextBox.url">
    <Setter Property="TextInputOptions.ContentType" Value="Url" />
    <Setter Property="TextInputOptions.ShowSuggestions" Value="False" />
  </Style>
  <Style Selector="TextBox.search">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="CornerRadius" Value="0" />
    <Setter Property="Padding" Value="10,0" />
    <Setter Property="MinHeight" Value="48" />
    <Setter Property="TextInputOptions.ContentType" Value="Search" />
    <Setter Property="TextInputOptions.ReturnKeyType" Value="Search" />
  </Style>
  <Style Selector="TextBox.search /template/ Border#PART_BorderElement">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="CornerRadius" Value="0" />
  </Style>
  <Style Selector="TextBox.search:focus /template/ Border#PART_BorderElement">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
  </Style>
  <Style Selector="TextBox.search:not(:focus) /template/ Border#PART_BorderElement">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
  </Style>
  <Style Selector="TextBox.password">
    <Setter Property="TextInputOptions.IsSensitive" Value="True" />
    <Setter Property="TextInputOptions.ShowSuggestions" Value="False" />
    <Setter Property="PasswordChar" Value="&#x2022;" />
  </Style>
  <Style Selector="TextBox.pin">
    <Setter Property="TextInputOptions.ContentType" Value="Digits" />
    <Setter Property="TextInputOptions.IsSensitive" Value="True" />
    <Setter Property="TextInputOptions.ShowSuggestions" Value="False" />
    <Setter Property="MaxLength" Value="4" />
    <Setter Property="PasswordChar" Value="&#x2022;" />
    <Setter Property="HorizontalContentAlignment" Value="Center" />
    <Setter Property="LetterSpacing" Value="8" />
  </Style>
  <Style Selector="TextBox.error">
    <Setter Property="BorderBrush" Value="{DynamicResource ErrorBrush}" />
    <Setter Property="BorderThickness" Value="1.5" />
  </Style>
  <Style Selector="TextBox.error:focus /template/ Border#PART_BorderElement">
    <Setter Property="BorderBrush" Value="{DynamicResource ErrorBrush}" />
    <Setter Property="BorderThickness" Value="1.5" />
  </Style>
  <Style Selector="TextBox.error:not(:focus) /template/ Border#PART_BorderElement">
    <Setter Property="BorderBrush" Value="{DynamicResource ErrorBrush}" />
    <Setter Property="BorderThickness" Value="1.5" />
  </Style>
  <Style Selector="TextBox.readonly">
    <Setter Property="IsReadOnly" Value="True" />
    <Setter Property="Background" Value="{DynamicResource Surface0Brush}" />
  </Style>

  <!-- Shared list/sheet primitives replacing duplicate desktop class names. -->
  <Style Selector="ListBox.NoctraSelectionList">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Padding" Value="0" />
  </Style>
  <Style Selector="ListBox.NoctraSelectionList > ListBoxItem">
    <Setter Property="Padding" Value="0" />
    <Setter Property="Margin" Value="0,0,0,8" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
    <Setter Property="Focusable" Value="False" />
  </Style>
  <Style Selector="Button.NoctraSheetOption">
    <Setter Property="MinHeight" Value="48" />
    <Setter Property="Padding" Value="14,8" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
    <Setter Property="BorderThickness" Value="0,0,0,1" />
    <Setter Property="CornerRadius" Value="8" />
    <Setter Property="HorizontalAlignment" Value="Stretch" />
    <Setter Property="HorizontalContentAlignment" Value="Stretch" />
  </Style>
  <Style Selector="Button.NoctraSheetOption:pointerover /template/ ContentPresenter">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
  </Style>
  <Style Selector="Button.NoctraSheetOption:pressed /template/ ContentPresenter">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}" />
  </Style>
'''

if 'Unified application interaction styles' in common:
    raise RuntimeError('CommonStyles already appears migrated')
common = add_before_closing(
    common,
    '</Styles>',
    shared_selector_styles + '\n\n  <!-- Desktop-only selectors live in the shared assembly; they are not duplicated by platform. -->\n' + desktop_modern_body.strip(),
)
write(COMMON_STYLES, common)

# 4. Redirect hosts to the shared resource dictionary and remove App-local style duplication.
mobile_app_path = ROOT / 'Noctra.Mobile/App.axaml'
desktop_app_path = ROOT / 'Noctra.Avalonia/App.axaml'
mobile_app = read(mobile_app_path)
desktop_app = read(desktop_app_path)
mobile_app = mobile_app.replace(
    'avares://Noctra.Mobile/Resources/Styles.axaml',
    'avares://Noctra.UI/Resources/Styles.axaml',
)
desktop_app = desktop_app.replace(
    'avares://Noctra/Resources/Styles.axaml',
    'avares://Noctra.UI/Resources/Styles.axaml',
)

mobile_app_styles = '''    <Application.Styles>\n        <materialIcons:MaterialIconStyles />\n        <FluentTheme />\n        <StyleInclude Source="avares://Noctra.UI/Controls/PremiumSpinner.axaml" />\n        <StyleInclude Source="avares://Noctra.Mobile/Controls/PremiumSpinner.axaml" />\n        <StyleInclude Source="avares://Noctra.UI/Resources/CommonStyles.axaml" />\n        <StyleInclude Source="avares://Noctra.UI/Resources/SettingsStyles.axaml" />\n    </Application.Styles>'''
desktop_app_styles = '''  <Application.Styles>\n    <materialIcons:MaterialIconStyles />\n    <FluentTheme />\n    <StyleInclude Source="avares://Noctra.UI/Controls/PremiumSpinner.axaml" />\n    <StyleInclude Source="avares://Noctra.UI/Resources/CommonStyles.axaml" />\n    <StyleInclude Source="avares://Noctra.UI/Resources/SettingsStyles.axaml" />\n    <StyleInclude Source="avares://Noctra/Controls/PremiumSpinner.axaml" />\n  </Application.Styles>'''
mobile_app, count = re.subn(r'\s*<Application\.Styles>.*?</Application\.Styles>', '\n\n' + mobile_app_styles, mobile_app, count=1, flags=re.S)
if count != 1:
    raise RuntimeError('Could not replace Mobile Application.Styles')
desktop_app, count = re.subn(r'\s*<Application\.Styles>.*?</Application\.Styles>', '\n' + desktop_app_styles, desktop_app, count=1, flags=re.S)
if count != 1:
    raise RuntimeError('Could not replace Desktop Application.Styles')
write(mobile_app_path, mobile_app)
write(desktop_app_path, desktop_app)

# 5. Replace near-identical desktop class/theme usages with canonical shared names.
replacements = {
    'Classes="DesktopPageTitle"': 'Classes="NoctraPageTitle"',
    'Classes="DesktopIconButton"': 'Theme="{StaticResource IconButton}"',
    'Classes="DesktopSettingsSelectionButton"': 'Classes="SettingsActionButton"',
    'Classes="DesktopSelectionList"': 'Classes="NoctraSelectionList"',
    'Classes="DesktopCategoryList"': 'Classes="NoctraSelectionList"',
    'Classes="DesktopSheetOption"': 'Classes="NoctraSheetOption"',
    'Classes="DesktopCategoryRow"': 'Classes="NoctraSheetOption"',
    'Classes="DesktopRevealButton"': 'Theme="{StaticResource RevealBtn}"',
    'Theme="{StaticResource SettingsActionButton}"': 'Classes="SettingsActionButton"',
    'Theme="{StaticResource UpgradeButton}"': 'Classes="UpgradeButton"',
}
for path in (ROOT / 'Noctra.Avalonia').rglob('*.axaml'):
    text = read(path)
    original = text
    for old, new in replacements.items():
        text = text.replace(old, new)
    if text != original:
        write(path, text)

# 6. Update source-contract tests to read canonical shared style locations.
search_test_path = ROOT / 'Noctra.Tests/SharedMobileSearchInputContractTests.cs'
search_test = read(search_test_path)
search_test = search_test.replace(
    'var mobileApp = Source("Noctra.Mobile", "App.axaml");',
    'var sharedStyles = Source("Noctra.UI", "Resources", "CommonStyles.axaml");',
)
search_test = search_test.replace('mobileApp, StringComparison.Ordinal', 'sharedStyles, StringComparison.Ordinal')
write(search_test_path, search_test)

release_guard_path = ROOT / 'Noctra.Tests/MobileReleaseGuardTests.cs'
release_guard = read(release_guard_path)
release_guard = release_guard.replace(
    'ReadProjectFile("Noctra.Mobile", "Resources", "Styles.axaml")',
    'ReadProjectFile("Noctra.UI", "Resources", "Styles.axaml")',
)
# App-local TextBox assertions now belong to CommonStyles. Replace only the style assertions,
# not unrelated App checks in the same test file.
if 'var commonStyles = ReadProjectFile("Noctra.UI", "Resources", "CommonStyles.axaml");' not in release_guard:
    release_guard = release_guard.replace(
        'var app = ReadProjectFile("Noctra.Mobile", "App.axaml");\n        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");',
        'var app = ReadProjectFile("Noctra.Mobile", "App.axaml");\n        var commonStyles = ReadProjectFile("Noctra.UI", "Resources", "CommonStyles.axaml");\n        var profile = ReadProjectFile("Noctra.Mobile", "Views", "ProfileSetupView.axaml");',
        1,
    )
    marker = 'public void MobileTextBoxes_UseAccessibleTouchTargetsAndNoctraSelectionColors()'
    idx = release_guard.find(marker)
    if idx >= 0:
        brace = release_guard.find('{', idx)
        insert_at = release_guard.find('\n', brace) + 1
        release_guard = release_guard[:insert_at] + '        var commonStyles = ReadProjectFile("Noctra.UI", "Resources", "CommonStyles.axaml");\n' + release_guard[insert_at:]

for expected in [
    '<Style Selector=\\"TextBox.search\\">',
    'TextInputOptions.ContentType\\" Value=\\"Search\\"',
    'TextInputOptions.ReturnKeyType\\" Value=\\"Search\\"',
    '<Style Selector=\\"TextBox.password\\">',
    'TextInputOptions.IsSensitive\\" Value=\\"True\\"',
    '<Style Selector=\\"TextBox.pin\\">',
    'TextInputOptions.ContentType\\" Value=\\"Digits\\"',
    '<Style Selector=\\"TextBox\\">',
    '<Setter Property=\\"MinHeight\\" Value=\\"48\\"',
    '<Setter Property=\\"CaretBrush\\" Value=\\"{DynamicResource AccentBrush}\\"',
    '<Setter Property=\\"SelectionBrush\\" Value=\\"{DynamicResource TextSelectionBrush}\\"',
    '<Setter Property=\\"SelectionForegroundBrush\\" Value=\\"{DynamicResource TextPrimaryBrush}\\"',
]:
    release_guard = release_guard.replace(f'Assert.Contains("{expected}", app)', f'Assert.Contains("{expected}", commonStyles)')
write(release_guard_path, release_guard)

# 7. Strengthen canonicalization tests and architecture contract.
canonical_path = ROOT / 'Noctra.Tests/SharedThemeCanonicalizationTests.cs'
canonical = read(canonical_path)
canonical = canonical.replace(
    'Path.Combine("Noctra.Mobile", "Resources", "LightTheme.axaml"),',
    'Path.Combine("Noctra.Mobile", "Resources", "LightTheme.axaml"),\n                     Path.Combine("Noctra.Mobile", "Resources", "Styles.axaml"),',
)
canonical = canonical.replace(
    'Path.Combine("Noctra.Avalonia", "Resources", "LightTheme.axaml")',
    'Path.Combine("Noctra.Avalonia", "Resources", "LightTheme.axaml"),\n                     Path.Combine("Noctra.Avalonia", "Resources", "Styles.axaml"),\n                     Path.Combine("Noctra.Avalonia", "Resources", "DesktopModernStyles.axaml")',
)
style_test = r'''

    [Fact]
    public void BothHosts_LoadOneMobileFirstSharedStyleSystem()
    {
        var sharedThemes = Read("Noctra.UI", "Resources", "Styles.axaml");
        var commonStyles = Read("Noctra.UI", "Resources", "CommonStyles.axaml");
        var mobileApp = Read("Noctra.Mobile", "App.axaml");
        var desktopApp = Read("Noctra.Avalonia", "App.axaml");

        foreach (var app in new[] { mobileApp, desktopApp })
            Assert.Contains("avares://Noctra.UI/Resources/Styles.axaml", app, StringComparison.Ordinal);

        Assert.Contains("x:Key=\"PrimaryButtonStyle\"", sharedThemes, StringComparison.Ordinal);
        Assert.Contains("PrimaryGradientBrush", sharedThemes, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SecondaryButton\"", sharedThemes, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"RevealBtn\"", sharedThemes, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"{x:Type CheckBox}\"", sharedThemes, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"{x:Type RadioButton}\"", sharedThemes, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"{x:Type ToggleSwitch}\"", sharedThemes, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"UpgradeButton\"", sharedThemes, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"SettingsActionButton\"", sharedThemes, StringComparison.Ordinal);

        Assert.Contains("<Style Selector=\"TextBox\">", commonStyles, StringComparison.Ordinal);
        Assert.Contains("FontSize\" Value=\"{DynamicResource FBodyL}\"", commonStyles, StringComparison.Ordinal);
        Assert.Contains("Button.NoctraSheetOption", commonStyles, StringComparison.Ordinal);
        Assert.Contains("ListBox.NoctraSelectionList", commonStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopModernStyles.axaml", desktopApp, StringComparison.Ordinal);
    }
'''
canonical = canonical.replace('\n    private static string Read(params string[] path)', style_test + '\n    private static string Read(params string[] path)')
write(canonical_path, canonical)

architecture_path = ROOT / 'Noctra.Tests/SharedUiArchitectureContractTests.cs'
architecture = read(architecture_path)
architecture = architecture.replace(
    'Path.Combine("Resources", "CommonStyles.axaml"),',
    'Path.Combine("Resources", "Styles.axaml"),\n                     Path.Combine("Resources", "CommonStyles.axaml"),',
)
architecture = architecture.replace(
    'Assert.Contains("avares://Noctra.UI/Resources/CommonStyles.axaml", app, StringComparison.Ordinal);',
    'Assert.Contains("avares://Noctra.UI/Resources/Styles.axaml", app, StringComparison.Ordinal);\n            Assert.Contains("avares://Noctra.UI/Resources/CommonStyles.axaml", app, StringComparison.Ordinal);',
)
write(architecture_path, architecture)

# 8. Platform style files have no remaining authority.
MOBILE_STYLES.unlink()
DESKTOP_STYLES.unlink()
DESKTOP_MODERN.unlink()

print('Shared style migration complete.')
