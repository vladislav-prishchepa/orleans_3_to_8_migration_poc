namespace Orleans8App.Grains;

public class TestGuidKeyGrain : TestGrainBase, ITestGuidKeyGrain
{
    public TestGuidKeyGrain(
        // set legacy GrainTypeString
        [PersistentState("Orleans3App.Grains.TestGuidKeyGrain,Orleans3App.TestGuidKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestGuidKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
