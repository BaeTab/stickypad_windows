using System;
using System.Threading;
using System.Threading.Tasks;

namespace StickyPad.Utils;

/// Debounces an async action: each Trigger() resets the timer; the action fires once after quietness.
public sealed class DebounceAction : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<CancellationToken, Task> _action;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private bool _disposed;

    public DebounceAction(TimeSpan delay, Func<CancellationToken, Task> action)
    {
        _delay = delay;
        _action = action;
    }

    public void Trigger()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = RunAsync(token);
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) return;
            await _action(token).ConfigureAwait(false);
        }
        catch (TaskCanceledException) { }
    }

    /// Run pending action immediately (e.g., on window close) without waiting for the debounce window.
    public async Task FlushAsync()
    {
        CancellationTokenSource? toCancel;
        lock (_lock)
        {
            toCancel = _cts;
            _cts = null;
        }
        toCancel?.Cancel();
        toCancel?.Dispose();
        await _action(CancellationToken.None).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
