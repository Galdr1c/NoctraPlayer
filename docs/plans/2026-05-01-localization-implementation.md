# Localization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Noctra için runtime dil değiştirmeyi destekleyen merkezi yerelleştirme altyapısını kurmak ve ilk fazda shell ile ayarlar ekranlarını bu altyapıya taşımak.

**Architecture:** Çeviri verileri JSON dosyalarında tutulacak, `ILocalizationService` bunları yükleyip fallback yönetecek, Avalonia tarafında bir translate bridge ile XAML metinleri runtime güncellenecek. İlk faz yalnızca `MainWindow`, `SettingsWindow` ve `GlobalSettingsWindow` sabit UI metinlerini kapsayacak.

**Tech Stack:** .NET 8, Avalonia 11, CommunityToolkit.Mvvm, xUnit

---

### Task 1: Localization Service Contract

**Files:**
- Create: `Noctra.Core/Services/Interfaces/ILocalizationService.cs`
- Test: `Noctra.Tests/LocalizationServiceTests.cs`

**Step 1: Write the failing test**

- `CurrentLanguage` default behavior
- `SetLanguage` normalization
- `LanguageChanged` event

**Step 2: Run test to verify it fails**

Run: `dotnet test Noctra.Tests/Noctra.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"`

Expected: FAIL because service contract and implementation do not exist.

**Step 3: Write minimal implementation contract**

- Add `CurrentLanguage`
- Add `GetString(string key)`
- Add `SetLanguage(string language)`
- Add `event Action? LanguageChanged`

**Step 4: Run test to verify partial compile progress**

Run: `dotnet test Noctra.Tests/Noctra.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"`

Expected: Compile failure moves from missing interface to missing implementation.

**Step 5: Commit**

```bash
git add Noctra.Core/Services/Interfaces/ILocalizationService.cs Noctra.Tests/LocalizationServiceTests.cs
git commit -m "test: define localization service contract"
```

### Task 2: Implement Translation Loading and Fallback

**Files:**
- Create: `Noctra.Core/Services/LocalizationService.cs`
- Create: `Noctra.Core/Localization/Translations/tr-TR.json`
- Create: `Noctra.Core/Localization/Translations/en-US.json`
- Create: `Noctra.Core/Localization/Translations/de-DE.json`
- Create: `Noctra.Core/Localization/Translations/fr-FR.json`
- Create: `Noctra.Core/Localization/Translations/es-ES.json`
- Modify: `Noctra.Tests/LocalizationServiceTests.cs`

**Step 1: Write the failing tests**

- Selected language returns selected translation
- Missing selected translation falls back to `en-US`
- Missing in all dictionaries returns key

**Step 2: Run test to verify it fails**

Run: `dotnet test Noctra.Tests/Noctra.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"`

Expected: FAIL on missing implementation behavior.

**Step 3: Write minimal implementation**

- Load JSON dictionaries
- Normalize short language codes
- Return translations with fallback
- Log missing keys

**Step 4: Run test to verify it passes**

Run: `dotnet test Noctra.Tests/Noctra.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"`

Expected: PASS

**Step 5: Commit**

```bash
git add Noctra.Core/Services/LocalizationService.cs Noctra.Core/Localization/Translations/*.json Noctra.Tests/LocalizationServiceTests.cs
git commit -m "feat: add localization service with translation fallback"
```

### Task 3: Register Localization in App Startup

**Files:**
- Modify: `Noctra.Avalonia/App.axaml.cs`
- Modify: service registration location inside `Noctra.Avalonia/App.axaml.cs`
- Test: `Noctra.Tests/LocalizationServiceTests.cs`

**Step 1: Write the failing test**

- Verify language normalization and startup-set flow through service-level behavior where practical

**Step 2: Run test to verify it fails**

Run: `dotnet test Noctra.Tests/Noctra.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"`

Expected: FAIL if initialization flow is incomplete.

**Step 3: Write minimal implementation**

- Register `ILocalizationService`
- Resolve service during app initialization
- Apply `settings.Settings.Language`
- Update service on `SettingsChanged`

**Step 4: Run targeted tests**

Run: `dotnet test Noctra.Tests/Noctra.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"`

Expected: PASS

**Step 5: Commit**

```bash
git add Noctra.Avalonia/App.axaml.cs
git commit -m "feat: initialize localization from application settings"
```

### Task 4: Build Avalonia Translate Bridge

**Files:**
- Create: `Noctra.Avalonia/Localization/LocalizationExtension.cs`
- Modify: `Noctra.Avalonia/App.axaml`
- Modify: `Noctra.Avalonia/Noctra.Avalonia.csproj`
- Test: manual UI verification

**Step 1: Write the failing integration check**

- Identify one temporary binding usage in XAML and confirm it cannot compile before extension exists

**Step 2: Run build to verify it fails**

Run: `dotnet build`

Expected: FAIL on missing `loc:Translate` extension or namespace.

**Step 3: Write minimal implementation**

- Add extension class
- Resolve `ILocalizationService`
- Return binding/observable-friendly localized value
- Add XAML namespace usage support

**Step 4: Run build to verify it passes**

Run: `dotnet build`

Expected: PASS

**Step 5: Commit**

```bash
git add Noctra.Avalonia/Localization/LocalizationExtension.cs Noctra.Avalonia/App.axaml Noctra.Avalonia/Noctra.Avalonia.csproj
git commit -m "feat: add Avalonia localization bridge"
```

### Task 5: Localize Global Settings Screen

**Files:**
- Modify: `Noctra.Avalonia/Views/GlobalSettingsWindow.axaml`
- Modify: `Noctra.Core/Localization/Translations/*.json`

**Step 1: Replace embedded strings with translation keys**

- Language section
- General section
- Auto-update labels
- Other visible static labels inside first phase scope

**Step 2: Run build to verify it passes**

Run: `dotnet build`

Expected: PASS

**Step 3: Manual verification**

- Open app
- Change language in global settings
- Confirm visible labels update immediately

**Step 4: Commit**

```bash
git add Noctra.Avalonia/Views/GlobalSettingsWindow.axaml Noctra.Core/Localization/Translations/*.json
git commit -m "feat: localize global settings window"
```

### Task 6: Localize Settings Screen

**Files:**
- Modify: `Noctra.Avalonia/Views/SettingsWindow.axaml`
- Modify: `Noctra.Core/Localization/Translations/*.json`

**Step 1: Replace embedded strings with translation keys**

- App language labels
- Section headings
- First phase static descriptions and toggles

**Step 2: Run build to verify it passes**

Run: `dotnet build`

Expected: PASS

**Step 3: Manual verification**

- Change language from settings window
- Confirm current screen texts refresh

**Step 4: Commit**

```bash
git add Noctra.Avalonia/Views/SettingsWindow.axaml Noctra.Core/Localization/Translations/*.json
git commit -m "feat: localize settings window"
```

### Task 7: Localize Main Window Shell

**Files:**
- Modify: `Noctra.Avalonia/MainWindow.axaml`
- Modify: `Noctra.Core/Localization/Translations/*.json`

**Step 1: Replace embedded shell strings**

- Search placeholder
- Search tooltip
- Settings tooltip
- Static shell labels in first phase scope

**Step 2: Run build to verify it passes**

Run: `dotnet build`

Expected: PASS

**Step 3: Manual verification**

- Confirm tooltips and watermark change with language switch

**Step 4: Commit**

```bash
git add Noctra.Avalonia/MainWindow.axaml Noctra.Core/Localization/Translations/*.json
git commit -m "feat: localize main window shell"
```

### Task 8: Verify and Stabilize

**Files:**
- Modify: `Noctra.Tests/LocalizationServiceTests.cs`
- Modify: any touched localization files if regressions are found

**Step 1: Run targeted tests**

Run: `dotnet test Noctra.Tests/Noctra.Tests.csproj --filter "FullyQualifiedName~LocalizationServiceTests"`

Expected: PASS

**Step 2: Run full test suite**

Run: `dotnet test Noctra.Tests/Noctra.Tests.csproj`

Expected: PASS

**Step 3: Run build**

Run: `dotnet build`

Expected: PASS

**Step 4: Manual smoke test**

- Launch app
- Switch among `tr`, `en`, `de`, `fr`, `es`
- Confirm texts persist after restart

**Step 5: Commit**

```bash
git add Noctra.Tests/LocalizationServiceTests.cs Noctra.Avalonia/MainWindow.axaml Noctra.Avalonia/Views/SettingsWindow.axaml Noctra.Avalonia/Views/GlobalSettingsWindow.axaml Noctra.Core/Services/LocalizationService.cs Noctra.Core/Localization/Translations/*.json
git commit -m "test: verify phase one localization flow"
```
