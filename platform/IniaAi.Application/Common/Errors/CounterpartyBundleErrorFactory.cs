using ErrorOr;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Errors;

/// <summary>
/// The errors of the counterparty-bundle transport (export and import), named once.
/// </summary>
public static class CounterpartyBundleErrorFactory
{
    public const string VERSION_UNSUPPORTED_CODE = "CounterpartyBundle.VersionUnsupported";
    public const string CYCLE_CODE = "CounterpartyBundle.Cycle";
    public const string CONCURRENT_CHANGE_CODE = "CounterpartyBundle.ConcurrentChange";

    public static Error VersionUnsupported(int version, int supported)
    {
        return Error.Validation(
            VERSION_UNSUPPORTED_CODE,
            $"The bundle is format version {version}; this deployment imports version {supported}.");
    }

    public static Error Cycle(IEnumerable<string> ids)
    {
        return Error.Validation(
            CYCLE_CODE,
            $"The bundle's parent links form a cycle through {CounterpartyTerms.PLURAL_LOWER} "
                + string.Join(", ", ids.Select(id => $"'{id}'"))
                + ". Fix the bundle and import again.");
    }

    public static Error ConcurrentChange()
    {
        return Error.Conflict(
            CONCURRENT_CHANGE_CODE,
            $"Another change to this organization's {CounterpartyTerms.PLURAL_LOWER} landed while the bundle was importing; nothing was written. Import again.");
    }
}
