using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.Testing.Documents;

/// <summary>
/// PDF bytes for tests, written byte by byte with no library, so every run writes the same file and no build needs a
/// paid library to write them. A page's text is set in Helvetica, one line of the page per line of the text, and a
/// line too long for the page wraps.
/// </summary>
public static partial class PdfFixtures
{
    public const string ONE_PIXEL_PNG_BASE64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    private const int CATALOG = 1;
    private const int PAGE_TREE = 2;
    private const int FONT = 3;
    private const int ABSENT_OBJECT = 999;
    private const int LINE_CHARACTERS = 90;
    private const string SIZED_PAGE_TEXT = "Sized page content";
    private const string PAGE_SIZE = "/MediaBox [0 0 612 792]";
    private const string LANDSCAPE_SIZE = "/MediaBox [0 0 842 595]";
    private const int LANDSCAPE_TOP = 555;
    private const int LEFT_COLUMN = 40;
    private const int RIGHT_COLUMN = 462;
    private const int COLUMN_CHARACTERS = 60;
    private const int ACROSS_CHARACTERS = 130;

    public static byte[] Minimal(string note)
    {
        return Encoding.ASCII.GetBytes($"%PDF-1.4\n1 0 obj\n<< /Note ({note}) >>\nendobj\nstartxref\n9\n%%EOF\n");
    }

    public static byte[] Plain(string text)
    {
        return Pages(text);
    }

    public static byte[] Pages(params string[] pageTexts)
    {
        return Written([.. pageTexts.Select(text => new PageContent(TextLines(text)))]);
    }

    /// <summary>
    /// A file of <paramref name="pages"/>. A landscape page is A4 on its side. A spread sets each half's text in its
    /// own column with an empty strip down the middle, as a scan of an open book or a two-up print has it.
    /// </summary>
    public static byte[] Layout(params LaidOutPage[] pages)
    {
        return Written([.. pages.Select(static page => page.Landscape ? new PageContent(LandscapeLines(page), MediaBox: LANDSCAPE_SIZE) : new PageContent(TextLines(page.Text)))]);
    }

    /// <summary>One page with a line of text and an image shown at <paramref name="widthPt"/> x <paramref name="heightPt"/> points.</summary>
    public static byte[] WithImage(string text, double widthPt, double heightPt)
    {
        string drawing = FormattableString.Invariant($"q {widthPt} 0 0 {heightPt} 72 400 cm /Im1 Do Q");
        return Written([new PageContent($"{TextLines(text)}\n{drawing}", HasImage: true)]);
    }

    /// <summary>One page with a visible line and a second line in white 1pt text: no reader sees it, every text extractor reads it.</summary>
    public static byte[] WithHiddenLine(string visibleText, string hiddenText)
    {
        return Written([new PageContent($"{TextLines(visibleText)}\nBT /F1 1 Tf 1 1 1 rg 72 600 Td ({Escaped(hiddenText)}) Tj ET")]);
    }

    /// <summary>One page of <paramref name="text"/> whose document properties hold a title, a subject and keywords.</summary>
    public static byte[] WithProperties(string text, string title, string subject, string keywords)
    {
        return Written([new PageContent(TextLines(text))], $"<< /Title ({Escaped(title)}) /Subject ({Escaped(subject)}) /Keywords ({Escaped(keywords)}) >>");
    }

    /// <summary>One page of <paramref name="widthPt"/> x <paramref name="heightPt"/> points with a line of text.</summary>
    public static byte[] SizedPage(double widthPt, double heightPt)
    {
        return Written([new PageContent(TextLines(SIZED_PAGE_TEXT), MediaBox: FormattableString.Invariant($"/MediaBox [0 0 {widthPt} {heightPt}]"))]);
    }

    /// <summary>One page whose crop box shows its lower half: <paramref name="shownText"/> sits inside it, <paramref name="croppedText"/> above it.</summary>
    public static byte[] WithTextOutsideTheCropBox(string shownText, string croppedText)
    {
        string content = $"BT /F1 12 Tf 72 200 Td ({Escaped(shownText)}) Tj ET\nBT /F1 12 Tf 72 700 Td ({Escaped(croppedText)}) Tj ET";
        return Written([new PageContent(content, CropBox: "/CropBox [0 0 612 396]")]);
    }

    /// <summary>
    /// <see cref="Pages"/> cut off in the middle of an entry of its cross-reference table, so it has no trailer, no
    /// startxref and no %%EOF. A reader that opens it directly fails.
    /// </summary>
    public static byte[] TruncatedInCrossReference(params string[] pageTexts)
    {
        const int entryLength = 20;
        const int insideAnEntry = 9;
        byte[] intact = Pages(pageTexts);
        string text = Encoding.Latin1.GetString(intact);
        int crossReference = text.IndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        int firstEntry = text.IndexOf('\n', text.IndexOf('\n', crossReference) + 1) + 1;
        int entryCount = (text.IndexOf("trailer", firstEntry, StringComparison.Ordinal) - firstEntry) / entryLength;
        return intact[..(firstEntry + (entryCount / 2 * entryLength) + insideAnEntry)];
    }

    /// <summary>
    /// A file whose page tree lists, at <paramref name="brokenPageNumber"/>, an object the file does not hold. Aspose
    /// counts that page but loads it as null, and a whole-file text extract over it throws a
    /// <see cref="NullReferenceException"/>.
    /// </summary>
    public static byte[] WithUnresolvablePage(int brokenPageNumber, params string[] pageTexts)
    {
        return Written([.. pageTexts.Select((text, index) => index + 1 == brokenPageNumber ? null : new PageContent(TextLines(text)))]);
    }

    /// <summary>
    /// A file where the page at <paramref name="brokenPageNumber"/> selects its font with a number where the font name
    /// belongs. Aspose throws "Operand value is not a name" when it reads or renders that page.
    /// </summary>
    public static byte[] WithUnparseablePage(int brokenPageNumber, params string[] pageTexts)
    {
        return Written([.. pageTexts.Select((text, index) => new PageContent(
            index + 1 == brokenPageNumber ? $"BT 12 12 Tf 72 700 Td ({Escaped(text)}) Tj ET" : TextLines(text)))]);
    }

    /// <summary>
    /// <paramref name="plain"/>, a file that <see cref="Pages"/> wrote, encrypted with AES-128 by the PDF standard
    /// security handler. A reader needs <paramref name="userPassword"/> to open it, or none when it is empty; the owner
    /// password lifts the permission to print only.
    /// </summary>
    public static byte[] Encrypted(byte[] plain, string userPassword, string ownerPassword)
    {
        return PdfEncryption.Encrypted(plain, userPassword, ownerPassword);
    }

    /// <summary>
    /// Writes the objects and the cross-reference table by hand, so a page can be broken in a way no PDF writer
    /// produces. A null page leaves an object the page tree names out of the file.
    /// </summary>
    private static byte[] Written(IReadOnlyList<PageContent?> pages, string? info = null)
    {
        int ObjectOfPage(int pageNumber) => FONT + (2 * pageNumber) - 1;
        bool hasImage = pages.Any(page => page?.HasImage == true);
        int image = FONT + (2 * pages.Count) + 1;
        IEnumerable<string> kids = Enumerable.Range(1, pages.Count).Select(pageNumber =>
            pages[pageNumber - 1] is null ? $"{ABSENT_OBJECT} 0 R" : $"{ObjectOfPage(pageNumber)} 0 R");

        List<string> objects =
        [
            $"<< /Type /Catalog /Pages {PAGE_TREE} 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pages.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        ];

        foreach (PageContent? page in pages)
        {
            if (page is null)
            {
                objects.Add("null");
                objects.Add("null");
                continue;
            }

            string images = page.HasImage ? $" /XObject << /Im1 {image} 0 R >>" : string.Empty;
            objects.Add($"<< /Type /Page /Parent {PAGE_TREE} 0 R {page.MediaBox ?? PAGE_SIZE}{(page.CropBox is null ? string.Empty : " " + page.CropBox)} /Resources << /Font << /F1 {FONT} 0 R >>{images} >> /Contents {objects.Count + 2} 0 R >>");
            objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(page.Content)} >>\nstream\n{page.Content}\nendstream");
        }

        if (hasImage)
        {
            objects.Add("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Length 1 >>\nstream\n\0\nendstream");
        }

        if (info is not null)
        {
            objects.Add(info);
        }

        var file = new StringBuilder("%PDF-1.4\n");
        List<int> offsets = [];
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(file.Length);
            _ = file.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int crossReferenceOffset = file.Length;
        _ = file.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            _ = file.Append($"{offset:D10} 00000 n \n");
        }

        string infoEntry = info is null ? string.Empty : $" /Info {objects.Count} 0 R";
        _ = file.Append($"trailer\n<< /Size {objects.Count + 1} /Root {CATALOG} 0 R{infoEntry} >>\nstartxref\n{crossReferenceOffset}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(file.ToString());
    }

    /// <summary>The text as lines from the top of the page, each line of the text wrapped to fit the page's width.</summary>
    private static string TextLines(string text)
    {
        IEnumerable<string> lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').SelectMany(static line => Wrapped(line, LINE_CHARACTERS));
        return $"BT /F1 12 Tf 14 TL 72 750 Td {string.Join(" T* ", lines.Select(line => $"({Escaped(line)}) Tj"))} ET";
    }

    private static string LandscapeLines(LaidOutPage page)
    {
        if (!page.IsSpread)
        {
            return Column(page.Text, LEFT_COLUMN, ACROSS_CHARACTERS);
        }

        string left = Column(page.Text, LEFT_COLUMN, COLUMN_CHARACTERS);
        return page.Right is null ? left : $"{left} {Column(page.Right, RIGHT_COLUMN, COLUMN_CHARACTERS)}";
    }

    private static string Column(string text, int left, int width)
    {
        IEnumerable<string> lines = Wrapped(text, width);
        return $"BT /F1 10 Tf 12 TL {left} {LANDSCAPE_TOP} Td {string.Join(" T* ", lines.Select(line => $"({Escaped(line)}) Tj"))} ET";
    }

    private static IEnumerable<string> Wrapped(string line, int width)
    {
        var current = new StringBuilder();
        foreach (string word in line.Split(' '))
        {
            for (int start = 0; start < Math.Max(word.Length, 1); start += width)
            {
                string piece = word.Length == 0 ? string.Empty : word.Substring(start, Math.Min(width, word.Length - start));
                if (current.Length > 0 && current.Length + 1 + piece.Length > width)
                {
                    yield return current.ToString();
                    _ = current.Clear();
                }

                _ = current.Length > 0 ? current.Append(' ').Append(piece) : current.Append(piece);
            }
        }

        yield return current.ToString();
    }

    private static string Escaped(string text)
    {
        return PdfStringSpecial().Replace(text, match => "\\" + match.Value);
    }

    [GeneratedRegex(@"[\\()]")]
    private static partial Regex PdfStringSpecial();

    private sealed record PageContent(string Content, bool HasImage = false, string? CropBox = null, string? MediaBox = null);

    /// <summary>
    /// The standard security handler at revision 4 with AES-128 (ISO 32000-1, 7.6.3), over a file that
    /// <see cref="Written"/> wrote: its streams are its only encrypted values.
    /// </summary>
    private static partial class PdfEncryption
    {
        private const int KEY_LENGTH = 16;
        private const int PERMISSIONS = -3900;
        private const int HASH_ROUNDS = 50;
        private const int RC4_ROUNDS = 20;

        private static readonly byte[] _padding =
        [
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
        ];

        private static readonly byte[] _aesSalt = "sAlT"u8.ToArray();

        public static byte[] Encrypted(byte[] plain, string userPassword, string ownerPassword)
        {
            string text = Encoding.Latin1.GetString(plain);
            List<(int Number, string Body)> objects = [.. ObjectPattern().Matches(text).Select(match => (int.Parse(match.Groups[1].Value), match.Groups[2].Value))];
            if (objects.Count == 0 || objects[0].Number != CATALOG)
            {
                throw new ArgumentException("Encrypted encrypts only a file that PdfFixtures wrote.", nameof(plain));
            }

            byte[] id = RandomNumberGenerator.GetBytes(KEY_LENGTH);
            byte[] owner = OwnerEntry(ownerPassword.Length > 0 ? ownerPassword : userPassword, userPassword);
            byte[] key = FileKey(userPassword, owner, id);
            byte[] user = UserEntry(key, id);
            int encrypt = objects.Count + 1;

            using var file = new MemoryStream();
            Write(file, "%PDF-1.6\n");
            List<long> offsets = [];
            foreach ((int number, string body) in objects)
            {
                offsets.Add(file.Position);
                Write(file, $"{number} 0 obj\n");
                Match stream = StreamPattern().Match(body);
                if (stream.Success)
                {
                    byte[] cipher = AesEncrypted(ObjectKey(key, number), Encoding.Latin1.GetBytes(stream.Groups[2].Value));
                    Write(file, $"<< {stream.Groups[1].Value} /Length {cipher.Length} >>\nstream\n");
                    file.Write(cipher);
                    Write(file, "\nendstream");
                }
                else
                {
                    Write(file, body);
                }

                Write(file, "\nendobj\n");
            }

            offsets.Add(file.Position);
            Write(file, $"{encrypt} 0 obj\n<< /Filter /Standard /V 4 /R 4 /Length 128 /CF << /StdCF << /CFM /AESV2 /AuthEvent /DocOpen /Length 16 >> >> "
                + $"/StmF /StdCF /StrF /StdCF /O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> /P {PERMISSIONS} >>\nendobj\n");

            long crossReference = file.Position;
            Write(file, $"xref\n0 {encrypt + 1}\n0000000000 65535 f \n");
            foreach (long offset in offsets)
            {
                Write(file, $"{offset:D10} 00000 n \n");
            }

            string fileId = Convert.ToHexString(id);
            Write(file, $"trailer\n<< /Size {encrypt + 1} /Root {CATALOG} 0 R /Encrypt {encrypt} 0 R /ID [<{fileId}> <{fileId}>] >>\nstartxref\n{crossReference}\n%%EOF\n");
            return file.ToArray();
        }

        /// <summary>Algorithm 3: the user password padded and encrypted with a key from the owner password.</summary>
        private static byte[] OwnerEntry(string ownerPassword, string userPassword)
        {
            byte[] hash = MD5.HashData(Padded(ownerPassword));
            for (int round = 0; round < HASH_ROUNDS; round++)
            {
                hash = MD5.HashData(hash.AsSpan(0, KEY_LENGTH));
            }

            return Rc4Rounds(hash[..KEY_LENGTH], Padded(userPassword));
        }

        /// <summary>Algorithm 2: the file key from the user password, the owner entry, the permissions and the file ID.</summary>
        private static byte[] FileKey(string userPassword, byte[] owner, byte[] id)
        {
            byte[] permissions = BitConverter.GetBytes(PERMISSIONS);
            byte[] hash = MD5.HashData([.. Padded(userPassword), .. owner, .. permissions, .. id]);
            for (int round = 0; round < HASH_ROUNDS; round++)
            {
                hash = MD5.HashData(hash.AsSpan(0, KEY_LENGTH));
            }

            return hash[..KEY_LENGTH];
        }

        /// <summary>Algorithm 5: the hash of the padding and the file ID, encrypted with the file key, then padded.</summary>
        private static byte[] UserEntry(byte[] key, byte[] id)
        {
            byte[] encrypted = Rc4Rounds(key, MD5.HashData([.. _padding, .. id]));
            return [.. encrypted, .. new byte[KEY_LENGTH]];
        }

        private static byte[] Rc4Rounds(byte[] key, byte[] data)
        {
            byte[] result = data;
            for (int round = 0; round < RC4_ROUNDS; round++)
            {
                result = Rc4([.. key.Select(value => (byte)(value ^ round))], result);
            }

            return result;
        }

        private static byte[] ObjectKey(byte[] key, int objectNumber)
        {
            byte[] number = BitConverter.GetBytes(objectNumber);
            return MD5.HashData([.. key, number[0], number[1], number[2], 0, 0, .. _aesSalt])[..KEY_LENGTH];
        }

        private static byte[] AesEncrypted(byte[] key, byte[] data)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            byte[] iv = RandomNumberGenerator.GetBytes(KEY_LENGTH);
            return [.. iv, .. aes.EncryptCbc(data, iv, PaddingMode.PKCS7)];
        }

        private static byte[] Padded(string password)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(password);
            return [.. bytes.Take(_padding.Length), .. _padding.Take(Math.Max(0, _padding.Length - bytes.Length))];
        }

        private static byte[] Rc4(byte[] key, byte[] data)
        {
            byte[] state = [.. Enumerable.Range(0, 256).Select(value => (byte)value)];
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + state[i] + key[i % key.Length]) & 0xFF;
                (state[i], state[j]) = (state[j], state[i]);
            }

            byte[] output = new byte[data.Length];
            for (int index = 0, i = 0, j = 0; index < data.Length; index++)
            {
                i = (i + 1) & 0xFF;
                j = (j + state[i]) & 0xFF;
                (state[i], state[j]) = (state[j], state[i]);
                output[index] = (byte)(data[index] ^ state[(state[i] + state[j]) & 0xFF]);
            }

            return output;
        }

        private static void Write(Stream file, string text)
        {
            file.Write(Encoding.Latin1.GetBytes(text));
        }

        [GeneratedRegex(@"(\d+) 0 obj\n(.*?)\nendobj\n", RegexOptions.Singleline)]
        private static partial Regex ObjectPattern();

        [GeneratedRegex(@"^<< (.*?) ?/Length \d+ >>\nstream\n(.*)\nendstream$", RegexOptions.Singleline)]
        private static partial Regex StreamPattern();
    }
}

/// <summary>
/// One page of <see cref="PdfFixtures.Layout"/>: text on a portrait page, text across a landscape page, or a landscape
/// spread of two pages side by side, its right page empty when <see cref="Right"/> is null.
/// </summary>
public sealed record LaidOutPage(string Text, bool Landscape, bool IsSpread, string? Right)
{
    public static LaidOutPage Portrait(string text)
    {
        return new LaidOutPage(text, Landscape: false, IsSpread: false, Right: null);
    }

    public static LaidOutPage Across(string text)
    {
        return new LaidOutPage(text, Landscape: true, IsSpread: false, Right: null);
    }

    public static LaidOutPage Spread(string left, string? right)
    {
        return new LaidOutPage(left, Landscape: true, IsSpread: true, Right: right);
    }
}
