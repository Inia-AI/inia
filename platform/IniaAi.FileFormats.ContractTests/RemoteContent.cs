using System.IO.Compression;
using System.Text;

namespace IniaAi.FileFormats.ContractTests;

/// <summary>
/// HTML that names resources at <c>probe</c> in every way a renderer may load one: images, style sheets, frames,
/// objects, CSS, SVG, a base address and a refresh.
/// </summary>
public static class RemoteContent
{
    public static string Head(Uri probe)
    {
        return $"<base href=\"{probe}base/\"><meta http-equiv=\"refresh\" content=\"0;url={probe}refresh\">"
            + $"<link rel=\"stylesheet\" href=\"{probe}style.css\"><link rel=\"preload\" as=\"image\" href=\"{probe}preload.png\">"
            + $"<style>@import url(\"{probe}import.css\"); @font-face {{ font-family: Probe; src: url({probe}font.woff); }} "
            + $".figures {{ background: url({probe}class-background.png); font-family: Probe; }}</style>";
    }

    public static string Body(Uri probe)
    {
        return $"<img src=\"{probe}picture.png\"><img src=\"relative.png\"><img srcset=\"{probe}srcset.png 2x\">"
            + $"<div class=\"figures\" style=\"background-image:url({probe}background.png)\">Figures</div>"
            + $"<table background=\"{probe}table.png\"><tr><td background=\"{probe}cell.png\">Cell</td></tr></table>"
            + $"<iframe src=\"{probe}frame.html\"></iframe><object data=\"{probe}object.svg\"></object><embed src=\"{probe}embed.svg\">"
            + $"<svg><image href=\"{probe}svg.png\" width=\"10\" height=\"10\"/></svg><input type=\"image\" src=\"{probe}input.png\">"
            + $"<video poster=\"{probe}poster.png\"></video><a href=\"https://example.test/report\">Report</a>";
    }

    /// <summary>
    /// <paramref name="document"/> with a picture whose data is linked at <paramref name="probe"/> rather than stored,
    /// and an INCLUDEPICTURE field that names another picture there.
    /// </summary>
    public static byte[] WordDocumentLinkingTo(byte[] document, Uri probe)
    {
        const string drawingMl = "http://schemas.openxmlformats.org/drawingml/2006/main";
        const string picture = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        string linked = "<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"952500\" cy=\"952500\"/><wp:docPr id=\"990\" name=\"Linked\"/>"
            + $"<a:graphic xmlns:a=\"{drawingMl}\"><a:graphicData uri=\"{picture}\"><pic:pic xmlns:pic=\"{picture}\">"
            + "<pic:nvPicPr><pic:cNvPr id=\"990\" name=\"Linked\"/><pic:cNvPicPr/></pic:nvPicPr>"
            + "<pic:blipFill><a:blip r:link=\"rIdLinkedProbe\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>"
            + "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"952500\" cy=\"952500\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>"
            + "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>"
            + "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + $"<w:r><w:instrText xml:space=\"preserve\"> INCLUDEPICTURE \"{probe}field.png\" \\d </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
        string relationship = "<Relationship Id=\"rIdLinkedProbe\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" "
            + $"Target=\"{probe}linked.png\" TargetMode=\"External\"/>";

        using var stream = new MemoryStream();
        stream.Write(document);
        using (var package = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            Rewrite(package, "word/document.xml", xml => xml.Replace("<w:body>", "<w:body>" + linked, StringComparison.Ordinal));
            Rewrite(package, "word/_rels/document.xml.rels", xml => xml.Replace("</Relationships>", relationship + "</Relationships>", StringComparison.Ordinal));
        }

        return stream.ToArray();
    }

    /// <summary><paramref name="workbook"/> with the picture of its first drawing linked at <paramref name="probe"/> rather than stored.</summary>
    public static byte[] WorkbookLinkingTo(byte[] workbook, Uri probe)
    {
        using var stream = new MemoryStream();
        stream.Write(workbook);
        using (var package = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            Rewrite(package, "xl/drawings/drawing1.xml", xml => xml.Replace("r:embed=", "r:link=", StringComparison.Ordinal));
            Rewrite(package, "xl/drawings/_rels/drawing1.xml.rels", xml => xml.Replace(
                "Target=\"../media/image1.png\"", $"Target=\"{probe}linked.png\" TargetMode=\"External\"", StringComparison.Ordinal));
        }

        return stream.ToArray();
    }

    private static void Rewrite(ZipArchive package, string part, Func<string, string> change)
    {
        ZipArchiveEntry entry = package.GetEntry(part) ?? throw new ArgumentException($"The package has no part {part}.", nameof(part));
        string xml;
        using (var reader = new StreamReader(entry.Open()))
        {
            xml = reader.ReadToEnd();
        }

        entry.Delete();
        using var writer = new StreamWriter(package.CreateEntry(part).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(change(xml));
    }
}
