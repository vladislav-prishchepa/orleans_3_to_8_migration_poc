using Shared;

namespace Orleans8App.Grains;

public interface ITestGuidExtendedKeyGrain : IGrainWithGuidCompoundKey, ITestGrain;
