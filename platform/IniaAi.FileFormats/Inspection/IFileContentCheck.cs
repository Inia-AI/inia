namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// One element of the inspection sequence. A check returns a verdict to stop the sequence, or
/// null to let the next check run. A check may record what it learned on the context for the
/// checks after it.
/// </summary>
public interface IFileContentCheck
{
    FileInspection? Run(FileInspectionContext context);
}
