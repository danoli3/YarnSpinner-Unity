using System;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yarn.Unity;

#nullable enable

namespace Yarn.Unity.Tests
{
    [TestFixture]
    public class StringTableCodecTests
    {
        class TestCodec : AesHmacStringTableCodec
        {
            public byte Seed = 7;
            protected override byte[] GetMasterKey()
            {
                var key = new byte[32];
                for (int i = 0; i < key.Length; i++) { key[i] = (byte)(i * 3 + Seed); }
                return key;
            }
        }

        TestCodec codec = null!;

        [SetUp] public void SetUp() => codec = ScriptableObject.CreateInstance<TestCodec>();
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(codec);

        [Test]
        public void RoundTrip_PreservesText()
        {
            const string text = "Hello, \"world\"! こんにちは 🌍";
            var encoded = codec.EncodeString("line:abc", text);
            Assert.AreEqual(text, codec.DecodeString("line:abc", encoded));
        }

        [TestCase("")]
        [TestCase("a")]
        [TestCase("Plain ASCII with punctuation: !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~")]
        [TestCase("line1\nline2\r\nline3\ttab")]
        [TestCase("a\0b")]
        [TestCase("Crème brûlée, Ærø, Straße, ñandú")]
        [TestCase("Αλφα βήτα — Привет, мир")]
        [TestCase("مرحبا بالعالم / שלום עולם")]
        [TestCase("नमस्ते สวัสดี")]
        [TestCase("こんにちは世界 你好，世界 안녕하세요")]
        [TestCase("🌍🚀😀👨‍👩‍👧‍👦 𝒳𝒴𝒵 𠮷")]
        [TestCase("é ﻿ zero​width")]
        [TestCase("[b]Hello[/b] {$name}! <<set $x to 1>> \"q\"")]
        [TestCase("0123456789abcde")] // 15 bytes: just under an AES block
        [TestCase("0123456789abcdef")] // 16 bytes: exactly one block
        [TestCase("0123456789abcdefg")] // 17 bytes: just over
        public void RoundTrip_VariousStrings(string text)
        {
            Assert.AreEqual(text, codec.DecodeString("line:x", codec.EncodeString("line:x", text)));
        }

        [Test]
        public void RoundTrip_LargeAndEveryBmpCodePoint()
        {
            var big = new string('x', 500_000) + string.Concat(System.Linq.Enumerable.Repeat("日本語🌍", 50_000));
            Assert.AreEqual(big, codec.DecodeString("line:big", codec.EncodeString("line:big", big)));

            var sb = new StringBuilder();
            for (int cp = 0; cp <= 0xFFFF; cp++)
            {
                if (cp >= 0xD800 && cp <= 0xDFFF) { continue; } // surrogates aren't valid UTF-8 on their own
                sb.Append((char)cp);
            }
            var all = sb.ToString();
            Assert.AreEqual(all, codec.DecodeString("line:all", codec.EncodeString("line:all", all)));
        }

        [Test]
        public void Encode_DoesNotContainPlaintext_AndIsRandomised()
        {
            var a = codec.EncodeString("line:abc", "secret dialogue text");
            var b = codec.EncodeString("line:abc", "secret dialogue text");
            CollectionAssert.AreNotEqual(a, b);
            StringAssert.DoesNotContain("secret", Encoding.UTF8.GetString(a));
        }

        [Test]
        public void Decode_WithDifferentContext_Fails()
        {
            var encoded = codec.EncodeString("line:abc", "text");
            Assert.Throws<CryptographicException>(() => codec.DecodeString("line:def", encoded));
        }

        [Test]
        public void Decode_WithTamperedData_Fails()
        {
            var encoded = codec.EncodeString("line:abc", "text");
            encoded[20] ^= 0xFF;
            Assert.Throws<CryptographicException>(() => codec.DecodeString("line:abc", encoded));
        }

        [Test]
        public void Decode_WithDifferentKey_Fails()
        {
            var encoded = codec.EncodeString("line:abc", "text");
            codec.Seed = 9;
            Assert.Throws<CryptographicException>(() => codec.DecodeString("line:abc", encoded));
        }

        [Test]
        public void Localization_StoresEncodedText_AndDecodesOnDemand()
        {
            var loc = ScriptableObject.CreateInstance<Localization>();
            try
            {
                loc.stringTableCodec = codec;
                loc.AddLocalisedStringToAsset("line:1", "Top secret");

                Assert.IsNull(loc.entries["line:1"].localizedString);
                Assert.IsNotNull(loc.entries["line:1"].encryptedString);
                Assert.AreEqual("Top secret", loc.GetLocalizedString("line:1"));
                Assert.IsTrue(loc.ContainsLocalizedString("line:1"));
            }
            finally { UnityEngine.Object.DestroyImmediate(loc); }
        }

        [Test]
        public void Localization_PlainEntryWithEmptyEncryptedArray_ReturnsPlainText()
        {
            // Unity deserializes a null byte[] as an empty array; that must not be treated as encoded data.
            var loc = ScriptableObject.CreateInstance<Localization>();
            try
            {
                loc.AddLocalisedStringToAsset("line:1", "Plain");
                loc.entries["line:1"].encryptedString = new byte[0];
                Assert.AreEqual("Plain", loc.GetLocalizedString("line:1"));
            }
            finally { UnityEngine.Object.DestroyImmediate(loc); }
        }

        [Test]
        public void MakeContext_EmptySalt_LeavesContextUnchanged()
        {
            Assert.AreEqual("line:1", StringTableCodec.MakeContext(null, "line:1"));
            Assert.AreEqual("line:1", StringTableCodec.MakeContext("", "line:1"));
            Assert.AreEqual("salt|line:1", StringTableCodec.MakeContext("salt", "line:1"));
        }

        [Test]
        public void Localization_WithSalt_DecodesOnlyWithSameSalt()
        {
            var loc = ScriptableObject.CreateInstance<Localization>();
            try
            {
                loc.stringTableCodec = codec;
                loc.codecSalt = "salt-a";
                loc.AddLocalisedStringToAsset("line:1", "Salted text");
                Assert.AreEqual("Salted text", loc.GetLocalizedString("line:1"));

                loc.codecSalt = "salt-b";
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Failed to decode text for"));
                Assert.IsNull(loc.GetLocalizedString("line:1"));

                loc.codecSalt = "salt-a";
                Assert.AreEqual("Salted text", loc.GetLocalizedString("line:1"));
            }
            finally { UnityEngine.Object.DestroyImmediate(loc); }
        }

        [Test]
        public void Localization_WithoutCodec_StoresPlainText()
        {
            var loc = ScriptableObject.CreateInstance<Localization>();
            try
            {
                loc.AddLocalisedStringToAsset("line:1", "Plain");
                Assert.AreEqual("Plain", loc.entries["line:1"].localizedString);
                Assert.IsNull(loc.entries["line:1"].encryptedString);
                Assert.AreEqual("Plain", loc.GetLocalizedString("line:1"));
            }
            finally { UnityEngine.Object.DestroyImmediate(loc); }
        }
    }
}
