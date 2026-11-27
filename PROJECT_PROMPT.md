# Prompt: UniSphere 360+ — how the whole project works

You are going to work on **UniSphere 360+**, a university platform for four roles: **Student, Graduate, Doctor (instructor), Admin**. Below is how the project currently behaves. Treat it as your context. Visual design, colors and styling are deliberately left out. The code is the source of truth; `README.md` is outdated (page list, controller list, test count).

---

## 1. Architecture

| Part | Technology | Port |
|---|---|---|
| Backend REST API + original web UI | ASP.NET Core (.NET 10), Blazor Server (interactive) in the same process | 5080 |
| New web UI (partial migration) | React 19, React Router 7, TypeScript, Vite | 5173 |
| RAG service | Python FastAPI (`rag-chatbot/api.py`) | 8000 |
| Database | SQLite via EF Core (`unisphere-v6.db`) | — |

- Both UIs call the same REST API. Blazor pages call it server-side through an `HttpClient` hard-coded to `http://127.0.0.1:5080`.
- Layout: `Controllers/`, `Services/`, `Models/`, `Dtos/`, `Validators/` (FluentValidation), `Middleware/`, `Data/` (DbContext, seeders, importers, CSV/JSON data), `Pages/` + `Components/` (Blazor), `frontend/` (React), `rag-chatbot/`, `tests/UniSphere.Api.Tests/` (xUnit + WebApplicationFactory).

### Running
- `dotnet run` starts the API and Blazor UI. Swagger is at `/swagger` (enabled in every environment). `/health` is anonymous.
- `start-dev.ps1` starts the API and Vite, waits for both ports, and stops both on Ctrl+C. It also repairs npm `.cmd` shims for paths containing `&`.
- `start-all.ps1` starts the Python service first, then calls `start-dev.ps1`. **It currently expects `rag-chatbot\rag-chatbot\.venv-new`, but that folder is empty**; the existing venv is `rag-chatbot\.venv`, so the script exits at its pre-flight check.
- Manual RAG start: `python -m uvicorn api:app --host 127.0.0.1 --port 8000` inside `rag-chatbot/`.
- Tailwind CSS is generated at build only if `tools\tailwind\tailwindcss.exe` exists; otherwise the committed CSS is used.

---

## 2. Startup and configuration (`Program.cs`)

Order at startup:
1. Load `.env` (searched upward up to 6 levels; never overrides existing env vars; skipped when `UNISPHERE_SKIP_DOTENV=1`).
2. Load `secret.json` if present (only `*_API_KEY`, `*_MODEL`, `*_BASE_URL`, `*_JWT_KEY`), then `appsettings.Local.json`.
3. Register services, JWT, CORS, rate limiters, validators.
4. If run with `--import-people [--dry-run]`: import students/doctors from CSV files, print a report, exit.
5. Log which AI providers are enabled and why others are hidden.
6. Database: `EnsureCreated` → `SchemaUpgrade` → `DbSchemaUpgrade` → `DbSchemaPatcher` → `DbSeeder.Seed` → `CatalogImporter` → `DbSeeder.EnsurePostCatalog` → `CourseNotesSeeder`.

**There are no EF migrations.** Schema changes are raw idempotent SQL (`CREATE TABLE IF NOT EXISTS`, add-column-if-missing) in those three upgrade classes. A new column or table needs both the model and a patch.

Data loading:
- `CatalogImporter` reads `Data/courses-*.csv` (one university per file) and inserts or updates courses.
- `CourseNotesSeeder` reads JSON in `Data/course-notes/` and `Data/course-materials/` and adds them as course materials, matched by course title, without duplicates.
- `PeopleImporter` reads `students-*.csv` / `doctors-*.csv`, creates or updates users, enrollments and instructor links. Initial password comes from `Import:InitialPassword`, otherwise a built-in default.
- `DbSeeder` creates demo accounts for all four roles (credentials are in the seeder, and the login screen offers them as one-click fill), plus demo courses, events, modules and a graduate career bundle (profile, skills, demo jobs, applications, roadmap).

Request pipeline: static files → no-cache headers on non-API paths → authentication → authorization → rate limiter → antiforgery → exception handler (generic 500 problem+json with traceId) → audit log.

**Audit log:** every authenticated request is stored (user, method, path, status, duration, IP, user agent), written after the response is sent. Chat streaming responses are not buffered.

CORS allows only localhost:5080, 5173, 3000 and `10.0.2.2:5080`, and exposes `X-Conversation-Id`.

---

## 3. Authentication, authorization, isolation

**Register** (`POST /api/auth/register`): Student, Doctor or Graduate only (Admin cannot be registered). Minimum age: Student 18, Graduate 22, Doctor 25; maximum 100. Password needs 8+ characters with upper, lower, digit and symbol. Phone is validated. Role-specific fields (department, academic year, specialization, graduated-from, graduation year) are stored on the single `User` table. Students and graduates get an empty current `SemesterRecord`; everyone gets a welcome notification and activity. Returns a token, so registration logs the user in.

**Login:** after 5 failed attempts per email, locked for 15 minutes (in memory; keyed by email because Blazor logs in from 127.0.0.1). Deactivated accounts get 403, deleted accounts get the generic 401. An optional `roleId` must match.

**Passwords:** PBKDF2-SHA256, 100,000 iterations, 16-byte salt, constant-time compare. Change password requires the current one and 8+ characters.

**JWT:** HS256, claims = user id, email, name, role, roleId; 60-minute expiry from config; no refresh token. Key comes from `UNISPHERE_JWT_KEY` or `Jwt:Key`, must be 32+ characters, and the `CHANGE-ME` placeholder is refused outside Development. Token validation does not re-check the database, so a deactivated user's token stays valid until it expires (university-scoped queries do return nothing for them). An unauthenticated GET to a non-API path redirects to `/login?returnUrl=…`.

**Client sessions**
- Blazor: `BlazorAuthService` is the `AuthenticationStateProvider`; token in `ProtectedLocalStorage`; has a "loading" state until storage is read. `RoleGate` and `GradPageBase` redirect wrong roles to their home.
- React: token in `localStorage["unisphere.auth.token"]`; 401 sends the user to `/login?expired=1`; `returnUrl` is safety-checked.
- Home per role: Student `/dashboard`, Graduate `/graduate`, Doctor `/doctor`, Admin `/admin`.

**University isolation** (`UniversityScope`): non-admins see courses, events and announcements of their own university or with no university; materials and enrollments only of their own university. No university means nothing is visible. Admin sees everything.

**Course eligibility:** course department empty or equal to the student's, and course year empty or equal to the student's.

**Doctor access** (`DoctorAccessService`): assigned if linked in **either** `CourseAssignments` or the older `CourseInstructors` table, and in the same university.

**Rate limits:** chat 20/min per user, exam generation 10/min, research upload 20/min.

---

## 4. Data model (main entities)

- **Identity:** User (role enum: Student=1, Graduate=2, Doctor=3, Admin=4; `IsActive`, `IsDeleted`), RoleItem, University, LoginRecord, AuditLog.
- **Academic:** Course (code unique per university, track, credits, department, year, semester, prerequisites), CourseAssignment, CourseInstructor, Enrollment (unique user+course; semester, grade letter and points, attendance, status), SemesterRecord, StudyPlanItem, CourseMaterial (type Summary/Lecture/Assignment/Exam/Announcement; text content, optional file, deadline), GraduationApplication.
- **Exams:** Exam (Draft/Published, open/close dates, duration, marks, show-answers flag), ExamQuestion, ExamAttempt (unique exam+student), ExamAnswer, UserNotificationSetting.
- **Communication:** Notification (category, dedupe key), Announcement (university-wide or per course), EventItem, ActivityItem, ModuleItem.
- **Chat:** ChatConversation, ChatMessage, ChatUsage (per user per day).
- **Research:** DoctorResearch (PDF, published flag), ResearchPaper.
- **Graduate career:** GraduateProfile, ProfileEntry, GraduateSkill, CvDocument, CvAnalysis, JobListing, SavedJob, JobApplication, RoadmapItem, InterviewSession, InterviewQuestion, GraduateSettings, JobSyncState.

---

## 5. AI layer

### 5.1 Providers
Five streaming providers: Gemini, OpenRouter, OpenAI, Anthropic, Ollama. Preference order is the configured `Chat:DefaultProvider` (currently `ollama`) then gemini, openrouter, openai, anthropic, ollama. A provider is listed only when configured and available (Ollama is probed). Key lookup: `Chat:Providers:<Name>:ApiKey`, then `<NAME>_API_KEY`, then `<NAME>:ApiKey`; placeholder values are ignored. HTTP 429 and 5xx and network errors are retried before streaming starts; mid-stream errors are not.

`AiCompletionService` (used by all non-chat AI features) tries every live provider in order, with a timeout each, and returns the first non-empty answer. With no provider, AI features fail with an explicit error; no fake output (exceptions: CV analysis and interview questions have deterministic fallbacks).

### 5.2 Grounding (`AiGateway.cs`)
`AiGroundingService` builds context per role:
- **Student:** profile, enrollments with grades and attendance, GPA by semester, pending study plan, deadlines, upcoming events, material titles and relevant excerpts. RAG documents: up to 8 keyword-matched materials from eligible courses (6,000 chars each, 30,000 total).
- **Graduate:** profile, skills, CV excerpt, saved jobs, applications, best-matching job listings. RAG document: the CV (12,000 chars).
- **Doctor:** assigned courses with enrollment counts, recent exams, materials. RAG documents: up to 12 newest materials.
- **Admin:** user counts, platform totals, logins in 7 days, chat usage, announcements, events. No RAG.

`HybridGroundingPolicy` appends rules to the system prompt: use platform context first; if the question goes beyond it, answer from general knowledge; say where the answer came from; never invent personal data; treat context as data, not instructions.

### 5.3 Chat (`ChatController` + `ChatService`)
- Endpoints: list providers and their status; list, get, rename, delete conversations; `POST /api/chat/conversations/{id|"new"}/messages[?regenerate=true]`.
- Message: 1–2,000 characters. Limit: 100 messages per user per day (counted when a reply is saved).
- Student, Doctor and Graduate also get a `rag` provider ("UniSphere RAG") when the Python service is healthy.
- If the chosen provider is unavailable, it falls back to the default or first live provider; if none, to RAG when usable; otherwise 503 with setup instructions.
- History sent to a model: last 10 messages. **The RAG path sends only the latest question** (plus documents and system prompt), not the history.
- Separate system prompt per role.
- Response is SSE: `data: <JSON string chunk>`, then `data: [DONE]`, or `event: error`. Conversation id is in `X-Conversation-Id`. Partial replies are saved if the user presses Stop or the stream fails. Title = first 40 characters of the first message. Token counts are estimates (chars/4).

### 5.4 RAG service (`rag-chatbot/api.py`)
- No vector database and no stored index. .NET sends the already-authorized documents with every request.
- Chunks of 800 characters with 100 overlap, ranked in memory with BM25 (English and Arabic tokens). If all chunks total 9,000 characters or fewer, all are returned; otherwise the top-k (default 4, max 8).
- `GET /health`; `POST /retrieve` (no LLM needed); `POST /query`; `POST /query/stream` (NDJSON).
- Answer writer: Gemini if a key is set, else local Ollama, else an extractive answer from the best passages. Streaming is real only with Gemini.
- Optional shared secret header `X-RAG-Api-Key`.
- .NET side: `RagClient` (falls back to `/query` if streaming returns 404), `RagChatProvider` (appends a "Sources:" list), `RagGroundingService` (12 s timeout, 30 s back-off after failure, silently skips).
- `rag-chatbot/main.py` is a **legacy** Chroma + Groq service with a different API contract; the app does not use it.

---

## 6. Student features

- **Dashboard:** GPA = average of graded enrollment points; attendance = average; completed = courses with points ≥ 2.0; deltas against the previous SemesterRecord. Also weekly study plan with toggle, rule-based insights, recommended courses, recent activity, materials, exams.
- **Course status rules:** grade points → letter A=4.0, A-=3.7, B+=3.3, B=3.0, B-=2.7, C+=2.3, C=2.0, C-=1.7, D+=1.3, D=1.0, F=0. Status: "Needs Attention" if attendance < 90 or points < 3.0; "Excellent" if points ≥ 3.7; else "Good".
- **Enrollments:** enroll in a visible course (no duplicates). **Students enter their own semester, grade and attendance** (`PUT /api/enrollments/{id}/my-record`). A graded course cannot be dropped. Doctors (assigned courses) and admins can also set grades.
- **My University:** courses with status Passed / InProgress / Available / Locked (prerequisites met = passed codes), materials with type and text filters and paging, upcoming items, secure file download via `/api/materials/{id}/file`.
- **Recommender:** majors containing AI, data, machine or computer engineering get the "AI" track, others "Software"; courses of that track first.
- **Performance analysis:** weighted score = grades 40% + attendance 25% + study-plan completion 20% + study hours 10% (target 14 h/week) + GPA trend 5%. Score < 45 AtRisk, < 70 NeedsAttention, else OnTrack. Also predicted GPA, confidence, strengths, weaknesses, up to 4 recommendations, per-course risk, GPA trend. Deterministic, no LLM.
- **Study plan generator:** splits 14 hours across current courses by need (weakness, class average difficulty, attendance gap, credits), blocks of 1–2 h, max 4 h/day over 6 days. "Apply" replaces only its own unfinished items. Manual items can be added, toggled and deleted. Despite the "AI study plan" label, it is rule-based.
- **Practice quiz** (`/api/student/ai`): LLM-generated MCQs from the student's course material.
- **Career assistant:** rule-based recommendations and skill gaps from major and courses.
- **Research Hub (student/graduate):** searches `ResearchPapers`; "summarize" returns the first 3 sentences of the abstract (no LLM); trending list.
- **Student services:** announcements (university-wide plus enrolled courses) and upcoming events.
- **Graduation:** eligibility needs GPA ≥ 2.0, all planned courses completed, no failed courses. Apply → Pending (one active application only), can withdraw. Admin approves or rejects with a note, which notifies the student. Approval sets `GraduationYear` and `GraduatedFrom` but **does not change the user's role to Graduate**. Certificate with honors: ≥3.9 Summa, ≥3.7 Magna, ≥3.5 Cum Laude, ≥3.0 Distinction, else Pass.

---

## 7. Doctor features

- **Dashboard, courses, students, course analytics, profile.** Only a limited set of profile fields is editable (identity fields are locked). A doctor can set a custom display title per course. Avatar: PNG/JPEG/WebP up to 5 MB, checked by magic bytes.
- **Materials:** CRUD with type, text content, deadline, optional file. Material files: PDF/Word/PowerPoint, 20 MB default (max 25), signature-checked, stored under a random name. A second upload endpoint accepts more types up to 10 MB. **Text is not extracted from uploaded files; AI features only read the typed `Content` field.**
- **Announcements** per course (university-wide ones are admin-managed). The announcements list for doctors checks only `CourseInstructors`, not `CourseAssignments`.
- **Exam Assistant** (the exam flow used by the UI):
  1. Pick a course and materials; choose 1–30 questions, difficulty (easy/medium/hard/mixed), language (en/ar) and question mix. Types: MCQ, TrueFalse, Essay, Definition.
  2. The LLM must answer strictly from the material and return JSON, parsed by `AiJsonExtractor`. RAG retrieval adds context when available, with a warning if it fails.
  3. Review and edit, optionally translate between Arabic and English.
  4. Save as Draft, then publish with a title and marks. Published exams cannot be edited or deleted. Open/close time, duration and show-answers are set **only** by `/api/doctor/ai/quiz/publish`, which no UI calls, so exams published from the UI have no time window and never reveal correct answers.
  5. Publishing notifies students enrolled with semester `"Current"`, respecting their notification setting (On / Off / MuteUntil), in the exam's language.
- **Student exam taking:** visible only if enrolled (`"Current"`) and same university. Start checks the opening time; submit checks the closing time. One attempt only. Correct answers are never sent while taking. MCQ/TrueFalse are auto-graded; written answers (max 5,000 chars) wait for the doctor to mark correct or incorrect, which recalculates the score. Score = number of correct questions (per-question marks are not used). **`DurationMinutes` is displayed but not enforced**, and start does not check the closing time.
- **Results:** takers with score and percentage, non-takers, pending written answers.
- **AI Assistant tools** (`/api/doctor/ai`): status, quiz generation, publish quiz (transactional, 2-minute duplicate guard, MCQ only), summarize lecture, ask about course material. Options: 4,096 max tokens, 90 s timeout, 12,000 context characters. The Blazor page uses status, summarize and ask.
- **Research Hub:** upload a PDF paper (max 20 MB, `%PDF-` check), edit, publish/unpublish, delete; browse and download published papers with filters. Two overlapping APIs exist: `/api/research-hub` and `/api/doctor/research`.
- `/api/doctor/exam-generator` is a second exam API that the UI does not use.

---

## 8. Graduate features (`/api/grad/*`)

- **Overview:** profile completion, next actions, roadmap progress, follow-ups, skill gaps.
- **Profile:** headline, summary, links, preferences, skills (unique per user), experience/project entries. Completion is a weighted checklist.
- **CV Studio:** PDF, DOCX or TXT up to 10 MB, structure-validated, stored in `uploads/cv/{userId}/` (outside the web root). Text is extracted by custom code (max 15,000 chars); the user can paste text manually if extraction fails. Analysis: section detection → LLM (RAG first, then providers) → final score is the average of the AI score and a deterministic section score. Without AI, deterministic result only.
- **Jobs:** search with filters and paging, save/unsave. Live jobs come from the public Remotive API, only when sync is requested, throttled to once per 5 minutes; demo jobs are seeded.
- **Matches:** score out of 100 = skills 40, title 20, industry 15, location 15, work mode 10, with explanation, matched and missing skills.
- **Skill gap:** compares skills with a target role in `CareerCatalog`.
- **Roadmap:** CRUD, progress, LLM suggestions that the user picks from (max 10 applied).
- **Applications:** tracker with statuses Applied / Interview / Offer / Rejected / Withdrawn, notes, follow-up dates, summary.
- **Interview coach:** 5 questions, AI-generated or from a curated bank; per-answer feedback with a 0–10 score.
- **Alumni, insights, settings:** match alerts and threshold, follow-up reminders, alert keywords and locations. Career notifications (matches, follow-ups, announcements) are generated on demand with dedupe keys. There is no background job scheduler.

---

## 9. Admin features (`/api/admin`, `/api/course-assignments`)

- Overview with at-risk counts, analytics (users, GPA ranges, enrollments), at-risk students, department stats, system health.
- Users: search with role and status filters, view, edit, activate/deactivate, change role, soft delete. You cannot change yourself, or deactivate, demote or delete the last active admin.
- Instructor assignments (same university required), doctors and courses lists.
- Send notifications to a user or a role; create university announcements; events CRUD (`/api/events`); review graduation applications; audit log.

---

## 10. Shared features

- Notifications: list, unread count, mark one or all read, delete.
- Global search (`/api/search`): role-aware groups such as courses, materials, study plan, notifications, pages, events, users; the top bar debounces and keeps recent searches in `localStorage`.
- Profile (`/api/users`): own profile and avatar (JPG/PNG/WebP, 5 MB); users can only modify themselves. Avatars are served as public static files.
- Browser Back guard on the Blazor home page.

---

## 11. The two front ends (migration status)

**React** (`frontend/src/App.tsx`) has: login, register, `/` role redirect, student dashboard, chat (`/chat`, `/doctor/ai`, `/graduate/assistant` — one shared component with provider picker, streaming, Stop, Regenerate, rename, delete), My University, performance analytics, student exams, doctor exam results.

**Every other React route** shows a "not migrated" page linking to the same path on the Blazor app (port 5080). That includes the home pages of Doctor, Graduate and Admin. Blazor and React store the token separately, so the user may need to log in again there.

**Blazor** (`Pages/`) has all features. Its menus (`Components/RoleNavMenu.razor`, `GradNav.razor`) differ from React's (`frontend/src/components/nav.ts`).

---

## 12. Tests and working rules

- `dotnet test tests/UniSphere.Api.Tests/UniSphere.Api.Tests.csproj`: integration tests with a temporary SQLite database, no real `.env`, AI keys cleared. React has no tests (`npm run build`, `npm run lint`).
- Put business logic in `Services/`, validation in `Validators/`.
- Every query on university data must apply `UniversityScope`; every doctor action must check course assignment.
- A schema change needs a patch in the `Data/` upgrade classes.
- A UI change may be needed in both React and Blazor.
- Never commit `.env`, `appsettings.Local.json` or `secret.json`.
