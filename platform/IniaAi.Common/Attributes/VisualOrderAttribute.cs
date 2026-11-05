namespace IniaAi.Common.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public class VisualOrderAttribute : Attribute
{
    public int Order { get; }

    public VisualOrderAttribute(int order)
    {
        Order = order;
    }
}
