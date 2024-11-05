using Orleans.Runtime;

namespace Orleans3App.Grains;

public class TestGuidKeyGrain : TestGrainBase, ITestGuidKeyGrain
{
    public TestGuidKeyGrain(
        [PersistentState("TestGuidKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestGuidKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
