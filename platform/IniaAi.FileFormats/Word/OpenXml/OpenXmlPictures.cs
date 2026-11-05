using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IniaAi.FileFormats.Images;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>Embeds images in the document and returns the run that shows each one, inline or floating.</summary>
internal sealed class OpenXmlPictures
{
    private const long EMU_PER_POINT = 12700;
    private const double POINTS_PER_SCREEN_PIXEL = 72.0 / 96.0;
    private const double UNREADABLE_IMAGE_SIDE = 72.0;
    private const double FLOAT_PADDING_POINTS = 8.0;
    private const string LEFT = "left";
    private const string RIGHT = "right";

    private readonly MainDocumentPart _main;
    private uint _nextId = 1;

    public OpenXmlPictures(MainDocumentPart main)
    {
        _main = main;
    }

    /// <summary>The image at its natural size, scaled down to <paramref name="maxWidth"/>, or stretched to it.</summary>
    public Run Run(byte[] image, double maxWidth, bool stretchToWidth = false)
    {
        (double width, double height) = NaturalSize(image);
        double scale = stretchToWidth || width > maxWidth ? maxWidth / width : 1;
        return Run(image, width * scale, height * scale);
    }

    public Run Run(byte[] image, double width, double height)
    {
        (A.Graphic graphic, long cx, long cy, uint id, string name) = Graphic(image, width, height);
        var inline = new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.DocProperties { Id = id, Name = name },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = 0,
            DistanceFromBottom = 0,
            DistanceFromLeft = 0,
            DistanceFromRight = 0,
        };

        return new Run(new Drawing(inline));
    }

    /// <summary>The image floating at the left or the right margin from the top of its paragraph, with text wrapping around it.</summary>
    public Run FloatingRun(byte[] image, double width, double height, bool atRight)
    {
        (A.Graphic graphic, long cx, long cy, uint id, string name) = Graphic(image, width, height);
        uint distance = (uint)(FLOAT_PADDING_POINTS * EMU_PER_POINT);
        var anchor = new DW.Anchor(
            new DW.SimplePosition { X = 0, Y = 0 },
            new DW.HorizontalPosition(new DW.HorizontalAlignment(atRight ? RIGHT : LEFT)) { RelativeFrom = DW.HorizontalRelativePositionValues.Margin },
            new DW.VerticalPosition(new DW.PositionOffset("0")) { RelativeFrom = DW.VerticalRelativePositionValues.Paragraph },
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new DW.WrapSquare { WrapText = DW.WrapTextValues.BothSides },
            new DW.DocProperties { Id = id, Name = name },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = 0,
            DistanceFromBottom = distance,
            DistanceFromLeft = distance,
            DistanceFromRight = distance,
            SimplePos = false,
            RelativeHeight = id,
            BehindDoc = false,
            Locked = false,
            LayoutInCell = true,
            AllowOverlap = true,
        };

        return new Run(new Drawing(anchor));
    }

    private (A.Graphic Graphic, long Cx, long Cy, uint Id, string Name) Graphic(byte[] image, double width, double height)
    {
        ImagePart part = _main.AddImagePart(PartType(image));
        using (var stream = new MemoryStream(image, writable: false))
        {
            part.FeedData(stream);
        }

        uint id = _nextId++;
        string name = $"Picture {id}";
        long cx = (long)(width * EMU_PER_POINT);
        long cy = (long)(height * EMU_PER_POINT);
        var graphic = new A.Graphic(new A.GraphicData(
            new PIC.Picture(
                new PIC.NonVisualPictureProperties(
                    new PIC.NonVisualDrawingProperties { Id = 0, Name = name },
                    new PIC.NonVisualPictureDrawingProperties()),
                new PIC.BlipFill(
                    new A.Blip { Embed = _main.GetIdOfPart(part) },
                    new A.Stretch(new A.FillRectangle())),
                new PIC.ShapeProperties(
                    new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
        {
            Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
        });
        return (graphic, cx, cy, id, name);
    }

    private static (double Width, double Height) NaturalSize(byte[] image)
    {
        return Pictures.ReadSize(image) is { Width: > 0, Height: > 0 } size
            ? (size.Width * POINTS_PER_SCREEN_PIXEL, size.Height * POINTS_PER_SCREEN_PIXEL)
            : (UNREADABLE_IMAGE_SIDE, UNREADABLE_IMAGE_SIDE);
    }

    private static PartTypeInfo PartType(byte[] image)
    {
        if (image.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8]))
        {
            return ImagePartType.Jpeg;
        }

        if (image.AsSpan().StartsWith("GIF"u8))
        {
            return ImagePartType.Gif;
        }

        return ImagePartType.Png;
    }
}
