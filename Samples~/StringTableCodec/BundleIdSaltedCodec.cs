using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Yarn.Unity;

// SAMPLE: replace PartA/PartB with your own bytes; the salt is stored in this asset (a new random one on create) and the bundle ID is opt-in because it isn't reliable inside import workers.
[CreateAssetMenu(fileName = "BundleIdSaltedCodec", menuName = "Yarn Spinner/String Table Codec/Bundle ID Salted")]
public class BundleIdSaltedCodec : AesHmacStringTableCodec
{
    // Not secret: stored in this asset so import workers and the player derive the same key.
    [SerializeField] private string fixedSalt = "";

    // Off by default: Application.identifier can differ in asset-import worker processes, which breaks decoding.
    [SerializeField] private bool includeBundleId = false;

    private void Reset() { fixedSalt = Guid.NewGuid().ToString("N"); }

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

        var salt = fixedSalt ?? string.Empty;
        if (includeBundleId)
        {
            var id = Application.identifier;
            if (string.IsNullOrEmpty(id)) { throw new InvalidOperationException("Application.identifier is EMPTY in this process, so the bundle ID can't be used as a salt here. Turn off Include Bundle Id."); }
            salt += "|" + id;
        }
        if (string.IsNullOrEmpty(fixedSalt))
        {
            throw new InvalidOperationException("Fixed Salt is empty: set one on the codec asset (use the Inspector Reset menu to generate a random one).");
        }

        using var hmac = new HMACSHA256(combined);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes("yarn-codec-salt|" + salt));
    }
}
