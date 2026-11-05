using IniaAi.FileFormats.Unlock;

namespace IniaAi.FileFormats.Tests.Unlock;

public class NonInteractiveUnlockPolicyTests
{
    [Theory]
    [InlineData(FileProtectionStatus.NotEncrypted)]
    [InlineData(FileProtectionStatus.Decrypted)]
    public void GetSkipReason_ProceedableStatuses_ReturnsNull(FileProtectionStatus status)
    {
        Assert.Null(NonInteractiveUnlockPolicy.GetSkipReason(status));
    }

    [Theory]
    [InlineData(FileProtectionStatus.PasswordRequired)]
    [InlineData(FileProtectionStatus.PasswordIncorrect)]
    public void GetSkipReason_PasswordNeededStatuses_ReturnsPasswordRequiredReason(FileProtectionStatus status)
    {
        Assert.Equal(
            NonInteractiveUnlockPolicy.MANUAL_UNLOCK_REQUIRED_REASON,
            NonInteractiveUnlockPolicy.GetSkipReason(status));
    }

    [Fact]
    public void GetSkipReason_UnsupportedProtection_ReturnsUnsupportedProtectionReason()
    {
        Assert.Equal(
            NonInteractiveUnlockPolicy.UNSUPPORTED_PROTECTION_REASON,
            NonInteractiveUnlockPolicy.GetSkipReason(FileProtectionStatus.UnsupportedProtection));
    }
}
