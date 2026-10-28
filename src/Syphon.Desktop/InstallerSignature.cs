using System.IO;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Diagnostics;

namespace Syphon.Desktop;

public static class InstallerSignature
{
    // Same existing local release identity; rotation requires a separately reviewed app update.
    public const string PublisherThumbprint = "3B47C910EDA7360A3F486154954A22A7DABBABDE";
    public static void VerifyPackage(string path, string expectedVersion)
    {
        Verify(path);
        if (FileVersionInfo.GetVersionInfo(path).ProductVersion?.Split('+')[0] != expectedVersion)
            throw new InvalidDataException("The signed installer version does not match the selected release.");
    }
    public static void Verify(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Length < 512) throw new InvalidDataException("The updater is not a signed Windows installer.");
        stream.Position = 0x3c; var pe = reader.ReadInt32();
        if (pe < 0 || pe + 256L >= stream.Length) throw new InvalidDataException("Invalid installer header.");
        stream.Position = pe; if (reader.ReadUInt32() != 0x4550) throw new InvalidDataException("Invalid installer signature header.");
        var optional = pe + 24; stream.Position = optional; var kind = reader.ReadUInt16();
        if (kind is not (0x10b or 0x20b)) throw new InvalidDataException("Invalid installer format.");
        var security = optional + (kind == 0x20b ? 112 : 96) + 32; var checksum = optional + 64;
        stream.Position = security; var certificate = reader.ReadUInt32(); var size = reader.ReadUInt32();
        if (certificate <= security + 8 || size < 8 || size > 1_048_576 || (long)certificate + size != stream.Length) throw new InvalidDataException("Invalid installer certificate boundary.");
        stream.Position = certificate; var certificateLength = reader.ReadUInt32();
        if (certificateLength < 8 || certificateLength > size || reader.ReadUInt16() != 0x200 || reader.ReadUInt16() != 2) throw new InvalidDataException("Invalid installer certificate.");
        var cms = new SignedCms(); cms.Decode(reader.ReadBytes(checked((int)certificateLength - 8))); cms.CheckSignature(true);
        if (cms.SignerInfos.Count != 1 || cms.SignerInfos[0].Certificate is not { } signer || signer.Thumbprint != PublisherThumbprint ||
            DateTime.UtcNow < signer.NotBefore.ToUniversalTime() || DateTime.UtcNow > signer.NotAfter.ToUniversalTime()) throw new InvalidDataException("The update was not signed by Syphon's expected publisher.");
        if (cms.ContentInfo.ContentType.Value != "1.3.6.1.4.1.311.2.1.4") throw new InvalidDataException("Unexpected installer signed content.");
        var asn = new AsnReader(cms.ContentInfo.Content, AsnEncodingRules.BER); var sequence = asn.ReadSequence(); sequence.ReadEncodedValue();
        var digest = sequence.ReadSequence(); var algorithm = digest.ReadSequence();
        if (algorithm.ReadObjectIdentifier() != "2.16.840.1.101.3.4.2.1") throw new InvalidDataException("The installer must have a SHA-256 signature.");
        var expected = digest.ReadOctetString();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void HashRange(long start, long count) {
            stream.Position = start; var buffer = new byte[131072];
            while (count > 0) { var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count)); if (read == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, read); count -= read; }
        }
        HashRange(0, checksum); HashRange(checksum + 4, security - checksum - 4); HashRange(security + 8, certificate - security - 8);
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expected)) throw new InvalidDataException("The installer signature does not match its file contents.");
    }
}
