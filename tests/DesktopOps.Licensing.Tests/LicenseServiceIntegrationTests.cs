using System.Security.Cryptography;
using DesktopOps.Licensing;
using DesktopOps.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DesktopOps.Licensing.Tests;

public sealed class LicenseServiceIntegrationTests
{
    [Fact]
    public async Task Install_product_signed_unlimited_enterprise_and_evaluate()
    {
        var privatePemPath = FindDevPrivateKey();
        if (privatePemPath is null)
        {
            return; // CI without local signing key
        }

        await using var harness = await SqliteHarness.CreateAsync();
        var db = harness.Db;
        await DatabaseInitializer.InitializeAsync(db);

        var priv = await File.ReadAllTextAsync(privatePemPath);
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Enterprise,
            MaxSeats = null,
            Customer = "HardTest Corp",
            ValidUntilUtc = null
        };

        var json = LicenseCrypto.SerializeDocument(LicenseCrypto.Sign(claims, priv));
        await LicenseService.InstallAsync(db, json);

        var eval = await LicenseService.EvaluateAsync(db);
        Assert.True(eval.HasInstalledLicense);
        Assert.True(eval.SignatureValid);
        Assert.Equal(LicenseTiers.Enterprise, eval.Tier);
        Assert.Null(eval.MaxSeats);
        Assert.False(eval.BlocksMutations);
        Assert.False(eval.IsOverSeatLimit);
    }

    [Fact]
    public async Task Install_expired_team_blocks_mutations()
    {
        var privatePemPath = FindDevPrivateKey();
        if (privatePemPath is null)
        {
            return;
        }

        await using var harness = await SqliteHarness.CreateAsync();
        var db = harness.Db;
        await DatabaseInitializer.InitializeAsync(db);

        var priv = await File.ReadAllTextAsync(privatePemPath);
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Team,
            MaxSeats = 25,
            Customer = "Expired Co",
            ValidUntilUtc = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        await LicenseService.InstallAsync(
            db,
            LicenseCrypto.SerializeDocument(LicenseCrypto.Sign(claims, priv)));

        var eval = await LicenseService.EvaluateAsync(db);
        Assert.True(eval.IsExpired);
        Assert.True(eval.BlocksMutations);
    }

    private static string? FindDevPrivateKey()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tools", "licensing", "dev-private.pem"));
        return File.Exists(path) ? path : null;
    }
}

internal sealed class SqliteHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteHarness(SqliteConnection connection, DesktopOpsDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public DesktopOpsDbContext Db { get; }

    public static async Task<SqliteHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DesktopOpsDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new DesktopOpsDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new SqliteHarness(connection, db);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
