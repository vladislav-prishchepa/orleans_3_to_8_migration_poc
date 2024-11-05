using Orleans.Storage;

namespace Orleans8App.Workaround;

public static class SiloBuilderExtensions
{
    public static void UseOrleans3CompatibleHashPickerWorkaround(this ISiloBuilder siloBuilder)
    {
        var storageDescriptor = siloBuilder.Services.LastOrDefault(static d
            => d.IsKeyedService && d.ServiceType == typeof(IGrainStorage));
    
        if (storageDescriptor is null)
            throw new InvalidOperationException("Unable to find IGrainStorage service descriptor.");
        if (storageDescriptor.KeyedImplementationFactory is null)
            throw new InvalidOperationException("Unexpected IGrainStorage service descriptor content.");

        siloBuilder.Services.Remove(storageDescriptor);
        siloBuilder.Services.Add(ServiceDescriptor.KeyedSingleton(
            storageDescriptor.ServiceType,
            storageDescriptor.ServiceKey,
            (provider, key) =>
            {
                var storage = storageDescriptor.KeyedImplementationFactory.Invoke(provider, key);
                if (storage is not AdoNetGrainStorage adoNetGrainStorage)
                    throw new InvalidOperationException("Unexpected IGrainStorage service implementation type.");

                adoNetGrainStorage.HashPicker = new Orleans3CompatibleStorageHashPicker();
                return adoNetGrainStorage;
            }));
    }
}
