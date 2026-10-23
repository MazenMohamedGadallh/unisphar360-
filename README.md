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

## Project Structure

```
UniSphere.Api/
├── Program.cs                    -- Entry point, DI, JWT, CORS, Blazor, middleware
├── UniSphere.Api.csproj          -- Project file
├── appsettings.json              -- Configuration
├── Components/                   -- Blazor components
│   ├── App.razor                 -- Root component with routing
│   └── MainLayout.razor           -- Layout with sidebar + topbar
├── Controllers/                  -- API controllers (11)
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
│   ├── EnrollmentsController.cs  -- NEW: enroll, drop, grade management
│   ├── AdminController.cs        -- NEW: analytics, audit logs, notifications
│   ├── CareerController.cs       -- NEW: career recommendations, skill gaps
│   └── ResearchController.cs     -- NEW: paper search, summarization
├── Data/
│   ├── AppDbContext.cs           -- EF Core DbContext (14 DbSets, indexes)
│   └── DbSeeder.cs              -- Seeds roles, users, courses, etc.
├── Middleware/
│   ├── ExceptionHandlingMiddleware.cs -- Global exception handler (RFC 7807)
│   └── AuditLogMiddleware.cs    -- Request/audit logging
├── Models/
│   ├── Entities.cs               -- User, Course, Enrollment, etc.
│   ├── AuditLog.cs              -- NEW: Audit trail
│   ├── Announcement.cs          -- NEW: Announcements
│   ├── CareerRecommendation.cs  -- NEW: Career recommendations
│   └── ResearchPaper.cs         -- NEW: Research papers
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
│   └── Search.razor             -- Search results page
├── Services/
│   ├── AcademicService.cs       -- Academic Advisor business logic
│   ├── BlazorAuthService.cs     -- NEW: Blazor auth state provider
│   ├── ClaimsExtensions.cs      -- GetUserId() from JWT
│   ├── PasswordService.cs       -- PBKDF2 hashing
│   ├── Recommender.cs           -- Rule-based course recommender
│   └── TokenService.cs          -- JWT creation
├── Validators/
│   └── RequestValidators.cs      -- NEW: FluentValidation validators
├── tests/                        -- Integration tests
│   └── UniSphere.Api.Tests/      -- 44 tests (all passing)
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

The Graduate account ships with a career-starter bundle: profile + skills, 12 labeled demo jobs, 2 applications, 1 saved job, roadmap steps, settings. Without AI keys configured, AI features fail with an explicit 503 (never fake output); the curated interview bank and deterministic matching work offline.

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
| GET | `/api/academic/quick-actions` | Shortcuts to Academic screens |
| GET | `/api/study-planner` | Weekly study plan |
| POST | `/api/study-planner` | Add study item |
| POST | `/api/study-planner/{id}/toggle` | Toggle completion |
| DELETE | `/api/study-planner/{id}` | Delete study item |

### Graduation
| Method | Route | Description |
|---|---|---|
| GET | `/api/graduation/overview` | Eligibility, checklist, ceremony, alumni conferral block |
| POST | `/api/graduation/apply` | Submit graduation application (Student, 422 with reasons if the audit fails) |
| GET | `/api/graduation/application` | My current graduation application |
| POST | `/api/graduation/application/withdraw` | Withdraw a pending / in-review application |
| GET | `/api/graduation/ceremony` | Next upcoming graduation ceremony |
| GET | `/api/graduation/certificate` | Digital certificate with Latin honors (after conferral) |
| GET | `/api/graduation/applications` | Admin: paged list of applications, `?status=` filter |
| POST | `/api/graduation/applications/{id}/review` | Admin: approve (confers the degree) or reject |

### Graduate Career Platform (role: Graduate, `api/grad/*`)
| Method | Route | Description |
|---|---|---|
| GET/PUT | `/api/grad/profile` | Career profile fields (headline, summary, links, preferences, goal) |
| GET | `/api/grad/profile/completion` | Weighted profile-completion score + missing fields |
| POST/DELETE | `/api/grad/profile/skills` , `/skills/{id}` | Add (case-insensitive dedupe) / remove skills |
| POST/PUT/DELETE | `/api/grad/profile/entries` , `/entries/{id}` | Experience / project / certification / education entries |
| POST/GET/DELETE | `/api/grad/profile/cv` , `/cv/{id}` | CV upload (.pdf/.docx/.txt, max 10 MB) with extraction, list, delete |
| PUT | `/api/grad/profile/cv/{id}/manual-text` | Manual CV text (fallback when extraction fails) |
| POST/GET | `/api/grad/profile/cv/{id}/analyze` , `/cv/{id}/analyses` | REAL AI CV analysis (503 with reason when no provider configured) + history |
| GET/PUT | `/api/grad/settings` | Career notifications + match threshold + GPA visibility |
| GET | `/api/grad/overview` | Career Overview aggregate (completion, matches, apps, roadmap, events, activity) |
| GET | `/api/grad/alumni` | Alumni & university services (career events, announcements, tips, contacts) |
| GET | `/api/grad/insights` | Job-catalog insights (top skills, industries, work types) |
| GET | `/api/grad/jobs` (`.?search&location&remote&sort&page`) | Search the catalog (labeled demo jobs + synced live rows) |
| GET | `/api/grad/jobs/{id}` , `/saved` , `/sync` | Job detail, saved list, live Remotive sync (5-min throttle) |
| POST/DELETE | `/api/grad/jobs/{id}/save` , `/save` | Save / unsave a job (409 on duplicate save) |
| GET | `/api/grad/jobs/matches?limit&minScore` | Deterministic match ranking (documented weights, no AI) |
| GET | `/api/grad/jobs/skill-gap?role=` | Target-role skill coverage + missing skills + steps |
| GET/PUT/DELETE | `/api/grad/applications` (…, `/{id}`) | Applications tracker CRUD + `/summary` stats |
| GET/POST/PUT/DELETE | `/api/grad/roadmap` , `/progress`, `/apply-suggestions` | Career roadmap CRUD + progress + AI suggestions (`/suggest` requires provider) |
| GET/POST/DELETE | `/api/grad/interview/sessions` , `/{id}` | Interview practice: AI-generated (503 without provider) or curated bank |
| POST | `/api/grad/interview/sessions/{sid}/questions/{qid}/answer` | Submit answer → REAL AI feedback (503 without provider) |

### Courses & Enrollments
| Method | Route | Description ||---|---|---|
| GET | `/api/courses` | List courses |
| GET | `/api/courses/{id}` | Course detail |
| GET | `/api/courses/recommended` | Recommended courses |
| GET | `/api/enrollments` | My enrollments |
| POST | `/api/enrollments` | Enroll in course |
| DELETE | `/api/enrollments/{id}` | Drop course |
| PUT | `/api/enrollments/{id}/grade` | Update grade (Admin/Doctor) |

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
- **CORS** restricted to known origins
- **Environment-based JWT key** (set `UNISPHERE_JWT_KEY` env var)

## Database

- **SQLite** database (`unisphere-v6.db`)
- **14 tables**: Users, Roles, Modules, Events, Notifications, Courses, Enrollments, SemesterRecords, StudyPlanItems, Activities, AuditLogs, Announcements, CareerRecommendations, ResearchPapers
- **Indexes** on all foreign keys and frequently queried columns
- **Unique constraints** on Users.Email and Enrollments(UserId, CourseId)
- **Seeded** with demo data on first run

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
