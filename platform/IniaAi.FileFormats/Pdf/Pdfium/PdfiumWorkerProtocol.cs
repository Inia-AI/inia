using System.Buffers.Binary;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// The messages between the server and a PDFium worker process, one request and one answer at a time over the
/// worker's standard input and output. A message is its length as a 32-bit little-endian integer, then its bytes;
/// the first byte of a request is its <see cref="Request"/>, and the first byte of an answer its <see cref="Answer"/>.
/// The worker compiles this file and <see cref="SeccompFilter"/> too, and references nothing else of ours.
/// </summary>
internal static class PdfiumWorkerProtocol
{
    public const int LENGTH_BYTES = sizeof(int);

    /// <summary>The worker exits with this code when its input closes in the middle of a message.</summary>
    public const int CUT_OFF_EXIT_CODE = 3;

    /// <summary>
    /// Followed by a port and a file path, this argument makes the worker try to connect to that port on the loopback
    /// address and to read that file from inside its sandbox, write what it reached to its error output, and exit.
    /// </summary>
    public const string CHECK_SANDBOX = "--check-sandbox";

    public enum Request : byte
    {
        /// <summary>The file's bytes follow. The worker closes any document it holds and opens this one.</summary>
        Open = 1,

        /// <summary>The page number follows; the answer is the page box, as <see cref="WriteBox"/> writes it.</summary>
        Box = 2,

        /// <summary>The page number follows; the answer is the page's width and height as a viewer shows it.</summary>
        DisplayedBox = 3,

        /// <summary>The page number and an optional area follow; the answer is the text as a string.</summary>
        Text = 4,

        /// <summary>The page number follows; the answer is a count, then each box.</summary>
        TextFragmentBoxes = 5,

        /// <summary>The page number, the resolution, an optional area, the image format and the quality follow; the answer is the image.</summary>
        Render = 6,

        /// <summary>
        /// The password's length in UTF-8 bytes, or -1 for none, the password, and the file's bytes follow. The answer is an
        /// <see cref="UnlockOutcome"/>, whether the file is signed, and the length and bytes of the copy without security.
        /// </summary>
        Unlock = 7,
    }

    public enum UnlockOutcome : byte
    {
        NotEncrypted = 0,
        Decrypted = 1,
        PasswordRequired = 2,
        PasswordIncorrect = 3,

        /// <summary>A security handler that takes no password, such as a certificate, or a strip that did not hold.</summary>
        UnsupportedProtection = 4,
    }

    public enum Answer : byte
    {
        /// <summary>The request's result follows; for <see cref="Request.Open"/>, the page count.</summary>
        Done = 0,

        NeedsPassword = 1,

        /// <summary>The file's structure is broken, as when its cross-reference table is cut off; a repair may open it.</summary>
        Damaged = 2,

        /// <summary>PDFium's error code follows.</summary>
        CannotOpen = 3,

        /// <summary>A message follows.</summary>
        NoSuchPage = 4,

        /// <summary>A message follows.</summary>
        Failed = 5,
    }

    public enum ImageFormat : byte
    {
        Png = 0,
        Jpeg = 1,
    }

    public static void WriteLength(Stream stream, int length)
    {
        Span<byte> prefix = stackalloc byte[LENGTH_BYTES];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, length);
        stream.Write(prefix);
    }

    /// <summary>The length of the next message, or null when the stream ends before one starts.</summary>
    public static int? ReadLength(Stream stream)
    {
        Span<byte> prefix = stackalloc byte[LENGTH_BYTES];
        int read = stream.ReadAtLeast(prefix, LENGTH_BYTES, throwOnEndOfStream: false);
        if (read == 0)
        {
            return null;
        }

        if (read < LENGTH_BYTES)
        {
            throw new EndOfStreamException("The stream ended inside a message length.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        return length < 1 ? throw new InvalidDataException($"A message of {length} bytes is not valid.") : length;
    }

    public static byte[] ReadMessage(Stream stream, int length)
    {
        byte[] message = new byte[length];
        stream.ReadExactly(message);
        return message;
    }

    public static void Send(Stream stream, MemoryStream message)
    {
        WriteLength(stream, (int)message.Length);
        message.Position = 0;
        message.CopyTo(stream);
        stream.Flush();
    }

    public static void WriteBox(BinaryWriter writer, double left, double bottom, double right, double top)
    {
        writer.Write(left);
        writer.Write(bottom);
        writer.Write(right);
        writer.Write(top);
    }

    public static (double Left, double Bottom, double Right, double Top) ReadBox(BinaryReader reader)
    {
        return (reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
    }

    public static void WriteArea(BinaryWriter writer, (double Left, double Bottom, double Right, double Top)? area)
    {
        writer.Write(area is not null);
        if (area is (double left, double bottom, double right, double top))
        {
            WriteBox(writer, left, bottom, right, top);
        }
    }

    public static (double Left, double Bottom, double Right, double Top)? ReadArea(BinaryReader reader)
    {
        return reader.ReadBoolean() ? ReadBox(reader) : null;
    }
}
