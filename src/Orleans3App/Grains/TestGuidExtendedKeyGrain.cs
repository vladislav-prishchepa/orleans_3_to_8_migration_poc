using Orleans.Runtime;

namespace Orleans3App.Grains;

public class TestGuidExtendedKeyGrain : TestGrainBase, ITestGuidExtendedKeyGrain
{
    public TestGuidExtendedKeyGrain(
        [PersistentState("TestGuidExtendedKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestGuidExtendedKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
