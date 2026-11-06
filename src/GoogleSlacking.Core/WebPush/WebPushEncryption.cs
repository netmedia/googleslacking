using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GoogleSlacking.Core.WebPush;

/// <summary>Message encryption for Web Push, RFC 8291 ("aes128gcm" content coding, RFC 8188), single record.</summary>
public static class WebPushEncryption
{
    private const int RecordSize = 4096;
    private static readonly byte[] KeyInfoPrefix = "WebPush: info\0"u8.ToArray();
    private static readonly byte[] CekInfo = "Content-Encoding: aes128gcm\0"u8.ToArray();
    private static readonly byte[] NonceInfo = "Content-Encoding: nonce\0"u8.ToArray();

    /// <param name="uaPublicKey">Subscription "p256dh" (65 bytes).</param>
    /// <param name="authSecret">Subscription "auth" (16 bytes).</param>
    /// <param name="serverKey">Ephemeral key (tests only; normally generated).</param>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] uaPublicKey, byte[] authSecret,
        ECDiffieHellman? serverKey = null, byte[]? salt = null)
    {
        if (plaintext.Length > RecordSize - 17 - 86) throw new ArgumentException("Payload too large for a single record.");

        using var asKey = serverKey ?? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var asParams = asKey.ExportParameters(false);
        byte[] asPublic = [0x04, .. asParams.Q.X!, .. asParams.Q.Y!];

        using var ua = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uaPublicKey[1..33], Y = uaPublicKey[33..65] },
        });
        var ecdhSecret = asKey.DeriveRawSecretAgreement(ua.PublicKey);

        salt ??= RandomNumberGenerator.GetBytes(16);
        var (cek, nonce) = DeriveKeys(ecdhSecret, authSecret, uaPublicKey, asPublic, salt);

        // Single record: plaintext || 0x02 (last-record delimiter), no extra padding.
        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded);
        padded[^1] = 0x02;

        var cipher = new byte[padded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
            aes.Encrypt(nonce, padded, cipher, tag);

        // Header: salt(16) | rs(4, BE) | idlen(1) | keyid(65 = server public key)
        var result = new byte[16 + 4 + 1 + asPublic.Length + cipher.Length + tag.Length];
        var span = result.AsSpan();
        salt.CopyTo(span);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], RecordSize);
        span[20] = (byte)asPublic.Length;
        asPublic.CopyTo(span[21..]);
        cipher.CopyTo(span[(21 + asPublic.Length)..]);
        tag.CopyTo(span[(21 + asPublic.Length + cipher.Length)..]);
        return result;
    }

    /// <summary>Inverse of <see cref="Encrypt"/> (user-agent side). Used by tests.</summary>
    public static byte[] Decrypt(byte[] body, ECDiffieHellman uaKey, byte[] authSecret)
    {
        var salt = body[..16];
        int idLen = body[20];
        var asPublic = body[21..(21 + idLen)];
        var payload = body[(21 + idLen)..];

        var uaParams = uaKey.ExportParameters(false);
        byte[] uaPublic = [0x04, .. uaParams.Q.X!, .. uaParams.Q.Y!];
        using var asKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = asPublic[1..33], Y = asPublic[33..65] },
        });
        var ecdhSecret = uaKey.DeriveRawSecretAgreement(asKey.PublicKey);
        var (cek, nonce) = DeriveKeys(ecdhSecret, authSecret, uaPublic, asPublic, salt);

        var cipher = payload[..^16];
        var tag = payload[^16..];
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(cek, 16))
            aes.Decrypt(nonce, cipher, tag, plain);

        var end = Array.LastIndexOf(plain, (byte)0x02);
        return plain[..end];
    }

    private static (byte[] Cek, byte[] Nonce) DeriveKeys(byte[] ecdhSecret, byte[] authSecret, byte[] uaPublic, byte[] asPublic, byte[] salt)
    {
        byte[] keyInfo = [.. KeyInfoPrefix, .. uaPublic, .. asPublic];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, CekInfo);
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, NonceInfo);
        return (cek, nonce);
    }
}
