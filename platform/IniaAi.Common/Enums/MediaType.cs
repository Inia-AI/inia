using System.Runtime.Serialization;

namespace IniaAi.Common.Enums;

public enum MediaType
{
    [EnumMember(Value = "image/jpeg")]
    ImageJpeg,

    [EnumMember(Value = "image/png")]
    ImagePng,

    [EnumMember(Value = "image/gif")]
    ImageGif,

    [EnumMember(Value = "image/webp")]
    ImageWebp,

    [EnumMember(Value = "image/svg+xml")]
    ImageSvg,

    [EnumMember(Value = "image/tiff")]
    ImageTiff,

    [EnumMember(Value = "image/bmp")]
    ImageBmp,

    [EnumMember(Value = "text/plain")]
    TextPlain,

    [EnumMember(Value = "text/csv")]
    TextCsv,

    [EnumMember(Value = "audio/aac")]
    AudioAac,

    [EnumMember(Value = "audio/mpeg")]
    AudioMp3,

    [EnumMember(Value = "audio/ogg")]
    AudioOgg,

    [EnumMember(Value = "audio/wav")]
    AudioWav,

    [EnumMember(Value = "application/pdf")]
    ApplicationPdf,

    [EnumMember(Value = "application/json")]
    ApplicationJson,

    [EnumMember(Value = "application/xml")]
    ApplicationXml,

    [EnumMember(Value = "application/rtf")]
    ApplicationRtf,

    [EnumMember(Value = "application/zip")]
    ApplicationZip,

    /// <summary>
    /// Represents Microsoft Excel files in the binary format.
    /// </summary>
    [EnumMember(Value = "application/vnd.ms-excel")]
    ApplicationVndMsExcel,

    /// <summary>
    /// Represents Microsoft Excel files in the Open XML format.
    /// </summary>
    [EnumMember(Value = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet,

    /// <summary>
    /// Represents Microsoft Word documents in the Open XML format.
    /// </summary>
    [EnumMember(Value = "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument,

    /// <summary>
    /// Represents email messages in RFC 822 format.
    /// </summary>
    [EnumMember(Value = "message/rfc822")]
    MessageRfc822,

    /// <summary>
    /// Represents Microsoft Outlook email files.
    /// </summary>
    [EnumMember(Value = "application/vnd.ms-outlook")]
    ApplicationVndMsOutlook,

    Unknown,

    [EnumMember(Value = "application/vnd.ms-excel.sheet.macroEnabled.12")]
    ApplicationVndMsExcelSheetMacroEnabled12,

    [EnumMember(Value = "application/msword")]
    ApplicationMsWord,

    [EnumMember(Value = "application/vnd.ms-powerpoint")]
    ApplicationVndMsPowerpoint,

    [EnumMember(Value = "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    ApplicationVndOpenXmlFormatsOfficeDocumentPresentationMlPresentation,

    [EnumMember(Value = "application/vnd.oasis.opendocument.text")]
    ApplicationVndOasisOpendocumentText,

    [EnumMember(Value = "application/vnd.oasis.opendocument.spreadsheet")]
    ApplicationVndOasisOpendocumentSpreadsheet,

    [EnumMember(Value = "application/vnd.oasis.opendocument.presentation")]
    ApplicationVndOasisOpendocumentPresentation,

    [EnumMember(Value = "text/html")]
    TextHtml,

    [EnumMember(Value = "text/css")]
    TextCss,

    [EnumMember(Value = "application/javascript")]
    ApplicationJavascript,

    [EnumMember(Value = "text/typescript")]
    TextTypescript,

    [EnumMember(Value = "application/x-yaml")]
    ApplicationXYaml,

    [EnumMember(Value = "image/x-icon")]
    ImageIcon,

    [EnumMember(Value = "audio/mp4")]
    AudioMp4,

    [EnumMember(Value = "video/mp4")]
    VideoMp4,

    [EnumMember(Value = "video/x-msvideo")]
    VideoAvi,

    [EnumMember(Value = "video/x-matroska")]
    VideoMkv,

    [EnumMember(Value = "video/quicktime")]
    VideoQuicktime,

    [EnumMember(Value = "video/webm")]
    VideoWebm,

    [EnumMember(Value = "application/x-rar-compressed")]
    ApplicationRar,

    [EnumMember(Value = "application/x-7z-compressed")]
    Application7z,

    [EnumMember(Value = "application/x-tar")]
    ApplicationTar,

    [EnumMember(Value = "application/gzip")]
    ApplicationGzip,

    [EnumMember(Value = "text/x-csharp")]
    TextCSharp,

    [EnumMember(Value = "text/x-python")]
    TextPython,

    [EnumMember(Value = "text/x-java-source")]
    TextJava,

    [EnumMember(Value = "text/x-c++src")]
    TextCpp,

    [EnumMember(Value = "text/x-csrc")]
    TextC,

    [EnumMember(Value = "text/x-chdr")]
    TextCHeader,

    [EnumMember(Value = "application/octet-stream")]
    ApplicationOctetStream,

    [EnumMember(Value = "text/tab-separated-values")]
    TextTabSeparatedValues,

    [EnumMember(Value = "text/markdown")]
    TextMarkdown,
}
