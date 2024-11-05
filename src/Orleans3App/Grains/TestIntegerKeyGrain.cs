using Orleans.Runtime;

namespace Orleans3App.Grains;

public class TestIntegerKeyGrain : TestGrainBase, ITestIntegerKeyGrain
{
    public TestIntegerKeyGrain(
        [PersistentState("TestIntegerKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestIntegerKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
