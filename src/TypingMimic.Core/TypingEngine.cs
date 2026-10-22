using System.Diagnostics;

namespace TypingMimic.Core;

public interface IKeyboardInputService
{
    void SendText(string text);
    void SendNewLine(NewLineShortcut shortcut = NewLineShortcut.Enter) => SendText("\n");
    void Backspace();
}

public interface IForegroundWindowService
{
    nint GetForegroundWindow();
    bool IsOwnWindow(nint window);
    bool AreModifiersPressed();
}

public sealed record TypingProgress(int Completed, int Total, TimeSpan Elapsed, TimeSpan Remaining);

public sealed class TargetChangedException() : Exception("Stopped because the target window changed.");

public sealed class TypingEngine(IKeyboardInputService keyboard, IForegroundWindowService foreground,
    HumanTypingDelayCalculator? delays = null, TemporaryErrorGenerator? errors = null)
{
    private readonly HumanTypingDelayCalculator delays = delays ?? new();
    private readonly TemporaryErrorGenerator errors = errors ?? new();
    private int running;
    private volatile bool paused;
    public bool IsPaused => paused;
    public void SetPaused(bool value) => paused = value;

    public async Task RunAsync(string source, TypingSettings settings, nint target,
        IProgress<TypingProgress>? progress, CancellationToken cancellationToken)
    {
        settings.Validate();
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            throw new InvalidOperationException("A typing session is already running.");
        try
        {
            if (target == 0 || foreground.IsOwnWindow(target))
                throw new InvalidOperationException("Focus a different application's text field during the countdown.");
            string text = settings.PrepareText(source);
            var eligible = TemporaryErrorGenerator.EligiblePositions(text);
            int total = text.EnumerateRunes().Count(), completed = 0, offset = 0;
            var elapsed = Stopwatch.StartNew();
            TimeSpan pausedTime = TimeSpan.Zero;
            var estimate = HumanTypingDelayCalculator.Estimate(text, settings);

            void CheckTarget()
            {
                var current = foreground.GetForegroundWindow();
                if (current == 0 || foreground.IsOwnWindow(current) || (settings.StopOnWindowChange && current != target))
                    throw new TargetChangedException();
            }

            async Task Ready()
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (paused || foreground.AreModifiersPressed())
                {
                    var start = elapsed.Elapsed;
                    await Task.Delay(15, cancellationToken).ConfigureAwait(false);
                    pausedTime += elapsed.Elapsed - start;
                }
                cancellationToken.ThrowIfCancellationRequested();
                CheckTarget();
            }

            async Task Wait(double milliseconds)
            {
                while (milliseconds > 0)
                {
                    await Ready().ConfigureAwait(false);
                    var slice = Math.Min(15, milliseconds);
                    await Task.Delay(TimeSpan.FromMilliseconds(slice), cancellationToken).ConfigureAwait(false);
                    milliseconds -= slice;
                }
            }

            foreach (var rune in text.EnumerateRunes())
            {
                await Ready().ConfigureAwait(false);
                string character = rune.ToString();
                char? wrong = settings.EnableErrors && character.Length == 1 && eligible[offset]
                    ? errors.Choose(character[0], settings.ErrorPercent) : null;
                if (wrong.HasValue)
                {
                    var correctionTarget = foreground.GetForegroundWindow();
                    keyboard.SendText(wrong.Value.ToString());
                    // Cancellation or focus changes interrupt immediately. Never send a cleanup
                    // Backspace into an unknown control; a stopped session can remain incomplete.
                    await Wait(errors.CorrectionDelayMs()).ConfigureAwait(false);
                    await Ready().ConfigureAwait(false);
                    if (foreground.GetForegroundWindow() != correctionTarget) throw new TargetChangedException();
                    keyboard.Backspace();
                }
                await Ready().ConfigureAwait(false);
                if (character == "\n") keyboard.SendNewLine(settings.NewLineShortcut);
                else keyboard.SendText(character);
                completed++;
                offset += rune.Utf16SequenceLength;
                var activeElapsed = elapsed.Elapsed - pausedTime;
                double fraction = (double)completed / Math.Max(1, total);
                progress?.Report(new(completed, total, activeElapsed,
                    TimeSpan.FromMilliseconds(estimate.TotalMilliseconds * (1 - fraction))));
                if (completed < total) await Wait(delays.NextDelayMs(settings, character)).ConfigureAwait(false);
            }
        }
        finally { paused = false; Volatile.Write(ref running, 0); }
    }
}
