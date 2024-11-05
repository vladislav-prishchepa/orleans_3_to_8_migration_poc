using Orleans;
using Shared;

namespace Orleans3App.Grains;

public interface ITestGuidExtendedKeyGrain : IGrainWithGuidCompoundKey, ITestGrain;
