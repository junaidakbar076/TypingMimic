using TypingMimic.Core;
using Xunit;

namespace TypingMimic.Tests;

public sealed class ShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F11", 3u, 0x7Au, "Ctrl+Alt+F11")]
    [InlineData(" shift + control + p ", 6u, 0x50u, "Ctrl+Shift+P")]
    [InlineData("Alt+1", 1u, 0x31u, "Alt+1")]
    [InlineData("Ctrl+F24", 2u, 0x87u, "Ctrl+F24")]
    public void ParsesAndNormalizesShortcut(string input, uint modifiers, uint key, string display)
    {
        var result = ShortcutGesture.Parse(input);
        Assert.Equal(modifiers, result.Modifiers);
        Assert.Equal(key, result.Key);
        Assert.Equal(display, result.ToString());
        Assert.Equal(result, ShortcutGesture.Parse(display));
    }

    [Theory]
    [InlineData("")] [InlineData("F11")] [InlineData("Shift+A")]
    [InlineData("Ctrl+Ctrl+A")] [InlineData("Win+A")]
    [InlineData("Ctrl+F25")] [InlineData("Alt+")] [InlineData("Ctrl+Enter")]
    public void RejectsInvalidShortcuts(string text) => Assert.Throws<ArgumentException>(() => ShortcutGesture.Parse(text));
}
