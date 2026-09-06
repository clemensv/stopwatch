using Xunit;

namespace StopwatchOverlay.Tests;

public class ControllerLayoutPolicyTests
{
    [Theory]
    [InlineData(560, true)]
    [InlineData(819.9, true)]
    [InlineData(820, false)]
    [InlineData(1040, false)]
    public void CompactLayout_UsesDocumentedBreakpoint(double width, bool expected)
        => Assert.Equal(expected, ControllerLayoutPolicy.UseCompactLayout(width));
}
