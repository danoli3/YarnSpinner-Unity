using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Yarn.Unity;

// SAMPLE: replace PartA/PartB with your own bytes; the bundle ID must match at import and runtime, and this deters ripping only, not hooking.
[CreateAssetMenu(fileName = "BundleIdSaltedCodec", menuName = "Yarn Spinner/String Table Codec/Bundle ID Salted")]
public class BundleIdSaltedCodec : AesHmacStringTableCodec
{
    // Not secret: pins the salt instead of using Application.identifier (empty = use the bundle identifier).
    [SerializeField] private string fixedSalt = "";

    // CHANGE THESE: 16+ random bytes each (e.g. from `openssl rand -hex 16`).
    private static readonly byte[] PartA = { 0x3a, 0x91, 0xc4, 0x07, 0x5e, 0xb2, 0x68, 0xd0, 0x1f, 0x7c, 0xa3, 0x49, 0xe6, 0x02, 0x8d, 0xf5 };
    private static readonly byte[] PartB = { 0xc2, 0x1d, 0x77, 0xaa, 0x30, 0x9b, 0xe1, 0x54, 0x6c, 0xf8, 0x03, 0xbe, 0x25, 0xd7, 0x4a, 0x91 };

    protected override byte[] GetMasterKey()
    {
        if (PartA.Length != PartB.Length || PartA.Length < 16)
        {
            throw new InvalidOperationException("PartA and PartB must be the same length (at least 16 bytes).");
        }

        var combined = new byte[PartA.Length];
        for (int i = 0; i < combined.Length; i++)
        {
            combined[i] = (byte)(PartA[i] ^ PartB[i]);
        }

        var salt = string.IsNullOrEmpty(fixedSalt) ? Application.identifier : fixedSalt;
        if (string.IsNullOrEmpty(salt))
        {
            throw new InvalidOperationException("No salt available: Application.identifier is empty and Fixed Salt is not set.");
        }

        using var hmac = new HMACSHA256(combined);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes("yarn-codec-salt|" + salt));
    }
}
