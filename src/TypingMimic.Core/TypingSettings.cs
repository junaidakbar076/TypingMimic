namespace TypingMimic.Core;

public enum NewLineShortcut
{
    Enter,
    ShiftEnter,
    CtrlEnter,
    AltEnter
}

public static class NewLineShortcutExtensions
{
    public static string ToDisplayString(this NewLineShortcut shortcut) => shortcut switch
    {
        NewLineShortcut.Enter => "Enter",
        NewLineShortcut.ShiftEnter => "Shift+Enter",
        NewLineShortcut.CtrlEnter => "Ctrl+Enter",
        NewLineShortcut.AltEnter => "Alt+Enter",
        _ => shortcut.ToString()
    };

    public static bool TryParse(string? text, out NewLineShortcut shortcut)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            shortcut = NewLineShortcut.Enter;
            return false;
        }

        string normalized = text.Trim().Replace(" ", "").Replace("+", "").ToUpperInvariant();
        switch (normalized)
        {
            case "ENTER":
            case "RETURN":
                shortcut = NewLineShortcut.Enter;
                return true;
            case "SHIFTENTER":
            case "SHIFTRETURN":
                shortcut = NewLineShortcut.ShiftEnter;
                return true;
            case "CTRLENTER":
            case "CONTROLENTER":
            case "CTRLRETURN":
            case "CONTROLRETURN":
                shortcut = NewLineShortcut.CtrlEnter;
                return true;
            case "ALTENTER":
            case "ALTRETURN":
                shortcut = NewLineShortcut.AltEnter;
                return true;
            default:
                shortcut = NewLineShortcut.Enter;
                return false;
        }
    }

    public static NewLineShortcut Parse(string text)
    {
        if (TryParse(text, out var result)) return result;
        throw new ArgumentException("New line shortcut must be Enter, Shift+Enter, Ctrl+Enter, or Alt+Enter.");
    }
}

public sealed record TypingSettings
{
    public double WordsPerMinute { get; init; } = 60;
    public double VariationPercent { get; init; } = 15;
    public double ErrorPercent { get; init; } = 2;
    public int CountdownSeconds { get; init; } = 5;
    public int CommaPauseMs { get; init; } = 150;
    public int SentencePauseMs { get; init; } = 350;
    public int NewLinePauseMs { get; init; } = 500;
    public bool PreserveLineBreaks { get; init; } = true;
    public NewLineShortcut NewLineShortcut { get; init; } = NewLineShortcut.Enter;
    public bool EnableErrors { get; init; } = true;
    public bool StopOnWindowChange { get; init; } = true;

    public void Validate()
    {
        Check(WordsPerMinute, 1, 1000, "WPM");
        Check(VariationPercent, 0, 100, "Speed variation");
        Check(ErrorPercent, 0, 25, "Error rate");
        Check(CountdownSeconds, 1, 60, "Countdown");
        Check(CommaPauseMs, 0, 10000, "Comma pause");
        Check(SentencePauseMs, 0, 10000, "Sentence pause");
        Check(NewLinePauseMs, 0, 10000, "Newline pause");
        if (!Enum.IsDefined(NewLineShortcut))
            throw new ArgumentException("Unknown newline shortcut.");
    }

    private static void Check(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name} must be between {min} and {max}.");
    }

    // Windows Enter or shortcut is one logical newline. Flattening is explicit and previewed by the UI.
    public string PrepareText(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return PreserveLineBreaks ? text : text.Replace('\n', ' ');
    }
}

