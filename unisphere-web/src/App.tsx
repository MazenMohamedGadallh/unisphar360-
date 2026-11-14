import type { ReactNode } from 'react';
import { Layout } from './components/Layout';
import { Loading } from './components/ui';
import { homeFor, useAuth, type Role } from './lib/auth';
import { Navigate, Routes, useLocation, type RouteDef } from './lib/router';

import Login from './pages/auth/Login';
import Register from './pages/auth/Register';

import Dashboard from './pages/student/Dashboard';
import University from './pages/student/University';
import AcademicAdvisor from './pages/student/AcademicAdvisor';
import StudyPlanner from './pages/student/StudyPlanner';
import { CourseDetail, CourseList } from './pages/student/Courses';
import Enrollments from './pages/student/Enrollments';
import CareerAssistant from './pages/student/CareerAssistant';
import StudentServices from './pages/student/StudentServices';
import Analytics from './pages/student/Analytics';
import StudentExams from './pages/student/Exams';
import Events from './pages/student/Events';

import PaperHub from './pages/shared/PaperHub';
import Chat from './pages/shared/Chat';
import Profile from './pages/shared/Profile';
import Notifications from './pages/shared/Notifications';
import Settings from './pages/shared/Settings';
import Help from './pages/shared/Help';
import Search from './pages/shared/Search';

import GradOverview from './pages/graduate/Overview';
import GradProfile from './pages/graduate/Profile';
import CvStudio from './pages/graduate/CvStudio';
import Jobs from './pages/graduate/Jobs';
import Matches from './pages/graduate/Matches';
import SkillGap from './pages/graduate/SkillGap';
import Roadmap from './pages/graduate/Roadmap';
import Applications from './pages/graduate/Applications';
import Interview from './pages/graduate/Interview';
import GradAssistant from './pages/graduate/Assistant';
import Alumni from './pages/graduate/Alumni';
import GradEvents from './pages/graduate/GradEvents';
import Insights from './pages/graduate/Insights';
import GradSettings from './pages/graduate/GradSettings';

import DoctorDashboard from './pages/doctor/DoctorDashboard';
import DoctorCourses from './pages/doctor/DoctorCourses';
import DoctorCourse from './pages/doctor/DoctorCourse';
import { CourseStudentsPage, MyStudents } from './pages/doctor/CourseStudents';
import { DoctorAnnouncementsPage, DoctorMaterialsPage } from './pages/doctor/DoctorMaterialsPage';
import DoctorAi from './pages/doctor/DoctorAi';
import ExamAssistant from './pages/doctor/ExamAssistant';
import DoctorProfile from './pages/doctor/DoctorProfile';
import DoctorResearch, { ResearchDetails } from './pages/doctor/DoctorResearch';

import AdminPanel from './pages/admin/AdminPanel';

const ALL: Role[] = ['Student', 'Graduate', 'Doctor', 'Admin'];

/**
 * Client-side route guard. The API enforces every role server-side
 * ([Authorize(Roles = ...)]); this only decides what to render:
 * anonymous → /login (with returnUrl), wrong role → that role's home.
 */
function Protected({ roles, children }: { roles: Role[]; children: ReactNode }) {
  const { status, role } = useAuth();
  const location = useLocation();
  if (status === 'loading') return <Loading label="Restoring your session…" />;
  if (status === 'anonymous') {
    const back = location.pathname + location.search;
    return <Navigate to={back && back !== '/' ? `/login?returnUrl=${encodeURIComponent(back)}` : '/login'} />;
  }
  if (!role || !roles.includes(role)) return <Navigate to={homeFor(role)} />;
  return <Layout>{children}</Layout>;
}

function PublicOnly({ children }: { children: ReactNode }) {
  const { status, role } = useAuth();
  if (status === 'loading') return <Loading label="Restoring your session…" />;
  if (status === 'authenticated') return <Navigate to={homeFor(role)} />;
  return <>{children}</>;
}

function Home() {
  const { status, role } = useAuth();
  if (status === 'loading') return <Loading label="Restoring your session…" />;
  return <Navigate to={status === 'authenticated' ? homeFor(role) : '/login'} />;
}

function NotFound() {
  return (
    <div className="not-found">
      <h1>Page not found</h1>
      <p>The page you are looking for does not exist.</p>
      <a href="/" className="btn btn-primary">
        Go home
      </a>
    </div>
  );
}

const r = (path: string, roles: Role[], element: ReactNode): RouteDef => ({ path, element: <Protected roles={roles}>{element}</Protected> });

const routes: RouteDef[] = [
  { path: '/', element: <Home /> },
  {
    path: '/login',
    element: (
      <PublicOnly>
        <Login />
      </PublicOnly>
    ),
  },
  {
    path: '/register',
    element: (
      <PublicOnly>
        <Register />
      </PublicOnly>
    ),
  },

  // Student
  r('/dashboard', ['Student'], <Dashboard />),
  r('/university', ['Student'], <University />),
  r('/academic-advisor', ['Student'], <AcademicAdvisor />),
  r('/study-planner', ['Student'], <StudyPlanner />),
  r('/courses', ['Student'], <CourseList />),
  r('/courses/:id', ['Student'], <CourseDetail />),
  r('/enrollments', ['Student'], <Enrollments />),
  r('/career-assistant', ['Student'], <CareerAssistant />),
  r('/student-services', ['Student'], <StudentServices />),
  r('/analytics', ['Student'], <Analytics />),
  r('/student/exams', ['Student'], <StudentExams />),
  r('/events', ['Student'], <Events />),
  r('/research-hub', ['Student', 'Graduate'], <PaperHub />),
  r('/chat', ['Student', 'Doctor', 'Graduate'], <Chat />),

  // Graduate
  r('/graduate', ['Graduate'], <GradOverview />),
  r('/graduate/profile', ['Graduate'], <GradProfile />),
  r('/graduate/cv', ['Graduate'], <CvStudio />),
  { path: '/graduate/cv-analysis', element: <Navigate to="/graduate/cv" /> },
  r('/graduate/jobs', ['Graduate'], <Jobs />),
  r('/graduate/matches', ['Graduate'], <Matches />),
  r('/graduate/skill-gap', ['Graduate'], <SkillGap />),
  r('/graduate/roadmap', ['Graduate'], <Roadmap />),
  r('/graduate/applications', ['Graduate'], <Applications />),
  r('/graduate/interview', ['Graduate'], <Interview />),
  r('/graduate/assistant', ['Graduate'], <GradAssistant />),
  r('/graduate/alumni', ['Graduate'], <Alumni />),
  r('/graduate/events', ['Graduate'], <GradEvents />),
  r('/graduate/insights', ['Graduate'], <Insights />),
  r('/graduate/settings', ['Graduate'], <GradSettings />),

  // Doctor
  r('/doctor', ['Doctor'], <DoctorDashboard />),
  r('/doctor/courses', ['Doctor'], <DoctorCourses />),
  r('/doctor/courses/:courseId', ['Doctor'], <DoctorCourse />),
  r('/doctor/courses/:courseId/students', ['Doctor'], <CourseStudentsPage />),
  r('/doctor/students', ['Doctor'], <MyStudents />),
  r('/doctor/materials', ['Doctor'], <DoctorMaterialsPage />),
  r('/doctor/announcements', ['Doctor'], <DoctorAnnouncementsPage />),
  r('/doctor/ai', ['Doctor'], <DoctorAi />),
  r('/doctor/exam-assistant', ['Doctor'], <ExamAssistant />),
  r('/doctor/profile', ['Doctor'], <DoctorProfile />),
  r('/doctor/research', ['Doctor', 'Admin'], <DoctorResearch />),
  r('/doctor/research/:id', ['Doctor', 'Admin'], <ResearchDetails />),

  // Admin
  r('/admin', ['Admin'], <AdminPanel />),

  // Every signed-in role
  r('/profile', ALL, <Profile />),
  r('/notifications', ALL, <Notifications />),
  r('/settings', ALL, <Settings />),
  r('/help', ALL, <Help />),
  r('/search', ALL, <Search />),
];

export default function App() {
  return <Routes routes={routes} fallback={<NotFound />} />;
}
