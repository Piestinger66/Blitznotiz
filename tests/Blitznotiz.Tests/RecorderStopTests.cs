using System.Diagnostics;
using Blitztext;
using Xunit;

namespace Blitznotiz.Tests;

// NAudio-Wettlauf: ein Stopp vor dem Anlaufen des Aufnahme-Threads wird überschrieben und geht verloren.
public class RecorderStopTests
{
    [Fact]
    public async Task StopsOnFirstTryWhenConfirmed()
    {
        var stopped = new TaskCompletionSource<bool>();
        int calls = 0;
        bool ok = await Recorder.StopWithRetryAsync(() => { calls++; stopped.TrySetResult(true); },
            stopped.Task, TimeSpan.FromMilliseconds(50), maxTries: 5);
        Assert.True(ok);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RepeatsStopWhenFirstOneWasLost()
    {
        var stopped = new TaskCompletionSource<bool>();
        int calls = 0;
        bool ok = await Recorder.StopWithRetryAsync(() => { if (++calls == 2) stopped.TrySetResult(true); },
            stopped.Task, TimeSpan.FromMilliseconds(50), maxTries: 5);
        Assert.True(ok);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GivesUpAfterMaxTriesInsteadOfHangingForever()
    {
        var never = new TaskCompletionSource<bool>();
        int calls = 0;
        var sw = Stopwatch.StartNew();
        bool ok = await Recorder.StopWithRetryAsync(() => calls++, never.Task, TimeSpan.FromMilliseconds(50), maxTries: 4);
        Assert.False(ok);
        Assert.Equal(4, calls);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ThrowingStopCountsAsTryAndDoesNotEscape()
    {
        var stopped = new TaskCompletionSource<bool>();
        int calls = 0;
        bool ok = await Recorder.StopWithRetryAsync(() =>
        {
            if (++calls == 1) throw new InvalidOperationException("waveInStop");
            stopped.TrySetResult(true);
        }, stopped.Task, TimeSpan.FromMilliseconds(50), maxTries: 3);
        Assert.True(ok);
        Assert.Equal(2, calls);
    }
}
