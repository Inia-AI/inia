namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// Direction of a Json2Xlsx block. <see cref="Inject"/> (default) writes JSON values into a
/// template and produces an .xlsx document. <see cref="Extract"/> does the inverse: it reads an
/// uploaded .xlsx back into JSON using the same MappingJson cell positions.
/// </summary>
public enum Json2XlsxDirection
{
    Inject = 0,
    Extract = 1,
}
