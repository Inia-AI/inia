using IniaAi.FileFormats.Inspection.Checks;
using OpenMcdf;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace IniaAi.FileFormats.Unlock;

/// <summary>
/// Decrypts a password-protected Office file as MS-OFFCRYPTO defines it: a compound file whose EncryptionInfo stream
/// says how the package in the EncryptedPackage stream was encrypted. Reads agile encryption, which Office 2010 and
/// later write, and the older AES standard encryption.
/// </summary>
internal sealed class OfficePackageDecryption
{
    private const int AGILE_SEGMENT_LENGTH = 4096;
    private const int AGILE_XML_OFFSET = 8;
    private const int PACKAGE_LENGTH_BYTES = 8;
    private const int STANDARD_SPIN_COUNT = 50000;
    private const int MAX_SPIN_COUNT = 10_000_000;
    private const int ITERATOR_BYTES = 4;
    private const int STANDARD_HEADER_OFFSET = 12;
    private const int AES_BLOCK = 16;
    private const int SHA1_LENGTH = 20;
    private const byte KEY_PADDING = 0x36;
    private const byte OUTER_PADDING = 0x5C;
    private const string PASSWORD_KEY_ENCRYPTOR = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";

    private static readonly XNamespace _encryption = "http://schemas.microsoft.com/office/2006/encryption";
    private static readonly XNamespace _passwordEncryptor = PASSWORD_KEY_ENCRYPTOR;
    private static readonly byte[] _verifierHashInputBlock = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] _verifierHashValueBlock = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] _encryptedKeyValueBlock = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    private static readonly int[] _standardAesAlgorithms = [0x660E, 0x660F, 0x6610];
    private static readonly int[] _aesKeyBits = [128, 192, 256];

    private readonly byte[] _fileBytes;
    private readonly byte[] _info;
    private readonly (XElement KeyData, XElement PasswordKey)? _agile;

    private OfficePackageDecryption(byte[] fileBytes, byte[] info, (XElement KeyData, XElement PasswordKey)? agile)
    {
        _fileBytes = fileBytes;
        _info = info;
        _agile = agile;
    }

    /// <summary>Reads how the file was encrypted, before any password is tried. The package itself is read only for a password that opens it.</summary>
    /// <exception cref="NotSupportedException">No password removes the protection, as with rights management or a certificate.</exception>
    public static OfficePackageDecryption Read(byte[] fileBytes)
    {
        using var input = new MemoryStream(fileBytes, writable: false);
        using RootStorage root = RootStorage.Open(input, StorageModeFlags.LeaveOpen);
        byte[] info = ReadStream(root, CompoundFileProbe.ENCRYPTION_INFO_STREAM, fileBytes.Length);
        if (!root.TryOpenStream(CompoundFileProbe.ENCRYPTED_PACKAGE_STREAM, out CfbStream? package))
        {
            throw new NotSupportedException($"The compound file has no {CompoundFileProbe.ENCRYPTED_PACKAGE_STREAM} stream.");
        }

        package.Dispose();
        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(info);
        ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(2));
        switch ((major, minor))
        {
            case (4, 4):
                return new OfficePackageDecryption(fileBytes, info, AgileKeys(info));
            case (2 or 3 or 4, 2):
                RequireStandardAes(info);
                return new OfficePackageDecryption(fileBytes, info, agile: null);
            default:
                throw new NotSupportedException($"Office encryption version {major}.{minor} is not supported.");
        }
    }

    /// <returns>The decrypted package, or null when <paramref name="password"/> does not open it.</returns>
    /// <exception cref="InvalidDataException">The package claims more bytes than it holds.</exception>
    public byte[]? Decrypt(string password)
    {
        return _agile is (XElement keyData, XElement passwordKey)
            ? DecryptAgile(keyData, passwordKey, password)
            : DecryptStandard(password);
    }

    private static (XElement KeyData, XElement PasswordKey) AgileKeys(byte[] info)
    {
        XElement encryption = XDocument.Parse(Encoding.UTF8.GetString(info, AGILE_XML_OFFSET, info.Length - AGILE_XML_OFFSET)).Root
            ?? throw new NotSupportedException("The encryption description is empty.");
        XElement keyData = encryption.Element(_encryption + "keyData") ?? throw new NotSupportedException("The encryption description has no key data.");
        XElement passwordKey = encryption
            .Element(_encryption + "keyEncryptors")?
            .Elements(_encryption + "keyEncryptor")
            .Where(encryptor => (string?)encryptor.Attribute("uri") == PASSWORD_KEY_ENCRYPTOR)
            .Select(encryptor => encryptor.Element(_passwordEncryptor + "encryptedKey"))
            .FirstOrDefault()
            ?? throw new NotSupportedException("The file is encrypted to a certificate, not a password.");

        RequireAes(keyData);
        RequireAes(passwordKey);
        long spinCount = (long)passwordKey.Attribute("spinCount")!;
        if (spinCount is < 0 or > MAX_SPIN_COUNT)
        {
            throw new NotSupportedException($"A spin count of {spinCount} is outside the 0 to {MAX_SPIN_COUNT} that MS-OFFCRYPTO allows.");
        }

        return (keyData, passwordKey);
    }

    private static void RequireStandardAes(byte[] info)
    {
        int algorithm = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(STANDARD_HEADER_OFFSET + 8));
        if (!_standardAesAlgorithms.Contains(algorithm))
        {
            throw new NotSupportedException($"Standard encryption with algorithm 0x{algorithm:X} is not supported.");
        }
    }

    private byte[]? DecryptAgile(XElement keyData, XElement passwordKey, string password)
    {
        HashAlgorithmName keyHash = HashNamed((string)passwordKey.Attribute("hashAlgorithm")!);
        byte[] keySalt = Base64(passwordKey, "saltValue");
        int keyBytes = (int)passwordKey.Attribute("keyBits")! / 8;
        byte[] keyIv = FitTo(keySalt, (int)passwordKey.Attribute("blockSize")!, KEY_PADDING);
        byte[] stretched = StretchedPasswordHash(keyHash, keySalt, password, (int)passwordKey.Attribute("spinCount")!);

        byte[] verifierInput = AesCbc(DeriveKey(keyHash, stretched, _verifierHashInputBlock, keyBytes), keyIv, Base64(passwordKey, "encryptedVerifierHashInput"))
            .AsSpan(0, keySalt.Length)
            .ToArray();
        byte[] verifierHash = AesCbc(DeriveKey(keyHash, stretched, _verifierHashValueBlock, keyBytes), keyIv, Base64(passwordKey, "encryptedVerifierHashValue"));
        byte[] expectedHash = Hash(keyHash, verifierInput);
        if (!verifierHash.AsSpan(0, expectedHash.Length).SequenceEqual(expectedHash))
        {
            return null;
        }

        byte[] secretKey = AesCbc(DeriveKey(keyHash, stretched, _encryptedKeyValueBlock, keyBytes), keyIv, Base64(passwordKey, "encryptedKeyValue"))
            .AsSpan(0, (int)keyData.Attribute("keyBits")! / 8)
            .ToArray();

        HashAlgorithmName dataHash = HashNamed((string)keyData.Attribute("hashAlgorithm")!);
        byte[] dataSalt = Base64(keyData, "saltValue");
        int dataBlockSize = (int)keyData.Attribute("blockSize")!;
        byte[] package = ReadPackage();
        byte[] plain = new byte[PlainLength(package)];
        byte[] segmentKey = [.. dataSalt, .. new byte[ITERATOR_BYTES]];
        Span<byte> segmentHash = stackalloc byte[HashLength(dataHash)];
        byte[] iv = new byte[dataBlockSize];
        byte[] lastSegment = new byte[AGILE_SEGMENT_LENGTH];

        using var aes = Aes.Create();
        aes.Key = secretKey;
        for (int segment = 0, offset = PACKAGE_LENGTH_BYTES; offset < package.Length && segment * AGILE_SEGMENT_LENGTH < plain.Length; segment++, offset += AGILE_SEGMENT_LENGTH)
        {
            BinaryPrimitives.WriteInt32LittleEndian(segmentKey.AsSpan(dataSalt.Length), segment);
            CryptographicOperations.HashData(dataHash, segmentKey, segmentHash);
            FitInto(segmentHash, iv, KEY_PADDING);
            int cipherLength = Math.Min(AGILE_SEGMENT_LENGTH, package.Length - offset);
            ReadOnlySpan<byte> cipher = package.AsSpan(offset, cipherLength - (cipherLength % dataBlockSize));
            int start = segment * AGILE_SEGMENT_LENGTH;
            int plainLength = Math.Min(cipher.Length, plain.Length - start);
            if (plainLength == cipher.Length)
            {
                aes.DecryptCbc(cipher, iv, plain.AsSpan(start, plainLength), PaddingMode.None);
            }
            else
            {
                aes.DecryptCbc(cipher, iv, lastSegment, PaddingMode.None);
                lastSegment.AsSpan(0, plainLength).CopyTo(plain.AsSpan(start));
            }
        }

        return plain;
    }

    private byte[]? DecryptStandard(string password)
    {
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(_info.AsSpan(8));
        int keyBytes = BinaryPrimitives.ReadInt32LittleEndian(_info.AsSpan(STANDARD_HEADER_OFFSET + 16)) / 8;
        ReadOnlySpan<byte> verifier = _info.AsSpan(STANDARD_HEADER_OFFSET + headerSize);
        int saltSize = BinaryPrimitives.ReadInt32LittleEndian(verifier);
        byte[] salt = verifier.Slice(4, saltSize).ToArray();
        byte[] encryptedVerifier = verifier.Slice(4 + saltSize, AES_BLOCK).ToArray();
        byte[] encryptedVerifierHash = verifier.Slice(4 + saltSize + AES_BLOCK + 4, 2 * AES_BLOCK).ToArray();

        byte[] stretched = StretchedPasswordHash(HashAlgorithmName.SHA1, salt, password, STANDARD_SPIN_COUNT);
        byte[] final = Hash(HashAlgorithmName.SHA1, stretched, new byte[4]);
        byte[] key = [.. Hash(HashAlgorithmName.SHA1, XorWith(final, KEY_PADDING)), .. Hash(HashAlgorithmName.SHA1, XorWith(final, OUTER_PADDING))];

        using var aes = Aes.Create();
        aes.Key = key[..keyBytes];
        byte[] verifierBytes = aes.DecryptEcb(encryptedVerifier, PaddingMode.None);
        byte[] verifierHash = aes.DecryptEcb(encryptedVerifierHash, PaddingMode.None);
        if (!verifierHash.AsSpan(0, SHA1_LENGTH).SequenceEqual(Hash(HashAlgorithmName.SHA1, verifierBytes)))
        {
            return null;
        }

        byte[] package = ReadPackage();
        byte[] plain = new byte[PlainLength(package)];
        ReadOnlySpan<byte> cipher = package.AsSpan(PACKAGE_LENGTH_BYTES);
        int wholeBlocks = plain.Length - (plain.Length % AES_BLOCK);
        aes.DecryptEcb(cipher[..wholeBlocks], plain.AsSpan(0, wholeBlocks), PaddingMode.None);
        if (wholeBlocks < plain.Length)
        {
            Span<byte> lastBlock = stackalloc byte[AES_BLOCK];
            aes.DecryptEcb(cipher.Slice(wholeBlocks, AES_BLOCK), lastBlock, PaddingMode.None);
            lastBlock[..(plain.Length - wholeBlocks)].CopyTo(plain.AsSpan(wholeBlocks));
        }

        return plain;
    }

    private byte[] ReadPackage()
    {
        using var input = new MemoryStream(_fileBytes, writable: false);
        using RootStorage root = RootStorage.Open(input, StorageModeFlags.LeaveOpen);
        return ReadStream(root, CompoundFileProbe.ENCRYPTED_PACKAGE_STREAM, _fileBytes.Length);
    }

    /// <summary>The length the package's header declares, which the encrypted bytes after it must hold.</summary>
    private static int PlainLength(byte[] package)
    {
        long length = BinaryPrimitives.ReadInt64LittleEndian(package);
        if (length < 0 || length > package.Length - PACKAGE_LENGTH_BYTES)
        {
            throw new InvalidDataException($"The encrypted package declares {length} bytes but holds {package.Length - PACKAGE_LENGTH_BYTES}.");
        }

        return (int)length;
    }

    /// <summary>The hash of the salt and the password, stretched <paramref name="spinCount"/> times (MS-OFFCRYPTO 2.3.4.11).</summary>
    private static byte[] StretchedPasswordHash(HashAlgorithmName hashName, byte[] salt, string password, int spinCount)
    {
        byte[] hash = Hash(hashName, salt, Encoding.Unicode.GetBytes(password));
        Span<byte> iteratorAndHash = stackalloc byte[ITERATOR_BYTES + hash.Length];
        Span<byte> next = stackalloc byte[hash.Length];
        hash.CopyTo(iteratorAndHash[ITERATOR_BYTES..]);
        for (int index = 0; index < spinCount; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(iteratorAndHash, index);
            CryptographicOperations.HashData(hashName, iteratorAndHash, next);
            next.CopyTo(iteratorAndHash[ITERATOR_BYTES..]);
        }

        return iteratorAndHash[ITERATOR_BYTES..].ToArray();
    }

    /// <summary>The key for one use of the stretched hash, cut or padded with 0x36 to <paramref name="keyLength"/> bytes.</summary>
    private static byte[] DeriveKey(HashAlgorithmName hashName, byte[] stretchedHash, byte[] blockKey, int keyLength)
    {
        return FitTo(Hash(hashName, stretchedHash, blockKey), keyLength, KEY_PADDING);
    }

    private static byte[] Hash(HashAlgorithmName hashName, params byte[][] parts)
    {
        using var hash = IncrementalHash.CreateHash(hashName);
        foreach (byte[] part in parts)
        {
            hash.AppendData(part);
        }

        return hash.GetHashAndReset();
    }

    private static byte[] FitTo(byte[] bytes, int length, byte padding)
    {
        byte[] fitted = new byte[length];
        FitInto(bytes, fitted, padding);
        return fitted;
    }

    private static void FitInto(ReadOnlySpan<byte> bytes, Span<byte> fitted, byte padding)
    {
        fitted.Fill(padding);
        bytes[..Math.Min(fitted.Length, bytes.Length)].CopyTo(fitted);
    }

    private static int HashLength(HashAlgorithmName hashName)
    {
        using var hash = IncrementalHash.CreateHash(hashName);
        return hash.HashLengthInBytes;
    }

    private static HashAlgorithmName HashNamed(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "SHA1" => HashAlgorithmName.SHA1,
            "SHA256" => HashAlgorithmName.SHA256,
            "SHA384" => HashAlgorithmName.SHA384,
            "SHA512" => HashAlgorithmName.SHA512,
            _ => throw new NotSupportedException($"The hash algorithm {name} is not supported."),
        };
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

    /// <summary>AES in CBC mode, with its own block and key sizes; the sizes are checked before any buffer is sized from them.</summary>
    private static void RequireAes(XElement element)
    {
        if (!string.Equals((string?)element.Attribute("cipherAlgorithm"), "AES", StringComparison.OrdinalIgnoreCase)
            || !string.Equals((string?)element.Attribute("cipherChaining"), "ChainingModeCBC", StringComparison.Ordinal))
        {
            throw new NotSupportedException("Only AES in CBC mode is supported.");
        }

        if ((long)element.Attribute("blockSize")! != AES_BLOCK || !_aesKeyBits.Contains((int)(long)element.Attribute("keyBits")!))
        {
            throw new NotSupportedException("AES encryption declares a block or key size AES does not have.");
        }
    }

    private static byte[] AesCbc(byte[] key, byte[] iv, byte[] cipher)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(cipher, iv, PaddingMode.None);
    }

    private static byte[] Base64(XElement element, string attribute)
    {
        return Convert.FromBase64String((string)element.Attribute(attribute)!);
    }

    /// <summary>A stream's bytes. OpenMcdf takes a stream's length from its directory entry, so a length the file cannot hold is refused before it sizes a buffer.</summary>
    private static byte[] ReadStream(RootStorage root, string name, int fileLength)
    {
        if (!root.TryOpenStream(name, out CfbStream? stream))
        {
            throw new NotSupportedException($"The compound file has no {name} stream.");
        }

        using (stream)
        {
            if (stream.Length > fileLength)
            {
                throw new InvalidDataException($"The compound file's {name} stream claims {stream.Length} bytes; the file holds {fileLength}.");
            }

            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
    }
}
