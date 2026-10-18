using Microsoft.EntityFrameworkCore;

namespace UniSphere.Api.Data;

/// <summary>
/// In-place schema evolution for SQLite databases created with
/// <c>Database.EnsureCreated()</c> (this project does not use EF migrations).
/// <see cref="Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.EnsureCreated"/>
/// only builds the schema for a brand-new database; an already-existing
/// unisphere-v6.db must be upgraded additively so existing development data
/// and seed rows are preserved (never dropped/recreated).
///
/// Every step is idempotent and safe to run on every startup:
/// tables use CREATE TABLE IF NOT EXISTS and columns are only added when the
/// PRAGMA table_info probe shows them missing.
/// </summary>
public static class DbSchemaUpgrade
{
    public static void Apply(AppDbContext db)
    {
        // CourseAssignments (doctor <-> course join) — new table.
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "CourseAssignments" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CourseAssignments" PRIMARY KEY AUTOINCREMENT,
                "DoctorUserId" INTEGER NOT NULL,
                "CourseId" INTEGER NOT NULL,
                "AssignedByUserId" INTEGER NULL,
                "AssignedAtUtc" TEXT NOT NULL,
                CONSTRAINT "FK_CourseAssignments_Courses_CourseId"
                    FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_CourseAssignments_DoctorUserId" ON "CourseAssignments" ("DoctorUserId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_CourseAssignments_CourseId" ON "CourseAssignments" ("CourseId");""");
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_CourseAssignments_DoctorUserId_CourseId" ON "CourseAssignments" ("DoctorUserId", "CourseId");""");

        AddColumnIfMissing(db, "CourseMaterials", "DeadlineUtc", "TEXT NULL");
        AddColumnIfMissing(db, "Announcements", "CourseId", "INTEGER NULL");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Announcements_CourseId" ON "Announcements" ("CourseId");""");
    }

    private static void AddColumnIfMissing(AppDbContext db, string table, string column, string columnType)
    {
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var exists = false;
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            cmd.Connection.Open();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            // ALTER TABLE ... ADD COLUMN on SQLite has no IF NOT EXISTS variant;
            // the PRAGMA probe above makes this safe to run repeatedly.
            // table/column names are internal constants, never user input.
            var sql = "ALTER TABLE \"" + table + "\" ADD COLUMN \"" + column + "\" " + columnType + ";";
#pragma warning disable EF1002 // identifier-only SQL built from internal constants
            db.Database.ExecuteSqlRaw(sql);
#pragma warning restore EF1002
        }
    }
}
