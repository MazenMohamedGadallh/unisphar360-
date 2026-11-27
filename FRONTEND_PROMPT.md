Rebuild the attached frontend of UniSphere 360+ so it looks and behaves exactly like the original.

The attached source code and screenshots are the single source of truth. Copy the existing files and images as they are; do not rewrite, redesign or "improve" anything. This description only helps you check the result. If it disagrees with the code, the code wins. If a file or image is missing, ask for it instead of inventing a replacement.

## Stack
React 19, TypeScript, Vite 8, Tailwind CSS 4, React Router 7. Scripts: `"dev": "vite"`, `"build": "tsc --noEmit && vite build"`. Keep `vite.config.ts` as it is: it copies `src/assets` into the build because pages load images from `/src/assets/...`. Dev server port 5173.

## Hard rules
- Frontend only. No server.ts, Express, mock data, fake APIs, AI SDKs or API keys.
- The backend is an existing ASP.NET Core API at `http://localhost:5080` (override with `VITE_API_BASE_URL`). Keep every API call, request body and field name from `src/lib/api.ts`, `src/lib/grad.ts` and the pages.
- Auth: Bearer JWT in localStorage under `unisphere.auth.token`.
- Chat streams over SSE from `POST /api/chat/conversations/{id|new}/messages`; the new conversation id is in the `X-Conversation-Id` header.

## Visual identity
- Royal purple. Light: primary #6d28d9, background #f4f1fa, card #ffffff, border #dfd6f5, text #1e1438. Dark: primary #a78bfa, background #110c21, card #1f1738, border #36285e, text #f5f3ff. All tokens are in `src/index.css`.
- Fonts: Plus Jakarta Sans (UI), Fraunces (login headline), JetBrains Mono (code).
- Radius: controls 10px, cards 14px. Sidebar 264px, top bar 64px.
- Light and dark themes with a toggle.

## Login page (must be identical)
Files: `src/pages/Login.tsx`, `src/pages/login.css`, `src/components/LiveCampusBackground.tsx` (variant "hero"), `src/components/BrandLogo.tsx`.
- Full-screen university campus photo (`university_campus_hero_1795788262420.jpg`) on #160d2e, with a purple gradient overlay, a soft violet glow and a slow "breathing" zoom.
- Top left: slowly spinning circular sphere logo and "UniSphere 360+" ("360+" in a lilac gradient). Top right: round theme toggle and a "Create Account" pill.
- Left side: large Fraunces headline "Your whole university life, in one place.", the line "Courses, grades, exams and advice for students, doctors and graduates.", and three frosted-glass pills: "Lectures, materials and your study plan", "Exams, results and attendance", "Career help when you graduate".
- Right side: frosted-glass card (max width 430px) with the spinning logo, "Welcome back", "Sign in to enter your university workspace", email field, password field with show/hide, purple gradient "Sign In" button, four "Quick Demo Access" pills (Student, Graduate, Doctor, Admin) and a "Create an account" link.
- Items fade up one after another on load. After a successful login a "Welcome, {name}" overlay shows for about 600ms, then the user goes to the page for their role. Respect reduced motion.
- `LoginCampusScene.tsx` is an older illustration; the current login page does not use it.

## App shell (all signed-in pages)
- Faded library photo background with soft purple orbs and thin architectural line art.
- Translucent collapsible left sidebar (drawer on mobile): logo, menu for the user's role (`src/components/nav.ts`), collapsible Quick Links (My Profile, Notifications, Settings, Help & Support, Logout), user chip at the bottom.
- Translucent sticky top bar: sidebar toggle, search, theme toggle, AI assistant, notifications with unread badge, avatar menu. Logout asks for confirmation.

## Pages
Keep every route in `src/App.tsx`: login, register, home; the student, doctor, graduate and admin workspaces; AI chat, search, profile, notifications, settings and help. Copy `register.css`, `dashboard.css`, `university.css`, `chat.css` and `App.css` unchanged.

## Done when
`npm install` and `npm run build` finish with no errors, and each page matches the screenshots in light and dark themes, on desktop and mobile. Report anything that differs.
