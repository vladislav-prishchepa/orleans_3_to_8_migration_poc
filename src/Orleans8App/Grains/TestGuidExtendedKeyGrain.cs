namespace Orleans8App.Grains;

public class TestGuidExtendedKeyGrain : TestGrainBase, ITestGuidExtendedKeyGrain
{
    public TestGuidExtendedKeyGrain(
        // set legacy GrainTypeString
        [PersistentState("Orleans3App.Grains.TestGuidExtendedKeyGrain,Orleans3App.TestGuidExtendedKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestGuidExtendedKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
