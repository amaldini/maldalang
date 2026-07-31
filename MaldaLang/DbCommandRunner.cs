namespace MaldaLang;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.Interpreter;
using Microsoft.Data.Sqlite;

internal sealed class DbCommandRunner
{
    private static readonly Regex MigrationRegistryRegex = new(
        @"localMigrationRegistry\s*=\s*\[(?<entries>.*?)\];",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MigrationEntryRegex = new(
        @"\{\s*""id""\s*:\s*""(?<id>[^""]+)""\s*,\s*""description""\s*:\s*""(?<description>[^""]+)""\s*\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex FunctionDefinitionRegex = new(
        @"\bfunction\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex LocalDataDirAssignmentRegex = new(
        @"^\s*var\s+localDataDir\s*=\s*"".*?""\s*;\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex LocalDatabaseFileAssignmentRegex = new(
        @"^\s*var\s+localDatabaseFile\s*=\s*.*?;\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public int Run(string[] args, TextWriter output, TextWriter error, string? workingDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(workingDirectory!);

        var options = ParseOptions(args, root, error);
        if (options == null)
        {
            return 1;
        }

        if (options.ShowHelp || string.IsNullOrWhiteSpace(options.Subcommand))
        {
            PrintUsage(output);
            return 0;
        }

        if (!TryResolveProject(options, error, out var project))
        {
            return 1;
        }

        try
        {
            return options.Subcommand switch
            {
                "status" => RunStatus(project!, options, output),
                "migrate" => RunMigrate(project!, options, output, error),
                "seed" => RunSeed(project!, options, output),
                "rollback" => RunRollback(project!, options, output, error),
                _ => WriteUnknownSubcommand(options.Subcommand, error)
            };
        }
        catch (Exception ex)
        {
            error.WriteLine($"db: {ex.Message}");
            return 1;
        }
    }

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage: malda db <status|migrate|seed|rollback> [options]");
        output.WriteLine("  status                     Inspect scaffolded local-first SQLite status, support, and registry drift");
        output.WriteLine("  migrate                    Apply pending scaffolded local-first migrations");
        output.WriteLine("  seed                       Apply migrations, then run scaffolded starter seed data");
        output.WriteLine("  rollback                   Roll back the latest applied scaffolded local migration");
        output.WriteLine("  --project, -p <path>       Project root to inspect (default: current directory)");
        output.WriteLine("  --module, -m <path>        Override migration module path");
        output.WriteLine("  --database, -d <path>      Override SQLite database path");
        output.WriteLine("  --format <human|json>      Output format (default: human)");
        output.WriteLine("  --json                     Shortcut for --format json");
        output.WriteLine("  --help, -h                 Show db command help");
        output.WriteLine();
        output.WriteLine("Conventions:");
        output.WriteLine("  - current slice supports scaffolded SQLite local-first projects only");
        output.WriteLine("  - by default MALDA reads config/data.example.json for databaseFile and migrationModule");
        output.WriteLine("  - the migration module is expected to expose localMigrationRegistry and initLocalDataPlatform()");
        output.WriteLine("  - seed expects seedLocalDataPlatform() in the migration module");
        output.WriteLine("  - rollback targets only the latest applied registry entry and expects openLocalDataPlatform()");
        output.WriteLine("    plus rollbackLocalMigration<ID>() using the registry id naming convention");
        output.WriteLine();
        output.WriteLine("Current limits:");
        output.WriteLine("  - seed/rollback follow scaffold conventions instead of supporting arbitrary layouts");
        output.WriteLine("  - rollback only reverts the latest applied migration that still exists in localMigrationRegistry");
    }

    private static DbCommandOptions? ParseOptions(string[] args, string root, TextWriter error)
    {
        var options = new DbCommandOptions
        {
            ProjectRoot = root
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--help" || arg == "-h")
            {
                options.ShowHelp = true;
                continue;
            }

            if (arg == "--json")
            {
                options.Format = "json";
                continue;
            }

            if (arg == "--format" || arg == "-f")
            {
                if (!TryReadValue(args, ref i, "db: --format requires a value.", error, out var value))
                {
                    return null;
                }

                options.Format = value!.Trim().ToLowerInvariant();
                continue;
            }

            if (arg == "--project" || arg == "-p")
            {
                if (!TryReadValue(args, ref i, "db: --project requires a value.", error, out var value))
                {
                    return null;
                }

                options.ProjectRoot = ResolvePath(root, value!);
                continue;
            }

            if (arg == "--module" || arg == "-m")
            {
                if (!TryReadValue(args, ref i, "db: --module requires a value.", error, out var value))
                {
                    return null;
                }

                options.ModulePath = value!;
                continue;
            }

            if (arg == "--database" || arg == "-d")
            {
                if (!TryReadValue(args, ref i, "db: --database requires a value.", error, out var value))
                {
                    return null;
                }

                options.DatabasePath = value!;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                error.WriteLine($"db: unknown option '{arg}'.");
                error.WriteLine("Run 'malda db --help' for usage.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(options.Subcommand))
            {
                options.Subcommand = arg.Trim().ToLowerInvariant();
                continue;
            }

            error.WriteLine($"db: unexpected argument '{arg}'.");
            error.WriteLine("Run 'malda db --help' for usage.");
            return null;
        }

        if (options.Format != "human" && options.Format != "json")
        {
            error.WriteLine("db: invalid format. Supported values: human, json.");
            return null;
        }

        return options;
    }

    private static bool TryResolveProject(DbCommandOptions options, TextWriter error, out ResolvedDbProject? project)
    {
        project = null;
        var projectRoot = Path.GetFullPath(options.ProjectRoot);
        if (!Directory.Exists(projectRoot))
        {
            error.WriteLine($"db: project directory not found: {projectRoot}");
            return false;
        }

        var contractPath = Path.Combine(projectRoot, "config", "data.example.json");
        string? driver = null;
        string? mode = null;
        string? moduleRaw = options.ModulePath;
        string? databaseRaw = options.DatabasePath;

        if (File.Exists(contractPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(contractPath));
                var root = document.RootElement;
                driver = GetString(root, "driver");
                mode = GetString(root, "mode");
                moduleRaw ??= GetString(root, "migrationModule");
                databaseRaw ??= GetString(root, "databaseFile");
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                error.WriteLine($"db: could not parse {contractPath}: {ex.Message}");
                return false;
            }
        }

        moduleRaw ??= GuessMigrationModule(projectRoot);
        if (string.IsNullOrWhiteSpace(moduleRaw))
        {
            error.WriteLine("db: could not resolve a local-first migration module.");
            error.WriteLine("     Expected config/data.example.json or pass --module explicitly.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(databaseRaw))
        {
            error.WriteLine("db: could not resolve a SQLite database path.");
            error.WriteLine("     Expected config/data.example.json or pass --database explicitly.");
            return false;
        }

        var modulePath = ResolvePath(projectRoot, moduleRaw);
        if (!File.Exists(modulePath))
        {
            error.WriteLine($"db: migration module not found: {modulePath}");
            return false;
        }

        var effectiveDriver = string.IsNullOrWhiteSpace(driver) ? "sqlite" : driver.Trim().ToLowerInvariant();
        var effectiveMode = string.IsNullOrWhiteSpace(mode) ? "local-first" : mode.Trim().ToLowerInvariant();
        if (effectiveDriver != "sqlite" || effectiveMode != "local-first")
        {
            error.WriteLine($"db: this command currently supports scaffolded sqlite/local-first projects only (found driver='{effectiveDriver}', mode='{effectiveMode}').");
            return false;
        }

        var moduleSource = File.ReadAllText(modulePath);
        var registry = ParseMigrationRegistry(moduleSource, modulePath, error);
        if (registry == null)
        {
            return false;
        }

        var moduleFunctions = ParseModuleFunctionNames(moduleSource);

        project = new ResolvedDbProject
        {
            ProjectRoot = projectRoot,
            ContractPath = File.Exists(contractPath) ? contractPath : null,
            Driver = effectiveDriver,
            Mode = effectiveMode,
            MigrationModulePath = modulePath,
            MigrationModuleDisplayPath = ToDisplayPath(projectRoot, modulePath, moduleRaw),
            DatabasePath = ResolvePath(projectRoot, databaseRaw),
            DatabaseDisplayPath = ToDisplayPath(projectRoot, ResolvePath(projectRoot, databaseRaw), databaseRaw),
            RegisteredMigrations = registry,
            ModuleFunctionNames = moduleFunctions
        };

        return true;
    }

    private static List<LocalMigrationDefinition>? ParseMigrationRegistry(string moduleSource, string modulePath, TextWriter error)
    {
        var registryMatch = MigrationRegistryRegex.Match(moduleSource);
        if (!registryMatch.Success)
        {
            error.WriteLine($"db: could not find localMigrationRegistry in {modulePath}.");
            return null;
        }

        var entries = new List<LocalMigrationDefinition>();
        foreach (Match entryMatch in MigrationEntryRegex.Matches(registryMatch.Groups["entries"].Value))
        {
            var id = entryMatch.Groups["id"].Value.Trim();
            var description = entryMatch.Groups["description"].Value.Trim();
            if (id.Length == 0)
            {
                continue;
            }

            entries.Add(new LocalMigrationDefinition
            {
                Id = id,
                Description = description
            });
        }

        return entries;
    }

    private static HashSet<string> ParseModuleFunctionNames(string moduleSource)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in FunctionDefinitionRegex.Matches(moduleSource))
        {
            var name = match.Groups["name"].Value.Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static int RunStatus(ResolvedDbProject project, DbCommandOptions options, TextWriter output)
    {
        var status = InspectStatus(project);
        if (options.Format == "json")
        {
            output.WriteLine(JsonSerializer.Serialize(status));
            return 0;
        }

        WriteHumanStatus(output, status);
        return 0;
    }

    private static int RunMigrate(ResolvedDbProject project, DbCommandOptions options, TextWriter output, TextWriter error)
    {
        var before = InspectStatus(project);
        ExecuteBootstrap(project);
        var after = InspectStatus(project);
        var previouslyAppliedIds = before.Migrations
            .Where(x => string.Equals(x.State, "applied", StringComparison.Ordinal))
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
        var newlyApplied = after.Migrations
            .Where(x => string.Equals(x.State, "applied", StringComparison.Ordinal))
            .Where(x => !previouslyAppliedIds.Contains(x.Id))
            .ToList();

        if (options.Format == "json")
        {
            output.WriteLine(JsonSerializer.Serialize(new DbMigrateResult
            {
                AppliedNow = newlyApplied,
                AppliedNowCount = newlyApplied.Count,
                Status = after
            }));
            return 0;
        }

        if (newlyApplied.Count == 0)
        {
            output.WriteLine("No pending local migrations.");
        }
        else
        {
            output.WriteLine($"Applied {newlyApplied.Count} local migration(s).");
            foreach (var migration in newlyApplied)
            {
                output.WriteLine($"  - {migration.Id}: {migration.Description}");
            }
        }

        output.WriteLine($"Database: {after.DatabaseFile} {(after.DatabaseExists ? "(present)" : "(missing)")}");
        output.WriteLine($"Applied total: {after.AppliedMigrationCount}");
        output.WriteLine($"Pending total: {after.PendingMigrationCount}");
        if (after.PendingMigrationCount > 0)
        {
            error.WriteLine("db: some migrations are still pending after migrate.");
            return 1;
        }

        return 0;
    }

    private static int RunSeed(ResolvedDbProject project, DbCommandOptions options, TextWriter output)
    {
        ExecuteSeed(project);
        var after = InspectStatus(project);
        if (after.PendingMigrationCount > 0)
        {
            throw new InvalidOperationException("some migrations are still pending after seed.");
        }

        if (options.Format == "json")
        {
            output.WriteLine(JsonSerializer.Serialize(new DbSeedResult
            {
                SeedFunction = "seedLocalDataPlatform",
                Status = after
            }));
            return 0;
        }

        output.WriteLine("Seeded scaffolded local-first data.");
        output.WriteLine($"Database: {after.DatabaseFile} {(after.DatabaseExists ? "(present)" : "(missing)")}");
        output.WriteLine($"Applied total: {after.AppliedMigrationCount}");
        output.WriteLine($"Pending total: {after.PendingMigrationCount}");
        return 0;
    }

    private static int RunRollback(ResolvedDbProject project, DbCommandOptions options, TextWriter output, TextWriter error)
    {
        var before = InspectStatus(project);
        var unknownApplied = before.Migrations
            .Where(x => string.Equals(x.State, "applied-not-in-module", StringComparison.Ordinal))
            .ToList();
        if (unknownApplied.Count > 0)
        {
            error.WriteLine("db: rollback only supports applied migrations that still exist in localMigrationRegistry.");
            error.WriteLine("     Remove or reconcile extra app_migrations entries before using 'malda db rollback'.");
            return 1;
        }

        var latestApplied = project.RegisteredMigrations
            .LastOrDefault(migration => before.Migrations.Any(status =>
                string.Equals(status.Id, migration.Id, StringComparison.Ordinal) &&
                string.Equals(status.State, "applied", StringComparison.Ordinal)));
        if (latestApplied == null)
        {
            if (options.Format == "json")
            {
                output.WriteLine(JsonSerializer.Serialize(new DbRollbackResult
                {
                    RolledBack = null,
                    RollbackFunction = null,
                    Status = before
                }));
            }
            else
            {
                output.WriteLine("No applied local migrations to rollback.");
            }

            return 0;
        }

        var rollbackFunctionName = BuildRollbackFunctionName(latestApplied.Id);
        ExecuteRollback(project, rollbackFunctionName);

        var after = InspectStatus(project);
        if (after.Migrations.Any(status =>
            string.Equals(status.Id, latestApplied.Id, StringComparison.Ordinal) &&
            string.Equals(status.State, "applied", StringComparison.Ordinal)))
        {
            error.WriteLine($"db: rollback finished but '{latestApplied.Id}' is still recorded as applied.");
            error.WriteLine($"     Ensure {rollbackFunctionName}() removes its own app_migrations entry.");
            return 1;
        }

        if (options.Format == "json")
        {
            output.WriteLine(JsonSerializer.Serialize(new DbRollbackResult
            {
                RolledBack = new DbRollbackMigration
                {
                    Id = latestApplied.Id,
                    Description = latestApplied.Description
                },
                RollbackFunction = rollbackFunctionName,
                Status = after
            }));
            return 0;
        }

        output.WriteLine($"Rolled back local migration {latestApplied.Id}: {latestApplied.Description}");
        output.WriteLine($"Database: {after.DatabaseFile} {(after.DatabaseExists ? "(present)" : "(missing)")}");
        output.WriteLine($"Applied total: {after.AppliedMigrationCount}");
        output.WriteLine($"Pending total: {after.PendingMigrationCount}");
        return 0;
    }

    private static DbStatusReport InspectStatus(ResolvedDbProject project)
    {
        var report = new DbStatusReport
        {
            Driver = project.Driver,
            Mode = project.Mode,
            ProjectRoot = project.ProjectRoot,
            ContractPath = project.ContractPath,
            MigrationModule = project.MigrationModuleDisplayPath,
            DatabaseFile = project.DatabaseDisplayPath,
            DatabaseExists = File.Exists(project.DatabasePath),
            RegisteredMigrationCount = project.RegisteredMigrations.Count,
            SeedFunction = "seedLocalDataPlatform",
            SeedSupportExists = project.ModuleFunctionNames.Contains("seedLocalDataPlatform")
        };

        var applied = new List<AppliedMigrationRecord>();
        if (report.DatabaseExists)
        {
            using var connection = new SqliteConnection($"Data Source={project.DatabasePath};Mode=ReadOnly");
            connection.Open();

            report.RegistryTableExists = HasMigrationTable(connection);
            if (report.RegistryTableExists)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT id, description, applied_at FROM app_migrations ORDER BY id ASC";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    applied.Add(new AppliedMigrationRecord
                    {
                        Id = reader.GetString(0),
                        Description = reader.GetString(1),
                        AppliedAt = reader.IsDBNull(2) ? null : reader.GetString(2)
                    });
                }
            }
        }

        var appliedLookup = applied.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var migration in project.RegisteredMigrations)
        {
            appliedLookup.TryGetValue(migration.Id, out var appliedRecord);
            report.Migrations.Add(new DbMigrationStatus
            {
                Id = migration.Id,
                Description = migration.Description,
                State = appliedRecord == null ? "pending" : "applied",
                AppliedAt = appliedRecord?.AppliedAt
            });
        }

        foreach (var unexpected in applied.Where(x => project.RegisteredMigrations.All(m => !string.Equals(m.Id, x.Id, StringComparison.Ordinal))))
        {
            report.Migrations.Add(new DbMigrationStatus
            {
                Id = unexpected.Id,
                Description = unexpected.Description,
                State = "applied-not-in-module",
                AppliedAt = unexpected.AppliedAt
            });
        }

        report.AppliedMigrationCount = report.Migrations.Count(x => string.Equals(x.State, "applied", StringComparison.Ordinal));
        report.PendingMigrationCount = report.Migrations.Count(x => string.Equals(x.State, "pending", StringComparison.Ordinal));
        report.AppliedMigrationsMissingFromSourceCount = report.Migrations.Count(x => string.Equals(x.State, "applied-not-in-module", StringComparison.Ordinal));

        var latestRegistered = project.RegisteredMigrations.LastOrDefault();
        if (latestRegistered != null)
        {
            var rollbackFunction = BuildRollbackFunctionName(latestRegistered.Id);
            report.LatestRegisteredMigration = new DbRegisteredMigrationInfo
            {
                Id = latestRegistered.Id,
                Description = latestRegistered.Description,
                RollbackFunction = rollbackFunction,
                RollbackSupportExists = project.ModuleFunctionNames.Contains(rollbackFunction)
            };
        }

        var latestApplied = applied
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .LastOrDefault();
        if (latestApplied != null)
        {
            var existsInSourceRegistry = project.RegisteredMigrations.Any(m => string.Equals(m.Id, latestApplied.Id, StringComparison.Ordinal));
            var rollbackFunction = BuildRollbackFunctionName(latestApplied.Id);
            report.LatestAppliedMigration = new DbAppliedMigrationInfo
            {
                Id = latestApplied.Id,
                Description = latestApplied.Description,
                AppliedAt = latestApplied.AppliedAt,
                ExistsInSourceRegistry = existsInSourceRegistry,
                SourceState = existsInSourceRegistry ? "applied" : "applied-not-in-module",
                RollbackFunction = rollbackFunction,
                RollbackSupportExists = existsInSourceRegistry && project.ModuleFunctionNames.Contains(rollbackFunction)
            };
        }

        return report;
    }

    private static void ExecuteBootstrap(ResolvedDbProject project)
    {
        ExecuteModuleSession(
            project,
            interpreter => CallRequiredModuleFunction(
                interpreter,
                "initLocalDataPlatform",
                $"could not find initLocalDataPlatform() in {project.MigrationModuleDisplayPath}."));
    }

    private static void ExecuteSeed(ResolvedDbProject project)
    {
        ExecuteModuleSession(project, interpreter =>
        {
            CallRequiredModuleFunction(
                interpreter,
                "initLocalDataPlatform",
                $"could not find initLocalDataPlatform() in {project.MigrationModuleDisplayPath}.");
            CallRequiredModuleFunction(
                interpreter,
                "seedLocalDataPlatform",
                $"seed requires seedLocalDataPlatform() in {project.MigrationModuleDisplayPath}.");
        });
    }

    private static void ExecuteRollback(ResolvedDbProject project, string rollbackFunctionName)
    {
        ExecuteModuleSession(project, interpreter =>
        {
            CallRequiredModuleFunction(
                interpreter,
                "openLocalDataPlatform",
                $"rollback requires openLocalDataPlatform() in {project.MigrationModuleDisplayPath}.");
            CallRequiredModuleFunction(
                interpreter,
                rollbackFunctionName,
                $"rollback requires {rollbackFunctionName}() in {project.MigrationModuleDisplayPath}.");
        });
    }

    private static void ExecuteModuleSession(ResolvedDbProject project, Action<Interpreter.Interpreter> action)
    {
        var source = RewriteScaffoldedLocalPaths(File.ReadAllText(project.MigrationModulePath), project);
        var lexer = new Lexer(source, project.MigrationModulePath);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens, project.MigrationModulePath);
        var statements = parser.Parse();
        if (parser.Errors.Count > 0)
        {
            throw parser.Errors[0];
        }

        var interpreter = new Interpreter.Interpreter(currentFile: project.MigrationModulePath);
        interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        try
        {
            action(interpreter);
        }
        finally
        {
            DisconnectLocalDb(interpreter);
        }
    }

    private static void CallRequiredModuleFunction(Interpreter.Interpreter interpreter, string functionName, string missingMessage)
    {
        if (!interpreter._globals.TryGet(functionName, out var runtimeValue) ||
            runtimeValue.Type != MaldaLang.Interpreter.ValueType.Function)
        {
            throw new InvalidOperationException(missingMessage);
        }

        interpreter.CallFunctionAsync(runtimeValue.AsFunction(), new List<RuntimeValue>()).GetAwaiter().GetResult();
    }

    private static void DisconnectLocalDb(Interpreter.Interpreter interpreter)
    {
        if (!interpreter._globals.TryGet("localDb", out var localDb) ||
            localDb.Type != MaldaLang.Interpreter.ValueType.Object)
        {
            return;
        }

        var localDbObject = localDb.AsObject();
        if (!localDbObject.TryGet("disconnect", out var disconnectValue) ||
            disconnectValue == null ||
            disconnectValue.Type != MaldaLang.Interpreter.ValueType.Function)
        {
            return;
        }

        interpreter.CallFunctionAsync(disconnectValue.AsFunction(), new List<RuntimeValue>()).GetAwaiter().GetResult();
    }

    private static bool HasMigrationTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'app_migrations' LIMIT 1";
        return command.ExecuteScalar() != null;
    }

    private static void WriteHumanStatus(TextWriter output, DbStatusReport status)
    {
        output.WriteLine("Local-first database status");
        output.WriteLine($"Project: {status.ProjectRoot}");
        output.WriteLine($"Mode: {status.Mode} ({status.Driver})");
        output.WriteLine($"Module: {status.MigrationModule}");
        output.WriteLine($"Database: {status.DatabaseFile} {(status.DatabaseExists ? "(present)" : "(missing)")}");
        output.WriteLine($"Applied: {status.AppliedMigrationCount}");
        output.WriteLine($"Pending: {status.PendingMigrationCount}");
        output.WriteLine($"Seed support: {(status.SeedSupportExists ? "yes" : "no")} ({status.SeedFunction})");

        if (status.LatestRegisteredMigration != null)
        {
            output.WriteLine($"Latest registered: {status.LatestRegisteredMigration.Id} - {status.LatestRegisteredMigration.Description}");
            output.WriteLine($"Rollback latest registered: {(status.LatestRegisteredMigration.RollbackSupportExists ? "yes" : "no")} ({status.LatestRegisteredMigration.RollbackFunction})");
        }
        else
        {
            output.WriteLine("Latest registered: none");
        }

        if (status.LatestAppliedMigration != null)
        {
            var appliedAtSuffix = string.IsNullOrWhiteSpace(status.LatestAppliedMigration.AppliedAt)
                ? string.Empty
                : $" @ {status.LatestAppliedMigration.AppliedAt}";
            var registrySuffix = status.LatestAppliedMigration.ExistsInSourceRegistry
                ? string.Empty
                : " [missing from source registry]";
            output.WriteLine($"Latest applied: {status.LatestAppliedMigration.Id} - {status.LatestAppliedMigration.Description}{appliedAtSuffix}{registrySuffix}");
            output.WriteLine($"Rollback latest applied: {(status.LatestAppliedMigration.RollbackSupportExists ? "yes" : "no")} ({status.LatestAppliedMigration.RollbackFunction})");
        }
        else
        {
            output.WriteLine("Latest applied: none");
        }

        if (status.AppliedMigrationsMissingFromSourceCount > 0)
        {
            output.WriteLine($"Registry drift: {status.AppliedMigrationsMissingFromSourceCount} applied migration(s) exist in the database but not in localMigrationRegistry.");
        }

        if (!status.DatabaseExists)
        {
            output.WriteLine("State: database file has not been created yet.");
        }
        else if (!status.RegistryTableExists)
        {
            output.WriteLine("State: database exists but app_migrations has not been initialized yet.");
        }

        if (status.Migrations.Count > 0)
        {
            output.WriteLine("Migrations:");
            foreach (var migration in status.Migrations.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var suffix = string.IsNullOrWhiteSpace(migration.AppliedAt) ? string.Empty : $" @ {migration.AppliedAt}";
                output.WriteLine($"  {migration.State,-21} {migration.Id} - {migration.Description}{suffix}");
            }
        }

        if (status.PendingMigrationCount > 0)
        {
            output.WriteLine("Next: run 'malda db migrate' from the project root.");
        }
        else
        {
            output.WriteLine("Local-first scaffold is up to date.");
        }
    }

    private static int WriteUnsupportedSubcommand(string subcommand, TextWriter error)
    {
        error.WriteLine($"db: '{subcommand}' is not supported yet for the scaffolded local-first slice.");
        error.WriteLine("    Today MALDA supports 'malda db status' and 'malda db migrate'.");
        return 1;
    }

    private static int WriteUnknownSubcommand(string subcommand, TextWriter error)
    {
        error.WriteLine($"db: unknown subcommand '{subcommand}'.");
        error.WriteLine("Run 'malda db --help' for usage.");
        return 1;
    }

    private static bool TryReadValue(string[] args, ref int index, string message, TextWriter error, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length)
        {
            error.WriteLine(message);
            return false;
        }

        value = args[++index];
        return true;
    }

    private static string ResolvePath(string root, string raw)
    {
        if (Path.IsPathRooted(raw))
        {
            return Path.GetFullPath(raw);
        }

        return Path.GetFullPath(Path.Combine(root, raw));
    }

    private static string? GuessMigrationModule(string projectRoot)
    {
        foreach (var candidate in new[] { Path.Combine("data", "local_first.malda"), Path.Combine("backend", "data", "local_first.malda") })
        {
            var fullPath = Path.Combine(projectRoot, candidate);
            if (File.Exists(fullPath))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string BuildRollbackFunctionName(string migrationId)
    {
        var builder = new StringBuilder("rollbackLocalMigration");
        foreach (Match segment in Regex.Matches(migrationId, "[A-Za-z0-9]+"))
        {
            var value = segment.Value;
            if (value.Length == 0)
            {
                continue;
            }

            if (value.All(char.IsDigit))
            {
                builder.Append(value);
                continue;
            }

            builder.Append(char.ToUpperInvariant(value[0]));
            if (value.Length > 1)
            {
                builder.Append(value.Substring(1).ToLowerInvariant());
            }
        }

        return builder.ToString();
    }

    private static string RewriteScaffoldedLocalPaths(string source, ResolvedDbProject project)
    {
        var dataDirectory = Path.GetDirectoryName(project.DatabasePath);
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return source;
        }

        var rewritten = LocalDataDirAssignmentRegex.Replace(
            source,
            $"var localDataDir = \"{EscapeMaldaString(dataDirectory)}\";");
        return LocalDatabaseFileAssignmentRegex.Replace(
            rewritten,
            $"var localDatabaseFile = \"{EscapeMaldaString(project.DatabasePath)}\";");
    }

    private static string EscapeMaldaString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string ToDisplayPath(string projectRoot, string absolutePath, string rawValue)
    {
        if (Path.IsPathRooted(rawValue))
        {
            return absolutePath;
        }

        var relative = Path.GetRelativePath(projectRoot, absolutePath);
        return relative.Replace('\\', '/');
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }

    private sealed class DbCommandOptions
    {
        public bool ShowHelp { get; set; }
        public string Subcommand { get; set; } = string.Empty;
        public string Format { get; set; } = "human";
        public string ProjectRoot { get; set; } = string.Empty;
        public string? ModulePath { get; set; }
        public string? DatabasePath { get; set; }
    }

    private sealed class ResolvedDbProject
    {
        public string ProjectRoot { get; init; } = string.Empty;
        public string? ContractPath { get; init; }
        public string Driver { get; init; } = string.Empty;
        public string Mode { get; init; } = string.Empty;
        public string MigrationModulePath { get; init; } = string.Empty;
        public string MigrationModuleDisplayPath { get; init; } = string.Empty;
        public string DatabasePath { get; init; } = string.Empty;
        public string DatabaseDisplayPath { get; init; } = string.Empty;
        public IReadOnlyList<LocalMigrationDefinition> RegisteredMigrations { get; init; } = Array.Empty<LocalMigrationDefinition>();
        public IReadOnlySet<string> ModuleFunctionNames { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    }

    private sealed class LocalMigrationDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }

    private sealed class AppliedMigrationRecord
    {
        public string Id { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? AppliedAt { get; init; }
    }

    private sealed class DbMigrateResult
    {
        public int AppliedNowCount { get; init; }
        public IReadOnlyList<DbMigrationStatus> AppliedNow { get; init; } = Array.Empty<DbMigrationStatus>();
        public DbStatusReport Status { get; init; } = new();
    }

    private sealed class DbSeedResult
    {
        public string SeedFunction { get; init; } = string.Empty;
        public DbStatusReport Status { get; init; } = new();
    }

    private sealed class DbRollbackResult
    {
        public DbRollbackMigration? RolledBack { get; init; }
        public string? RollbackFunction { get; init; }
        public DbStatusReport Status { get; init; } = new();
    }

    private sealed class DbRollbackMigration
    {
        public string Id { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }

    private sealed class DbStatusReport
    {
        public string ProjectRoot { get; init; } = string.Empty;
        public string? ContractPath { get; init; }
        public string Driver { get; init; } = string.Empty;
        public string Mode { get; init; } = string.Empty;
        public string MigrationModule { get; init; } = string.Empty;
        public string DatabaseFile { get; init; } = string.Empty;
        public bool DatabaseExists { get; set; }
        public bool RegistryTableExists { get; set; }
        public int RegisteredMigrationCount { get; set; }
        public int AppliedMigrationCount { get; set; }
        public int PendingMigrationCount { get; set; }
        public string SeedFunction { get; init; } = string.Empty;
        public bool SeedSupportExists { get; set; }
        public int AppliedMigrationsMissingFromSourceCount { get; set; }
        public DbRegisteredMigrationInfo? LatestRegisteredMigration { get; set; }
        public DbAppliedMigrationInfo? LatestAppliedMigration { get; set; }
        public List<DbMigrationStatus> Migrations { get; } = new();
    }

    private sealed class DbRegisteredMigrationInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string RollbackFunction { get; init; } = string.Empty;
        public bool RollbackSupportExists { get; init; }
    }

    private sealed class DbAppliedMigrationInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? AppliedAt { get; init; }
        public string SourceState { get; init; } = string.Empty;
        public bool ExistsInSourceRegistry { get; init; }
        public string RollbackFunction { get; init; } = string.Empty;
        public bool RollbackSupportExists { get; init; }
    }

    private sealed class DbMigrationStatus
    {
        public string Id { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string? AppliedAt { get; init; }
    }
}
