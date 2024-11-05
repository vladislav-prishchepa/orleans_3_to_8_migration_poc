namespace Orleans8App.Grains;

public class TestStringKeyGrain : TestGrainBase, ITestStringKeyGrain
{
    public TestStringKeyGrain(
        // set legacy GrainTypeString
        [PersistentState("Orleans3App.Grains.TestStringKeyGrain,Orleans3App.TestStringKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestStringKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
