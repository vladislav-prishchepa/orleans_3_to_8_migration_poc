using Orleans.Storage;

namespace Orleans8App.Workaround;

/// <summary>
/// Orleans v3-compatible hash picker implementation for Orleans v3 -> v7+ migration scenarios.
/// </summary>
public class Orleans3CompatibleStorageHashPicker : IStorageHasherPicker
{
    private readonly Orleans3CompatibleHasher _nonStringHasher;

    public Orleans3CompatibleStorageHashPicker()
    {
        _nonStringHasher = new();
        HashProviders = [_nonStringHasher];
    }

    public IHasher PickHasher<T>(
        string serviceId,
        string storageProviderInstanceName,
        string grainType,
        GrainId grainId,
        IGrainState<T> grainState,
        string? tag = null)
    {
        // string-only grain keys had special behaviour in Orleans v3
        if (grainId.TryGetIntegerKey(out _, out _) || grainId.TryGetGuidKey(out _, out _))
            return _nonStringHasher;

        // unable to cache hasher instances: content-aware behaviour, see hasher implementation for details
        return new Orleans3CompatibleStringKeyHasher(_nonStringHasher, grainType);
    }

    public ICollection<IHasher> HashProviders { get; }
}
