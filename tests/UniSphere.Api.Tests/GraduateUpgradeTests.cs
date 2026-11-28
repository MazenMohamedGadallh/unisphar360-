using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniSphere.Api.Data;
using Xunit;

namespace UniSphere.Api.Tests;

public class GraduateUpgradeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Patcher_UpgradesOldSchema_TwiceSafely_AndOverviewSeedsDefaults()
    {
        var client = factory.GraduateClient;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Back to the pre-upgrade schema, and a graduate with no profile row at all.
            await db.Database.ExecuteSqlRawAsync("DROP TABLE GraduateQuestProgress");
            foreach (var column in new[] { "TargetRole", "PrimaryStruggle", "PreferredWorkMode", "PreferredCity", "HasCompletedOnboarding" })
#pragma warning disable EF1002 // column names are the constants above
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE GraduateProfiles DROP COLUMN {column}");
#pragma warning restore EF1002
            await db.Database.ExecuteSqlRawAsync("DELETE FROM GraduateProfiles");

            // A quest table from the first release, without the newer columns.
            DbSchemaPatcher.Patch(db);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE GraduateQuestProgress DROP COLUMN SkippedLevelsCsv");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE GraduateQuestProgress DROP COLUMN PrepJson");

            DbSchemaPatcher.Patch(db);
            DbSchemaPatcher.Patch(db);
        }

        foreach (var url in new[] { "/api/grad/overview", "/api/graduate/overview" })
        {
            var response = await client.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode, $"{url} {(int)response.StatusCode}");
            var overview = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(overview.GetProperty("hasCompletedOnboarding").GetBoolean());
            Assert.Equal(0, overview.GetProperty("xp").GetInt32());
        }
    }
}
