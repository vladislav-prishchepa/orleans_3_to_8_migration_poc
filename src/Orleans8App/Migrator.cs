using System.Reflection;
using System.Text;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans8App;

public class Migrator
{
    private readonly IOptionsMonitor<AdoNetGrainStorageOptions> _options;
    private readonly ILogger<Migrator> _logger;

    public Migrator(
        IOptionsMonitor<AdoNetGrainStorageOptions> options,
        ILogger<Migrator> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task MigrateAsync()
    {
        var options = _options.Get(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
        switch (options.Invariant)
        {
            case "MySql.Data.MySqlConnector":
                return MigrateMySqlAsync(options.ConnectionString);
            case "Npgsql":
                return MigratePostgresAsync(options.ConnectionString);
            default:
                throw new NotSupportedException("Unsupported ADO.NET invariant");
        }
    }

    private async Task MigrateMySqlAsync(string connectionString)
    {
        var mysqlSettings = new MySqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrEmpty(mysqlSettings.Database))
            throw new Exception("");

        var database = mysqlSettings.Database;

        mysqlSettings.Database = null;
        var connectionStringWithoutDatabase = mysqlSettings.ConnectionString;

        var beforeRunScript = GetMigrationScript("mysql");

        await using (var connection = new MySqlConnection(connectionStringWithoutDatabase))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                                   SELECT SCHEMA_NAME
                                   FROM INFORMATION_SCHEMA.SCHEMATA
                                   WHERE SCHEMA_NAME = '{MySqlHelper.EscapeString(database)}'
                                   """;

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Database is not initialized yet");
        }

        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = beforeRunScript;
            await command.ExecuteNonQueryAsync();
            _logger.LogInformation("Before run database scripts executed");
        }
    }

    private async Task MigratePostgresAsync(string connectionString)
    {
        var npgsqlSettings = new NpgsqlConnectionStringBuilder(connectionString);

        if (string.IsNullOrEmpty(npgsqlSettings.Database))
            throw new InvalidOperationException("Database name must be specified");

        var database = npgsqlSettings.Database;

        // setting default database to create new one
        npgsqlSettings.Database = "postgres";

        var connectionStringWithDefaultDatabase = npgsqlSettings.ConnectionString;

        var beforeRunScript = GetMigrationScript("postgres");

        await using (var connection = new NpgsqlConnection(connectionStringWithDefaultDatabase))
        {
            await connection.OpenAsync();

            var dbExists = false;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"""
                                       SELECT 1 FROM pg_database
                                       WHERE datname = '{database}'
                                       """;
                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    dbExists = true;
                }
            }

            if (!dbExists)
                throw new InvalidOperationException("Database is not initialized yet");
        }

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();

            var schemaInitialized = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM pg_catalog.pg_tables WHERE schemaname='public'";
                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    schemaInitialized = true;
                }
            }

            if (!schemaInitialized)
                throw new InvalidOperationException("Database is not initialized yet");

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = beforeRunScript;
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Before run database scripts executed");
            }
        }
    }

    private static string GetMigrationScript(string databaseKind)
    {
        var entryAssembly = Assembly.GetEntryAssembly()!;
        var entryAssemblyDirectory = Path.GetDirectoryName(entryAssembly.Location)!;
        var scriptsDirectory = Path.Combine(entryAssemblyDirectory, "migrations", databaseKind);

        if (!Directory.Exists(scriptsDirectory))
            throw new InvalidOperationException();

        var beforeRunScriptPath = Path.Combine(scriptsDirectory, "before_run.sql");

        if (!File.Exists(beforeRunScriptPath))
            throw new InvalidOperationException();

        return File.ReadAllText(beforeRunScriptPath, Encoding.UTF8);
    }
}
