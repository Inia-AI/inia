using IniaAi.Common.Reflection;

namespace IniaAi.Common.Tests.Reflection;

public class CloneHelperTests
{
    private class ScalarTarget
    {
        public string? Name { get; set; }
        public int Count { get; set; }
        public bool Flag { get; set; }
    }

    private class IgnoredTarget
    {
        public string? Copied { get; set; }

        [CloneIgnore]
        public string? Skipped { get; set; }
    }

    [Fact]
    public void CopyScalars_CopiesAllWritablePublicScalarProperties()
    {
        var source = new ScalarTarget { Name = "hello", Count = 42, Flag = true };
        var target = new ScalarTarget();

        CloneHelper.CopyScalars(source, target);

        Assert.Equal("hello", target.Name);
        Assert.Equal(42, target.Count);
        Assert.True(target.Flag);
    }

    [Fact]
    public void CopyScalars_SkipsCloneIgnoreProperties()
    {
        var source = new IgnoredTarget { Copied = "yes", Skipped = "source-value" };
        var target = new IgnoredTarget { Skipped = "target-value" };

        CloneHelper.CopyScalars(source, target);

        Assert.Equal("yes", target.Copied);
        Assert.Equal("target-value", target.Skipped);
    }

    private class ListHolder
    {
        public List<string> Tags { get; set; } = [];
    }

    private class DictHolder
    {
        public Dictionary<string, string> Headers { get; set; } = [];
    }

    [Fact]
    public void CopyScalars_DuplicatesListOfStringsInsteadOfSharingReference()
    {
        var source = new ListHolder { Tags = ["a", "b", "c"] };
        var target = new ListHolder();

        CloneHelper.CopyScalars(source, target);

        Assert.Equal(["a", "b", "c"], target.Tags);
        Assert.NotSame(source.Tags, target.Tags);

        target.Tags.Add("d");
        Assert.Equal(["a", "b", "c"], source.Tags);
    }

    private abstract class BaseWithField
    {
        public string? InheritedName { get; set; }
    }

    private class DerivedWithField : BaseWithField
    {
        public int DerivedCount { get; set; }
    }

    [Fact]
    public void CopyScalars_CopiesInheritedBaseClassProperties()
    {
        var source = new DerivedWithField { InheritedName = "base-value", DerivedCount = 7 };
        var target = new DerivedWithField();

        CloneHelper.CopyScalars(source, target);

        Assert.Equal("base-value", target.InheritedName);
        Assert.Equal(7, target.DerivedCount);
    }

    private class WithICollection
    {
        public ICollection<string> Tags { get; set; } = [];
    }

    [Fact]
    public void CopyScalars_ThrowsOnICollectionProperty()
    {
        var source = new WithICollection();
        var target = new WithICollection();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => CloneHelper.CopyScalars(source, target));

        Assert.Contains("Tags", ex.Message);
        Assert.Contains("[CloneIgnore]", ex.Message);
    }

    private class WithIList
    {
        public IList<int> Numbers { get; set; } = [];
    }

    [Fact]
    public void CopyScalars_ThrowsOnIListProperty()
    {
        _ = Assert.Throws<InvalidOperationException>(
            () => CloneHelper.CopyScalars(new WithIList(), new WithIList()));
    }

    private class WithHashSet
    {
        public HashSet<string> Bag { get; set; } = [];
    }

    [Fact]
    public void CopyScalars_ThrowsOnHashSetProperty()
    {
        _ = Assert.Throws<InvalidOperationException>(
            () => CloneHelper.CopyScalars(new WithHashSet(), new WithHashSet()));
    }

    private class WithArray
    {
        public byte[]? Payload { get; set; }
    }

    [Fact]
    public void CopyScalars_ThrowsOnArrayProperty()
    {
        _ = Assert.Throws<InvalidOperationException>(
            () => CloneHelper.CopyScalars(new WithArray(), new WithArray()));
    }

    private class WithCloneIgnoredArray
    {
        public string? Name { get; set; }

        [CloneIgnore]
        public byte[]? Payload { get; set; }
    }

    [Fact]
    public void CopyScalars_DoesNotThrowWhenCollectionShapedPropertyIsCloneIgnored()
    {
        var source = new WithCloneIgnoredArray { Name = "keep", Payload = [1, 2, 3] };
        var target = new WithCloneIgnoredArray();

        CloneHelper.CopyScalars(source, target);

        Assert.Equal("keep", target.Name);
        Assert.Null(target.Payload);
    }

    private class WithPrivateSetter
    {
        public string? PublicProp { get; set; }
        public string? PrivateSetProp { get; private set; }

        public void SetPrivate(string value)
        {
            PrivateSetProp = value;
        }
    }

    [Fact]
    public void CopyScalars_SkipsPropertiesWithPrivateSetter()
    {
        var source = new WithPrivateSetter { PublicProp = "public-value" };
        source.SetPrivate("source-private");
        var target = new WithPrivateSetter();
        target.SetPrivate("target-private");

        CloneHelper.CopyScalars(source, target);

        Assert.Equal("public-value", target.PublicProp);
        Assert.Equal("target-private", target.PrivateSetProp);
    }

    private class WithInternalSetter
    {
        public string? PublicProp { get; set; }
        public string? InternalSetProp { get; internal set; }
    }

    [Fact]
    public void CopyScalars_SkipsPropertiesWithInternalSetter()
    {
        var source = new WithInternalSetter
        {
            PublicProp = "public-value",
            InternalSetProp = "source-internal",
        };
        var target = new WithInternalSetter { InternalSetProp = "target-internal" };

        CloneHelper.CopyScalars(source, target);

        Assert.Equal("public-value", target.PublicProp);
        Assert.Equal("target-internal", target.InternalSetProp);
    }

    private class ExecutionStateStub
    {
        public string? Data { get; set; }
    }

    private class WithReference
    {
        public ExecutionStateStub? ExecutionState { get; set; }
    }

    [Fact]
    public void CopyScalars_ShallowCopiesOtherReferenceTypes()
    {
        var state = new ExecutionStateStub { Data = "shared" };
        var source = new WithReference { ExecutionState = state };
        var target = new WithReference();

        CloneHelper.CopyScalars(source, target);

        Assert.Same(state, target.ExecutionState);
    }

    [Fact]
    public void CopyScalars_DuplicatesDictionaryInsteadOfSharingReference()
    {
        var source = new DictHolder { Headers = new() { { "a", "1" }, { "b", "2" } } };
        var target = new DictHolder();

        CloneHelper.CopyScalars(source, target);

        Assert.Equal(2, target.Headers.Count);
        Assert.Equal("1", target.Headers["a"]);
        Assert.Equal("2", target.Headers["b"]);
        Assert.NotSame(source.Headers, target.Headers);

        target.Headers["c"] = "3";
        Assert.False(source.Headers.ContainsKey("c"));
    }
}
