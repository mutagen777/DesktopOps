using Microsoft.EntityFrameworkCore;

namespace DesktopOps.Server.Data;

/// <summary>Creates or patches the DesktopOps database schema.</summary>
public static class DatabaseInitializer
{
    /// <summary>Ensures the database exists and applies lightweight column patches.</summary>
    public static async Task InitializeAsync(DesktopOpsDbContext dbContext, CancellationToken cancellationToken = default)
    {
        // EnsureCreated is not concurrency-safe across processes; tolerate already-created schemas.
        try
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Another process created the schema first.
        }

        await EnsureColumnAsync(
            dbContext,
            "UserGroups",
            "ActiveDirectoryGroup",
            sqliteAlter: """ALTER TABLE "UserGroups" ADD COLUMN "ActiveDirectoryGroup" TEXT NULL;""",
            sqlServerAlter: """
                IF COL_LENGTH('UserGroups', 'ActiveDirectoryGroup') IS NULL
                    ALTER TABLE UserGroups ADD ActiveDirectoryGroup nvarchar(200) NULL;
                """,
            cancellationToken);

        await EnsureColumnAsync(
            dbContext,
            "ReleasePackages",
            "RolloutPercent",
            sqliteAlter: """ALTER TABLE "ReleasePackages" ADD COLUMN "RolloutPercent" INTEGER NOT NULL DEFAULT 100;""",
            sqlServerAlter: """
                IF COL_LENGTH('ReleasePackages', 'RolloutPercent') IS NULL
                    ALTER TABLE ReleasePackages ADD RolloutPercent int NOT NULL CONSTRAINT DF_ReleasePackages_RolloutPercent DEFAULT 100;
                """,
            cancellationToken);

        await EnsureColumnAsync(
            dbContext,
            "ReleasePackages",
            "SignaturePath",
            sqliteAlter: """ALTER TABLE "ReleasePackages" ADD COLUMN "SignaturePath" TEXT NULL;""",
            sqlServerAlter: """
                IF COL_LENGTH('ReleasePackages', 'SignaturePath') IS NULL
                    ALTER TABLE ReleasePackages ADD SignaturePath nvarchar(260) NULL;
                """,
            cancellationToken);

        await EnsureColumnAsync(
            dbContext,
            "ReleasePackages",
            "DeltaPath",
            sqliteAlter: """ALTER TABLE "ReleasePackages" ADD COLUMN "DeltaPath" TEXT NULL;""",
            sqlServerAlter: """
                IF COL_LENGTH('ReleasePackages', 'DeltaPath') IS NULL
                    ALTER TABLE ReleasePackages ADD DeltaPath nvarchar(260) NULL;
                """,
            cancellationToken);

        await EnsureColumnAsync(
            dbContext,
            "ReleasePackages",
            "DeltaHash",
            sqliteAlter: """ALTER TABLE "ReleasePackages" ADD COLUMN "DeltaHash" TEXT NULL;""",
            sqlServerAlter: """
                IF COL_LENGTH('ReleasePackages', 'DeltaHash') IS NULL
                    ALTER TABLE ReleasePackages ADD DeltaHash nvarchar(128) NULL;
                """,
            cancellationToken);

        await EnsureColumnAsync(
            dbContext,
            "ReleasePackages",
            "DeltaSize",
            sqliteAlter: """ALTER TABLE "ReleasePackages" ADD COLUMN "DeltaSize" INTEGER NULL;""",
            sqlServerAlter: """
                IF COL_LENGTH('ReleasePackages', 'DeltaSize') IS NULL
                    ALTER TABLE ReleasePackages ADD DeltaSize bigint NULL;
                """,
            cancellationToken);

        await EnsureColumnAsync(
            dbContext,
            "ReleasePackages",
            "DeltaBaseVersion",
            sqliteAlter: """ALTER TABLE "ReleasePackages" ADD COLUMN "DeltaBaseVersion" TEXT NULL;""",
            sqlServerAlter: """
                IF COL_LENGTH('ReleasePackages', 'DeltaBaseVersion') IS NULL
                    ALTER TABLE ReleasePackages ADD DeltaBaseVersion nvarchar(50) NULL;
                """,
            cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        DesktopOpsDbContext dbContext,
        string tableName,
        string columnName,
        string sqliteAlter,
        string sqlServerAlter,
        CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            if (await SqliteHasColumnAsync(dbContext, tableName, columnName, cancellationToken))
            {
                return;
            }

            await dbContext.Database.ExecuteSqlRawAsync(sqliteAlter, cancellationToken);
            return;
        }

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync(sqlServerAlter, cancellationToken);
        }
    }

    private static async Task<bool> SqliteHasColumnAsync(
        DesktopOpsDbContext dbContext,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
