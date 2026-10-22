using System.Windows.Threading;
using TypingMimic.App.Services;
using TypingMimic.App.ViewModels;
using TypingMimic.Core;
using Xunit;

namespace TypingMimic.Tests;

public sealed class DisplayAwakeTests
{
    [Fact]
    public void RequestsDisplayAndSystemAndReleasesWithoutRedundantCalls()
    {
        var calls = new List<uint>();
        var service = new DisplayAwakeService(flags => { calls.Add(flags); return 0x80000000; });
        Assert.True(service.SetRequired(false));
        Assert.True(service.SetRequired(true));
        Assert.True(service.SetRequired(true));
        Assert.True(service.SetRequired(false));
        Assert.True(service.SetRequired(false));
        Assert.Equal(new uint[] { 0x80000003, 0x80000000 }, calls);
    }

    [Fact]
    public void FailedNativeCallsCanBeRetried()
    {
        bool fail = true;
        int calls = 0;
        var service = new DisplayAwakeService(_ => { calls++; return fail ? 0u : 0x80000000u; });
        Assert.False(service.SetRequired(true));
        fail = false;
        Assert.True(service.SetRequired(true));
        fail = true;
        Assert.False(service.SetRequired(false));
        fail = false;
        Assert.True(service.SetRequired(false));
        Assert.Equal(4, calls);
    }

    [Theory]
    [InlineData("complete")] [InlineData("stop")] [InlineData("pause")]
    [InlineData("close")] [InlineData("error")] [InlineData("disabled")]
    public async Task SessionReleasesDisplayRequestOnAllExitPaths(string scenario)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                MainViewModel? model = null;
                try
                {
                    var screen = new FakeDisplay();
                    var keyboard = new FakeKeyboard { Fail = scenario == "error" };
                    var foreground = new FakeForeground();
                    model = new(new TypingEngine(keyboard, foreground), foreground, new PdfTextExtractor(), _ => true, screen)
                    {
                        Text = scenario is "complete" or "disabled" or "error" ? "a" : "abc",
                        Countdown = "1", Wpm = "1", Errors = false,
                        KeepScreenOn = scenario != "disabled"
                    };
                    model.SetHotkeysReady();
                    model.StartCommand.Execute(null);
                    Assert.False(screen.Active); // No request during countdown.
                    if (scenario is "complete" or "error" or "disabled")
                        await Until(() => !model.IsBusy);
                    else
                    {
                        await Until(() => screen.Active);
                        if (scenario == "pause")
                        {
                            model.TogglePause(); Assert.False(screen.Active);
                            model.TogglePause(); Assert.True(screen.Active);
                        }
                        if (scenario == "close") model.Dispose(); else model.Stop();
                        Assert.False(screen.Active);
                        await Until(() => !model.IsBusy);
                    }
                    Assert.False(screen.Active);
                    Assert.Equal(scenario != "disabled", screen.WasActive);
                    Assert.All(screen.Threads, id => Assert.Equal(Environment.CurrentManagedThreadId, id));
                    if (scenario == "error") Assert.Contains("Test input failure", model.Status);
                    completion.SetResult();
                }
                catch (Exception ex) { completion.SetException(ex); }
                finally { model?.Dispose(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }
    private sealed class FakeDisplay : IDisplayAwakeService
    {
        public bool Active, WasActive;
        public List<int> Threads { get; } = new();
        public bool SetRequired(bool required)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            Active = required; WasActive |= required; return true;
        }
    }
    private sealed class FakeKeyboard : IKeyboardInputService
    {
        public bool Fail;
        public void SendText(string text) { if (Fail) throw new InvalidOperationException("Test input failure"); }
        public void SendNewLine(NewLineShortcut shortcut = NewLineShortcut.Enter) { if (Fail) throw new InvalidOperationException("Test input failure"); }
        public void Backspace() { }
    }
    private sealed class FakeForeground : IForegroundWindowService
    {
        public nint GetForegroundWindow() => 1;
        public bool IsOwnWindow(nint window) => false;
        public bool AreModifiersPressed() => false;
    }
}
