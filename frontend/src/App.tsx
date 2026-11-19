import { Route, Routes } from "react-router-dom";
import { Login } from "./pages/Login";
import { Register } from "./pages/Register";
import { Chat } from "./pages/Chat";
import { Home } from "./pages/Home";
import { Dashboard } from "./pages/Dashboard";
import { University } from "./pages/University";
import { NotMigrated } from "./pages/NotMigrated";
import { Analytics } from "./pages/Analytics";
import { Exams } from "./pages/Exams";
import { ExamResults } from "./pages/ExamResults";
import { AppLayout } from "./components/AppLayout";

export default function App() {
  return (
    <Routes>
      {/* Public pages (no shell). */}
      <Route path="/login" element={<Login />} />
      <Route path="/register" element={<Register />} />
      {/* Role-based redirector: / -> /dashboard, /doctor, /graduate, ... */}
      <Route path="/" element={<Home />} />
      {/* Every protected page shares the Sidebar + Topbar shell. Routes
          not migrated yet fall back to a "not migrated" notice. */}
      <Route element={<AppLayout />}>
        {/* Migrated pages */}
        <Route path="/dashboard" element={<Dashboard />} />
        {/* One RAG assistant for students, doctors and graduates. */}
        <Route path="/chat" element={<Chat />} />
        <Route path="/doctor/ai" element={<Chat />} />
        <Route path="/graduate/assistant" element={<Chat />} />
        <Route path="/university" element={<University />} />
        <Route path="/analytics" element={<Analytics />} />
        <Route path="/student/exams" element={<Exams />} />
        <Route path="/doctor/exam-results" element={<ExamResults />} />
        {/* Everything else falls back to a "not migrated" notice. */}
        <Route path="*" element={<NotMigrated />} />
      </Route>
    </Routes>
  );
}
