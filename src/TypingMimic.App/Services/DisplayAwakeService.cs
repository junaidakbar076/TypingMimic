using System.Runtime.InteropServices;

namespace TypingMimic.App.Services;

public interface IDisplayAwakeService
{
    bool SetRequired(bool required);
}

// Execution-state requests belong to the calling thread. All calls come from
// the WPF dispatcher, including the continuations after asynchronous typing.
public sealed class DisplayAwakeService : IDisplayAwakeService
{
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;
    private const uint DisplayRequired = 0x00000002;
    private readonly Func<uint, uint> setState;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool active;

    public DisplayAwakeService() : this(SetThreadExecutionState) { }
    public DisplayAwakeService(Func<uint, uint> setState) => this.setState = setState;

    public bool SetRequired(bool required)
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Display requests must be changed on the owning UI thread.");
        if (required == active) return true;
        if (setState(Continuous | (required ? SystemRequired | DisplayRequired : 0)) == 0) return false;
        active = required;
        return true;
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
