using System.Diagnostics;
using CielWin.App.Tests.Alerts;

namespace CielWin.App.Tests.Wallpaper;

/// <summary>
/// the only thing that actually EXERCISES the shipped explorer-scene
/// wallpaper page (<c>CielWin.App/Wallpaper/Web/explorer/</c>) instead of just checking the files
/// exist -- modelled on <see cref="ProcessingSceneNodeTests"/>, which proves the same kind of thing
/// for the processing scene (and, before that, <see cref="Alerts.AlertLayerLayoutNodeTests"/>, for
/// the single-file <c>alert-layer.js</c>).
/// </summary>
/// <remarks>
/// Loads the REAL shipped page directory (copied to the test output next to the executable, same
/// <c>CopyToOutputDirectory</c> mechanism <c>CielWin.App.csproj</c> already uses for
/// <c>Wallpaper\Web\**</c>) into a Node <c>vm</c> sandbox and drives it from
/// <c>explorer-scene.tests.js</c> (<c>CielWin.App.Tests/Wallpaper/Web/</c>, copied to the build
/// output the same way as <c>processing-scene.tests.js</c>). Reuses <see
/// cref="NodeAvailability"/>/<see cref="RequiresNodeFactAttribute"/> from
/// <c>CielWin.App.Tests.Alerts</c> rather than duplicating the "gate, never fake" node-on-PATH
/// check.
/// </remarks>
public sealed class ExplorerSceneNodeTests
{
    private static readonly string ExplorerSceneDirectory =
        Path.Combine(AppContext.BaseDirectory, "Wallpaper", "Web", "explorer");

    // the shared alert-overlay module every scene page now loads from
    // ../shared/js/ -- see CielWin.App/Wallpaper/Web/shared/js/alert-overlay.js.
    private static readonly string SharedDirectory =
        Path.Combine(AppContext.BaseDirectory, "Wallpaper", "Web", "shared");

    private static readonly string HarnessScriptPath =
        Path.Combine(AppContext.BaseDirectory, "Wallpaper", "Web", "explorer-scene.tests.js");

    /// <summary>
    /// Generous, but bounded -- see <see cref="ProcessingSceneNodeTests.HarnessTimeout"/>'s own
    /// remarks (same shape: turns "the process manager wedged" into a reported test failure instead
    /// of a <c>dotnet test</c> run that never comes back). Much larger than that 30s budget: unlike
    /// the processing scene, this scene's own js/earth.js bakes a one-time equirectangular noise
    /// texture at LOAD time (verbatim reference code, unrelated to D6a) that costs several real
    /// seconds per FRESH vm sandbox realm under Node (no JIT warm-up across separate
    /// vm.createContext() realms, unlike a real browser tab that loads this scene exactly once).
    /// explorer-scene.tests.js now calls loadPage() 18 times: measured at ~157s alone and ~218s
    /// while the rest of the suite runs in parallel on a 16-thread dev machine, and a 2-core CI
    /// runner is several times slower. This is a hang guard, not a performance budget, so it is
    /// deliberately far above any observed run.
    /// </summary>
    private static readonly TimeSpan HarnessTimeout = TimeSpan.FromMinutes(15); // was 240s: timed out on the 2-core GitHub Actions runner

    [RequiresNodeFact]
    public void ExplorerSceneHarness_PassesAgainstTheRealShippedPage()
    {
        Assert.True(Directory.Exists(ExplorerSceneDirectory),
            $"Expected '{ExplorerSceneDirectory}' to exist (shipped content, see CielWin.App.csproj's Wallpaper\\Web\\** Content item).");
        Assert.True(File.Exists(Path.Combine(ExplorerSceneDirectory, "js", "see-through-hook.js")),
            $"Expected the shipped see-through-hook.js under '{ExplorerSceneDirectory}'.");
        Assert.True(File.Exists(Path.Combine(SharedDirectory, "js", "alert-overlay.js")),
            $"Expected the shipped shared alert-overlay.js under '{SharedDirectory}'.");
        Assert.True(File.Exists(HarnessScriptPath),
            $"Expected '{HarnessScriptPath}' to exist (test content, see this project's .csproj).");

        var result = RunNode(HarnessTimeout, HarnessScriptPath, ExplorerSceneDirectory);

        Assert.False(
            result.TimedOut,
            $"Node harness did not exit within {HarnessTimeout} and was killed (process tree).{Environment.NewLine}" +
            $"--- stdout ---{Environment.NewLine}{result.Stdout}{Environment.NewLine}" +
            $"--- stderr ---{Environment.NewLine}{result.Stderr}");
        Assert.True(
            result.ExitCode == 0,
            $"Node harness failed (exit {result.ExitCode}).{Environment.NewLine}" +
            $"--- stdout ---{Environment.NewLine}{result.Stdout}{Environment.NewLine}" +
            $"--- stderr ---{Environment.NewLine}{result.Stderr}");
    }

    private static (int ExitCode, string Stdout, string Stderr, bool TimedOut, int ProcessId) RunNode(
        TimeSpan timeout, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start `node` -- RequiresNodeFact should have skipped this fact instead.");

        // Read BOTH streams CONCURRENTLY -- see AlertLayerLayoutNodeTests.RunNode's own remarks on
        // why a sequential read here would risk a full-pipe deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            return (-1, stdoutTask.Result, stderrTask.Result, TimedOut: true, process.Id);
        }

        return (process.ExitCode, stdoutTask.Result, stderrTask.Result, TimedOut: false, process.Id);
    }
}
