using IniaAi.Testing;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IniaAi.WebApp.Tests.Components;

public partial class BrowserTabIconTests
{
    private const string APP_PATH = "IniaAi.WebApp/Components/App.razor";
    private const string WWWROOT = "IniaAi.WebApp/wwwroot";
    private const string EDITOR_TILE_COPPER = "#AD8977";
    private const int PNG_WIDTH_OFFSET = 16;
    private const int PNG_HEIGHT_OFFSET = 20;
    private const int PNG_COLOR_TYPE_OFFSET = 25;
    private const byte PNG_COLOR_TYPE_RGB = 2;

    private static readonly string _repositoryRoot = RepositoryRoot.Find();

    [GeneratedRegex("""<link rel="(icon|apple-touch-icon)"[^>]*>""")]
    private static partial Regex AnyIconLink();

    [Fact]
    public void App_LinksAnSvgIconA32PxIcoFallbackAndATouchIcon()
    {
        string app = File.ReadAllText(Path.Combine(_repositoryRoot, APP_PATH));

        Assert.Equal(3, AnyIconLink().Count(app));
        Assert.Contains("""<link rel="icon" href="@Assets["favicon.ico"]" sizes="32x32" />""", app, StringComparison.Ordinal);
        Assert.Contains("""<link rel="icon" href="@Assets["icon.svg"]" type="image/svg+xml" />""", app, StringComparison.Ordinal);
        Assert.Contains("""<link rel="apple-touch-icon" href="@Assets["apple-touch-icon.png"]" />""", app, StringComparison.Ordinal);
    }

    [Fact]
    public void SvgIcon_DrawsOnTheCopperTileThatSetsItApartFromTheNavyAnalystTile()
    {
        XElement svg = XDocument.Load(WwwrootPath("icon.svg")).Root!;
        XNamespace svgNamespace = svg.Name.Namespace;

        Assert.Equal("svg", svg.Name.LocalName);
        Assert.Equal("0 0 64 64", svg.Attribute("viewBox")?.Value);
        Assert.Equal(EDITOR_TILE_COPPER, svg.Element(svgNamespace + "rect")?.Attribute("fill")?.Value);
    }

    [Fact]
    public void IcoFallback_HoldsOne32PxImage()
    {
        byte[] ico = File.ReadAllBytes(WwwrootPath("favicon.ico"));

        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));
        Assert.Equal<byte[]>([32, 32], ico[6..8]);
    }

    [Fact]
    public void TouchIcon_IsAnOpaque180PxSquare()
    {
        byte[] png = File.ReadAllBytes(WwwrootPath("apple-touch-icon.png"));

        Assert.Equal(180u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(PNG_WIDTH_OFFSET)));
        Assert.Equal(180u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(PNG_HEIGHT_OFFSET)));
        Assert.Equal(PNG_COLOR_TYPE_RGB, png[PNG_COLOR_TYPE_OFFSET]);
    }

    private static string WwwrootPath(string file) => Path.Combine(_repositoryRoot, WWWROOT, file);
}
