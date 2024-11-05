using Shared;

namespace Orleans8App.Grains;

public interface ITestIntegerExtendedKeyGrain : IGrainWithIntegerCompoundKey, ITestGrain;
