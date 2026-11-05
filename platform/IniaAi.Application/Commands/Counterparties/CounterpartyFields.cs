using System.Linq.Expressions;
using System.Reflection;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Commands.Counterparties;

/// <summary>
/// Copies, compares and exports the text fields <see cref="ICounterpartyFields"/> declares,
/// whatever their number, so the interface stays the only list of them: a field added there lands
/// on create, update and import, reads back through the read model, and leaves on export. The
/// property bag travels by its own rules.
/// </summary>
public static class CounterpartyFields
{
    private static readonly (PropertyInfo Field, PropertyInfo Stored)[] _textFields = [.. typeof(ICounterpartyFields)
        .GetProperties()
        .Where(property => property.Name != nameof(ICounterpartyFields.Properties))
        .Select(field => field.PropertyType == typeof(string)
            ? (field, typeof(Counterparty).GetProperty(field.Name)
                ?? throw new InvalidOperationException($"{nameof(Counterparty)} has no {field.Name}, which {nameof(ICounterpartyFields)} names."))
            : throw new InvalidOperationException($"{nameof(ICounterpartyFields)}.{field.Name} is not text, so {nameof(CounterpartyFields)} cannot carry it."))];

    public static readonly Expression<Func<Counterparty, CounterpartyBundleEntryDto>> ToBundleEntry = BundleEntryProjection();

    public static void CopyText(ICounterpartyFields source, Counterparty target)
    {
        foreach ((PropertyInfo field, PropertyInfo stored) in _textFields)
        {
            stored.SetValue(target, field.GetValue(source));
        }
    }

    public static void ReadText<TFields>(Counterparty source, TFields target)
        where TFields : class, ICounterpartyFields
    {
        foreach ((PropertyInfo stored, PropertyInfo written) in TextFieldsOf<TFields>.Pairs)
        {
            written.SetValue(target, stored.GetValue(source));
        }
    }

    public static bool SameText(Counterparty stored, ICounterpartyFields fields)
    {
        return _textFields.All(pair => string.Equals((string?)pair.Stored.GetValue(stored), (string?)pair.Field.GetValue(fields), StringComparison.Ordinal));
    }

    private static Expression<Func<Counterparty, CounterpartyBundleEntryDto>> BundleEntryProjection()
    {
        ParameterExpression counterparty = Expression.Parameter(typeof(Counterparty), nameof(counterparty));
        IEnumerable<(PropertyInfo Stored, PropertyInfo Written)> members = TextFieldsOf<CounterpartyBundleEntryDto>.Pairs.Prepend(
            (typeof(Counterparty).GetProperty(nameof(Counterparty.Id))!, typeof(CounterpartyBundleEntryDto).GetProperty(nameof(CounterpartyBundleEntryDto.Id))!));

        return Expression.Lambda<Func<Counterparty, CounterpartyBundleEntryDto>>(
            Expression.MemberInit(
                Expression.New(typeof(CounterpartyBundleEntryDto)),
                members.Select(member => Expression.Bind(member.Written, Expression.Property(counterparty, member.Stored)))),
            counterparty);
    }

    private static class TextFieldsOf<TFields>
        where TFields : class, ICounterpartyFields
    {
        public static readonly (PropertyInfo Stored, PropertyInfo Written)[] Pairs = [.. _textFields
            .Select(pair => (pair.Stored, typeof(TFields).GetProperty(pair.Field.Name)
                ?? throw new InvalidOperationException($"{typeof(TFields).Name} has no {pair.Field.Name}, which {nameof(ICounterpartyFields)} names.")))];
    }
}
