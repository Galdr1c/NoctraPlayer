# Contributing to Noctra

First off, thank you for considering contributing to Noctra! It's people like you that make open-source tools great.

## Development Setup

1. **Prerequisites:**
   - .NET 8.0 SDK
   - Avalonia UI extensions for your IDE (Visual Studio, Rider, or VS Code)
   - Node.js (Optional, for any frontend tooling if added later)

2. **Building the Project:**
   ```powershell
   dotnet restore
   dotnet build Noctra.sln
   ```

3. **Running the Application:**
   ```powershell
   dotnet run --project Noctra.Avalonia/Noctra.Avalonia.csproj
   ```

4. **Running Tests:**
   We strictly enforce tests for new features. Ensure existing tests pass:
   ```powershell
   dotnet test Noctra.Tests/Noctra.Tests.csproj
   ```

## Workflow

1. **Fork & Clone:** Fork the repository and clone your fork locally.
2. **Branch:** Create a feature branch (`git checkout -b feature/amazing-feature`).
3. **Code:** Write your code, adhering to the existing `.editorconfig` style guidelines.
4. **Test:** Add unit tests for your changes. Run the full test suite.
5. **Commit:** Write clear, concise commit messages.
6. **Pull Request:** Open a PR against the `main` branch. Describe your changes in detail.

## Code Style

- We use standard C# conventions enforced by `.editorconfig`.
- Avoid `async void` unless it's an event handler (and wrap it in try-catch).
- Use `CommunityToolkit.Mvvm` attributes (`[ObservableProperty]`, `[RelayCommand]`) for ViewModels.
- Keep UI logic out of the Core project.

## Reporting Issues

If you find a bug, please create an issue with:
- Steps to reproduce
- Expected vs actual behavior
- OS and .NET version
- App logs (if applicable)
