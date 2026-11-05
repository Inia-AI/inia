using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenMcdf;

namespace IniaAi.Testing.Documents;

/// <summary>
/// Encrypts an Office Open XML package with Office's standard encryption, AES-128 (MS-OFFCRYPTO 2.3.4.5 and 2.3.4.6):
/// a compound file with the EncryptionInfo and EncryptedPackage streams.
/// </summary>
public static class OfficeEncryption
{
    private const string ENCRYPTION_INFO_STREAM = "EncryptionInfo";
    private const string ENCRYPTED_PACKAGE_STREAM = "EncryptedPackage";
    private const string PROVIDER = "Microsoft Enhanced RSA and AES Cryptographic Provider";
    private const int FLAGS = 0x24;
    private const int AES_128 = 0x660E;
    private const int SHA1_ALGORITHM_ID = 0x8004;
    private const int KEY_BITS = 128;
    private const int PROVIDER_TYPE = 0x18;
    private const int SALT_LENGTH = 16;
    private const int BLOCK = 16;
    private const int SHA1_LENGTH = 20;
    private const int SPIN_COUNT = 50000;

    public static byte[] Encrypted(byte[] package, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SALT_LENGTH);
        byte[] key = Key(salt, password);
        byte[] verifier = RandomNumberGenerator.GetBytes(BLOCK);

        using var aes = Aes.Create();
        aes.Key = key;
        byte[] verifierHash = new byte[2 * BLOCK];
        SHA1.HashData(verifier).CopyTo(verifierHash, 0);

        using var info = new MemoryStream();
        using (var writer = new BinaryWriter(info, Encoding.Unicode, leaveOpen: true))
        {
            byte[] provider = Encoding.Unicode.GetBytes(PROVIDER + '\0');
            writer.Write((ushort)4);
            writer.Write((ushort)2);
            writer.Write(FLAGS);
            writer.Write((8 * sizeof(int)) + provider.Length);
            writer.Write(FLAGS);
            writer.Write(0);
            writer.Write(AES_128);
            writer.Write(SHA1_ALGORITHM_ID);
            writer.Write(KEY_BITS);
            writer.Write(PROVIDER_TYPE);
            writer.Write(0);
            writer.Write(0);
            writer.Write(provider);
            writer.Write(SALT_LENGTH);
            writer.Write(salt);
            writer.Write(aes.EncryptEcb(verifier, PaddingMode.None));
            writer.Write(SHA1_LENGTH);
            writer.Write(aes.EncryptEcb(verifierHash, PaddingMode.None));
        }

        byte[] padded = new byte[(package.Length + BLOCK - 1) / BLOCK * BLOCK];
        package.CopyTo(padded, 0);
        byte[] encryptedPackage = new byte[sizeof(long) + padded.Length];
        BinaryPrimitives.WriteInt64LittleEndian(encryptedPackage, package.Length);
        aes.EncryptEcb(padded, PaddingMode.None).CopyTo(encryptedPackage, sizeof(long));

        using var file = new MemoryStream();
        using (RootStorage root = RootStorage.Create(file, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            Write(root, ENCRYPTION_INFO_STREAM, info.ToArray());
            Write(root, ENCRYPTED_PACKAGE_STREAM, encryptedPackage);
        }

        return file.ToArray();
    }

    /// <summary>The key from the salted password hash, stretched and derived as MS-OFFCRYPTO 2.3.4.7 says.</summary>
    private static byte[] Key(byte[] salt, string password)
    {
        byte[] hash = SHA1.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
        byte[] iterator = new byte[sizeof(int)];
        for (int index = 0; index < SPIN_COUNT; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(iterator, index);
            hash = SHA1.HashData([.. iterator, .. hash]);
        }

        byte[] final = SHA1.HashData([.. hash, .. new byte[sizeof(int)]]);
        byte[] inner = SHA1.HashData(XorWith(final, 0x36));
        byte[] outer = SHA1.HashData(XorWith(final, 0x5C));
        byte[] derived = [.. inner, .. outer];
        return derived[..(KEY_BITS / 8)];
    }

    private static byte[] XorWith(byte[] hash, byte pad)
    {
        byte[] buffer = new byte[64];
        Array.Fill(buffer, pad);
        for (int index = 0; index < hash.Length; index++)
        {
            buffer[index] ^= hash[index];
        }

        return buffer;
    }

    private static void Write(RootStorage root, string name, byte[] bytes)
    {
        using CfbStream stream = root.CreateStream(name);
        stream.Write(bytes);
    }
}
