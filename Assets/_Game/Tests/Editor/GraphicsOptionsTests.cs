using NUnit.Framework;

namespace FPSLAN.Tests
{
    public class GraphicsOptionsTests
    {
        static readonly string[] Levels = { "Very Low", "Low", "Medium", "High", "Very High", "Ultra" };

        [TestCase("Intel(R) HD Graphics Family", 128, 8097, "Low")]          // i7-4600U + HD 4400 (máquina medida)
        [TestCase("Intel(R) Iris(R) Xe Graphics", 2048, 16000, "Medium")]
        [TestCase("Intel(R) UHD Graphics 620", 1024, 8000, "Low")]
        [TestCase("AMD Radeon(TM) Graphics", 512, 16000, "Low")]               // APU
        [TestCase("AMD Radeon RX 6600", 8192, 16000, "Very High")]
        [TestCase("NVIDIA GeForce GTX 1050 Ti", 4096, 16000, "High")]
        [TestCase("NVIDIA GeForce GT 1030", 2048, 8000, "Medium")]
        [TestCase("NVIDIA GeForce RTX 3060", 12288, 4096, "Medium")]           // pouca RAM: nunca acima de Medium
        [TestCase("Intel(R) Arc(TM) A770 Graphics", 16384, 16000, "Very High")] // Intel dedicada
        [TestCase("Radeon RX Vega 64", 8192, 16000, "Very High")]                // Vega dedicada
        [TestCase("AMD Radeon(TM) Vega 8 Graphics", 512, 8000, "Low")]           // Vega integrada (APU)
        public void FirstLaunchQualityMatchesHardware(string gpu, int vram, int ram, string expected)
        {
            Assert.AreEqual(expected, Levels[GraphicsOptions.RecommendedQuality(gpu, vram, ram, Levels)]);
        }

        [Test]
        public void NeverPicksUltraByDefault()
        {
            int ultra = System.Array.IndexOf(Levels, "Ultra");
            Assert.AreNotEqual(ultra, GraphicsOptions.RecommendedQuality("NVIDIA GeForce RTX 4090", 24576, 65536, Levels));
        }

        [Test]
        public void RenamedLevelsFallBackToTheMiddle()
        {
            var custom = new[] { "Fast", "Balanced", "Pretty", "Max" };
            int i = GraphicsOptions.RecommendedQuality("NVIDIA GeForce RTX 3060", 12288, 16000, custom);
            Assert.AreEqual(1, i);
        }
    }
}
