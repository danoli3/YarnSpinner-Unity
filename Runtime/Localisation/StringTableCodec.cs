/*
Yarn Spinner is licensed to you under the terms found in the file LICENSE.md.
*/

using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

#nullable enable

namespace Yarn.Unity
{
    /// <summary>Transforms Yarn string data (line text and the compiled program) between its stored form in a build and its runtime form.</summary>
    /// <remarks>Raises the cost of casually ripping assets, but can't stop someone who hooks the running game.</remarks>
    public interface IStringTableCodec
    {
        /// <summary>Encodes <paramref name="plaintext"/> for storage.</summary>
        /// <param name="context">Identifies what is encoded (a line ID or <see cref="StringTableCodec.ProgramContext"/>); bind the output to it.</param>
        /// <param name="plaintext">The data to encode.</param>
        byte[] Encode(string context, byte[] plaintext);

        /// <summary>Decodes data produced by <see cref="Encode"/>.</summary>
        /// <param name="context">The context passed to <see cref="Encode"/>.</param>
        /// <param name="encoded">The stored data.</param>
        byte[] Decode(string context, byte[] encoded);
    }

    /// <summary>Base class for codecs, so one can be assigned to a Yarn Project and travel into builds.</summary>
    /// <remarks>Don't store key material in serialized fields: they end up in the build in plain view.</remarks>
    public abstract class StringTableCodec : ScriptableObject, IStringTableCodec
    {
        /// <summary>The context used when encoding a Yarn Project's compiled program.</summary>
        public const string ProgramContext = "yarn:program";

        /// <inheritdoc/>
        public abstract byte[] Encode(string context, byte[] plaintext);

        /// <inheritdoc/>
        public abstract byte[] Decode(string context, byte[] encoded);

        /// <summary>Combines an optional per-project salt with a context, so keys differ between projects; an empty salt returns the context unchanged.</summary>
        public static string MakeContext(string? salt, string context) => string.IsNullOrEmpty(salt) ? context : salt + "|" + context;

        internal byte[] EncodeString(string context, string text) => Encode(context, Encoding.UTF8.GetBytes(text));

        internal string DecodeString(string context, byte[] encoded) => Encoding.UTF8.GetString(Decode(context, encoded));
    }

    /// <summary>AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC) with per-context keys derived from a master key your subclass supplies.</summary>
    /// <remarks>Abstract on purpose, so each game has its own key source. Stored format: <c>[version:1][iv:16][ciphertext][tag:32]</c>.</remarks>
    public abstract class AesHmacStringTableCodec : StringTableCodec
    {
        private const byte FormatVersion = 1;
        private const int IVSize = 16;
        private const int TagSize = 32;

        /// <summary>Returns the master key (at least 16 bytes); assemble it from several sources, not a string literal.</summary>
        protected abstract byte[] GetMasterKey();

        private static byte[] Hmac(byte[] key, byte[] data)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }

        private void DeriveKeys(string context, out byte[] encKey, out byte[] macKey)
        {
            var master = GetMasterKey();
            if (master == null || master.Length < 16)
            {
                throw new InvalidOperationException($"{GetType().Name}.{nameof(GetMasterKey)} must return at least 16 bytes.");
            }
            encKey = Hmac(master, Encoding.UTF8.GetBytes("yarn-enc:" + context));
            macKey = Hmac(master, Encoding.UTF8.GetBytes("yarn-mac:" + context));
        }

        /// <inheritdoc/>
        public override byte[] Encode(string context, byte[] plaintext)
        {
            DeriveKeys(context, out var encKey, out var macKey);

            var iv = new byte[IVSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }

            byte[] cipher;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encKey;
                aes.IV = iv;
                using var enc = aes.CreateEncryptor();
                cipher = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            var body = new byte[1 + IVSize + cipher.Length];
            body[0] = FormatVersion;
            Buffer.BlockCopy(iv, 0, body, 1, IVSize);
            Buffer.BlockCopy(cipher, 0, body, 1 + IVSize, cipher.Length);

            var tag = Hmac(macKey, body);
            var result = new byte[body.Length + TagSize];
            Buffer.BlockCopy(body, 0, result, 0, body.Length);
            Buffer.BlockCopy(tag, 0, result, body.Length, TagSize);
            return result;
        }

        /// <inheritdoc/>
        public override byte[] Decode(string context, byte[] encoded)
        {
            if (encoded.Length < 1 + IVSize + 16 + TagSize || encoded[0] != FormatVersion)
            {
                throw new CryptographicException($"Encoded data for '{context}' is malformed.");
            }

            DeriveKeys(context, out var encKey, out var macKey);

            var bodyLength = encoded.Length - TagSize;
            var body = new byte[bodyLength];
            Buffer.BlockCopy(encoded, 0, body, 0, bodyLength);

            var expected = Hmac(macKey, body);
            int diff = 0;
            for (int i = 0; i < TagSize; i++)
            {
                diff |= expected[i] ^ encoded[bodyLength + i];
            }
            if (diff != 0)
            {
                throw new CryptographicException($"Encoded data for '{context}' failed authentication (wrong key or tampered data).");
            }

            var iv = new byte[IVSize];
            Buffer.BlockCopy(encoded, 1, iv, 0, IVSize);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encKey;
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            return dec.TransformFinalBlock(encoded, 1 + IVSize, bodyLength - 1 - IVSize);
        }
    }
}
