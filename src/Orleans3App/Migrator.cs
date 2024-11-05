using System.Reflection;
using System.Text;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans3App;

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
            throw new InvalidOperationException("Database name must be specified");

        var database = mysqlSettings.Database;

        mysqlSettings.Database = null;
        var connectionStringWithoutDatabase = mysqlSettings.ConnectionString;

        var (initScript, beforeRunScript) = GetMigrationScripts("mysql");

        await using (var connection = new MySqlConnection(connectionStringWithoutDatabase))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE IF NOT EXISTS '{database}';";
            await command.ExecuteNonQueryAsync();
        }

        await using (var connection = new MySqlConnection(connectionString))
        {
            await connection.OpenAsync();

            var schemaInitialized = false;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SHOW TABLES";
                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                    schemaInitialized = true;
            }

            if (!schemaInitialized)
            {
                if (initScript is null)
                    throw new InvalidOperationException("Database is not initialized yet");

                await using var command = connection.CreateCommand();
                command.CommandText = initScript;
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Database initialized");
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = beforeRunScript;
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Before run database scripts executed");
            }
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

        var (initScript, beforeRunScript) = GetMigrationScripts("postgres");

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
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE {database}";
                await command.ExecuteNonQueryAsync();
            }
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
            {
                await using var command = connection.CreateCommand();
                command.CommandText = initScript;
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Database initialized");
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = beforeRunScript;
                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Before run database scripts executed");
            }
        }
    }

    private static (string InitScript, string BeforeRunScript) GetMigrationScripts(string databaseKind)
    {
        var entryAssembly = Assembly.GetEntryAssembly()!;
        var entryAssemblyDirectory = Path.GetDirectoryName(entryAssembly.Location)!;
        var scriptsDirectory = Path.Combine(entryAssemblyDirectory, "migrations", databaseKind);

        if (!Directory.Exists(scriptsDirectory))
            throw new InvalidOperationException();

        var initScriptPath = Path.Combine(scriptsDirectory, "init_database.sql");

        if (!File.Exists(initScriptPath))
            throw new InvalidOperationException();

        var initScript = File.ReadAllText(initScriptPath, Encoding.UTF8);

        var beforeRunScriptPath = Path.Combine(scriptsDirectory, "before_run.sql");

        if (!File.Exists(beforeRunScriptPath))
            throw new InvalidOperationException();

        var beforeRunScript = File.ReadAllText(beforeRunScriptPath, Encoding.UTF8);

        return (initScript, beforeRunScript);
    }
}
