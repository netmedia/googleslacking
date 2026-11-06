using System.Security.Cryptography;

namespace GoogleSlacking.Core.WebPush;

public sealed class WebPushOptions
{
    public const string Section = "Push";

    /// <summary>VAPID public key (base64url, 65-byte uncompressed P-256 point). Generate: dotnet run --project src/GoogleSlacking.Api -- --generate-vapid</summary>
    public string? VapidPublicKey { get; set; }

    /// <summary>VAPID private key (base64url, 32 bytes). Secret!</summary>
    public string? VapidPrivateKey { get; set; }

    /// <summary>Contact for push services, "mailto:..." or https URL.</summary>
    public string Subject { get; set; } = "mailto:admin@example.com";

    public bool IsConfigured => !string.IsNullOrEmpty(VapidPublicKey) && !string.IsNullOrEmpty(VapidPrivateKey);
}

public sealed record VapidKeys(string PublicKey, string PrivateKey)
{
    public static VapidKeys Generate()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(includePrivateParameters: true);
        byte[] pub = [0x04, .. p.Q.X!, .. p.Q.Y!];
        return new VapidKeys(Base64Url.Encode(pub), Base64Url.Encode(p.D!));
    }

    internal ECDsa CreateSigner()
    {
        var pub = Base64Url.Decode(PublicKey);
        if (pub.Length != 65 || pub[0] != 0x04) throw new InvalidOperationException("Invalid VAPID public key.");
        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.Decode(PrivateKey),
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
        });
    }
}
