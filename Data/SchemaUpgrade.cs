using System.Data;
using Microsoft.EntityFrameworkCore;

namespace UniSphere.Api.Data;

/// <summary>
/// Small, idempotent schema upgrades for installations created before EF
/// migrations were introduced. A backup (unisphere-backup.db) is taken before
/// applying these. Fresh databases get the same shape from EnsureCreated.
/// </summary>
public static class SchemaUpgrade
{
    public static void Apply(AppDbContext db, ILogger? logger = null)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed) connection.Open();
        try
        {
            AddColumnIfMissing(connection, "Courses", "Prerequisites", "TEXT NOT NULL DEFAULT ''");
            AddColumnIfMissing(connection, "Events", "UniversityId", "INTEGER NULL");
            AddColumnIfMissing(connection, "Announcements", "UniversityId", "INTEGER NULL");
            AddColumnIfMissing(connection, "Announcements", "CourseId", "INTEGER NULL");

            // Phase 1: locked doctor profile address.
            AddColumnIfMissing(connection, "Users", "Address", "TEXT NOT NULL DEFAULT ''");

            // Part 1: doctor assignments (fresh databases get this table from
            // EnsureCreated; existing installations get it here).
            Execute(connection,
                "CREATE TABLE IF NOT EXISTS \"CourseInstructors\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_CourseInstructors\" PRIMARY KEY AUTOINCREMENT, " +
                "\"CourseId\" INTEGER NOT NULL, " +
                "\"DoctorUserId\" INTEGER NOT NULL, " +
                "\"Term\" TEXT NOT NULL, " +
                "\"CreatedAtUtc\" TEXT NOT NULL, " +
                "CONSTRAINT \"FK_CourseInstructors_Courses_CourseId\" FOREIGN KEY (\"CourseId\") " +
                "REFERENCES \"Courses\" (\"Id\") ON DELETE CASCADE)");

            // Phase 1: doctor-editable course display title (catalog
            // title/code stay untouched). After the table is guaranteed.
            AddColumnIfMissing(connection, "CourseInstructors", "DisplayTitle", "TEXT NULL");

            Execute(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Courses_UniversityId_Code\" " +
                "ON \"Courses\" (\"UniversityId\", \"Code\")");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_Events_UniversityId\" ON \"Events\" (\"UniversityId\")");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_Announcements_UniversityId\" ON \"Announcements\" (\"UniversityId\")");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_Announcements_CourseId\" ON \"Announcements\" (\"CourseId\")");
            Execute(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_CourseInstructors_CourseId_DoctorUserId_Term\" " +
                "ON \"CourseInstructors\" (\"CourseId\", \"DoctorUserId\", \"Term\")");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_CourseInstructors_DoctorUserId\" " +
                "ON \"CourseInstructors\" (\"DoctorUserId\")");

            // Phase 2: research papers uploaded by doctors. Fresh databases
            // get this table from EnsureCreated; existing installations get it
            // here. PdfPath stores only the generated file name (no paths).
            Execute(connection,
                "CREATE TABLE IF NOT EXISTS \"DoctorResearch\" (" +
                "\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_DoctorResearch\" PRIMARY KEY AUTOINCREMENT, " +
                "\"DoctorUserId\" INTEGER NOT NULL, " +
                "\"UniversityId\" INTEGER NULL, " +
                "\"Title\" TEXT NOT NULL, " +
                "\"Abstract\" TEXT NOT NULL, " +
                "\"Authors\" TEXT NOT NULL, " +
                "\"Keywords\" TEXT NOT NULL, " +
                "\"Year\" INTEGER NOT NULL, " +
                "\"Field\" TEXT NOT NULL, " +
                "\"PdfPath\" TEXT NOT NULL, " +
                "\"DoiOrUrl\" TEXT NULL, " +
                "\"CreatedAtUtc\" TEXT NOT NULL, " +
                "\"UpdatedAtUtc\" TEXT NOT NULL, " +
                "\"IsPublished\" INTEGER NOT NULL)");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_DoctorResearch_UniversityId\" " +
                "ON \"DoctorResearch\" (\"UniversityId\")");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_DoctorResearch_Year\" " +
                "ON \"DoctorResearch\" (\"Year\")");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_DoctorResearch_Title\" " +
                "ON \"DoctorResearch\" (\"Title\")");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_DoctorResearch_DoctorUserId\" " +
                "ON \"DoctorResearch\" (\"DoctorUserId\")");
            Execute(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_DoctorResearch_IsPublished\" " +
                "ON \"DoctorResearch\" (\"IsPublished\")");

            logger?.LogInformation("Database schema is up to date");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Schema upgrade failed; restore unisphere-backup.db if needed");
            throw;
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
    }

    private static void AddColumnIfMissing(
        System.Data.Common.DbConnection connection,
        string table,
        string column,
        string definition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        var exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();

        if (!exists)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
            alter.ExecuteNonQuery();
        }
    }

    private static void Execute(System.Data.Common.DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
