using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Storage.SavedQueries;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>
/// Verifies that Phase 10 services can be instantiated and wired via a minimal DI container.
/// </summary>
public sealed class WiringTests : IDisposable
{
    private readonly string _tmpDir;

    public WiringTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"wiring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        // best-effort cleanup; SQLite may briefly hold the file
        try { Directory.Delete(_tmpDir, recursive: true); }
        catch (IOException) { /* ignore on Windows */ }
    }

    private IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var dbPath = Path.Combine(_tmpDir, "annotations.db");
        var bundlesRoot = Path.Combine(_tmpDir, "bundles");

        services.AddSingleton<ISavedQueryStore>(
            new SqliteSavedQueryStore(dbPath, NullLogger<SqliteSavedQueryStore>.Instance));

        services.AddSingleton(new SqlExecutorConfig
        {
            DefaultTimeoutSeconds = 30,
            DefaultMaxRows        = 100_000,
            MaxMemoryMb           = 512,
        });

        services.AddSingleton(new BundleLibraryService(bundlesRoot));
        services.AddSingleton(new BundleExportService(bundlesRoot));
        services.AddSingleton(new BundleImportService(bundlesRoot, NullLogger<BundleImportService>.Instance));
        services.AddSingleton(new ViewSqlTemplateService());

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ISavedQueryStore_Resolvable()
    {
        var sp = BuildProvider();
        var store = sp.GetRequiredService<ISavedQueryStore>();
        Assert.NotNull(store);
    }

    [Fact]
    public void SqlExecutorConfig_Resolvable()
    {
        var sp = BuildProvider();
        var cfg = sp.GetRequiredService<SqlExecutorConfig>();
        Assert.Equal(30, cfg.DefaultTimeoutSeconds);
    }

    [Fact]
    public void BundleLibraryService_Resolvable()
    {
        var sp = BuildProvider();
        var svc = sp.GetRequiredService<BundleLibraryService>();
        Assert.NotNull(svc);
    }

    [Fact]
    public void BundleExportService_Resolvable()
    {
        var sp = BuildProvider();
        var svc = sp.GetRequiredService<BundleExportService>();
        Assert.NotNull(svc);
    }

    [Fact]
    public void BundleImportService_Resolvable()
    {
        var sp = BuildProvider();
        var svc = sp.GetRequiredService<BundleImportService>();
        Assert.NotNull(svc);
    }

    [Fact]
    public void ViewSqlTemplateService_Resolvable()
    {
        var sp = BuildProvider();
        var svc = sp.GetRequiredService<ViewSqlTemplateService>();
        Assert.NotNull(svc);
    }

    [Fact]
    public void SqliteSavedQueryStore_ImplementsInterface()
    {
        var store = new SqliteSavedQueryStore(
            Path.Combine(_tmpDir, $"test-{Guid.NewGuid():N}.db"),
            NullLogger<SqliteSavedQueryStore>.Instance);
        Assert.IsAssignableFrom<ISavedQueryStore>(store);
    }

    [Fact]
    public void SqlExecutorConfig_Defaults_AreReasonable()
    {
        var cfg = new SqlExecutorConfig();
        Assert.True(cfg.DefaultTimeoutSeconds > 0);
        Assert.True(cfg.DefaultMaxRows > 0);
        Assert.True(cfg.MaxMemoryMb > 0);
    }
}
