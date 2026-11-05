namespace IniaAi.Testing.Documents;

/// <summary>
/// Generated PDFs with the page layouts that a scan or a print run produces: two-page spreads, landscape single pages,
/// and portrait pages with a spread among them. The text is the Lorem ipsum filler, and each page opens with the words
/// the layout tests look for.
/// </summary>
public static class LayoutSamples
{
    private const string LOREM = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.";
    private const string IN_NON_MAURIS = "In non mauris justo. Duis vehicula mi vel mi pretium, a viverra erat efficitur. Cras aliquam est ac eros varius, id iaculis dui auctor. Duis pretium neque ligula, et pulvinar mi placerat et. Nulla nec nunc sit amet nunc posuere vestibulum. Ut id neque eget tortor mattis tristique.";
    private const string ULLAMCORPER = "ullamcorper sem, quis scelerisque risus. Integer vitae arcu at magna laoreet facilisis. Praesent ac nisi sit amet ligula vulputate porttitor. Vivamus id felis eget eros feugiat interdum vel vel orci. Phasellus vitae dui at enim interdum ultrices sed eget massa. Aliquam erat volutpat.";
    private const string QUIS = "quis feugiat elit. Nam sodales lectus in ante varius, at posuere magna feugiat. Morbi eget dolor eget nunc dignissim cursus. Mauris sit amet metus in dui varius placerat. Sed ultricies lacus ac pharetra tincidunt. Fusce gravida diam sed nisl maximus, at ornare nisl elementum.";
    private const string MAECENAS = "Maecenas mauris lectus, lobortis et purus mattis, blandit dictum tellus. Etiam eget neque in lacus lacinia ullamcorper. Curabitur tincidunt erat ac lectus pharetra, ut fermentum sapien porta. Nunc vel odio eget orci elementum ultrices. Donec consequat ex eget urna fringilla posuere.";
    private const string CRAS = "Cras id eros a libero tempus dignissim. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia curae. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas. Quisque bibendum velit id lectus interdum, nec euismod nisi sagittis.";

    /// <summary>Three landscape pages, each a spread of two pages: the last spread's right page is empty.</summary>
    public static byte[] DoublePages()
    {
        return PdfFixtures.Layout(
            LaidOutPage.Spread(LOREM, IN_NON_MAURIS),
            LaidOutPage.Spread(ULLAMCORPER, QUIS),
            LaidOutPage.Spread(CRAS, null));
    }

    /// <summary>Three landscape pages whose text runs across the whole width.</summary>
    public static byte[] LandscapeSinglePages()
    {
        return PdfFixtures.Layout(
            LaidOutPage.Across(LOREM),
            LaidOutPage.Across(IN_NON_MAURIS),
            LaidOutPage.Across(CRAS));
    }

    /// <summary>Two portrait pages, a landscape spread, then a portrait page.</summary>
    public static byte[] MixedPages()
    {
        return PdfFixtures.Layout(
            LaidOutPage.Portrait(LOREM),
            LaidOutPage.Portrait(IN_NON_MAURIS),
            LaidOutPage.Spread(ULLAMCORPER, MAECENAS),
            LaidOutPage.Portrait(CRAS));
    }
}
