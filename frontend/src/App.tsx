import { Route, Routes } from "react-router-dom";
import { Login } from "./pages/Login";
import { Register } from "./pages/Register";
import { Chat } from "./pages/Chat";
import { Home } from "./pages/Home";
import { Dashboard } from "./pages/Dashboard";
import { University } from "./pages/University";
import { Analytics } from "./pages/Analytics";
import { Exams } from "./pages/Exams";
import { ExamResults } from "./pages/ExamResults";
import { ExamAssistant } from "./pages/ExamAssistant";
import { DoctorWorkspace } from "./pages/DoctorWorkspace";
import { DoctorResearch } from "./pages/DoctorResearch";
import { GraduateOverview } from "./pages/GraduateOverview";
import { GraduateCvStudio } from "./pages/GraduateCvStudio";
import { GraduateJobs } from "./pages/GraduateJobs";
import { GraduateRoadmap } from "./pages/GraduateRoadmap";
import { GraduateApplications } from "./pages/GraduateApplications";
import { GraduateAlumni } from "./pages/GraduateAlumni";
import { GraduateInterview } from "./pages/GraduateInterview";
import { AdminPanel } from "./pages/AdminPanel";
import { Profile } from "./pages/Profile";
import { Notifications } from "./pages/Notifications";
import { Settings } from "./pages/Settings";
import { Help } from "./pages/Help";
import { Search } from "./pages/Search";
import { StudyPlanner } from "./pages/StudyPlanner";
import { AcademicAdvisor } from "./pages/AcademicAdvisor";
import { CoursesCatalog } from "./pages/CoursesCatalog";
import { Graduation } from "./pages/Graduation";
import { ResearchHub } from "./pages/ResearchHub";
import { AppLayout } from "./components/AppLayout";

export default function App() {
  return (
    <Routes>
      {/* Public pages (no shell) */}
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />
      {/* Role-based redirector: / -> /dashboard, /doctor, /graduate, /admin */}
      <Route path="/" element={<Home />} />

      {/* Protected pages sharing the collapsible Sidebar + Topbar shell */}
      <Route element={<AppLayout />}>
        {/* Student */}
        <Route path="/dashboard" element={<Dashboard />} />
        <Route path="/university" element={<University />} />
        <Route path="/courses" element={<CoursesCatalog />} />
        <Route path="/enrollments" element={<CoursesCatalog />} />
        <Route path="/study-planner" element={<StudyPlanner />} />
        <Route path="/academic-advisor" element={<AcademicAdvisor />} />
        <Route path="/analytics" element={<Analytics />} />
        <Route path="/student/exams" element={<Exams />} />
        <Route path="/graduation" element={<Graduation />} />
        <Route path="/research-hub" element={<ResearchHub />} />

        {/* Doctor */}
        <Route path="/doctor" element={<DoctorWorkspace />} />
        <Route path="/doctor/courses" element={<DoctorWorkspace />} />
        <Route path="/doctor/materials" element={<DoctorWorkspace />} />
        <Route path="/doctor/students" element={<DoctorWorkspace />} />
        <Route path="/doctor/exam-assistant" element={<ExamAssistant />} />
        <Route path="/doctor/exam-results" element={<ExamResults />} />
        <Route path="/doctor/research" element={<DoctorResearch />} />
        <Route path="/research" element={<DoctorResearch />} />

        {/* Graduate (all 8 sections + legacy aliases) */}
        <Route path="/graduate" element={<GraduateOverview />} />
        <Route path="/graduate/overview" element={<GraduateOverview />} />
        <Route path="/graduate/cv" element={<GraduateCvStudio />} />
        <Route path="/graduate/profile" element={<GraduateCvStudio />} />
        <Route path="/graduate/jobs" element={<GraduateJobs />} />
        <Route path="/graduate/matches" element={<GraduateJobs />} />
        <Route path="/graduate/roadmap" element={<GraduateRoadmap />} />
        <Route path="/graduate/skill-gap" element={<GraduateRoadmap />} />
        <Route path="/graduate/insights" element={<GraduateRoadmap />} />
        <Route path="/graduate/applications" element={<GraduateApplications />} />
        <Route path="/graduate/interview" element={<GraduateInterview />} />
        <Route path="/graduate/alumni" element={<GraduateAlumni />} />
        <Route path="/graduate/campus" element={<GraduateAlumni />} />

        {/* Admin */}
        <Route path="/admin" element={<AdminPanel />} />
        <Route path="/admin/analytics" element={<AdminPanel />} />
        <Route path="/admin/health" element={<AdminPanel />} />
        <Route path="/admin/users" element={<AdminPanel />} />
        <Route path="/admin/courses" element={<AdminPanel />} />
        <Route path="/admin/graduation" element={<AdminPanel />} />
        <Route path="/admin/events" element={<AdminPanel />} />
        <Route path="/admin/announcements" element={<AdminPanel />} />
        <Route path="/admin/audit" element={<AdminPanel />} />

        {/* Hybrid RAG AI Assistant (all roles) */}
        <Route path="/chat" element={<Chat />} />
        <Route path="/doctor/ai" element={<Chat />} />
        <Route path="/graduate/assistant" element={<Chat />} />

        {/* Global & Quick Links (all roles) */}
        <Route path="/search" element={<Search />} />
        <Route path="/profile" element={<Profile />} />
        <Route path="/notifications" element={<Notifications />} />
        <Route path="/settings" element={<Settings />} />
        <Route path="/help" element={<Help />} />

        {/* Smart role-home fallback (zero NotMigrated dead ends) */}
        <Route path="*" element={<Home />} />
      </Route>
    </Routes>
  );
}
