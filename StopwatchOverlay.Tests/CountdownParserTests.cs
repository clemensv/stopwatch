using System;
using Xunit;
using StopwatchOverlay;

namespace StopwatchOverlay.Tests
{
    public class CountdownParserTests
    {
        // Fixed reference instant for deterministic parsing.
        // 2026-06-20 is a Saturday, 10:00:00.
        private static readonly DateTime Now = new(2026, 6, 20, 10, 0, 0);

        [Fact]
        public void Scaffold_Compiles()
        {
            Assert.True(true);
        }
    }
}
