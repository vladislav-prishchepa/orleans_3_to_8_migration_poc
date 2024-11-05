using Orleans;
using Orleans.Runtime;
using Shared;

namespace Orleans3App.Grains;

public abstract class TestGrainBase : Grain, ITestGrain
{
    private readonly IPersistentState<TestGrainState> _persistentState;
    private readonly ILogger<TestGrainBase> _logger;

    protected TestGrainBase(IPersistentState<TestGrainState> persistentState, ILogger<TestGrainBase> logger)
    {
        _persistentState = persistentState;
        _logger = logger;
    }

    public async Task SetOrVerifyData(string data)
    {
        if (_persistentState.State.Data is null)
        {
            _persistentState.State.Data = data;
            await _persistentState.WriteStateAsync();
            _logger.LogInformation("Grain state initialized");
        }

        if (_persistentState.State.Data != data)
            throw new InvalidOperationException($"Unexpected data: expected '{data}', actual '{_persistentState.State.Data}'");

        _logger.LogInformation("Grain data check passed");
    }

    public Task<string?> GetData() => Task.FromResult(_persistentState.State.Data);
}
