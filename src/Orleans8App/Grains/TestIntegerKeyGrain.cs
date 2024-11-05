namespace Orleans8App.Grains;

public class TestIntegerKeyGrain : TestGrainBase, ITestIntegerKeyGrain
{
    public TestIntegerKeyGrain(
        // set legacy GrainTypeString
        [PersistentState("Orleans3App.Grains.TestIntegerKeyGrain,Orleans3App.TestIntegerKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestIntegerKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
