using System.Globalization;
using ErrorOr;
using IniaAi.Application.Common.RateLimits;
using IniaAi.Application.Documents;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Application.Common.UploadLimits;

/// <summary>Why an upload was not stored: the organization's storage is full, or it uploaded too many files in the last hour.</summary>
public abstract record UploadRefusal
{
    public const string RESETS_AT_METADATA_KEY = "resetsAt";

    private UploadRefusal()
    {
    }

    /// <summary>The reason the refusal trail records, one of <see cref="IntakeRefusalReasons"/>.</summary>
    public abstract string ReasonCode { get; }

    /// <summary>The error the upload answers with: <c>File.Refused.</c> and the reason, in words the uploader reads.</summary>
    public abstract Error ToError();

    /// <summary>The organization's stored files and this one would be more than its storage quota.</summary>
    public sealed record StorageFull : UploadRefusal
    {
        public const string MESSAGE = "Your account has reached its storage limit. Delete documents you no longer need, or contact support.";

        public override string ReasonCode => IntakeRefusalReasons.STORAGE_FULL;

        public override Error ToError()
        {
            return Error.Forbidden(IntakeRefusalErrors.CODE_PREFIX + ReasonCode, MESSAGE);
        }
    }

    /// <summary>The organization uploaded <paramref name="FilesPerHour"/> files in the last hour; one more fits from <paramref name="ResetsAt"/>.</summary>
    /// <param name="RetryAfterSeconds">Seconds from the check to <paramref name="ResetsAt"/>.</param>
    public sealed record TooManyUploads(int FilesPerHour, DateTime ResetsAt, long RetryAfterSeconds) : UploadRefusal
    {
        public const string POLICY_NAME = "upload";
        public const int WINDOW_SECONDS = 3600;

        public override string ReasonCode => IntakeRefusalReasons.TOO_MANY_UPLOADS;

        public string Message => string.Create(
            CultureInfo.InvariantCulture,
            $"Your organization has uploaded the most files allowed in an hour. You can upload more from {ResetsAt:HH:mm} UTC.");

        public override Error ToError()
        {
            return Error.Custom(
                RateLimitRefusal.ERROR_TYPE,
                IntakeRefusalErrors.CODE_PREFIX + ReasonCode,
                Message,
                new Dictionary<string, object>
                {
                    [RESETS_AT_METADATA_KEY] = ResetsAt,
                    [RateLimitRefusal.METADATA_KEY] = new RateLimitRefusal(POLICY_NAME, FilesPerHour, WINDOW_SECONDS, RetryAfterSeconds),
                });
        }
    }
}
