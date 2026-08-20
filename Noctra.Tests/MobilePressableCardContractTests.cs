namespace Noctra.Tests;

public sealed class MobilePressableCardContractTests
{
    [Fact]
    public void PressableCard_SubscribesToParentScrollOnlyWhilePressed()
    {
        var source = File.ReadAllText(ProjectSource(
            "Noctra.Mobile", "Controls", "MobilePressableCard.cs"));
        var attached = MethodBody(source, "private void OnAttachedToVisualTree");
        var pressed = MethodBody(source, "private void OnPointerPressed");
        var reset = MethodBody(source, "private void ResetPressedState");

        Assert.DoesNotContain("ScrollChanged +=", attached, StringComparison.Ordinal);
        Assert.Contains("AttachToAncestorScrollViewer", pressed, StringComparison.Ordinal);
        Assert.Contains("DetachFromAncestorScrollViewer", reset, StringComparison.Ordinal);
        Assert.Contains("ScrollChanged +=", source, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string methodName)
    {
        var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Method not found: {methodName}");
        var braceStart = source.IndexOf('{', methodStart);
        Assert.True(braceStart >= 0, $"Method body not found: {methodName}");

        var depth = 0;
        for (var index = braceStart; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };

            if (depth == 0)
            {
                return source[braceStart..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Unterminated method body: {methodName}");
    }

    private static string ProjectSource(params string[] segments)
        => Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }
                .Concat(segments)
                .ToArray()));
}
