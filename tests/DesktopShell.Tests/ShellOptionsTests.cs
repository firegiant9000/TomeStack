using TomeStack.DesktopShell;

namespace TomeStack.DesktopShell.Tests;

/// <summary>
/// Audit 2026-09-28, LIVING_SPECS D11: the development switches (<c>TOMESTACK_DEV_FIXTURES=1</c>, <c>--devtools</c>) are
/// honoured only in a Debug build or together with <c>--smoke</c>. A Release launch of the shipped exe ignores both.
/// </summary>
public class ShellOptionsTests
{
    [Fact]
    public void A_release_launch_ignores_the_development_switches()
    {
        var options = ShellOptions.Parse(["--devtools"], devFixturesVariable: "1", debugBuild: false);

        Assert.False(options.DevTools);
        Assert.False(options.DevFixtures);
        Assert.False(options.Smoke);
    }

    [Fact]
    public void A_smoke_run_or_a_debug_build_honours_them()
    {
        var smoke = ShellOptions.Parse(["--smoke", "--devtools"], devFixturesVariable: "1", debugBuild: false);
        var debug = ShellOptions.Parse(["--devtools"], devFixturesVariable: "1", debugBuild: true);

        Assert.Equal((true, true), (smoke.DevTools, smoke.DevFixtures));
        Assert.Equal((true, true), (debug.DevTools, debug.DevFixtures));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("true")]
    public void Only_the_value_1_seeds_fixtures(string? value)
    {
        Assert.False(ShellOptions.Parse(["--smoke"], value, debugBuild: true).DevFixtures);
    }

    [Fact]
    public void Without_the_switches_nothing_is_on_even_in_debug()
    {
        var options = ShellOptions.Parse([], devFixturesVariable: null, debugBuild: true);
        Assert.Equal((false, false, false), (options.DevTools, options.DevFixtures, options.SimulateMissingRuntime));
    }

    [Fact]
    public void The_simulated_missing_runtime_needs_smoke_even_in_debug()
    {
        Assert.False(ShellOptions.Parse(["--simulate-missing-webview2"], null, debugBuild: true).SimulateMissingRuntime);
        Assert.True(ShellOptions.Parse(["--smoke", "--simulate-missing-webview2"], null, debugBuild: false).SimulateMissingRuntime);
    }

    [Fact]
    public void The_built_shell_uses_its_own_build_flavour()
    {
        // The real entry point reads the environment and the shell's compile-time flavour. The tests build in the same
        // configuration as the shell, so a Release gate run proves the shipped flavour ignores the switches.
        var previous = Environment.GetEnvironmentVariable("TOMESTACK_DEV_FIXTURES");
        Environment.SetEnvironmentVariable("TOMESTACK_DEV_FIXTURES", "1");
        try
        {
            var options = ShellOptions.Parse(["--devtools"]);
#if DEBUG
            Assert.Equal((true, true), (options.DevTools, options.DevFixtures));
#else
            Assert.Equal((false, false), (options.DevTools, options.DevFixtures));
#endif
        }
        finally
        {
            Environment.SetEnvironmentVariable("TOMESTACK_DEV_FIXTURES", previous);
        }
    }
}
