using IniaAi.FileFormats.Inspection;
using IniaAi.WebApp.Components.Components;

namespace IniaAi.WebApp.Tests.Components.Documents;

public class UploadPickerFormatsTests
{
    [Fact]
    public void Accept_InTheOpenBuild_OffersNoFormatTheBuildRefuses()
    {
        string accept = UploadPickerFormats.Accept(new OpenBuildFormats());

        Assert.Equal(".pdf, .jpg, .jpeg, .png, .csv, .xlsx, .xlsm, .docx", accept);
    }
}
