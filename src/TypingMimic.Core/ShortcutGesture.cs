namespace TypingMimic.Core;

public sealed record ShortcutGesture(uint Modifiers, uint Key)
{
    public static ShortcutGesture Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) throw new ArgumentException("Use Ctrl or Alt plus a letter, digit, or F1–F24 (for example Ctrl+Alt+F11).");
        uint modifiers = 0;
        foreach (string part in parts[..^1])
        {
            uint flag = part.ToUpperInvariant() switch
            {
                "ALT" => 1, "CTRL" or "CONTROL" => 2, "SHIFT" => 4,
                _ => throw new ArgumentException("Shortcut modifiers can be Ctrl, Alt, and Shift.")
            };
            if ((modifiers & flag) != 0) throw new ArgumentException("Do not repeat a shortcut modifier.");
            modifiers |= flag;
        }
        if ((modifiers & 3) == 0) throw new ArgumentException("Shortcuts must include Ctrl or Alt.");
        string key = parts[^1].ToUpperInvariant();
        uint code;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) code = key[0];
        else if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int number) && number is >= 1 and <= 24)
            code = (uint)(0x70 + number - 1);
        else throw new ArgumentException("Use a letter, digit, or function key F1–F24.");
        return new(modifiers, code);
    }

    public override string ToString() =>
        ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "") +
        ((Modifiers & 4) != 0 ? "Shift+" : "") + (Key is >= 0x70 and <= 0x87 ? $"F{Key - 0x70 + 1}" : ((char)Key).ToString());
}
