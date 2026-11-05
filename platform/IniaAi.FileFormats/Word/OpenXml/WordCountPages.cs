using DocumentFormat.OpenXml.Packaging;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.FileFormats.Images;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>One block of a Word document in reading order: a paragraph or a table as Markdown, and its pictures.</summary>
internal sealed record TextBlock(string Markdown, IReadOnlyList<WordPicture> Pictures)
{
    public static TextBlock Text(string markdown)
    {
        return new TextBlock(markdown, []);
    }
}

/// <summary>A picture of the document, read from its part only when a page shows it to a model.</summary>
internal sealed class WordPicture(ImagePart part)
{
    public string ContentType => part.ContentType;

    public byte[] ReadBytes()
    {
        using Stream stream = part.GetStream();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}

/// <summary>
/// Splits a document into pages by word count, the way the open build pages a Word upload. A page closes after the
/// block that takes it to the words of a page. A paragraph longer than a page is split between words; a table stays
/// whole. The header and footer lines of a section close the page that holds the section's first block.
/// </summary>
internal static class WordCountPages
{
    public static List<WordPageContent> Split(
        IReadOnlyList<TextBlock> blocks,
        IReadOnlyDictionary<int, IReadOnlyList<string>> headerFooterLinesByFirstBlock,
        int wordsPerPage,
        int maxPages,
        VisionTier? visionTier)
    {
        var pages = new List<WordPageContent>();
        var text = new List<string>();
        var images = new List<BinaryContent>();
        var trailer = new List<string>();
        int words = 0;

        void ClosePage()
        {
            if (text.Count == 0 && images.Count == 0 && trailer.Count == 0)
            {
                return;
            }

            string pageText = string.Join("\n\n", text);
            if (trailer.Count > 0)
            {
                string lines = string.Join("\n", trailer);
                pageText = pageText.Length == 0 ? lines : $"{pageText}\n\n{lines}";
            }

            pages.Add(new WordPageContent(pageText, [.. images]));
            text.Clear();
            images.Clear();
            trailer.Clear();
            words = 0;
        }

        for (int i = 0; i < blocks.Count && pages.Count < maxPages; i++)
        {
            if (headerFooterLinesByFirstBlock.TryGetValue(i, out IReadOnlyList<string>? lines))
            {
                trailer.AddRange(lines);
            }

            if (visionTier is VisionTier tier)
            {
                images.AddRange(blocks[i].Pictures.Select(picture => ImageRegion(picture, tier)).OfType<BinaryContent>());
            }

            foreach (string piece in SplitLongParagraph(blocks[i].Markdown, wordsPerPage))
            {
                if (piece.Length > 0)
                {
                    text.Add(piece);
                    words += CountWords(piece);
                }

                if (words >= wordsPerPage)
                {
                    ClosePage();
                }
            }
        }

        if (pages.Count < maxPages)
        {
            ClosePage();
        }

        return pages;
    }

    public static int CountWords(string text)
    {
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// The picture as the model reads it: a PNG or JPEG that fits the vision tier passes as it is, and any other
    /// picture becomes a PNG that fits it. Null when the picture cannot be decoded, such as a Windows metafile.
    /// </summary>
    public static BinaryContent? ImageRegion(WordPicture picture, VisionTier visionTier)
    {
        MediaType? mediaType = picture.ContentType.ToLowerInvariant() switch
        {
            "image/png" => MediaType.ImagePng,
            "image/jpeg" or "image/jpg" => MediaType.ImageJpeg,
            _ => (MediaType?)null,
        };

        try
        {
            byte[] bytes = picture.ReadBytes();
            PictureSize? size = Pictures.ReadSize(bytes);
            if (size is null)
            {
                return null;
            }

            if (mediaType is MediaType passthrough
                && Math.Max(size.Width, size.Height) <= visionTier.LongEdgePx)
            {
                return new BinaryContent(bytes, passthrough);
            }

            return new BinaryContent(Pictures.ToPngWithin(bytes, visionTier.LongEdgePx), MediaType.ImagePng);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SplitLongParagraph(string markdown, int wordsPerPage)
    {
        if (markdown.StartsWith('|') || CountWords(markdown) <= wordsPerPage)
        {
            yield return markdown;
            yield break;
        }

        string[] words = markdown.Split(' ');
        for (int start = 0; start < words.Length; start += wordsPerPage)
        {
            yield return string.Join(' ', words.Skip(start).Take(wordsPerPage));
        }
    }
}
