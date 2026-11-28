# Graduate module: UniSphere Career Journey

## Architecture decisions

- Same backend (ASP.NET Core + SQLite) and same React app. Schema changes are additive only (`DbSchemaPatcher`).
- One evidence source (`CareerEvidenceService`) feeds skill gap, job matching, missions and the report.
- Missions are code, not rows. Status is computed from persisted evidence. XP is a ledger (`XpTransactions`, unique per user + mission), added on top of the existing quiz/drill XP.
- AI Career Coach is the existing chat (history, BM25 retrieval over the graduate's own CV) with richer graduate context. It is keyword retrieval, not hybrid RAG.
- AI features use the existing provider chain. No provider means a clear 503, never invented output.
- Skill Proof Lab uses non-executable formats (scenario multiple choice + written task). No user code runs on the server.

## Checklist

- [x] 2. Graduate shell, grouped navigation, design tokens
- [x] 3. Onboarding, Career Profile, Career DNA
- [x] 4. CV extraction and ATS analysis fixes, history, job fit
- [x] 5. Career Journey: 8 levels, missions, XP, badges
- [x] 6. Skill gap, Skill Proof Lab
- [x] 7. Coach context, interview type/difficulty/stronger answer
- [x] 8. Portfolio
- [x] 9. Job evidence, application statuses and analytics
- [x] 10. Readiness report, dashboard
- [x] 11. Tests, build, live run, browser check

## Deliberately not built

Public portfolio links, project images, voice interviews, executable code challenges, embeddings.

## Verified (2026-11-28)

284 automated tests, frontend type-check and build, live run against Gemini on a database copy, headless Edge pass over every graduate page.

## Open

- A generated PDF is covered by a test. No Word-authored DOCX or designer-tool PDF was tried.
- Live job feed currently holds few technical roles, so match scores are low for developers.
- Skill tags already stored on imported jobs keep the old C# false positive until the next sync.
- Job match score does not weigh education, experience or certifications.
- `GraduateOverview.tsx`, `GraduateCvStudio.tsx` and `CareerCalibration.tsx` are no longer routed and can be deleted.
