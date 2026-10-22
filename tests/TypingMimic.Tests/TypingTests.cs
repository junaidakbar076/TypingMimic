using System.Text;
using System.IO;
using TypingMimic.Core;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Core;
using Xunit;

namespace TypingMimic.Tests;

public sealed class TypingTests
{
    [Theory]
    [InlineData(1)] [InlineData(1000)] [InlineData(100000)]
    public void RandomTextHasRequestedLengthAndPrintableContent(int length)
    {
        var text = new RandomTextGenerator(new Random(42)).Generate(length, true);
        Assert.Equal(length, text.Length);
        Assert.All(text, c => Assert.True(char.IsAsciiLetter(c) || " ,.!?\n".Contains(c)));
        Assert.All(text.Split(new[] { ' ', ',', '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries),
            word => Assert.Contains(word, RandomTextGenerator.Words));
    }

    [Fact]
    public void RandomEnglishWordsAreNeverTruncatedAtSmallCharacterLimits()
    {
        var generator = new RandomTextGenerator(new Random(123));
        for (int length = 1; length <= 200; length++)
        {
            string text = generator.Generate(length, false);
            Assert.Equal(length, text.Length);
            Assert.All(text.Split(new[] { ' ', ',', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries),
                word => Assert.Contains(word, RandomTextGenerator.Words));
        }
    }

    [Fact]
    public void RandomTextRespectsLineBreakSettingAndVariesBetweenSessions()
    {
        var generator = new RandomTextGenerator(new Random(42));
        var first = generator.Generate(1000, true);
        Assert.Contains('\n', first);
        Assert.Contains(',', first);
        Assert.Contains('.', first);
        Assert.NotEqual(first, generator.Generate(1000, true));
        Assert.DoesNotContain('\n', generator.Generate(1000, false));
        Assert.Throws<ArgumentException>(() => generator.Generate(0, true));
        Assert.Throws<ArgumentException>(() => generator.Generate(100001, true));
    }

    [Fact]
    public async Task RandomTextUsesNormalTypingEngineAndTemporaryCorrections()
    {
        var source = new RandomTextGenerator(new Random(42)).Generate(30, true);
        var keyboard = new FakeKeyboard();
        var engine = new TypingEngine(keyboard, new FakeForeground(), errors: new TemporaryErrorGenerator(new AlwaysErrorRandom()));
        await engine.RunAsync(source, Fast with { EnableErrors = true, ErrorPercent = 25 }, 1, null, CancellationToken.None);
        Assert.Equal(source, keyboard.Output);
        Assert.True(keyboard.Backspaces > 0);
    }

    private static readonly TypingSettings Fast = new()
    {
        WordsPerMinute = 1000, VariationPercent = 0, EnableErrors = false,
        CommaPauseMs = 0, SentencePauseMs = 0, NewLinePauseMs = 0
    };

    [Theory]
    [InlineData(60, 200)] [InlineData(120, 100)] [InlineData(1, 12000)]
    public void WpmUsesFiveCharactersPerWord(double wpm, double expected) =>
        Assert.Equal(expected, HumanTypingDelayCalculator.BaseDelayMs(wpm));

    [Fact]
    public void RandomDelayStaysWithinBounds()
    {
        var calculator = new HumanTypingDelayCalculator(new Random(42));
        var settings = new TypingSettings();
        var samples = Enumerable.Range(0, 1000).Select(_ => calculator.NextDelayMs(settings, "a")).ToArray();
        Assert.All(samples, d => Assert.InRange(d, 170, 230));
        Assert.True(samples.Distinct().Count() > 900);
        Assert.InRange(calculator.NextDelayMs(settings, ","), 320, 380);
        Assert.InRange(calculator.NextDelayMs(settings, "!"), 520, 580);
        Assert.InRange(calculator.NextDelayMs(settings, "\n"), 670, 730);
    }

    [Fact]
    public void ErrorRateIsApproximatelyConfiguredProbability()
    {
        var generator = new TemporaryErrorGenerator(new Random(12));
        int errors = Enumerable.Range(0, 100000).Count(_ => generator.Choose('g', 2).HasValue);
        Assert.InRange(errors, 1800, 2200);
        Assert.All(Enumerable.Range(0, 1000), _ => Assert.Null(generator.Choose('g', 0)));
    }

    [Fact]
    public void MistakesAreKeyboardNeighborsAndPreserveCase()
    {
        var generator = new TemporaryErrorGenerator(new Random(1));
        for (int i = 0; i < 500; i++)
        {
            Assert.Contains(generator.Choose('g', 100)!.Value, "fhtyvb");
            Assert.Contains(generator.Choose('G', 100)!.Value, "FHTYVB");
        }
        Assert.Null(generator.Choose('é', 100));
        Assert.Null(generator.Choose('7', 100));
    }

    [Theory]
    [InlineData("https://example.com/a?x=1")]
    [InlineData("www.example.com")]
    [InlineData("name@example.com")]
    [InlineData("1234567890123456789")]
    [InlineData(@"C:\Users\someone\file.txt")]
    [InlineData("/usr/local/file")]
    [InlineData("camelCase snake_case foo() x=>y")]
    [InlineData("var value = hello;")]
    [InlineData("```\nhello world\n```")]
    [InlineData("`hello`")]
    [InlineData(" \t,.!?—")]
    public void SensitiveTokensAreProtected(string source) =>
        Assert.DoesNotContain(true, TemporaryErrorGenerator.EligiblePositions(source));

    [Fact]
    public void PlainProseIsEligibleButPunctuationIsNot()
    {
        var mask = TemporaryErrorGenerator.EligiblePositions("Hello, world!");
        Assert.Equal(10, mask.Count(x => x));
        Assert.False(mask[5]); Assert.False(mask[6]); Assert.False(mask[12]);
    }

    [Fact]
    public async Task CompletedOutputMatchesUnicodeSourceAfterTemporaryErrors()
    {
        var keyboard = new FakeKeyboard();
        var engine = new TypingEngine(keyboard, new FakeForeground(), errors: new TemporaryErrorGenerator(new AlwaysErrorRandom()));
        const string source = "Good day! café 中文 😀\nhttps://example.com\t123456789";
        await engine.RunAsync(source, Fast with { EnableErrors = true, ErrorPercent = 25 }, 1, null, CancellationToken.None);
        Assert.Equal(source, keyboard.Output);
        Assert.True(keyboard.Backspaces > 0);
    }

    [Fact]
    public async Task LineBreakNormalizationAndFlatteningAreExplicit()
    {
        var keyboard = new FakeKeyboard();
        var engine = new TypingEngine(keyboard, new FakeForeground());
        await engine.RunAsync("a\r\nb\rc\nd", Fast with { PreserveLineBreaks = false }, 1, null, CancellationToken.None);
        Assert.Equal("a b c d", keyboard.Output);
        Assert.Equal("a\nb\nc", Fast.PrepareText("a\r\nb\rc"));
    }

    [Fact]
    public async Task PausedEngineSendsNothingUntilResumed()
    {
        var keyboard = new FakeKeyboard();
        var engine = new TypingEngine(keyboard, new FakeForeground());
        engine.SetPaused(true);
        var run = engine.RunAsync("abc", Fast, 1, null, CancellationToken.None);
        await Task.Delay(60);
        Assert.Equal("", keyboard.Output);
        engine.SetPaused(false);
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("abc", keyboard.Output);
    }

    [Fact]
    public async Task CancellationInterruptsPauseAndEngineCanBeReused()
    {
        var keyboard = new FakeKeyboard();
        var engine = new TypingEngine(keyboard, new FakeForeground());
        using var cancellation = new CancellationTokenSource();
        engine.SetPaused(true);
        var run = engine.RunAsync("abc", Fast, 1, null, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("", keyboard.Output);
        await engine.RunAsync("z", Fast, 1, null, CancellationToken.None);
        Assert.Equal("z", keyboard.Output);
    }

    [Fact]
    public async Task CancellationInterruptsLongDelay()
    {
        var keyboard = new FakeKeyboard();
        var engine = new TypingEngine(keyboard, new FakeForeground());
        using var cancellation = new CancellationTokenSource();
        var run = engine.RunAsync("abc", Fast with { WordsPerMinute = 1 }, 1, null, cancellation.Token);
        await keyboard.FirstInput.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("a", keyboard.Output);
    }

    [Fact]
    public async Task DuplicateSessionsAreRejected()
    {
        var engine = new TypingEngine(new FakeKeyboard(), new FakeForeground());
        engine.SetPaused(true);
        using var cancellation = new CancellationTokenSource();
        var run = engine.RunAsync("a", Fast, 1, null, cancellation.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync("b", Fast, 1, null, CancellationToken.None));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task FocusChangeStopsBeforeNextInput()
    {
        var keyboard = new FakeKeyboard();
        var foreground = new FakeForeground();
        keyboard.OnInput = () => foreground.Handle = 2;
        var engine = new TypingEngine(keyboard, foreground);
        await Assert.ThrowsAsync<TargetChangedException>(() => engine.RunAsync("abc", Fast, 1, null, CancellationToken.None));
        Assert.Equal("a", keyboard.Output);
    }

    [Fact]
    public async Task HeldModifiersPreventInput()
    {
        var keyboard = new FakeKeyboard();
        var foreground = new FakeForeground { Modifiers = true };
        var engine = new TypingEngine(keyboard, foreground);
        var run = engine.RunAsync("a", Fast, 1, null, CancellationToken.None);
        await Task.Delay(60);
        Assert.Equal("", keyboard.Output);
        foreground.Modifiers = false;
        await run.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("a", keyboard.Output);
    }

    [Fact]
    public async Task CorrectionNeverBackspacesIntoChangedWindowEvenWhenProtectionIsOff()
    {
        var keyboard = new FakeKeyboard();
        var foreground = new FakeForeground();
        keyboard.OnInput = () => foreground.Handle = 2;
        var engine = new TypingEngine(keyboard, foreground, errors: new TemporaryErrorGenerator(new AlwaysErrorRandom()));
        await Assert.ThrowsAsync<TargetChangedException>(() => engine.RunAsync("good", Fast with
        { EnableErrors = true, ErrorPercent = 25, StopOnWindowChange = false }, 1, null, CancellationToken.None));
        Assert.Equal(0, keyboard.Backspaces);
        Assert.Single(keyboard.Output);
    }

    [Fact]
    public async Task CancellationDuringCorrectionSendsNoCleanupInput()
    {
        var keyboard = new FakeKeyboard();
        using var cancellation = new CancellationTokenSource();
        keyboard.OnInput = cancellation.Cancel;
        var engine = new TypingEngine(keyboard, new FakeForeground(), errors: new TemporaryErrorGenerator(new AlwaysErrorRandom()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.RunAsync("good", Fast with
        { EnableErrors = true, ErrorPercent = 25 }, 1, null, cancellation.Token));
        Assert.Equal(0, keyboard.Backspaces);
        Assert.Single(keyboard.Output);
    }

    [Fact]
    public async Task OwnWindowCannotBeTargeted()
    {
        var keyboard = new FakeKeyboard();
        var engine = new TypingEngine(keyboard, new FakeForeground { Handle = 99 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync("a", Fast, 99, null, CancellationToken.None));
        Assert.Empty(keyboard.Output);
    }

    [Fact]
    public void InvalidSettingsAreRejected()
    {
        foreach (var settings in new[] { Fast with { WordsPerMinute = 0 }, Fast with { VariationPercent = double.NaN },
            Fast with { ErrorPercent = 26 }, Fast with { CountdownSeconds = 0 }, Fast with { NewLinePauseMs = -1 },
            Fast with { NewLineShortcut = (NewLineShortcut)99 } })
            Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Theory]
    [InlineData("Enter", NewLineShortcut.Enter, "Enter")]
    [InlineData("return", NewLineShortcut.Enter, "Enter")]
    [InlineData("Shift+Enter", NewLineShortcut.ShiftEnter, "Shift+Enter")]
    [InlineData("shift enter", NewLineShortcut.ShiftEnter, "Shift+Enter")]
    [InlineData("Shift + Return", NewLineShortcut.ShiftEnter, "Shift+Enter")]
    [InlineData("Ctrl+Enter", NewLineShortcut.CtrlEnter, "Ctrl+Enter")]
    [InlineData("ctrl enter", NewLineShortcut.CtrlEnter, "Ctrl+Enter")]
    [InlineData("Control+Enter", NewLineShortcut.CtrlEnter, "Ctrl+Enter")]
    [InlineData("Alt+Enter", NewLineShortcut.AltEnter, "Alt+Enter")]
    [InlineData("alt enter", NewLineShortcut.AltEnter, "Alt+Enter")]
    public void ParsesAndFormatsNewLineShortcuts(string input, NewLineShortcut expected, string display)
    {
        var parsed = NewLineShortcutExtensions.Parse(input);
        Assert.Equal(expected, parsed);
        Assert.Equal(display, parsed.ToDisplayString());
        Assert.True(NewLineShortcutExtensions.TryParse(input, out var tryParsed));
        Assert.Equal(expected, tryParsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Win+Enter")]
    [InlineData("Shift+Ctrl+Enter")]
    [InlineData("Unknown")]
    public void RejectsInvalidNewLineShortcuts(string input)
    {
        Assert.False(NewLineShortcutExtensions.TryParse(input, out _));
        Assert.Throws<ArgumentException>(() => NewLineShortcutExtensions.Parse(input));
    }

    [Theory]
    [InlineData(NewLineShortcut.Enter)]
    [InlineData(NewLineShortcut.ShiftEnter)]
    [InlineData(NewLineShortcut.CtrlEnter)]
    [InlineData(NewLineShortcut.AltEnter)]
    public async Task EngineSendsConfiguredNewLineShortcut(NewLineShortcut shortcut)
    {
        var keyboard = new FakeKeyboard();
        var engine = new TypingEngine(keyboard, new FakeForeground());
        await engine.RunAsync("Hello\nWorld\n!", Fast with { NewLineShortcut = shortcut }, 1, null, CancellationToken.None);
        Assert.Equal("Hello\nWorld\n!", keyboard.Output);
        Assert.Equal(new[] { shortcut, shortcut }, keyboard.SentNewLines);
    }

    [Theory]
    [InlineData("")] [InlineData(" \n\t\r")]
    public void EmptyPdfTextIsDetected(string text) => Assert.False(new PdfExtractionResult(text, 1).HasText);

    [Fact]
    public async Task PdfExtractionPreservesPageOrderAndDetectsBlankPages()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(300, 300).AddText("First page", 12, new PdfPoint(20, 200), font);
        builder.AddPage(300, 300).AddText("Second page", 12, new PdfPoint(20, 200), font);
        string path = Path.Combine(Path.GetTempPath(), $"TypingMimic-{Guid.NewGuid()}.pdf");
        try
        {
            await File.WriteAllBytesAsync(path, builder.Build());
            var result = await new PdfTextExtractor().ExtractAsync(path, CancellationToken.None);
            Assert.Equal(2, result.PageCount);
            Assert.True(result.HasText);
            Assert.True(result.Text.IndexOf("First page", StringComparison.Ordinal) < result.Text.IndexOf("Second page", StringComparison.Ordinal));
            var blank = new PdfDocumentBuilder(); blank.AddPage(300, 300);
            await File.WriteAllBytesAsync(path, blank.Build());
            Assert.False((await new PdfTextExtractor().ExtractAsync(path, CancellationToken.None)).HasText);
            await File.WriteAllTextAsync(path, "broken PDF");
            await Assert.ThrowsAnyAsync<Exception>(() => new PdfTextExtractor().ExtractAsync(path, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    private sealed class AlwaysErrorRandom : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }
    private sealed class FakeForeground : IForegroundWindowService
    {
        public nint Handle = 1;
        public volatile bool Modifiers;
        public nint GetForegroundWindow() => Handle;
        public bool IsOwnWindow(nint window) => window == 99;
        public bool AreModifiersPressed() => Modifiers;
    }
    private sealed class FakeKeyboard : IKeyboardInputService
    {
        private readonly StringBuilder text = new();
        public string Output { get { lock (text) return text.ToString(); } }
        public int Backspaces { get; private set; }
        public Action? OnInput { get; set; }
        public TaskCompletionSource FirstInput { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<NewLineShortcut> SentNewLines { get; } = new();
        public void SendText(string value)
        {
            lock (text) text.Append(value);
            OnInput?.Invoke(); FirstInput.TrySetResult();
        }
        public void SendNewLine(NewLineShortcut shortcut = NewLineShortcut.Enter)
        {
            lock (text)
            {
                SentNewLines.Add(shortcut);
                text.Append('\n');
            }
            OnInput?.Invoke(); FirstInput.TrySetResult();
        }
        public void Backspace() { lock (text) { text.Length--; Backspaces++; } }
    }
}
