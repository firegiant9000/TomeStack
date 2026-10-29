using FsCheck.Xunit;

namespace TomeStack.AppService.Tests.Properties;

/// <summary>
/// A property whose cases each open data folders and so take seconds: <paramref name="maxTest"/> cases locally, fewer on CI
/// (GitHub Actions sets <c>CI=true</c>), where they were the longest single tests (CI time, 2026-10-25). Each CI run draws new
/// cases, so the runs together still cover more than one local run. <c>TOMESTACK_FULL_PROPERTIES=1</c> runs every case on CI too.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CiPropertyAttribute : PropertyAttribute
{
    public CiPropertyAttribute(int maxTest, int ciMaxTest) => MaxTest = Reduced ? ciMaxTest : maxTest;

    internal static bool Reduced =>
        IsSet("CI") && !IsSet("TOMESTACK_FULL_PROPERTIES");

    private static bool IsSet(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value && !value.Equals("false", StringComparison.OrdinalIgnoreCase) && value != "0";
}
