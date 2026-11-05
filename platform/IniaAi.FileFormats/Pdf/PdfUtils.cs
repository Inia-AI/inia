using Microsoft.Extensions.Logging;
using System.Text;

namespace IniaAi.FileFormats.Pdf;

/// <summary>
/// PDF utility methods.
/// </summary>
public static class PdfUtils
{
    public const double POINTS_PER_INCH = 72;

    private const int HEAD_SCAN_BYTES = 64 * 1024;
    private const int TAIL_SCAN_BYTES = 4096;

    public static ReadOnlySpan<byte> Signature => "%PDF"u8;

    private static readonly byte[] StartxrefMarker = "startxref"u8.ToArray();
    private static readonly byte[] EofMarker = "%%EOF"u8.ToArray();
    private static readonly byte[] ObjectMarker = "obj"u8.ToArray();

    /// <summary>
    /// True when the bytes carry the PDF signature and nothing a reader could open: no object in
    /// the first 64 KiB and no startxref pointer in the tail. A damaged PDF that still carries
    /// objects is not a stub; the reader downstream repairs it.
    /// </summary>
    public static bool IsHeaderOnlyStub(byte[] fileBytes)
    {
        if (!fileBytes.AsSpan().StartsWith(Signature))
        {
            return false;
        }

        int head = Math.Min(HEAD_SCAN_BYTES, fileBytes.Length);
        if (fileBytes.AsSpan(0, head).IndexOf(ObjectMarker) >= 0)
        {
            return false;
        }

        int tailStart = Math.Max(0, fileBytes.Length - TAIL_SCAN_BYTES);
        return fileBytes.AsSpan(tailStart).IndexOf(StartxrefMarker) < 0;
    }

    /// <summary>
    /// Performs a lightweight structural check on raw PDF bytes without opening the file.
    /// Checks for the presence of the required startxref pointer and %%EOF marker
    /// in the tail of the file. This is a fast byte scan — no Aspose dependency.
    /// </summary>
    /// <param name="fileBytes">The raw PDF file bytes.</param>
    /// <returns>True if the PDF has valid structure markers; false if it appears truncated or damaged.</returns>
    public static bool HasValidStructure(byte[] fileBytes)
    {
        if (fileBytes.Length < 32)
        {
            return false;
        }

        // Only need to check the tail of the file — these markers are always near the end.
        // Use a generous window since large xref tables can push %%EOF far from the end,
        // but startxref/%%EOF should still be within the last few KB of a valid file.
        int tailSize = Math.Min(4096, fileBytes.Length);
        int searchStart = fileBytes.Length - tailSize;

        bool hasStartxref = FindOccurrence(fileBytes, StartxrefMarker, searchStart) >= 0;
        bool hasEof = FindOccurrence(fileBytes, EofMarker, searchStart) >= 0;

        return hasStartxref && hasEof;
    }

    /// <summary>
    /// Finds the first occurrence of a byte pattern starting from a given position.
    /// </summary>
    private static int FindOccurrence(byte[] data, byte[] pattern, int from)
    {
        for (int i = from; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Attempts to repair a PDF stream that is missing the startxref marker.
    /// Rebuilds the entire cross-reference table by scanning the file body for all
    /// object declarations ("N G obj"), the same approach used by Adobe Reader and Chrome.
    /// This ensures all objects — including fonts, encodings, and spacing metadata — are
    /// properly indexed, even if the original xref table was truncated.
    /// </summary>
    public static Stream TryRepairStartxref(Stream stream, ILogger? logger)
    {
        stream.Position = 0;
        byte[] pdfBytes;

        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            pdfBytes = ms.ToArray();
        }

        // Check if startxref already exists (shouldn't, but guard against it)
        if (FindLastOccurrence(pdfBytes, StartxrefMarker) >= 0)
        {
            logger?.LogWarning("startxref marker already exists, repair not needed.");
            return new MemoryStream(pdfBytes);
        }

        // Scan the entire file body for all "N G obj" declarations
        Dictionary<int, (int offset, int generation)> objects = ScanAllObjects(pdfBytes, logger);
        if (objects.Count == 0)
        {
            logger?.LogError("No PDF objects found in file, cannot repair.");
            return new MemoryStream(pdfBytes);
        }

        // Find the root catalog object
        int rootObjNum = FindRootCatalogObjectNumber(pdfBytes, logger);

        // Determine where the file body ends (strip any existing broken xref table)
        int bodyEnd = FindBodyEnd(pdfBytes);

        // Build a complete xref table from the scanned objects
        int xrefOffset = bodyEnd;
        string xrefTable = BuildXrefTable(objects);
        string trailer = $"trailer\n<< /Size {objects.Keys.Max() + 1} /Root {rootObjNum} 0 R >>\n" +
                          $"startxref\n{xrefOffset}\n%%EOF\n";

        byte[] xrefBytes = Encoding.ASCII.GetBytes(xrefTable);
        byte[] trailerBytes = Encoding.ASCII.GetBytes(trailer);

        logger?.LogInformation(
            "Rebuilt xref from object scan: {ObjectCount} objects found, " +
            "max object number {MaxObj}. Root catalog: {Root} 0 R.",
            objects.Count, objects.Keys.Max(), rootObjNum);

        // Assemble: [file body] + [rebuilt xref] + [trailer]
        var repairedMs = new MemoryStream(bodyEnd + xrefBytes.Length + trailerBytes.Length);
        repairedMs.Write(pdfBytes, 0, bodyEnd);
        repairedMs.Write(xrefBytes);
        repairedMs.Write(trailerBytes);
        repairedMs.Position = 0;

        logger?.LogInformation(
            "PDF stream repaired via full object scan: {OriginalSize} -> {RepairedSize} bytes.",
            pdfBytes.Length, repairedMs.Length);

        return repairedMs;
    }

    /// <summary>
    /// Scans the entire file for all PDF object declarations ("N G obj") and records
    /// their byte offsets. This is the same recovery approach used by Adobe Reader.
    /// </summary>
    private static Dictionary<int, (int offset, int generation)> ScanAllObjects(byte[] data, ILogger? logger)
    {
        var objects = new Dictionary<int, (int offset, int generation)>();
        byte[] marker = " obj"u8.ToArray();

        for (int i = 0; i <= data.Length - marker.Length; i++)
        {
            // Quick check: does " obj" start here?
            if (data[i] != ' ' || data[i + 1] != 'o' || data[i + 2] != 'b' || data[i + 3] != 'j')
            {
                continue;
            }

            // Verify " obj" is followed by whitespace, '<', or EOF (not "object", "objection", etc.)
            int afterObj = i + 4;
            if (afterObj < data.Length
                && data[afterObj] != '\r' && data[afterObj] != '\n'
                && data[afterObj] != ' ' && data[afterObj] != '<')
            {
                continue;
            }

            // Parse backwards: expect "N G obj" where N=object number, G=generation
            // Position i is at the space before "obj", so we need "G" before that space

            // Read generation number (digits before position i)
            int pos = i - 1;
            while (pos >= 0 && char.IsDigit((char)data[pos]))
            {
                pos--;
            }

            int genStart = pos + 1;
            int genLen = i - genStart;
            if (genLen is 0 or > 5)
            {
                continue; // generation is 1-5 digits
            }

            // Expect a space before generation number
            if (pos < 0 || data[pos] != ' ')
            {
                continue;
            }

            pos--;

            // Read object number (digits before the space)
            while (pos >= 0 && char.IsDigit((char)data[pos]))
            {
                pos--;
            }

            int objNumStart = pos + 1;
            int objNumLen = i - genLen - 1 - objNumStart;
            if (objNumLen is <= 0 or > 10)
            {
                continue; // object number is 1-10 digits
            }

            // Verify preceded by newline or start of file (real object declaration, not inside a stream)
            if (pos >= 0 && data[pos] != '\n' && data[pos] != '\r')
            {
                continue;
            }

            // Parse the numbers
            if (!int.TryParse(Encoding.ASCII.GetString(data, objNumStart, objNumLen), out int objNum))
            {
                continue;
            }

            if (!int.TryParse(Encoding.ASCII.GetString(data, genStart, genLen), out int gen))
            {
                continue;
            }

            // Record the offset — points to the start of "N G obj" (where N begins)
            objects[objNum] = (objNumStart, gen);
        }

        logger?.LogInformation("Object scan found {Count} objects in {Size} bytes.", objects.Count, data.Length);

        return objects;
    }

    /// <summary>
    /// Builds a complete xref table string from scanned object offsets.
    /// Objects not found are marked as free entries.
    /// </summary>
    private static string BuildXrefTable(Dictionary<int, (int offset, int generation)> objects)
    {
        int maxObjNum = objects.Keys.Max();

        // Guard against unreasonable object numbers that could cause OOM/DoS
        const int maxAllowedObjectNumber = 500_000;
        if (maxObjNum > maxAllowedObjectNumber)
        {
            throw new InvalidOperationException(
                $"PDF contains object number {maxObjNum} which exceeds the allowed maximum of {maxAllowedObjectNumber}. " +
                "The file may be crafted or severely corrupted.");
        }

        int size = maxObjNum + 1;

        // Pre-allocate: "xref\n0 N\n" header + N entries of 20 bytes each
        var sb = new StringBuilder(20 + (size * 20));
        _ = sb.Append("xref\n");
        _ = sb.Append($"0 {size}\n");

        for (int i = 0; i < size; i++)
        {
            if (i == 0)
            {
                // Object 0 is always the head of the free list
                _ = sb.Append("0000000000 65535 f \r\n");
            }
            else if (objects.TryGetValue(i, out (int offset, int generation) info))
            {
                // In-use object: 10-digit offset, 5-digit generation, 'n'
                _ = sb.Append($"{info.offset:D10} {info.generation:D5} n \r\n");
            }
            else
            {
                // Free entry for objects not found in the file body
                _ = sb.Append("0000000000 00000 f \r\n");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Finds where the PDF body ends, stripping any existing broken/truncated xref table.
    /// </summary>
    private static int FindBodyEnd(byte[] data)
    {
        // Look for the start of the xref table
        byte[] xrefMarker = "xref"u8.ToArray();
        int xrefPos = FindLastOccurrence(data, xrefMarker, searchEntireFile: true);

        if (xrefPos > 0)
        {
            // Verify this is a real xref keyword (followed by newline, not inside a stream)
            int after = xrefPos + 4;
            if (after < data.Length && (data[after] == '\r' || data[after] == '\n'))
            {
                return xrefPos;
            }
        }

        // No xref found — body extends to end of file
        return data.Length;
    }

    /// <summary>
    /// Finds the root catalog object number by scanning for /Type/Catalog or /Type /Catalog
    /// near the beginning of the file. Falls back to object 1 (the most common default).
    /// </summary>
    private static int FindRootCatalogObjectNumber(byte[] pdfBytes, ILogger? logger)
    {
        // Search the first 4KB for the catalog object — it's typically one of the first objects
        int searchLen = Math.Min(4096, pdfBytes.Length);
        string header = Encoding.ASCII.GetString(pdfBytes, 0, searchLen);

        // Look for patterns like "/Type/Catalog" or "/Type /Catalog"
        int catalogIdx = header.IndexOf("/Type/Catalog", StringComparison.Ordinal);
        if (catalogIdx < 0)
        {
            catalogIdx = header.IndexOf("/Type /Catalog", StringComparison.Ordinal);
        }

        if (catalogIdx < 0)
        {
            logger?.LogWarning("Could not find /Type/Catalog in header, defaulting to root object 1.");
            return 1;
        }

        // Scan backwards from the catalog type to find "N 0 obj"
        string beforeCatalog = header[..catalogIdx];
        int objIdx = beforeCatalog.LastIndexOf(" 0 obj", StringComparison.Ordinal);
        if (objIdx < 0)
        {
            logger?.LogWarning("Could not find object declaration before /Type/Catalog, defaulting to root object 1.");
            return 1;
        }

        // Extract the object number (digits before " 0 obj")
        int numStart = objIdx - 1;
        while (numStart >= 0 && char.IsDigit((char)pdfBytes[numStart]))
        {
            numStart--;
        }

        numStart++;

        return numStart < objIdx && int.TryParse(beforeCatalog[numStart..objIdx], out int objNum) ? objNum : 1;
    }

    /// <summary>
    /// Finds the last occurrence of a byte sequence in a byte array.
    /// </summary>
    private static int FindLastOccurrence(byte[] data, byte[] pattern, bool searchEntireFile = false)
    {
        int searchStart = searchEntireFile ? 0 : Math.Max(0, data.Length - 4096);

        for (int i = data.Length - pattern.Length; i >= searchStart; i--)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
