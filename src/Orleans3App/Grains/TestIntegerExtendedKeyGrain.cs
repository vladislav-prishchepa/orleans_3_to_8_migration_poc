using Orleans.Runtime;

namespace Orleans3App.Grains;

public class TestIntegerExtendedKeyGrain : TestGrainBase, ITestIntegerExtendedKeyGrain
{
    public TestIntegerExtendedKeyGrain(
        [PersistentState("TestIntegerExtendedKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestIntegerExtendedKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
