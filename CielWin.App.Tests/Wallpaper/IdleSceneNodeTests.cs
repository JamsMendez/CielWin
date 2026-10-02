using System.Diagnostics;
using CielWin.App.Tests.Alerts;

namespace CielWin.App.Tests.Wallpaper;

/// <summary>
/// The only thing that actually EXERCISES the shipped idle-scene wallpaper
/// page (<c>CielWin.App/Wallpaper/Web/idle/</c>) instead of just checking the files exist --
/// modelled on <see cref="ExplorerSceneNodeTests"/>, which proves the same kind of thing for the
/// explorer scene (same underlying config/math/glyphs/earth/rings family).
/// </summary>
/// <remarks>
/// Loads the REAL shipped page directory (copied to the test output next to the executable, same
/// <c>CopyToOutputDirectory</c> mechanism <c>CielWin.App.csproj</c> already uses for
/// <c>Wallpaper\Web\**</c>) into a Node <c>vm</c> sandbox and drives it from
/// <c>idle-scene.tests.js</c> (<c>CielWin.App.Tests/Wallpaper/Web/</c>, copied to the build output
/// the same way as <c>explorer-scene.tests.js</c>). Reuses <see cref="NodeAvailability"/>/<see
/// cref="RequiresNodeFactAttribute"/> from <c>CielWin.App.Tests.Alerts</c> rather than duplicating
/// the "gate, never fake" node-on-PATH check.
/// </remarks>
public sealed class IdleSceneNodeTests
{
    private static readonly string IdleSceneDirectory =
        Path.Combine(AppContext.BaseDirectory, "Wallpaper", "Web", "idle");

    // the shared alert-overlay module every scene page loads from
    // ../shared/js/ -- see CielWin.App/Wallpaper/Web/shared/js/alert-overlay.js.
    private static readonly string SharedDirectory =
        Path.Combine(AppContext.BaseDirectory, "Wallpaper", "Web", "shared");

    private static readonly string HarnessScriptPath =
        Path.Combine(AppContext.BaseDirectory, "Wallpaper", "Web", "idle-scene.tests.js");

    /// <summary>
    /// Generous, but bounded -- see <see cref="ExplorerSceneNodeTests.HarnessTimeout"/>'s own
    /// remarks (same shape: turns "the process manager wedged" into a reported test failure instead
    /// of a <c>dotnet test</c> run that never comes back). Idle shares explorer's own js/earth.js
    /// (verbatim, identical family -- see the feature doc's D6c entry), which bakes a one-time
    /// equirectangular noise texture at LOAD time that costs several real seconds per FRESH vm
    /// sandbox realm under Node. idle-scene.tests.js now calls loadPage() 15 times (~212s observed
    /// while the rest of the suite runs in parallel on a 16-thread dev machine), so this keeps the
    /// same hang-guard budget explorer's own harness uses.
    /// </summary>
    private static readonly TimeSpan HarnessTimeout = TimeSpan.FromMinutes(15); // was 240s: timed out on the 2-core GitHub Actions runner

    [RequiresNodeFact]
    public void IdleSceneHarness_PassesAgainstTheRealShippedPage()
    {
        Assert.True(Directory.Exists(IdleSceneDirectory),
            $"Expected '{IdleSceneDirectory}' to exist (shipped content, see CielWin.App.csproj's Wallpaper\\Web\\** Content item).");
        Assert.True(File.Exists(Path.Combine(IdleSceneDirectory, "js", "see-through-hook.js")),
            $"Expected the shipped see-through-hook.js under '{IdleSceneDirectory}'.");
        Assert.True(File.Exists(Path.Combine(SharedDirectory, "js", "alert-overlay.js")),
            $"Expected the shipped shared alert-overlay.js under '{SharedDirectory}'.");
        Assert.True(File.Exists(HarnessScriptPath),
            $"Expected '{HarnessScriptPath}' to exist (test content, see this project's .csproj).");

        var result = RunNode(HarnessTimeout, HarnessScriptPath, IdleSceneDirectory);

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
