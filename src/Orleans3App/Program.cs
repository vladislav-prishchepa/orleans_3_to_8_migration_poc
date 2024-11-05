using Newtonsoft.Json;
using Orleans;
using Orleans.Hosting;
using Orleans3App;
using Orleans3App.Grains;

var builder = WebApplication.CreateBuilder(args);

var dbKind = builder.Configuration["PersistenceSettings:DbKind"];

builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();

    siloBuilder.ConfigureApplicationParts(static parts => parts.AddFromApplicationBaseDirectory());

    siloBuilder.AddAdoNetGrainStorageAsDefault(optionsBuilder =>
    {
        optionsBuilder.BindConfiguration($"PersistenceSettings:{dbKind}");
        optionsBuilder.Configure(static options =>
        {
            options.UseJsonFormat = true;
            options.TypeNameHandling = TypeNameHandling.None;
            options.ConfigureJsonSerializerSettings = static settings =>
            {
                settings.PreserveReferencesHandling = PreserveReferencesHandling.None;
                settings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
            };
            options.UseFullAssemblyNames = false;
        });
    });
});

builder.Services.AddSingleton<Migrator>();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test");

await app.Services.GetRequiredService<Migrator>().MigrateAsync();
logger.LogInformation("Database prepare scripts executed");

await app.StartAsync();

await InitializeGrains();

await app.StopAsync();
return;

async Task InitializeGrains()
{
    var clusterClient = app.Services.GetRequiredService<IClusterClient>();

    var stringKeyGrain = clusterClient.GetGrain<ITestStringKeyGrain>("some");

    await stringKeyGrain.SetOrVerifyData("cec216cc-875a-4052-bae5-0ce2e4632f5c");

    var integerKeyGrain = clusterClient.GetGrain<ITestIntegerKeyGrain>(42);

    await integerKeyGrain.SetOrVerifyData("8adbb243-bc2b-43ed-aae1-e1b55620089a");

    var integerExtendedKeyGrain = clusterClient.GetGrain<ITestIntegerExtendedKeyGrain>(42, "some");

    await integerExtendedKeyGrain.SetOrVerifyData("fad53772-797c-4fcd-a93d-2635351fcc4b");

    var guidKeyGrain = clusterClient.GetGrain<ITestGuidKeyGrain>(Guid.Parse("888477cd-ed44-49a9-8567-e9b19cf51325"));

    await guidKeyGrain.SetOrVerifyData("aad6741f-c6fb-4da7-a84e-b52165f13bf2");

    var guidExtendedKeyGrain = clusterClient.GetGrain<ITestGuidExtendedKeyGrain>(Guid.Parse("888477cd-ed44-49a9-8567-e9b19cf51325"), "some");

    await guidExtendedKeyGrain.SetOrVerifyData("2bdc4bf1-faf2-4c5a-8f0a-14dab19c736a");

    logger.LogInformation("All grain tests completed");
}
