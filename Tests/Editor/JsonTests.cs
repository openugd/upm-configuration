using NUnit.Framework;

namespace OpenUGD.Tests
{
    /// <summary>
    /// <see cref="ConfigurationManagerExtensions.AddJson"/> as a strict RFC 8259 reader: every
    /// malformation is a <see cref="ConfigurationException"/>, numbers follow JSON's grammar, a byte-order mark is
    /// accepted.
    /// </summary>
    [TestFixture]
    public class JsonTests
    {
        private static ConfigurationException Malformed(string json) =>
            Assert.Throws<ConfigurationException>(() => new ConfigurationManager().AddJson(json));

        [Test]
        public void AUnicodeEscapeIsDecoded()
        {
            var configuration = new ConfigurationManager().AddJson("{\"a\":\"\\u0041\\u00e9\"}");

            Assert.AreEqual("Aé", configuration["a"]);
        }

        [TestCase("{\"a\":\"\\u00zz\"}", 10)]
        [TestCase("{\"a\":\"\\uG000\"}", 8)]
        public void ANonHexadecimalUnicodeEscapeIsAConfigurationException(string json, int offset)
        {
            var error = Malformed(json);

            StringAssert.Contains("character " + offset, error.Message);
            StringAssert.Contains("hexadecimal", error.Message);
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        [TestCase("+1")]
        [TestCase("01")]
        [TestCase(".5")]
        [TestCase("1.")]
        [TestCase("1e")]
        [TestCase("1e+")]
        [TestCase("-")]
        [TestCase("0x10")]
        [TestCase("1_000")]
        public void ANumberOutsideJsonsGrammarIsMalformed(string literal)
        {
            var error = Malformed("{\"a\":" + literal + "}");

            StringAssert.Contains("not a valid JSON value", error.Message);
        }

        [TestCase("0")]
        [TestCase("-0")]
        [TestCase("7")]
        [TestCase("-12")]
        [TestCase("1.5")]
        [TestCase("0.25")]
        [TestCase("1e10")]
        [TestCase("1E+2")]
        [TestCase("-1.25e-3")]
        public void ANumberInJsonsGrammarIsStoredAsWritten(string literal)
        {
            var configuration = new ConfigurationManager().AddJson("{\"a\":" + literal + "}");

            Assert.AreEqual(literal, configuration["a"]);
        }

        [Test]
        public void ALeadingByteOrderMarkIsSkipped()
        {
            var configuration = new ConfigurationManager().AddJson("\uFEFF{\"a\":1}");

            Assert.AreEqual("1", configuration["a"]);
        }

        [Test]
        public void AByteOrderMarkAnywhereElseIsMalformed()
        {
            Malformed("{\"a\":\uFEFF1}");
        }

        [Test]
        public void ARawControlCharacterInsideAStringIsMalformed()
        {
            var error = Malformed("{\"a\":\"line\nbreak\"}");

            StringAssert.Contains("control character", error.Message);
        }

        [Test]
        public void OnlyJsonWhitespaceSeparatesTokens()
        {
            var configuration = new ConfigurationManager().AddJson(" \t\r\n{ \"a\" :\t1 }\r\n");
            Assert.AreEqual("1", configuration["a"]);

            Malformed("{\u00A0\"a\":1}");
        }

        [Test]
        public void AnEmptyPropertyNameIsAnOrdinaryKey()
        {
            var configuration = new ConfigurationManager().AddJson("{\"a\":1,\"\":{\"a\":2}}");
            Assert.AreEqual("1", configuration["a"]);
            Assert.AreEqual("2", configuration[":a"]);

            Assert.AreEqual("3", new ConfigurationManager().AddJson("{\"\":3}")[""]);
            Assert.AreEqual("4", new ConfigurationManager().AddJson("{\"\":{\"a\":4}}", "p")["p::a"]);
        }

        [Test]
        public void ThirtyTwoNestedContainersAreTheLimit()
        {
            new ConfigurationManager().AddJson(new string('[', 32) + "1" + new string(']', 32), "deep");
            new ConfigurationManager().AddJson(new string('[', 32) + new string(']', 32), "deep");

            StringAssert.Contains("32 levels", Malformed(new string('[', 33) + "1" + new string(']', 33)).Message);
            StringAssert.Contains("32 levels", Malformed(new string('[', 33) + new string(']', 33)).Message);
        }

        [Test]
        public void EscapedControlCharactersAreFine()
        {
            var configuration = new ConfigurationManager().AddJson("{\"a\":\"tab\\there\\nnext\"}");

            Assert.AreEqual("tab\there\nnext", configuration["a"]);
        }
    }
}
