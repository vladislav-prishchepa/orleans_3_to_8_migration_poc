namespace Orleans8App.Grains;

public class TestIntegerExtendedKeyGrain : TestGrainBase, ITestIntegerExtendedKeyGrain
{
    public TestIntegerExtendedKeyGrain(
        // set legacy GrainTypeString
        [PersistentState("Orleans3App.Grains.TestIntegerExtendedKeyGrain,Orleans3App.TestIntegerExtendedKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestIntegerExtendedKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
