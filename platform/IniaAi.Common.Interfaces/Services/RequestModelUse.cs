namespace IniaAi.Common.Interfaces.Services;

public sealed class RequestModelUse : IRequestModelUse
{
    private volatile bool _modelCalled;

    public bool ModelCalled => _modelCalled;

    public void MarkModelCalled()
    {
        _modelCalled = true;
    }
}
