using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.Documents;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Inspection.Checks;
using IniaAi.FileFormats.Unlock;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.Documents;

/// <summary>
/// The one code path every mouth runs its bytes through before it stores them. Inspects the
/// bytes under the deployment's limits, unlocks an encrypted container by what the inspector
/// named, inspects the decrypted copy again, and reports a refusal to the recorder itself, so a
/// refusal and its trail cannot come apart. A mouth that knows the size before it reads enters
/// with the declared size and a reader, so a file over the limit is refused and recorded from
/// the size alone and never read. A format the build does not read (<see cref="IReadableFormats"/>)
/// is refused at every mouth. A mouth whose sender vouches for the file keeps only that refusal and
/// the refusals that protect the platform; any other refusal admits the file as the type its bytes
/// were detected as, or as the type its name claims when the bytes name none, and stands when
/// neither names a type. Nothing here persists bytes: an admitted file is returned for the mouth
/// to store.
/// </summary>
public sealed class DocumentIntakeGate
{
    private readonly IIntakeRefusalRecorder _recorder;
    private readonly IDocumentUnlocker _unlocker;
    private readonly IReadableFormats _formats;
    private readonly FileInspectionLimits _limits;

    public DocumentIntakeGate(
        IIntakeRefusalRecorder recorder,
        IDocumentUnlocker unlocker,
        IReadableFormats formats,
        IOptions<PlatformSettings> platformSettings)
    {
        _recorder = recorder;
        _unlocker = unlocker;
        _formats = formats;
        _limits = FileInspectionLimits.FromSettings(platformSettings.Value.IniaAi);
    }

    /// <summary>
    /// Records a refusal the mouth made after the gate admitted the bytes, such as an organization's
    /// upload limit, on the same trail as the gate's own refusals.
    /// </summary>
    public Task RecordRefusalAsync(IntakeRefusal refusal, IntakeMouth mouth, CancellationToken cancellationToken)
    {
        return _recorder.RecordAsync(refusal, mouth, cancellationToken);
    }

    /// <summary>False when this build reads no file of this file's type, which <see cref="ScreenAsync(byte[], string, string?, IntakeMouth, CancellationToken)"/> refuses.</summary>
    public bool Reads(byte[] bytes, string fileName)
    {
        return NotRead(FileContentInspector.Default.Inspect(bytes, fileName, _limits).Detected) is null;
    }

    public Task<IntakeScreening> ScreenAsync(
        byte[] bytes,
        string fileName,
        string? password,
        IntakeMouth mouth,
        CancellationToken cancellationToken)
    {
        return ScreenAsync(bytes, fileName, password, mouth, FileContentInspector.Default, cancellationToken);
    }

    public async Task<IntakeScreening> ScreenAsync(
        byte[] bytes,
        string fileName,
        string? password,
        IntakeMouth mouth,
        FileContentInspector inspector,
        CancellationToken cancellationToken)
    {
        FileInspection first = inspector.Inspect(bytes, fileName, _limits);
        if (first.Outcome == FileInspectionOutcome.Refused)
        {
            return await RefuseAsync(bytes, fileName, first, mouth, unlock: null, cancellationToken);
        }

        if (first.Outcome == FileInspectionOutcome.Accepted)
        {
            return await AdmitAsync(bytes, fileName, first.Detected!, mouth, unlock: null, cancellationToken);
        }

        if (NotRead(first.Detected) is { } notRead)
        {
            return await RecordRefusalAsync(bytes, fileName, notRead, mouth, cancellationToken);
        }

        FileUnlockResult unlock = await _unlocker.TryUnlockAsync(bytes, first.Container!.Value, password, cancellationToken);
        switch (unlock.Status)
        {
            case FileProtectionStatus.NotEncrypted:
                return AdmissionCheck.Refusal(first.Detected!) is { } refusal
                    ? await RefuseAsync(bytes, fileName, refusal, mouth, unlock, cancellationToken)
                    : await AdmitAsync(bytes, fileName, first.Detected!, mouth, unlock, cancellationToken);
            case FileProtectionStatus.PasswordRequired:
                return Locked(IntakeOutcome.PasswordRequired, bytes, first, unlock);
            case FileProtectionStatus.PasswordIncorrect:
                return Locked(IntakeOutcome.PasswordIncorrect, bytes, first, unlock);
            case FileProtectionStatus.UnsupportedProtection:
                return Locked(IntakeOutcome.UnsupportedProtection, bytes, first, unlock);
            case FileProtectionStatus.Decrypted:
            default:
                break;
        }

        byte[] decrypted = unlock.DecryptedBytes!;
        FileInspection second = inspector.Inspect(decrypted, fileName, _limits);
        return second.Outcome switch
        {
            FileInspectionOutcome.Accepted => await AdmitAsync(decrypted, fileName, second.Detected!, mouth, unlock, cancellationToken),
            FileInspectionOutcome.Refused => await RefuseAsync(decrypted, fileName, second, mouth, unlock, cancellationToken),
            _ => Locked(IntakeOutcome.UnsupportedProtection, bytes, second, unlock),
        };
    }

    public Task<IntakeScreening> ScreenAsync(
        IFormFile file,
        string? password,
        IntakeMouth mouth,
        FileContentInspector inspector,
        CancellationToken cancellationToken)
    {
        return ScreenAsync(
            file.FileName,
            file.Length,
            ct => FormFileBytes.ReadAsync(file, ct),
            password,
            mouth,
            inspector,
            cancellationToken);
    }

    public async Task<IntakeScreening> ScreenAsync(
        string fileName,
        long declaredSize,
        Func<CancellationToken, Task<byte[]>> readBytes,
        string? password,
        IntakeMouth mouth,
        FileContentInspector inspector,
        CancellationToken cancellationToken)
    {
        if (declaredSize > _limits.MaxFileBytes)
        {
            return await RefuseUnreadAsync(fileName, declaredSize, mouth, cancellationToken);
        }

        return await ScreenAsync(await readBytes(cancellationToken), fileName, password, mouth, inspector, cancellationToken);
    }

    private async Task<IntakeScreening> RefuseUnreadAsync(
        string fileName,
        long sizeBytes,
        IntakeMouth mouth,
        CancellationToken cancellationToken)
    {
        await _recorder.RecordAsync(
            IntakeRefusal.Unread(fileName, sizeBytes, IntakeRefusalReasons.FILE_TOO_LARGE),
            mouth,
            cancellationToken);

        return new IntakeScreening(
            IntakeOutcome.Refused,
            [],
            null,
            null,
            IntakeRefusalReasons.FILE_TOO_LARGE,
            SizeLimitCheck.TooLargeDetail(sizeBytes, _limits.MaxFileBytes));
    }

    private static DetectedContent? VouchedType(FileInspection verdict, string fileName)
    {
        if (verdict.Detected is { PlatformMediaType: not null } detected)
        {
            return detected;
        }

        MediaType claimed = Utils.DetermineMediaType(fileName);
        return claimed == MediaType.Unknown ? null : DetectedContent.FromPlatformMediaType(claimed);
    }

    /// <summary>Admits the file, unless this build reads no file of its type.</summary>
    private async Task<IntakeScreening> AdmitAsync(
        byte[] bytes,
        string fileName,
        DetectedContent detected,
        IntakeMouth mouth,
        FileUnlockResult? unlock,
        CancellationToken cancellationToken)
    {
        return NotRead(detected) is { } notRead
            ? await RecordRefusalAsync(bytes, fileName, notRead, mouth, cancellationToken)
            : Admitted(bytes, detected, unlock);
    }

    /// <summary>The refusal of a type this build reads no file of, which stands whoever vouches for the file.</summary>
    private FileInspection? NotRead(DetectedContent? detected)
    {
        return detected?.PlatformMediaType is MediaType mediaType && _formats.Refusal(mediaType) is { } detail
            ? FileInspection.Refuse(IntakeRefusalReasons.FORMAT_NOT_READ, detail, detected)
            : null;
    }

    private static IntakeScreening Admitted(byte[] bytes, DetectedContent detected, FileUnlockResult? unlock)
    {
        return new IntakeScreening(IntakeOutcome.Admitted, bytes, detected, unlock, null, null);
    }

    private static IntakeScreening Locked(IntakeOutcome outcome, byte[] bytes, FileInspection verdict, FileUnlockResult unlock)
    {
        return new IntakeScreening(outcome, bytes, verdict.Detected, unlock, null, verdict.Detail);
    }

    private async Task<IntakeScreening> RefuseAsync(
        byte[] screenedBytes,
        string fileName,
        FileInspection verdict,
        IntakeMouth mouth,
        FileUnlockResult? unlock,
        CancellationToken cancellationToken)
    {
        if (mouth.ContentIsVouchedFor
            && !IntakeRefusalReasons.ProtectsThePlatform(verdict.ReasonCode!)
            && VouchedType(verdict, fileName) is { } admittedAs)
        {
            return await AdmitAsync(screenedBytes, fileName, admittedAs, mouth, unlock, cancellationToken);
        }

        return await RecordRefusalAsync(screenedBytes, fileName, verdict, mouth, cancellationToken);
    }

    private async Task<IntakeScreening> RecordRefusalAsync(
        byte[] screenedBytes,
        string fileName,
        FileInspection verdict,
        IntakeMouth mouth,
        CancellationToken cancellationToken)
    {
        await _recorder.RecordAsync(
            IntakeRefusal.OfBytes(fileName, screenedBytes, verdict.ReasonCode!, verdict.DetectedMediaType),
            mouth,
            cancellationToken);

        return new IntakeScreening(IntakeOutcome.Refused, screenedBytes, verdict.Detected, null, verdict.ReasonCode, verdict.Detail);
    }
}
