using Microsoft.EntityFrameworkCore;

namespace UniSphere.Api.Data;

public static class DbSchemaUpgrade
{
    public static void Apply(AppDbContext db)
    {
        // CourseAssignments (doctor <-> course join) - new table.
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
        AddColumnIfMissing(db, "CourseMaterials", "StoredFileName", "TEXT NULL");
        AddColumnIfMissing(db, "CourseMaterials", "OriginalFileName", "TEXT NULL");
        AddColumnIfMissing(db, "CourseMaterials", "FileSizeBytes", "INTEGER NULL");
        AddColumnIfMissing(db, "CourseMaterials", "FileContentType", "TEXT NULL");
        AddColumnIfMissing(db, "Announcements", "CourseId", "INTEGER NULL");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Announcements_CourseId" ON "Announcements" ("CourseId");""");

        AddColumnIfMissing(db, "Users", "Address", "TEXT NULL");
        AddColumnIfMissing(db, "CourseAssignments", "DisplayTitle", "TEXT NULL");

        db.Database.ExecuteSqlRaw("""
            INSERT INTO "CourseAssignments" ("DoctorUserId", "CourseId", "AssignedByUserId", "AssignedAtUtc", "DisplayTitle")
            SELECT ci."DoctorUserId", ci."CourseId", NULL, ci."CreatedAtUtc", ci."DisplayTitle"
            FROM "CourseInstructors" ci
            WHERE NOT EXISTS (
                SELECT 1 FROM "CourseAssignments" a
                WHERE a."DoctorUserId" = ci."DoctorUserId"
                  AND a."CourseId" = ci."CourseId");
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "DoctorResearches" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DoctorResearches" PRIMARY KEY AUTOINCREMENT,
                "DoctorUserId" INTEGER NOT NULL,
                "UniversityId" INTEGER NOT NULL,
                "Title" TEXT NOT NULL,
                "Abstract" TEXT NOT NULL,
                "Authors" TEXT NOT NULL,
                "Keywords" TEXT NOT NULL,
                "Year" INTEGER NOT NULL,
                "Field" TEXT NOT NULL,
                "PdfPath" TEXT NULL,
                "DoiOrUrl" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsPublished" INTEGER NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_DoctorResearches_DoctorUserId" ON "DoctorResearches" ("DoctorUserId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_DoctorResearches_UniversityId" ON "DoctorResearches" ("UniversityId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_DoctorResearches_Year" ON "DoctorResearches" ("Year");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_DoctorResearches_Title" ON "DoctorResearches" ("Title");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_DoctorResearches_IsPublished" ON "DoctorResearches" ("IsPublished");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Exams" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Exams" PRIMARY KEY AUTOINCREMENT,
                "DoctorUserId" INTEGER NOT NULL,
                "CourseId" INTEGER NOT NULL,
                "Title" TEXT NOT NULL,
                "QuestionCount" INTEGER NOT NULL,
                "QuestionTypes" TEXT NOT NULL,
                "Difficulty" TEXT NOT NULL,
                "Language" TEXT NOT NULL,
                "MaterialsUsed" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "FK_Exams_Courses_CourseId" FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Exams_DoctorUserId" ON "Exams" ("DoctorUserId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Exams_CourseId" ON "Exams" ("CourseId");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ExamQuestions" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ExamQuestions" PRIMARY KEY AUTOINCREMENT,
                "ExamId" INTEGER NOT NULL,
                "QuestionText" TEXT NOT NULL,
                "QuestionType" TEXT NOT NULL,
                "OptionsJson" TEXT NULL,
                "CorrectAnswer" TEXT NOT NULL,
                "Explanation" TEXT NULL,
                "SourceMaterial" TEXT NULL,
                "OrderIndex" INTEGER NOT NULL,
                CONSTRAINT "FK_ExamQuestions_Exams_ExamId" FOREIGN KEY ("ExamId") REFERENCES "Exams" ("Id") ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ExamQuestions_ExamId" ON "ExamQuestions" ("ExamId");""");

        AddColumnIfMissing(db, "Exams", "Status", "TEXT NOT NULL DEFAULT 'Draft'");
        AddColumnIfMissing(db, "Exams", "PublishedAtUtc", "TEXT NULL");
        AddColumnIfMissing(db, "Exams", "Marks", "INTEGER NULL");
        AddColumnIfMissing(db, "ExamQuestions", "Marks", "INTEGER NULL");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "UserNotificationSettings" (
                "UserId" INTEGER NOT NULL CONSTRAINT "PK_UserNotificationSettings" PRIMARY KEY,
                "Preference" TEXT NOT NULL DEFAULT 'On',
                "MutedUntilUtc" TEXT NULL,
                CONSTRAINT "FK_UserNotificationSettings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
            );
            """);

        AddColumnIfMissing(db, "Exams", "OpensAtUtc", "TEXT NULL");
        AddColumnIfMissing(db, "Exams", "ClosesAtUtc", "TEXT NULL");
        AddColumnIfMissing(db, "Exams", "DurationMinutes", "INTEGER NULL");
        AddColumnIfMissing(db, "Exams", "ShowAnswersAfterSubmit", "INTEGER NOT NULL DEFAULT 0");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ExamAttempts" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ExamAttempts" PRIMARY KEY AUTOINCREMENT,
                "ExamId" INTEGER NOT NULL,
                "StudentUserId" INTEGER NOT NULL,
                "StartedAtUtc" TEXT NOT NULL,
                "SubmittedAtUtc" TEXT NULL,
                "Score" INTEGER NULL,
                "MaxScore" INTEGER NULL,
                CONSTRAINT "FK_ExamAttempts_Exams_ExamId"
                    FOREIGN KEY ("ExamId") REFERENCES "Exams" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ExamAttempts_Users_StudentUserId"
                    FOREIGN KEY ("StudentUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExamAttempts_ExamId_StudentUserId" ON "ExamAttempts" ("ExamId", "StudentUserId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ExamAttempts_StudentUserId" ON "ExamAttempts" ("StudentUserId");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "ExamAnswers" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ExamAnswers" PRIMARY KEY AUTOINCREMENT,
                "AttemptId" INTEGER NOT NULL,
                "QuestionId" INTEGER NOT NULL,
                "SelectedOption" INTEGER NULL,
                "IsCorrect" INTEGER NOT NULL,
                CONSTRAINT "FK_ExamAnswers_ExamAttempts_AttemptId"
                    FOREIGN KEY ("AttemptId") REFERENCES "ExamAttempts" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ExamAnswers_ExamQuestions_QuestionId"
                    FOREIGN KEY ("QuestionId") REFERENCES "ExamQuestions" ("Id") ON DELETE CASCADE
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExamAnswers_AttemptId_QuestionId" ON "ExamAnswers" ("AttemptId", "QuestionId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_ExamAnswers_QuestionId" ON "ExamAnswers" ("QuestionId");""");
        AddColumnIfMissing(db, "ExamAnswers", "TextAnswer", "TEXT NULL");
        AddColumnIfMissing(db, "ExamAnswers", "GradedAtUtc", "TEXT NULL");
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
            var sql = "ALTER TABLE \"" + table + "\" ADD COLUMN \"" + column + "\" " + columnType + ";";
#pragma warning disable EF1002 // identifier-only SQL built from internal constants
            db.Database.ExecuteSqlRaw(sql);
#pragma warning restore EF1002
        }
    }
}
