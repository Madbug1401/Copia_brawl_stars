using NUnit.Framework;

namespace FPSLAN.Tests
{
    public class LanAddressTests
    {
        [TestCase("192.168.1.20", "192.168.1.20", 0)]
        [TestCase(" 192.168.1.20:7778 ", "192.168.1.20", 7778)]
        [TestCase("localhost", "127.0.0.1", 0)]
        [TestCase("localhost:7778", "127.0.0.1", 7778)]
        [TestCase("", "127.0.0.1", 0)]
        [TestCase("10.0.0.5:65535", "10.0.0.5", 65535)]
        public void ServerCodesResolveTheExpectedAddressAndPort(string code, string address, int port)
        {
            Assert.IsTrue(ConnectionManager.TryParseCode(code, out var actualAddress, out var actualPort));
            Assert.AreEqual(address, actualAddress);
            Assert.AreEqual(port, actualPort);
        }

        [TestCase("192.168.1")]
        [TestCase("256.168.1.20")]
        [TestCase("192.168.1.20:0")]
        [TestCase("192.168.1.20:65536")]
        [TestCase("192.168.1.20:hello")]
        [TestCase("not-an-address")]
        [TestCase("::1")]
        public void InvalidServerCodesAreRejected(string code) =>
            Assert.IsFalse(ConnectionManager.TryParseCode(code, out _, out _));
    }
}
