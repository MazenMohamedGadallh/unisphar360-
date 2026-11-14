# UniSphere 360+ — React frontend

A React + TypeScript (Vite) frontend for the existing **UniSphere.Api** ASP.NET Core backend.
It replaces the Blazor pages; the backend is used as-is (no backend changes are required).

## What you need

- **Node.js 20.19+ or 22+** (for Vite 7) — check with `node -v`
- The backend running on **http://localhost:5080** (`dotnet run` in the repo root)

## Run it

```bash
cd unisphere-web
npm install
npm run dev
```

Open **http://localhost:5173** and sign in with a demo account (the login page lists them):

| Role | Email | Password |
|---|---|---|
| Student | mazenmohamed@unisphere.edu | Password123! |
| Student (Helwan) | helwan.student@unisphere.edu | Password123! |
| Graduate | graduate@unisphere.edu | Graduate123! |
| Doctor | doctor@unisphere.edu | Doctor123! |
| Admin | admin@unisphere.edu | Admin123! |

During development Vite proxies `/api` and `/uploads` to `http://localhost:5080`, so the
browser only talks to one origin and no CORS setup is needed. To point the proxy elsewhere,
set `VITE_API_PROXY` (see `.env.example`).

## Production build

```bash
npm run build      # type-checks, then writes static files to dist/
npm run preview    # serves dist/ locally on :4173
```

When `dist/` is hosted on a different origin from the API, set `VITE_API_BASE_URL`
(e.g. `https://api.example.com`) **before** building, and add that frontend origin to
`Cors:AllowedOrigins` in the backend's `appsettings.json`. (Without a `Cors` section the
backend already allows `http://localhost:5173` and `http://localhost:3000`.)

## How it talks to the backend

- **Auth:** `POST /api/auth/login` returns a JWT, stored in `localStorage` and sent as
  `Authorization: Bearer …` on every request. `GET /api/auth/me` restores the session after a
  refresh. Any `401` signs the user out and returns to `/login`.
- **Roles:** each route is limited to the roles the backend allows (Student, Graduate, Doctor,
  Admin). The server stays the real authority — every `/api` endpoint has its own
  `[Authorize(Roles = …)]`.
- **Errors:** plain-string, ProblemDetails and `{ message | error }` responses are all turned
  into readable messages (`src/lib/api.ts`).
- **AI chat** streams Server-Sent Events from
  `POST /api/chat/conversations/{id|new}/messages` and supports Stop and Regenerate.
- **Downloads** (material attachments, research PDFs) are fetched with the token and saved
  as a Blob, because a plain link cannot carry the bearer token.
- **Types** in `src/types/dto.ts` were generated from the backend's C# records
  (`Dtos/*.cs` and request records in `Controllers/`), so field names match the API exactly.

## Project layout

```
src/
  lib/          api client, auth context, router, theme, hooks, formatting
  components/   Layout (sidebar + top bar), ui kit, Icon, ChatPanel, Markdown, EventList
  pages/
    auth/       Login, Register
    student/    Dashboard, University, Academic Advisor (+ Graduation), Study Planner,
                Courses, Enrollments, Career, Student Services, Analytics, Exams, Events
    graduate/   Overview, Career Profile, CV Studio, Jobs, Matches, Skill Gap, Roadmap,
                Applications, Interview Coach, Assistant, Alumni, Events, Insights, Settings
    doctor/     Dashboard, Courses, Course detail (students/analytics/materials/
                announcements), Students, Materials, Announcements, AI Assistant,
                Exam Assistant, Research Hub, Profile
    admin/      Admin Panel (overview, users, assignments, graduation review,
                notifications & events, at-risk, departments, audit log, system)
    shared/     Research Hub (papers), AI chat, Profile, Notifications, Settings, Help, Search
  styles/       app.css (the original Blazor stylesheet) + react.css (additions)
```

The router (`src/lib/router.tsx`) is a small History-API router written for this app, so the
only runtime dependencies are `react` and `react-dom`.

## Features the Blazor UI did not expose

The backend already supported these; the React UI adds screens for them:

- Student **graduation**: degree audit, apply / withdraw, next ceremony, certificate
  (Academic Advisor page).
- Admin **graduation review** (approve / reject), **send notifications**, **announcements**
  and **event management** (Admin Panel).
- Per-user **notification preference** (Settings), deleting notifications, deleting study
  plan items, attaching files to doctor materials, editing announcements and exam drafts.
