# UniSphere 360+

A full-stack university student platform with a Blazor Server web dashboard and ASP.NET Core Web API backend.

## Technology Stack

| Layer | Technology |
|---|---|
| **Backend** | ASP.NET Core 10 (C#) REST API |
| **Frontend** | Blazor Server (interactive) |
| **Database** | SQLite via EF Core |
| **Authentication** | JWT (HMAC-SHA256) |
| **Validation** | FluentValidation |
| **Testing** | xUnit + WebApplicationFactory |
| **AI** | Provider registry (OpenAI/Anthropic/Gemini/OpenRouter/Ollama) + lexical RAG |

## Project Structure

```
UniSphere.Api/
├── Program.cs                    -- Entry point, DI, JWT, CORS, Blazor, middleware
├── UniSphere.Api.csproj          -- Project file
├── appsettings.json              -- Configuration
├── Components/                   -- Blazor components
│   ├── App.razor                 -- Root component with routing
│   └── MainLayout.razor           -- Layout with sidebar + topbar
├── Controllers/                  -- API controllers (14)
│   ├── AuthController.cs         -- register, login, me, change-password
│   ├── UsersController.cs        -- profile CRUD, role-based lookup
│   ├── HomeController.cs         -- Home screen aggregate
│   ├── EventsController.cs       -- CRUD (Admin-only write)
│   ├── NotificationsController.cs -- list, mark-read
│   ├── SearchController.cs       -- courses/events/modules search
│   ├── ModulesController.cs      -- quick-access tiles
│   ├── AcademicController.cs     -- dashboard, stats, performance, insights
│   ├── StudyPlannerController.cs -- CRUD + toggle
│   ├── ActivityController.cs     -- recent activity feed
│   ├── CoursesController.cs      -- list, get, recommended
│   ├── EnrollmentsController.cs  -- enroll, drop, grade management
│   ├── AdminController.cs        -- analytics, audit logs, notifications
│   ├── CareerController.cs       -- career recommendations, skill gaps
│   ├── ResearchController.cs     -- paper search, summarization
│   ├── CourseAssignmentsController.cs -- NEW: doctor↔course assignment (Admin-only)
│   ├── DoctorController.cs       -- NEW: dashboard, courses, materials, announcements, grades
│   └── DoctorAiController.cs     -- NEW: AI teaching assistant (quiz/summarize/ask)
├── Data/
│   ├── AppDbContext.cs           -- EF Core DbContext (18 DbSets, indexes)
│   └── DbSeeder.cs              -- Seeds roles, users, courses, doctor assignments
├── Middleware/
│   ├── ExceptionHandlingMiddleware.cs -- Global exception handler (RFC 7807)
│   └── AuditLogMiddleware.cs    -- Request/audit logging
├── Models/
│   ├── Entities.cs               -- User, Course, Enrollment, etc.
│   ├── CourseAssignment.cs       -- NEW: doctor↔course join entity
│   ├── AuditLog.cs              -- Audit trail
│   ├── Announcement.cs          -- Announcements (course-scoped support)
│   ├── CareerRecommendation.cs  -- Career recommendations
│   └── ResearchPaper.cs         -- Research papers
├── Pages/                        -- Blazor Server pages
│   ├── Dashboard.razor           -- Main dashboard
│   ├── Login.razor              -- Login page
│   ├── AcademicAdvisor.razor    -- Academic Advisor page
│   ├── CareerAssistant.razor    -- Career Assistant page
│   ├── ResearchHub.razor        -- Research Hub page
│   ├── StudentServices.razor    -- Student Services page
│   ├── Analytics.razor          -- Analytics & Admin page
│   ├── StudyPlannerPage.razor   -- Study Planner page
│   ├── Courses.razor            -- Courses list + detail
│   ├── Enrollments.razor        -- My Enrollments page
│   ├── Profile.razor            -- My Profile page
│   ├── Notifications.razor     -- Notifications page
│   ├── Settings.razor           -- Settings page
│   ├── Help.razor               -- Help & Support page
│   ├── Search.razor             -- Search results page
│   ├── DoctorDashboard.razor    -- NEW: Doctor dashboard overview
│   ├── DoctorCourses.razor      -- NEW: Doctor assigned courses list
│   ├── DoctorCourse.razor       -- NEW: Course detail (students/analytics/materials/announcements)
│   └── DoctorAiAssistant.razor  -- NEW: AI Teaching Assistant page
├── Services/
│   ├── AcademicService.cs       -- Academic Advisor business logic
│   ├── BlazorAuthService.cs     -- Blazor auth state provider
│   ├── ClaimsExtensions.cs      -- GetUserId() from JWT
│   ├── DoctorAccessService.cs   -- NEW: Doctor course-scoped authorization
│   ├── DoctorService.cs         -- NEW: Doctor dashboard/students/analytics/alerts
│   ├── DoctorAiService.cs       -- NEW: AI quiz/summarize/grounded Q&A
│   ├── DoctorAiTypes.cs         -- NEW: AI options + exceptions
│   ├── PasswordService.cs       -- PBKDF2 hashing
│   ├── Recommender.cs           -- Rule-based course recommender
│   └── TokenService.cs          -- JWT creation
├── Validators/
│   └── RequestValidators.cs      -- NEW: FluentValidation validators (incl. doctor module)
├── tests/                        -- Integration tests
│   └── UniSphere.Api.Tests/      -- 78 tests (all passing)
└── wwwroot/
    └── css/
        └── app.css              -- Complete design system
```

## Quick Start

### Prerequisites
- .NET 10 SDK: https://dotnet.microsoft.com/download

### Run the Application

```bash
dotnet restore
dotnet run
```

The application will:
1. Create and seed the SQLite database (`unisphere-v6.db`)
2. Start the web server on `http://localhost:5080`
3. Serve the Blazor Server dashboard at `http://localhost:5080/`
4. Serve Swagger UI at `http://localhost:5080/swagger`

### Run Tests

```bash
dotnet test tests/UniSphere.Api.Tests/UniSphere.Api.Tests.csproj
```

## Demo Accounts

| Role | Email | Password |
|---|---|---|
| Student | mazenmohamed@unisphere.edu | Password123! |
| Graduate | graduate@unisphere.edu | Graduate123! |
| Doctor | doctor@unisphere.edu | Doctor123! |
| Admin | admin@unisphere.edu | Admin123! |

## API Endpoints

### Authentication
| Method | Route | Description |
|---|---|---|
| POST | `/api/auth/register` | Create account (Student/Graduate/Doctor) |
| POST | `/api/auth/login` | Login, returns JWT |
| GET | `/api/auth/me` | Current user (requires token) |
| POST | `/api/auth/change-password` | Change password |
| GET | `/api/auth/roles` | List all roles |

### Academic
| Method | Route | Description |
|---|---|---|
| GET | `/api/academic/dashboard` | Full dashboard aggregate |
| GET | `/api/academic/stats` | GPA, attendance, progress |
| GET | `/api/academic/performance` | Grade table |
| GET | `/api/academic/insights` | AI insights (rule-based) |
| GET | `/api/study-planner` | Weekly study plan |
| POST | `/api/study-planner` | Add study item |
| POST | `/api/study-planner/{id}/toggle` | Toggle completion |
| DELETE | `/api/study-planner/{id}` | Delete study item |

### Courses & Enrollments
| Method | Route | Description |
|---|---|---|
| GET | `/api/courses` | List courses |
| GET | `/api/courses/{id}` | Course detail |
| GET | `/api/courses/recommended` | Recommended courses |
| GET | `/api/enrollments` | My enrollments |
| POST | `/api/enrollments` | Enroll in course |
| DELETE | `/api/enrollments/{id}` | Drop course |
| PUT | `/api/enrollments/{id}/grade` | Update grade (Admin/Doctor) |

### Doctor Module
| Method | Route | Description |
|---|---|---|
| GET | `/api/doctor/dashboard` | Doctor overview (courses, students, alerts, avg grade/attendance) |
| GET | `/api/doctor/courses` | Courses assigned to the calling doctor |
| GET | `/api/doctor/courses/{id}` | Course detail |
| GET | `/api/doctor/courses/{id}/students` | Enrolled students with grades/attendance |
| GET | `/api/doctor/courses/{id}/analytics` | Grade & attendance distributions + alerts |
| GET | `/api/doctor/courses/{id}/materials` | Materials & assignments list |
| POST | `/api/doctor/courses/{id}/materials` | Create material |
| PUT | `/api/doctor/materials/{id}` | Update material (own course only) |
| DELETE | `/api/doctor/materials/{id}` | Delete material (own course only) |
| POST | `/api/doctor/courses/{id}/materials/upload` | Upload lecture file (10 MB, whitelisted types) |
| GET | `/api/doctor/courses/{id}/announcements` | Announcements list |
| POST | `/api/doctor/courses/{id}/announcements` | Create announcement |
| PUT | `/api/doctor/announcements/{id}` | Update announcement (own course only) |
| DELETE | `/api/doctor/announcements/{id}` | Delete announcement (own course only) |
| GET | `/api/doctor/ai/status` | AI provider availability |
| POST | `/api/doctor/ai/quiz` | Generate quiz draft from material |
| POST | `/api/doctor/ai/summarize` | Summarize a lecture material |
| POST | `/api/doctor/ai/ask` | Ask a grounded question about a course |

### Admin: Course Assignments
| Method | Route | Description |
|---|---|---|
| GET | `/api/course-assignments` | List doctor↔course assignments |
| POST | `/api/course-assignments` | Assign a doctor to a course |
| DELETE | `/api/course-assignments/{id}` | Remove an assignment |

### Career & Research
| Method | Route | Description |
|---|---|---|
| GET | `/api/career/recommendations` | Career path recommendations |
| GET | `/api/career/skill-gaps` | Skill gap analysis |
| GET | `/api/research/papers` | Search papers |
| GET | `/api/research/trending` | Trending papers |
| GET | `/api/research/summarize/{id}` | Paper summary |
| POST | `/api/research/papers` | Add paper (Admin/Doctor) |

### Admin
| Method | Route | Description |
|---|---|---|
| GET | `/api/admin/analytics` | Platform analytics |
| GET | `/api/admin/audit-logs` | Audit log viewer |
| POST | `/api/admin/notifications` | Send notification |
| POST | `/api/admin/announcements` | Create announcement |
| GET | `/api/admin/announcements` | List announcements |
| GET | `/api/admin/users` | List all users |

### Other
| Method | Route | Description |
|---|---|---|
| GET | `/api/home` | Home screen aggregate |
| GET | `/api/events` | Events list |
| GET | `/api/notifications` | User notifications |
| GET | `/api/search?q=` | Search courses/events/modules |
| GET | `/api/activity` | Recent activity |
| GET | `/api/users/{id}` | User profile |
| PUT | `/api/users/{id}` | Update profile |

## Security Features

- **JWT Authentication** with HMAC-SHA256 signing
- **PBKDF2 Password Hashing** (100,000 iterations, SHA256)
- **Role-Based Authorization** (Student, Graduate, Doctor, Admin)
- **Global Exception Handling** (RFC 7807 JSON responses)
- **Audit Logging** (every authenticated API call logged)
- **FluentValidation** on all request DTOs
- **Course-scoped Doctor authorization** — every doctor course action re-verifies the doctor is assigned to that course (doctors can never read/modify another doctor's course or university-wide data)
- **CORS** restricted to known origins
- **Environment-based JWT key** (set `UNISPHERE_JWT_KEY` env var)
- **Safe file uploads** — server-generated GUID filenames, extension whitelist, 10 MB cap, relative URL only
- **Honest AI** — unavailable provider returns 503 (never a fabricated success); malformed AI output returns 502

## Database

- **SQLite** database (`unisphere-v6.db`)
- **18 tables**: Users, Roles, Modules, Events, Notifications, Courses, Enrollments, SemesterRecords, StudyPlanItems, Activities, AuditLogs, Announcements, CourseAssignments, CareerRecommendations, ResearchPapers, CourseMaterials, ...
- **Indexes** on all foreign keys and frequently queried columns
- **Unique constraints** on Users.Email and Enrollments(UserId, CourseId) and CourseAssignments(DoctorUserId, CourseId)
- **Seeded** with demo data on first run; additive SQLite schema upgrade runs before seeding

## Frontend Features

- **Responsive dashboard** with sidebar navigation
- **Academic summary cards** (GPA, courses, attendance, etc.)
- **Academic performance table** with status badges
- **Weekly study planner** with toggle completion
- **Course recommendations** with enrollment
- **Quick actions** panel
- **Recent activity** feed
- **AI insights** (rule-based analysis)
- **Career guidance** with skill gap analysis
- **Research hub** with paper search and summarization
- **Admin analytics** dashboard
- **Doctor module** — dashboard, assigned courses, student grades & attendance, course analytics, materials & assignments, announcements, AI teaching assistant
- **Profile management**
- **Notifications** with mark-as-read
- **Settings** with password change
- **Search** across all content

## Configuration

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `UNISPHERE_JWT_KEY` | JWT signing key (min 32 chars) | Production only |

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=unisphere-v6.db"
  },
  "Jwt": {
    "Key": "CHANGE-ME-dev-only-secret-key-at-least-32-chars!",
    "Issuer": "UniSphere.Api",
    "Audience": "UniSphere.App",
    "ExpiryMinutes": 1440
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5080", "http://10.0.2.2:5080"]
  }
}
```

## Connecting the Flutter App

- Android emulator: `http://10.0.2.2:5080`
- iOS simulator / Flutter web / desktop: `http://localhost:5080`
- Real phone: `http://<your-PC-LAN-IP>:5080` (same Wi-Fi, allow port 5080 in the firewall)

## Switching to PostgreSQL Later

Replace the `Microsoft.EntityFrameworkCore.Sqlite` package with `Npgsql.EntityFrameworkCore.PostgreSQL`, change `UseSqlite` to `UseNpgsql` in `Program.cs`, and update the connection string.

## License

This is a graduation project for educational purposes.
