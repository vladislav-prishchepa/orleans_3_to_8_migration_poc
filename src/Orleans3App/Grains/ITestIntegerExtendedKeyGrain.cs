﻿using Orleans;
using Shared;

namespace Orleans3App.Grains;

public interface ITestIntegerExtendedKeyGrain : IGrainWithIntegerCompoundKey, ITestGrain;