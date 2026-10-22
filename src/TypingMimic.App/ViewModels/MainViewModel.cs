using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using TypingMimic.Core;
using TypingMimic.App.Services;

namespace TypingMimic.App.ViewModels;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TypingEngine engine;
    private readonly IForegroundWindowService foreground;
    private readonly IPdfTextExtractor extractor;
    private readonly Func<int, bool> confirmLong;
    private readonly IDisplayAwakeService displayAwake;
    private bool keepScreenOn = true;
    private string screenStatus = "Keep-screen-on request inactive.";
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? session;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private string text = "", status = "Paste text or open a PDF to begin.", pdfInfo = "No PDF imported", validation = "";
    private bool busy, importing, hotkeysReady, paused, typing, disposed;
    private int completed, total;
    private TimeSpan remaining;
    private string wpm = "60", variation = "15", errorRate = "2", countdown = "5", comma = "150", sentence = "350", newline = "500";
    private string newlineShortcut = "Enter";
    private bool preserve = true, errors = true, stopOnChange = true;
    private bool randomInput;
    private string randomLength = RandomTextGenerator.DefaultLength.ToString(CultureInfo.InvariantCulture);

    public MainViewModel(TypingEngine engine, IForegroundWindowService foreground, IPdfTextExtractor extractor, Func<int, bool> confirmLong,
        IDisplayAwakeService? displayAwake = null)
    {
        this.engine = engine; this.foreground = foreground; this.extractor = extractor; this.confirmLong = confirmLong;
        this.displayAwake = displayAwake ?? new DisplayAwakeService();
        StartCommand = new(() => _ = StartAsync(), () => CanEdit && hotkeysReady && (RandomInput || Text.Length > 0) && Validation.Length == 0);
        PauseCommand = new(TogglePause, () => busy);
        StopCommand = new(Stop, () => busy);
        timer = new(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => Changed(nameof(Elapsed)), Dispatcher.CurrentDispatcher);
        Validate();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void Refresh()
    {
        foreach (var name in new[] { nameof(CanEdit), nameof(IsBusy), nameof(Status), nameof(PauseLabel), nameof(Progress), nameof(Counts), nameof(Remaining), nameof(Elapsed) }) Changed(name);
        StartCommand.Refresh(); PauseCommand.Refresh(); StopCommand.Refresh();
    }
    public RelayCommand StartCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand StopCommand { get; }
    public bool CanEdit => !busy && !importing && !disposed;
    public bool IsBusy => busy;
    public string Text { get => text; set { text = value; Changed(); Validate(); } }
    public string Status { get => status; private set { status = value; Changed(); } }
    public string PdfInfo { get => pdfInfo; private set { pdfInfo = value; Changed(); } }
    public string Validation { get => validation; private set { validation = value; Changed(); } }
    public string PauseShortcut { get; private set; } = "Ctrl+Alt+F11";
    public string StopShortcut { get; private set; } = "Ctrl+Alt+F12";
    public string ShortcutSummary => hotkeysReady
        ? $"Global shortcuts: {PauseShortcut} — pause / resume    •    {StopShortcut} — stop"
        : "Global shortcuts unavailable — apply two available shortcuts to enable typing.";
    public string PauseLabel => paused ? "_Resume" : "_Pause";
    public double Progress => total == 0 ? 0 : 100.0 * completed / total;
    public string Counts => $"{completed:N0} / {total:N0} characters";
    public string Elapsed => $"Elapsed {Format(clock.Elapsed)}";
    public string Remaining => $"Estimated remaining {Format(remaining)}";
    public string Estimate { get; private set; } = "";
    private static string Format(TimeSpan duration) => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    public string Wpm { get => wpm; set { wpm = value; Validate(); } }
    public string Variation { get => variation; set { variation = value; Validate(); } }
    public string ErrorRate { get => errorRate; set { errorRate = value; Validate(); } }
    public string Countdown { get => countdown; set { countdown = value; Validate(); } }
    public string Comma { get => comma; set { comma = value; Validate(); } }
    public string Sentence { get => sentence; set { sentence = value; Validate(); } }
    public string Newline { get => newline; set { newline = value; Validate(); } }
    public IReadOnlyList<string> AvailableNewLineShortcuts { get; } =
        [ "Enter", "Shift+Enter", "Ctrl+Enter", "Alt+Enter" ];
    public string NewLineShortcut { get => newlineShortcut; set { newlineShortcut = value; Changed(); Validate(); } }
    public bool Preserve { get => preserve; set { preserve = value; Changed(); Validate(); } }
    public bool Errors { get => errors; set { errors = value; Changed(); Validate(); } }
    public bool StopOnChange { get => stopOnChange; set { stopOnChange = value; Changed(); Validate(); } }
    public bool RandomInput { get => randomInput; set { randomInput = value; Changed(); Validate(); } }
    public bool KeepScreenOn { get => keepScreenOn; set { if (!CanEdit) return; keepScreenOn = value; Changed(); } }
    public string ScreenStatus => screenStatus;
    public string RandomLength { get => randomLength; set { randomLength = value; Changed(); Validate(); } }

    private int ReadRandomLength()
    {
        if (!int.TryParse(randomLength, out int length)) throw new ArgumentException("Enter a whole number for random character count.");
        RandomTextGenerator.ValidateLength(length);
        return length;
    }

    private TypingSettings ReadSettings()
    {
        double Number(string value, string name) => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double n)
            && double.IsFinite(n) ? n : throw new ArgumentException($"Enter a valid number for {name}.");
        int Integer(string value, string name) => int.TryParse(value, out int n) ? n : throw new ArgumentException($"Enter a whole number for {name}.");
        if (!NewLineShortcutExtensions.TryParse(newlineShortcut, out var parsedShortcut))
            throw new ArgumentException("Choose a valid new line shortcut (Enter, Shift+Enter, Ctrl+Enter, or Alt+Enter).");
        var settings = new TypingSettings
        {
            WordsPerMinute = Number(wpm, "WPM"), VariationPercent = Number(variation, "variation"), ErrorPercent = Number(errorRate, "error rate"),
            CountdownSeconds = Integer(countdown, "countdown"), CommaPauseMs = Integer(comma, "comma pause"),
            SentencePauseMs = Integer(sentence, "sentence pause"), NewLinePauseMs = Integer(newline, "newline pause"),
            PreserveLineBreaks = preserve, EnableErrors = errors, StopOnWindowChange = stopOnChange,
            NewLineShortcut = parsedShortcut
        };
        settings.Validate();
        return settings;
    }
    private void Validate()
    {
        try
        {
            var settings = ReadSettings();
            // A seeded sample keeps the estimate stable while settings are edited.
            string prepared = RandomInput
                ? new RandomTextGenerator(new Random(0)).Generate(ReadRandomLength(), settings.PreserveLineBreaks)
                : settings.PrepareText(text);
            if (prepared.Any(c => char.IsControl(c) && c != '\n' && c != '\t'))
                throw new ArgumentException("Remove unsupported control characters from the text (only tabs and newlines are supported).");
            Validation = "";
            Estimate = $"{prepared.EnumerateRunes().Count():N0} {(RandomInput ? "random characters" : "characters")} · Estimated typing time {Format(HumanTypingDelayCalculator.Estimate(prepared, settings))}";
        }
        catch (ArgumentException ex) { Validation = ex.Message; Estimate = "Check settings to estimate duration."; }
        Changed(nameof(Estimate)); StartCommand.Refresh();
    }
    public void SetHotkeysReady(bool ready = true, string? pauseKey = null, string? stopKey = null)
    {
        hotkeysReady = ready;
        if (pauseKey is not null) PauseShortcut = pauseKey;
        if (stopKey is not null) StopShortcut = stopKey;
        Changed(nameof(PauseShortcut)); Changed(nameof(StopShortcut)); Changed(nameof(ShortcutSummary));
        StartCommand.Refresh();
    }
    public void ReportError(string message) => Status = message;

    public async Task ImportPdfAsync(string path)
    {
        if (!CanEdit) return;
        if (!string.Equals(System.IO.Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        { Status = "Choose a PDF file."; return; }
        importing = true; Status = "Extracting PDF text…"; Refresh();
        try
        {
            var result = await extractor.ExtractAsync(path, lifetime.Token);
            if (disposed) return;
            PdfInfo = $"{System.IO.Path.GetFileName(path)} · {result.PageCount:N0} pages · {result.CharacterCount:N0} extracted characters";
            if (!result.HasText)
                Status = "This PDF has no extractable text and may be scanned or empty. OCR is not included. Your existing preview has been kept.";
            else { Text = result.Text; Status = "PDF imported. Review reading order, paragraphs, and tables before typing."; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = $"Could not read PDF. It may be encrypted, damaged, or inaccessible. {ex.Message}"; }
        finally { importing = false; Refresh(); }
    }

    private async Task StartAsync()
    {
        if (!StartCommand.CanExecute(null)) return;
        TypingSettings settings;
        try { settings = ReadSettings(); } catch (ArgumentException ex) { Status = ex.Message; return; }
        string source;
        try
        {
            source = RandomInput ? new RandomTextGenerator().Generate(ReadRandomLength(), settings.PreserveLineBreaks)
                : settings.PrepareText(Text);
        }
        catch (ArgumentException ex) { Status = ex.Message; return; }
        if (source.Length > 10000 && !confirmLong(source.EnumerateRunes().Count())) return;
        // Make flattening and newline normalization visible before the immutable session snapshot.
        if (!RandomInput) Text = source;
        busy = true; paused = false; typing = false; completed = 0; total = source.EnumerateRunes().Count();
        remaining = HumanTypingDelayCalculator.Estimate(source, settings);
        session = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = session.Token;
        clock.Reset(); Refresh();
        try
        {
            var countdownClock = Stopwatch.StartNew();
            double left = settings.CountdownSeconds * 1000;
            while (left > 0)
            {
                token.ThrowIfCancellationRequested();
                Status = paused ? $"Countdown paused — {PauseShortcut} to resume." : $"Starting in {Math.Ceiling(left / 1000)}… Focus the target text field.";
                countdownClock.Restart();
                await Task.Delay(25, token);
                if (!paused) left -= countdownClock.Elapsed.TotalMilliseconds;
            }
            var target = foreground.GetForegroundWindow();
            typing = true; engine.SetPaused(paused);
            if (!UpdateDisplayRequest(KeepScreenOn && !paused))
                throw new InvalidOperationException("Windows could not activate the keep-screen-on request. Disable the option or try again.");
            Status = $"Typing — {PauseShortcut} pauses; {StopShortcut} stops.";
            clock.Start(); timer.Start();
            var progress = new Progress<TypingProgress>(p =>
            {
                if (!busy || disposed) return;
                completed = p.Completed; remaining = p.Remaining;
                Changed(nameof(Counts)); Changed(nameof(Progress)); Changed(nameof(Remaining));
            });
            await Task.Run(() => engine.RunAsync(source, settings, target, progress, token), token);
            completed = total; remaining = TimeSpan.Zero;
            Status = "Finished sending text. Review the result in the target application.";
        }
        catch (OperationCanceledException) { Status = "Stopped. The target contains partial text; check for an uncorrected temporary error before restarting."; }
        catch (Exception ex) { Status = ex.Message; }
        finally
        {
            UpdateDisplayRequest(false);
            clock.Stop(); timer.Stop(); engine.SetPaused(false);
            typing = false; busy = false; paused = false;
            session.Dispose(); session = null; Refresh();
        }
    }
    public void TogglePause()
    {
        if (!busy) return;
        paused = !paused; engine.SetPaused(paused);
        if (typing)
        {
            if (!UpdateDisplayRequest(KeepScreenOn && !paused))
            {
                session?.Cancel();
                Status = "Could not update the keep-screen-on request. Stopping typing.";
                Refresh();
                return;
            }
            if (paused) clock.Stop(); else clock.Start();
            Status = paused ? $"Paused. Focus the original target, then press {PauseShortcut}." : "Typing…";
        }
        Refresh();
    }
    private bool UpdateDisplayRequest(bool required)
    {
        bool success = displayAwake.SetRequired(required);
        screenStatus = success
            ? required ? "Keeping the screen and computer awake while typing." : "Keep-screen-on request inactive."
            : "Windows could not update the keep-screen-on request.";
        Changed(nameof(ScreenStatus));
        return success;
    }

    public void Stop()
    {
        session?.Cancel();
        UpdateDisplayRequest(false);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel(); session?.Cancel(); timer.Stop(); lifetime.Dispose();
        UpdateDisplayRequest(false);
    }
}
