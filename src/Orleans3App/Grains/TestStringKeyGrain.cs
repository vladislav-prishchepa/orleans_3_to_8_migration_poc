using Orleans.Runtime;

namespace Orleans3App.Grains;

public class TestStringKeyGrain : TestGrainBase, ITestStringKeyGrain
{
    public TestStringKeyGrain(
        [PersistentState("TestStringKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestStringKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
