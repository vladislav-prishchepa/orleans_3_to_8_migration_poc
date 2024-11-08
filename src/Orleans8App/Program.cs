using Newtonsoft.Json;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans8App;
using Orleans8App.Grains;
using Orleans8App.Workaround;
using Shared;

var builder = WebApplication.CreateBuilder(args);

var dbKind = builder.Configuration["PersistenceSettings:DbKind"];

builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();

    // add this options if you test with localhost clustering & default settings: defaults changed in Orleans 7+
    siloBuilder.Configure<ClusterOptions>(static options => options.ServiceId = "dev");

    siloBuilder.AddAdoNetGrainStorageAsDefault(optionsBuilder
        => optionsBuilder.BindConfiguration($"PersistenceSettings:{dbKind}"));

    // configuring Orleans v3-compatible HashPicker to keep using existing grain state (orleans v3-initialized)
    siloBuilder.UseOrleans3CompatibleHashPickerWorkaround();

    siloBuilder.Configure<OrleansJsonSerializerOptions>(static options =>
    {
        // TypeNameHandling MUST be set to TypeNameHandling.None if type full names changed during migration
        options.JsonSerializerSettings.TypeNameHandling = TypeNameHandling.None;
        options.JsonSerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
        options.JsonSerializerSettings.PreserveReferencesHandling = PreserveReferencesHandling.None;
    });
});

builder.Services.AddSingleton<Migrator>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test");

await app.Services.GetRequiredService<Migrator>().MigrateAsync();
logger.LogInformation("Database prepare scripts executed");

await app.StartAsync();

await TestGrainsAsync();

await app.StopAsync();
return;

async Task TestGrainsAsync()
{
    try
    {
        var clusterClient = app.Services.GetRequiredService<IClusterClient>();

        await CheckDataAsync(
            clusterClient.GetGrain<ITestStringKeyGrain>("some"),
            "cec216cc-875a-4052-bae5-0ce2e4632f5c");

        await CheckDataAsync(
            clusterClient.GetGrain<ITestIntegerKeyGrain>(42),
            "8adbb243-bc2b-43ed-aae1-e1b55620089a");

        await CheckDataAsync(
            clusterClient.GetGrain<ITestIntegerExtendedKeyGrain>(42, "some"),
            "fad53772-797c-4fcd-a93d-2635351fcc4b");

        await CheckDataAsync(
            clusterClient.GetGrain<ITestGuidKeyGrain>(Guid.Parse("888477cd-ed44-49a9-8567-e9b19cf51325")),
            "aad6741f-c6fb-4da7-a84e-b52165f13bf2");

        await CheckDataAsync(
            clusterClient.GetGrain<ITestGuidExtendedKeyGrain>(Guid.Parse("888477cd-ed44-49a9-8567-e9b19cf51325"), "some"),
            "2bdc4bf1-faf2-4c5a-8f0a-14dab19c736a");

        logger.LogInformation("v3-created grains tested");

        var stringKeyGrain = clusterClient.GetGrain<ITestStringKeyGrain>("new_some");

        await stringKeyGrain.SetOrVerifyData("1858c362-9bd7-4113-a992-b0a63a14b153");

        var integerKeyGrain = clusterClient.GetGrain<ITestIntegerKeyGrain>(42_000);

        await integerKeyGrain.SetOrVerifyData("4dd4b73b-4225-4bf6-889f-2e68ea28dce4");

        var integerExtendedKeyGrain = clusterClient.GetGrain<ITestIntegerExtendedKeyGrain>(42_000, "new_some");

        await integerExtendedKeyGrain.SetOrVerifyData("1aaa3784-48f2-4b0b-91b8-234c2213d843");

        var guidKeyGrain = clusterClient.GetGrain<ITestGuidKeyGrain>(Guid.Parse("45672b26-3d27-4825-8c30-1b818c8b63e2"));

        await guidKeyGrain.SetOrVerifyData("af27e5c3-9a10-423a-abd4-4f30de81a8d6");

        var guidExtendedKeyGrain = clusterClient.GetGrain<ITestGuidExtendedKeyGrain>(Guid.Parse("45672b26-3d27-4825-8c30-1b818c8b63e2"), "new_some");

        await guidExtendedKeyGrain.SetOrVerifyData("a97449a3-b44c-4873-8f94-c2e9c584e701");

        logger.LogInformation("v8-created grains tested");

        return;

        async Task CheckDataAsync<TGrain>(TGrain grain, string expectedData) where TGrain : ITestGrain, IAddressable
        {
            var actualData = await grain.GetData();
            if (expectedData != actualData)
                throw new InvalidOperationException($"{typeof(TGrain)} persistence test failed, expected '{expectedData}', actual '{actualData ?? "<NULL>"}'");

            logger.LogInformation("Grain {grainType} check OK", typeof(TGrain));
        }
    }
    catch (Exception exception)
    {
        logger.LogCritical(exception, "Grain persistent state test failed");
    }
}
