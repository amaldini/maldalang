namespace MaldaLang.Tests;

using System.IO;
using System.Text.Json;
using MaldaLang.Scaffolding;
using Microsoft.Data.Sqlite;

[Collection("DbCommandRunnerSerial")]
public class DbCommandRunnerTests : TestBase
{
    [Fact]
    public void Run_Status_ForScaffoldedWebApiReportsPendingWithoutCreatingDatabase()
    {
        var root = CreateTempDirectory("malda_db_status_");
        var project = Path.Combine(root, "sample-api");
        try
        {
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = project,
                LocalFirst = true
            };

            Assert.Equal(0, scaffolder.Scaffold("webapi", project, new StringWriter(), new StringWriter(), options));

            var databasePath = Path.Combine(project, "data", "sample-api.db");
            Assert.False(File.Exists(databasePath));

            var output = new StringWriter();
            var error = new StringWriter();
            var runner = new DbCommandRunner();

            var code = runner.Run(new[] { "status" }, output, error, project);

            var text = output.ToString();
            Assert.Equal(0, code);
            Assert.Contains("Pending: 1", text);
            Assert.Contains("Seed support: yes", text);
            Assert.Contains("Rollback latest registered: yes", text);
            Assert.Contains("Latest applied: none", text);
            Assert.Contains("database file has not been created yet", text);
            Assert.Contains("001_baseline", text);
            Assert.False(File.Exists(databasePath));
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_Migrate_ForScaffoldedFullstackAppliesBaselineAndStatusJsonReflectsBackendConvention()
    {
        var root = CreateTempDirectory("malda_db_migrate_");
        var project = Path.Combine(root, "sales-portal");
        try
        {
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "fullstack",
                DestinationPath = project,
                LocalFirst = true
            };

            Assert.Equal(0, scaffolder.Scaffold("fullstack", project, new StringWriter(), new StringWriter(), options));

            var runner = new DbCommandRunner();
            var migrateOutput = new StringWriter();
            var migrateError = new StringWriter();

            var migrateCode = runner.Run(new[] { "migrate" }, migrateOutput, migrateError, project);

            Assert.True(migrateCode == 0, $"stdout: {migrateOutput} stderr: {migrateError}");
            Assert.Contains("Applied 1 local migration", migrateOutput.ToString());
            Assert.Contains("001_baseline", migrateOutput.ToString());
            Assert.Equal(string.Empty, migrateError.ToString());

            var statusOutput = new StringWriter();
            var statusError = new StringWriter();
            var statusCode = runner.Run(new[] { "status", "--json" }, statusOutput, statusError, project);

            Assert.Equal(0, statusCode);
            Assert.Equal(string.Empty, statusError.ToString());

            using var json = JsonDocument.Parse(statusOutput.ToString());
            var rootElement = json.RootElement;
            Assert.Equal("backend/data/local_first.malda", rootElement.GetProperty("MigrationModule").GetString());
            Assert.True(rootElement.GetProperty("DatabaseExists").GetBoolean());
            Assert.Equal(1, rootElement.GetProperty("AppliedMigrationCount").GetInt32());
            Assert.Equal(0, rootElement.GetProperty("PendingMigrationCount").GetInt32());
            Assert.True(rootElement.GetProperty("SeedSupportExists").GetBoolean());
            Assert.Equal(0, rootElement.GetProperty("AppliedMigrationsMissingFromSourceCount").GetInt32());
            Assert.Equal("001_baseline", rootElement.GetProperty("LatestRegisteredMigration").GetProperty("Id").GetString());
            Assert.True(rootElement.GetProperty("LatestRegisteredMigration").GetProperty("RollbackSupportExists").GetBoolean());
            Assert.Equal("001_baseline", rootElement.GetProperty("LatestAppliedMigration").GetProperty("Id").GetString());
            Assert.True(rootElement.GetProperty("LatestAppliedMigration").GetProperty("ExistsInSourceRegistry").GetBoolean());
            Assert.True(rootElement.GetProperty("LatestAppliedMigration").GetProperty("RollbackSupportExists").GetBoolean());
            Assert.True(File.Exists(Path.Combine(project, "data", "sales-portal.db")));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_Seed_ForScaffoldedWebApiIsIdempotentAndCreatesStarterRows()
    {
        var root = CreateTempDirectory("malda_db_seed_");
        var project = Path.Combine(root, "notes-api");
        try
        {
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = project,
                LocalFirst = true
            };

            Assert.Equal(0, scaffolder.Scaffold("webapi", project, new StringWriter(), new StringWriter(), options));

            var runner = new DbCommandRunner();
            var firstOutput = new StringWriter();
            var firstError = new StringWriter();
            var databasePath = Path.Combine(project, "data", "notes-api.db");

            var firstCode = runner.Run(new[] { "seed" }, firstOutput, firstError, project);

            Assert.Equal(0, firstCode);
            Assert.Contains("Seeded scaffolded local-first data.", firstOutput.ToString());
            Assert.Equal(string.Empty, firstError.ToString());
            Assert.True(File.Exists(databasePath));
            Assert.Equal(2, QueryScalarInt(databasePath, "SELECT COUNT(*) FROM app_notes"));
            Assert.Equal(2, QueryScalarInt(databasePath, "SELECT COUNT(*) FROM app_settings"));

            var secondOutput = new StringWriter();
            var secondError = new StringWriter();
            var secondCode = runner.Run(new[] { "seed" }, secondOutput, secondError, project);

            Assert.Equal(0, secondCode);
            Assert.Equal(string.Empty, secondError.ToString());
            Assert.Equal(2, QueryScalarInt(databasePath, "SELECT COUNT(*) FROM app_notes"));
            Assert.Equal(2, QueryScalarInt(databasePath, "SELECT COUNT(*) FROM app_settings"));
            Assert.Equal(1, QueryScalarInt(databasePath, "SELECT COUNT(*) FROM app_migrations"));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_Status_ReportsAppliedMigrationDriftWhenDatabaseContainsEntriesMissingFromSourceRegistry()
    {
        var root = CreateTempDirectory("malda_db_drift_");
        var project = Path.Combine(root, "notes-api");
        try
        {
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = project,
                LocalFirst = true
            };

            Assert.Equal(0, scaffolder.Scaffold("webapi", project, new StringWriter(), new StringWriter(), options));

            var runner = new DbCommandRunner();
            Assert.Equal(0, runner.Run(new[] { "migrate" }, new StringWriter(), new StringWriter(), project));

            var databasePath = Path.Combine(project, "data", "notes-api.db");
            ExecuteNonQuery(
                databasePath,
                "INSERT INTO app_migrations (id, description, applied_at) VALUES ($id, $description, $appliedAt)",
                ("$id", "999_manual_hotfix"),
                ("$description", "Manual hotfix outside source registry"),
                ("$appliedAt", "2026-03-15 12:00:00"));

            var statusOutput = new StringWriter();
            var statusError = new StringWriter();

            Assert.Equal(0, runner.Run(new[] { "status", "--json" }, statusOutput, statusError, project));
            Assert.Equal(string.Empty, statusError.ToString());

            using var json = JsonDocument.Parse(statusOutput.ToString());
            var rootElement = json.RootElement;
            Assert.Equal(1, rootElement.GetProperty("AppliedMigrationsMissingFromSourceCount").GetInt32());
            Assert.Equal("999_manual_hotfix", rootElement.GetProperty("LatestAppliedMigration").GetProperty("Id").GetString());
            Assert.False(rootElement.GetProperty("LatestAppliedMigration").GetProperty("ExistsInSourceRegistry").GetBoolean());
            Assert.Equal("applied-not-in-module", rootElement.GetProperty("LatestAppliedMigration").GetProperty("SourceState").GetString());
            Assert.False(rootElement.GetProperty("LatestAppliedMigration").GetProperty("RollbackSupportExists").GetBoolean());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_Migrate_UsesHelpersToRepairPartialBaselineSchemaBeforeRecordingMigration()
    {
        var root = CreateTempDirectory("malda_db_partial_");
        var project = Path.Combine(root, "notes-api");
        try
        {
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "webapi",
                DestinationPath = project,
                LocalFirst = true
            };

            Assert.Equal(0, scaffolder.Scaffold("webapi", project, new StringWriter(), new StringWriter(), options));

            var databasePath = Path.Combine(project, "data", "notes-api.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            ExecuteNonQuery(databasePath, "CREATE TABLE app_settings (id INTEGER PRIMARY KEY AUTOINCREMENT, key TEXT NOT NULL UNIQUE, value_json TEXT NOT NULL, updated_at TEXT DEFAULT CURRENT_TIMESTAMP)");
            ExecuteNonQuery(databasePath, "CREATE TABLE app_notes (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, body TEXT NOT NULL, created_at TEXT DEFAULT CURRENT_TIMESTAMP)");

            var runner = new DbCommandRunner();
            var migrateOutput = new StringWriter();
            var migrateError = new StringWriter();

            var code = runner.Run(new[] { "migrate" }, migrateOutput, migrateError, project);

            Assert.True(code == 0, $"stdout: {migrateOutput} stderr: {migrateError}");
            Assert.Equal(string.Empty, migrateError.ToString());
            Assert.Equal(1, QueryScalarInt(databasePath, "SELECT COUNT(*) FROM app_migrations WHERE id = '001_baseline'"));
            Assert.True(ColumnExists(databasePath, "app_notes", "deleted_at"));
            Assert.True(ColumnExists(databasePath, "app_notes", "deleted_by"));
            Assert.True(ColumnExists(databasePath, "app_notes", "updated_at"));
            Assert.True(ColumnExists(databasePath, "app_notes", "row_version"));
            Assert.True(IndexExists(databasePath, "idx_app_notes_created_at"));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Run_Rollback_ForScaffoldedFullstackRollsBackLatestAppliedMigration()
    {
        var root = CreateTempDirectory("malda_db_rollback_");
        var project = Path.Combine(root, "sales-portal");
        try
        {
            var scaffolder = new TemplateScaffolder();
            var options = new NewCommandOptions
            {
                TemplateName = "fullstack",
                DestinationPath = project,
                LocalFirst = true
            };

            Assert.Equal(0, scaffolder.Scaffold("fullstack", project, new StringWriter(), new StringWriter(), options));

            var runner = new DbCommandRunner();
            Assert.Equal(0, runner.Run(new[] { "migrate" }, new StringWriter(), new StringWriter(), project));

            var rollbackOutput = new StringWriter();
            var rollbackError = new StringWriter();
            var rollbackCode = runner.Run(new[] { "rollback" }, rollbackOutput, rollbackError, project);
            var databasePath = Path.Combine(project, "data", "sales-portal.db");

            Assert.Equal(0, rollbackCode);
            Assert.Contains("Rolled back local migration 001_baseline", rollbackOutput.ToString());
            Assert.Equal(string.Empty, rollbackError.ToString());
            Assert.True(File.Exists(databasePath));
            Assert.False(TableExists(databasePath, "app_migrations"));
            Assert.False(TableExists(databasePath, "app_settings"));
            Assert.False(TableExists(databasePath, "tickets"));

            var statusOutput = new StringWriter();
            var statusError = new StringWriter();
            Assert.Equal(0, runner.Run(new[] { "status", "--json" }, statusOutput, statusError, project));
            Assert.Equal(string.Empty, statusError.ToString());

            using var json = JsonDocument.Parse(statusOutput.ToString());
            var rootElement = json.RootElement;
            Assert.False(rootElement.GetProperty("RegistryTableExists").GetBoolean());
            Assert.Equal(0, rootElement.GetProperty("AppliedMigrationCount").GetInt32());
            Assert.Equal(1, rootElement.GetProperty("PendingMigrationCount").GetInt32());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    private static int QueryScalarInt(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void ExecuteNonQuery(string databasePath, string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static bool TableExists(string databasePath, string tableName)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() != null;
    }

    private static bool ColumnExists(string databasePath, string tableName, string columnName)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM pragma_table_info('{tableName.Replace("'", "''")}') WHERE name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", columnName);
        return command.ExecuteScalar() != null;
    }

    private static bool IndexExists(string databasePath, string indexName)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", indexName);
        return command.ExecuteScalar() != null;
    }
}
