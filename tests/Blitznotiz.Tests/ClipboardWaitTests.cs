using System.Diagnostics;
using Blitztext;
using Xunit;

namespace Blitznotiz.Tests;

// Nutzt die echte Windows-Zwischenablage (öffnet sie nur kurz, ändert keinen Inhalt).
[Collection("Clipboard")]
public class ClipboardWaitTests
{
    [Fact]
    public async Task WaitsUntilAnotherProcessReleasesClipboard()
    {
        using var holder = HoldClipboard(TimeSpan.FromMilliseconds(300));
        Assert.True(holder.Opened.IsSet, "Hilfsprozess hat die Ablage nicht geöffnet");

        var sw = Stopwatch.StartNew();
        bool free = await Paster.WaitForClipboardFreeAsync(TimeSpan.FromSeconds(2));

        Assert.True(free);
        Assert.True(sw.ElapsedMilliseconds >= 100, $"nur {sw.ElapsedMilliseconds} ms gewartet");
    }

    [Fact]
    public async Task GivesUpAfterTimeout()
    {
        using var holder = HoldClipboard(TimeSpan.FromMilliseconds(600));
        Assert.True(holder.Opened.IsSet, "Hilfsprozess hat die Ablage nicht geöffnet");

        var sw = Stopwatch.StartNew();
        bool free = await Paster.WaitForClipboardFreeAsync(TimeSpan.FromMilliseconds(100));

        Assert.False(free);
        Assert.True(sw.ElapsedMilliseconds < 500, $"{sw.ElapsedMilliseconds} ms – Timeout nicht eingehalten");
    }

    [Fact]
    public async Task ReturnsImmediatelyWhenClipboardIsFree()
    {
        var sw = Stopwatch.StartNew();
        Assert.True(await Paster.WaitForClipboardFreeAsync(TimeSpan.FromSeconds(1)));
        Assert.True(sw.ElapsedMilliseconds < 100);
    }

    private sealed class Holder : IDisposable
    {
        public ManualResetEventSlim Opened { get; } = new();
        public Process? Process;
        public string Flag = "", Script = "";
        public void Dispose()
        {
            Process?.WaitForExit(5000);
            File.Delete(Flag);
            File.Delete(Script);
        }
    }

    // Hält die Zwischenablage in einem fremden Prozess offen (wie es z.B. der Win+V-Verlauf kurz tut).
    // Wichtig: mit echtem Fenster öffnen – OpenClipboard(NULL) sperrt andere Prozesse nicht.
    private static Holder HoldClipboard(TimeSpan duration)
    {
        var id = Guid.NewGuid().ToString("N");
        var h = new Holder { Flag = Path.Combine(Path.GetTempPath(), $"bn-clip-{id}.flag") };
        var script = Path.Combine(Path.GetTempPath(), $"bn-clip-{id}.ps1");
        File.WriteAllText(script, $$"""
            Add-Type -Name C -Namespace W -MemberDefinition '[DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr h); [DllImport("user32.dll")] public static extern bool CloseClipboard();'
            Add-Type -AssemblyName System.Windows.Forms
            $f = New-Object System.Windows.Forms.Form   # unsichtbares Fenster als Besitzer
            while (-not [W.C]::OpenClipboard($f.Handle)) { Start-Sleep -Milliseconds 5 }
            Set-Content '{{h.Flag}}' 'offen'
            Start-Sleep -Milliseconds {{(int)duration.TotalMilliseconds}}
            [void][W.C]::CloseClipboard()
            """);
        h.Process = Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        var sw = Stopwatch.StartNew();
        while (!File.Exists(h.Flag) && sw.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(5);
        if (File.Exists(h.Flag)) h.Opened.Set();
        h.Script = script;
        return h;
    }
}
