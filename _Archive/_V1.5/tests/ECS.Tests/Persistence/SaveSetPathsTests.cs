using System;
using System.IO;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class SaveSetPathsTests
    {
        [Fact]
        public void SystemFile_ValidName_ReturnsPathUnderSystemsDir()
        {
            var paths = new SaveSetPaths("Saves");

            string result = paths.SystemFile("WeatherSystem");

            Assert.Equal(Path.Combine("Saves", "Systems", "WeatherSystem.bin"), result);
        }

        [Theory]
        [InlineData("Bad/Name")]
        [InlineData("Bad\\Name")]
        [InlineData("Bad:Name")]
        public void SystemFile_InvalidName_Throws(string systemName)
        {
            var paths = new SaveSetPaths("Saves");

            Assert.Throws<ArgumentException>(() => paths.SystemFile(systemName));
        }
    }
}
