/*
 * Role-based sidebar navigation, mirroring the Blazor layout exactly:
 * - Graduate  -> GradNav.razor (career-focused menu)
 * - Doctor    -> RoleNavMenu.razor (Doctor branch)
 * - everyone else (Student, Admin, …) -> the inline list in MainLayout.razor
 */

export interface NavItem {
  to: string;
  label: string;
  icon: string;
  /** Exact-match only (react-router NavLink `end`), like NavLinkMatch.All. */
  end?: boolean;
}

/** GradNav.razor — labels are exact per spec. */
const GRADUATE_NAV: NavItem[] = [
  { to: "/graduate", label: "Career Overview", icon: "house", end: true },
  { to: "/graduate/profile", label: "My Career Profile", icon: "user-round" },
  { to: "/graduate/cv", label: "CV & Resume Studio", icon: "file-text" },
  { to: "/graduate/jobs", label: "Find Jobs", icon: "search" },
  { to: "/graduate/matches", label: "AI Job Matches", icon: "thumbs-up" },
  { to: "/graduate/skill-gap", label: "Skill Gap Analysis", icon: "target" },
  { to: "/graduate/roadmap", label: "Career Roadmap", icon: "map" },
  { to: "/graduate/applications", label: "My Applications", icon: "file-check-2" },
  { to: "/graduate/interview", label: "AI Interview Coach", icon: "mic" },
  { to: "/graduate/assistant", label: "AI Career Assistant", icon: "message-square" },
  { to: "/graduate/alumni", label: "Alumni & University Services", icon: "landmark" },
  { to: "/graduate/events", label: "Graduated Events", icon: "party-popper" },
  { to: "/graduate/insights", label: "Career Insights", icon: "chart-no-axes-combined" },
];

/** RoleNavMenu.razor — Doctor branch. */
const DOCTOR_NAV: NavItem[] = [
  { to: "/doctor", label: "Home", icon: "house" },
  { to: "/doctor/courses", label: "My Courses", icon: "book-open" },
  { to: "/doctor/research", label: "Research Hub", icon: "microscope" },
  { to: "/doctor/profile", label: "Doctor Profile", icon: "user-round" },
  { to: "/doctor/students", label: "My Students", icon: "users" },
  { to: "/doctor/materials", label: "Materials", icon: "file-text" },
  { to: "/doctor/announcements", label: "Announcements", icon: "megaphone" },
  { to: "/doctor/exam-results", label: "Exam Results", icon: "clipboard-check" },
  { to: "/doctor/ai", label: "AI Assistant", icon: "sparkles" },
];

/** MainLayout.razor — inline list for Student / Admin (and any other role). */
function defaultNav(role: string): NavItem[] {
  const items: NavItem[] = [
    { to: "/dashboard", label: "Home", icon: "house" },
    { to: "/academic-advisor", label: "Academic Advisor", icon: "graduation-cap" },
    { to: "/career-assistant", label: "Career Assistant", icon: "briefcase" },
    { to: "/research-hub", label: "Research Hub", icon: "microscope" },
  ];
  if (role === "Student" || role === "Graduate" || role === "Doctor") {
    items.push({ to: "/university", label: "My University", icon: "landmark" });
  }
  items.push({ to: "/student-services", label: "Student Services", icon: "headset" });
  if (role === "Student") {
    items.push({ to: "/analytics", label: "Performance Analysis", icon: "chart-no-axes-combined" });
    items.push({ to: "/student/exams", label: "My Exams", icon: "clipboard-check" });
  }
  if (role === "Admin") {
    items.push({ to: "/admin", label: "Admin Panel", icon: "shield-check" });
  }
  items.push({ to: "/chat", label: "AI Assistant", icon: "sparkles" });
  return items;
}

/** Sidebar links for the signed-in role (never empty; unknown roles get the default list). */
export function navFor(role: string | null | undefined): NavItem[] {
  switch ((role ?? "").trim()) {
    case "Graduate":
      return GRADUATE_NAV;
    case "Doctor":
      return DOCTOR_NAV;
    default:
      return defaultNav(role ?? "");
  }
}

/** Settings link differs per role (MainLayout.razor quick links). */
export function settingsFor(role: string | null | undefined): string {
  return (role ?? "").trim() === "Graduate" ? "/graduate/settings" : "/settings";
}
