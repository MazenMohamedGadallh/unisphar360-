/*
 * Role-based sidebar navigation for Student, Graduate, Doctor, and Admin.
 * Every route maps directly to a live React workspace connected to the backend API.
 */

export interface NavItem {
  to: string;
  label: string;
  icon: string;
  end?: boolean;
}

const GRADUATE_NAV: NavItem[] = [
  { to: "/graduate", label: "Career Overview", icon: "house", end: true },
  { to: "/graduate/cv", label: "CV & Resume Studio", icon: "file-text" },
  { to: "/graduate/jobs", label: "Jobs", icon: "briefcase" },
  { to: "/graduate/roadmap", label: "Skills & Roadmap", icon: "map" },
  { to: "/graduate/applications", label: "My Applications", icon: "clipboard-check" },
  { to: "/graduate/interview", label: "AI Interview Coach", icon: "mic" },
  { to: "/graduate/alumni", label: "Campus", icon: "landmark" },
  { to: "/research-hub", label: "Research Hub", icon: "microscope" },
  { to: "/graduate/assistant", label: "AI Assistant", icon: "sparkles" },
];

const DOCTOR_NAV: NavItem[] = [
  { to: "/doctor", label: "Faculty Hub", icon: "house", end: true },
  { to: "/doctor/exam-assistant", label: "Exam Assistant (EN/AR)", icon: "languages" },
  { to: "/doctor/exam-results", label: "Exam Results & Grading", icon: "clipboard-check" },
  { to: "/doctor/research", label: "Research Hub", icon: "microscope" },
  { to: "/doctor/materials", label: "Course Materials", icon: "book-open" },
  { to: "/doctor/ai", label: "AI Teaching Assistant", icon: "sparkles" },
];

const ADMIN_NAV: NavItem[] = [
  { to: "/admin", label: "Overview & Health", icon: "shield-check", end: true },
  { to: "/admin/users", label: "Users & Roles", icon: "users" },
  { to: "/admin/courses", label: "Course Assignments", icon: "book-open" },
  { to: "/admin/graduation", label: "Graduation Review", icon: "graduation-cap" },
  { to: "/admin/events", label: "Campus Events", icon: "calendar-days" },
  { to: "/admin/announcements", label: "Broadcasts & Alerts", icon: "megaphone" },
  { to: "/admin/audit", label: "Audit Logs", icon: "history" },
  { to: "/chat", label: "AI Assistant", icon: "sparkles" },
];

const STUDENT_NAV: NavItem[] = [
  { to: "/dashboard", label: "Dashboard", icon: "house", end: true },
  { to: "/university", label: "My University", icon: "landmark" },
  { to: "/courses", label: "Courses & Enrollment", icon: "book-open" },
  { to: "/study-planner", label: "Study Planner", icon: "calendar-days" },
  { to: "/academic-advisor", label: "Academic Advisor", icon: "graduation-cap" },
  { to: "/analytics", label: "Performance Analysis", icon: "chart-no-axes-combined" },
  { to: "/student/exams", label: "My Exams", icon: "clipboard-check" },
  { to: "/graduation", label: "Graduation & Honors", icon: "award" },
  { to: "/research-hub", label: "Research & Quiz", icon: "microscope" },
  { to: "/chat", label: "AI Study Assistant", icon: "sparkles" },
];

export function navFor(role: string | null | undefined): NavItem[] {
  switch ((role ?? "").trim()) {
    case "Graduate":
      return GRADUATE_NAV;
    case "Doctor":
      return DOCTOR_NAV;
    case "Admin":
      return ADMIN_NAV;
    default:
      return STUDENT_NAV;
  }
}

export function aiRouteFor(role: string | null | undefined): string {
  switch ((role ?? "").trim()) {
    case "Graduate":
      return "/graduate/assistant";
    case "Doctor":
      return "/doctor/ai";
    default:
      return "/chat";
  }
}

export function settingsFor(_role: string | null | undefined): string {
  return "/settings";
}
