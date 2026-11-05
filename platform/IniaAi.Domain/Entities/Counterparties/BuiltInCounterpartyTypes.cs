namespace IniaAi.Domain.Entities.Counterparties;

/// <summary>
/// The platform's built-in counterparty types and those the installed closed modules add. A build
/// without a module neither seeds nor protects its types; a row left in the database is an
/// ordinary type there.
/// </summary>
public sealed class BuiltInCounterpartyTypes
{
    private static readonly BuiltInCounterpartyType[] _platformTypes =
    [
        new(CounterpartyType.TYPE_REAL_MONEY_FUND_MANAGER, "Asset management firms that manage real money funds"),
        new(CounterpartyType.TYPE_REAL_MONEY_FUND, "Investment funds that invest in real money assets"),
        new(CounterpartyType.TYPE_CORPORATION, "A company or group of people authorized to act as a single entity"),
    ];

    public BuiltInCounterpartyTypes(IEnumerable<BuiltInCounterpartyType> moduleTypes)
    {
        All = [.. _platformTypes.Concat(moduleTypes).DistinctBy(type => type.Name)];
        Names = [.. All.Select(type => type.Name)];
    }

    public IReadOnlyList<BuiltInCounterpartyType> All { get; }

    public IReadOnlyList<string> Names { get; }

    public bool Contains(string name)
    {
        return Names.Contains(name);
    }
}
