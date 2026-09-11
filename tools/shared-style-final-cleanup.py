from pathlib import Path
import re

ROOT = Path('.')


def read(path):
    return Path(path).read_text(encoding='utf-8')


def write(path, text):
    Path(path).write_text(text.replace('\r\n', '\n'), encoding='utf-8')

# Move the reusable transparent ListBoxItem ControlTheme out of Mobile App resources.
mobile_app_path = 'Noctra.Mobile/App.axaml'
shared_styles_path = 'Noctra.UI/Resources/Styles.axaml'
mobile_app = read(mobile_app_path)
shared_styles = read(shared_styles_path)

pattern = re.compile(
    r'\s*<ControlTheme x:Key="TransparentListBoxItemTheme" TargetType="ListBoxItem">.*?</ControlTheme>\s*',
    re.S,
)
match = pattern.search(mobile_app)
if not match:
    raise RuntimeError('TransparentListBoxItemTheme was not found in Mobile App')
block = match.group(0).strip()
if 'x:Key="TransparentListBoxItemTheme"' in shared_styles:
    raise RuntimeError('TransparentListBoxItemTheme already exists in shared Styles')
mobile_app = pattern.sub('\n', mobile_app, count=1)
insert = shared_styles.rfind('</ResourceDictionary>')
if insert < 0:
    raise RuntimeError('Shared Styles closing ResourceDictionary not found')
shared_styles = (
    shared_styles[:insert]
    + '\n  <!-- Shared item-container theme used by mobile virtualized/list surfaces. -->\n  '
    + block.replace('\n', '\n  ')
    + '\n\n'
    + shared_styles[insert:]
)
# Remove the now-empty migration heading left after desktop geometry tokens moved out.
shared_styles = shared_styles.replace(
    '    <!-- ==========================================\n         UI SCALING & RATIOS\n         ========================================== -->\n',
    '',
)
write(mobile_app_path, mobile_app)
write(shared_styles_path, shared_styles)

# MainWindow had a near-identical local UpgradeButton copy. The canonical mobile-first
# Button.UpgradeButton selector already lives in shared SettingsStyles.axaml.
main_path = 'Noctra.Avalonia/MainWindow.axaml'
main = read(main_path)
for selector in [
    'Button.UpgradeButton',
    'Button.UpgradeButton:pressed /template/ ContentPresenter',
]:
    style_pattern = re.compile(
        r'\s*<Style Selector="' + re.escape(selector) + r'">.*?</Style>\s*',
        re.S,
    )
    main, count = style_pattern.subn('\n', main, count=1)
    if count != 1:
        raise RuntimeError(f'Expected one local duplicate style: {selector}')
write(main_path, main)

# Strengthen the architecture contract around these last duplicate removals.
test_path = 'Noctra.Tests/SharedThemeCanonicalizationTests.cs'
test = read(test_path)
needle = '''        Assert.DoesNotContain("DesktopModernStyles.axaml", desktopApp, StringComparison.Ordinal);
        Assert.Contains("avares://Noctra/Resources/DesktopStyleOverrides.axaml", desktopApp, StringComparison.Ordinal);
'''
replacement = needle + '''        Assert.Contains("x:Key=\\"TransparentListBoxItemTheme\\"", sharedThemes, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\\"TransparentListBoxItemTheme\\"", mobileApp, StringComparison.Ordinal);

        var desktopMainWindow = Read("Noctra.Avalonia", "MainWindow.axaml");
        Assert.DoesNotContain("<Style Selector=\\"Button.UpgradeButton\\">", desktopMainWindow, StringComparison.Ordinal);
        Assert.Contains("Classes=\\"UpgradeButton\\"", desktopMainWindow, StringComparison.Ordinal);
'''
if needle not in test:
    raise RuntimeError('Shared style-system assertion insertion point not found')
test = test.replace(needle, replacement, 1)
write(test_path, test)

# Mobile virtualization regression should verify that the canonical theme exists in
# shared Styles, while the mobile surfaces continue to consume it by key.
recent_path = 'Noctra.Tests/MobileRecentRegressionTests.cs'
recent = read(recent_path)
old = '''        var app = File.ReadAllText(ProjectFile("Noctra.Mobile", "App.axaml"));
        var live = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileLiveView.axaml"));'''
new = '''        var app = File.ReadAllText(ProjectFile("Noctra.Mobile", "App.axaml"));
        var sharedStyles = File.ReadAllText(ProjectFile("Noctra.UI", "Resources", "Styles.axaml"));
        var live = File.ReadAllText(ProjectFile("Noctra.Mobile", "Views", "MobileLiveView.axaml"));'''
if old not in recent:
    raise RuntimeError('MobileRecentRegression setup block not found')
recent = recent.replace(old, new, 1)
old_assert = '        Assert.Contains("x:Key=\\"TransparentListBoxItemTheme\\"", app, StringComparison.Ordinal);'
new_assert = '''        Assert.Contains("x:Key=\\"TransparentListBoxItemTheme\\"", sharedStyles, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\\"TransparentListBoxItemTheme\\"", app, StringComparison.Ordinal);'''
if old_assert not in recent:
    raise RuntimeError('MobileRecentRegression legacy App theme assertion not found')
recent = recent.replace(old_assert, new_assert, 1)
write(recent_path, recent)

print('Final shared style cleanup applied.')
