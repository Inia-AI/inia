using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class StringManipulationBlockSerializationTests
{
    [Fact]
    public void NewtonsoftRoundTrip_PreservesStringManipulationProperties()
    {
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.StringManipulation, workflow)
        {
            Name = "String Manipulation",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "hello",
                Operation = StringManipulationOperation.Replace,
                Find = "hello",
                ReplaceWith = "world",
            },
        };

        string json = JsonConvert.SerializeObject(block);
        Block? deserialized = JsonConvert.DeserializeObject<Block>(json);

        Assert.NotNull(deserialized);
        StringManipulationProperties typed = Assert.IsType<StringManipulationProperties>(deserialized.Properties);
        Assert.Equal(StringManipulationOperation.Replace, typed.Operation);
        Assert.Equal("hello", typed.InputTemplate);
        Assert.Equal("hello", typed.Find);
        Assert.Equal("world", typed.ReplaceWith);
    }
}
