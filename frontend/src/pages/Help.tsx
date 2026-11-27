import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, LoadingBlock } from "../components/ui";
import { aiRouteFor } from "../components/nav";
import { API_BASE_URL, authHeaders, clearToken, type AuthUser } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface FaqItem {
  id: string;
  category: string;
  question: string;
  answer: string;
}

interface QuickLinkItem {
  to: string;
  label: string;
  description: string;
  icon: string;
}

const COMMON_FAQS: FaqItem[] = [
  {
    id: "reset-password",
    category: "Account",
    question: "How do I change or reset my password?",
    answer:
      "Open My Profile from the sidebar or top-right menu, scroll to the Password & Security section, enter your current password and new password, and click Update Password. If you are locked out of your account, email admin@unisphere.edu from your university email address.",
  },
  {
    id: "profile-photo",
    category: "Account",
    question: "How do I update my profile photo or theme?",
    answer:
      "You can upload, replace, or remove your profile picture anytime from My Profile. To switch between Light and Dark mode, click the sun/moon icon in the top bar or visit Settings — your preference is saved automatically.",
  },
];

const ROLE_FAQS: Record<string, FaqItem[]> = {
  Student: [
    {
      id: "student-ai",
      category: "AI Assistant",
      question: "How do I get the best answers from the AI Study Assistant?",
      answer:
        "Use the provider selector at the top of the chat to choose between course-focused answers (drawn directly from your university notes and schedule) or general study help for broader coding, math, and explanation questions.",
    },
    {
      id: "student-exams",
      category: "Exams",
      question: "How do online exams and written questions work?",
      answer:
        "Go to My Exams to see all open and upcoming exams. Multiple-choice and True/False questions are scored right after you submit, while written essay or definition answers are reviewed by your instructor and added to your final grade once graded.",
    },
    {
      id: "student-courses",
      category: "Courses",
      question: "Where do I find lecture notes or enroll in courses?",
      answer:
        "Visit My University to read and download lecture notes and assignments for your courses. To browse the full catalog or enroll in and drop courses for the term, open Courses & Enrollment.",
    },
    {
      id: "student-planner",
      category: "Study Plan",
      question: "Can I edit my weekly study plan or get a recommended schedule?",
      answer:
        "Yes! Open Study Planner to check off completed sessions, edit subjects and study hours for any day, mark rest days, or apply a recommended weekly schedule tailored to courses that need extra attention.",
    },
  ],
  Graduate: [
    {
      id: "grad-cv",
      category: "CV & Profile",
      question: "How does CV upload and automated resume review work?",
      answer:
        "Open CV & Resume Studio to upload your resume (PDF, DOCX, or TXT) or paste your resume text. UniSphere extracts your text, gives you a readiness score with clear improvement tips, and lets you sync detected skills straight into your profile.",
    },
    {
      id: "grad-jobs",
      category: "Jobs & Applications",
      question: "How are job match percentages calculated, and how do I track applications?",
      answer:
        "Match percentages compare the skills on your Career Profile against each job's required skills. Clicking Save or Apply Now automatically adds the role to My Applications, where you can update stages (Applied, Interview, Offer), add notes, or withdraw anytime.",
    },
    {
      id: "grad-interview",
      category: "Interview Coach",
      question: "How do I practice with the AI Interview Coach?",
      answer:
        "Choose your target role, track (Technical, System Design, or Behavioral), and level, then either generate a fresh practice question or pick one from the Curated Question Bank. Submit your answer to receive a score out of 100 along with strengths and next steps.",
    },
    {
      id: "grad-campus",
      category: "Campus & Alumni",
      question: "How can I request an official transcript or connect with an alumni mentor?",
      answer:
        "Visit the Campus page to read university announcements, RSVP to upcoming career fairs, browse alumni mentors, and submit official transcript or mentorship requests.",
    },
  ],
  Doctor: [
    {
      id: "doc-materials",
      category: "Courses & Materials",
      question: "How do I upload lecture notes or assignments for my students?",
      answer:
        "From Faculty Hub, select your course, choose the material type (Lecture, Summary, Assignment, or Announcement), enter your notes, and click Publish Material. Enrolled students are notified right away and can access the content immediately.",
    },
    {
      id: "doc-exams",
      category: "Exams",
      question: "How do I create bilingual (English / Arabic) exams?",
      answer:
        "Open Exam Assistant (EN/AR), select a course and source material, choose your mix of Multiple Choice, True/False, Definition, and Essay questions, and switch between English and Arabic anytime before saving as a draft or publishing to students.",
    },
    {
      id: "doc-grading",
      category: "Grading",
      question: "How do I review exam results and grade written answers?",
      answer:
        "Go to Exam Results & Grading and pick a published exam to see student completion rates, scores, and pending written answers. Mark each written response as Correct or Incorrect and the student's final score updates automatically.",
    },
    {
      id: "doc-research",
      category: "Research",
      question: "Where can I track my publications, grants, and thesis advisees?",
      answer:
        "Open Research Hub from the sidebar to add or update publications, active research grants, and senior graduation projects alongside your supervised students.",
    },
  ],
  Admin: [
    {
      id: "admin-users",
      category: "Users",
      question: "How do I add a new account or deactivate an existing user?",
      answer:
        "Open Users & Roles in the sidebar to create Student, Doctor, Graduate, or Admin accounts. Use Deactivate or Restore on any row to manage account access safely without losing historical records.",
    },
    {
      id: "admin-courses",
      category: "Courses",
      question: "How do I assign faculty members to university courses?",
      answer:
        "Go to Course Assignments and pick an instructor from the dropdown next to any course. Changes take effect immediately across both the student catalog and the instructor's Faculty Hub.",
    },
    {
      id: "admin-announcements",
      category: "Announcements",
      question: "How do campus-wide announcements reach users?",
      answer:
        "Visit Announcements to publish a message for All Roles or a specific group (Students, Doctors, or Graduates). Published announcements appear in user notifications and campus feeds immediately.",
    },
    {
      id: "admin-overview",
      category: "Monitoring",
      question: "Where can I check daily activity trends and recent actions?",
      answer:
        "Use Overview & Health to review 14-day sign-in activity and platform status, and open Activity History (Audit Logs) to review recent administrative and account changes.",
    },
  ],
};

function quickLinksFor(role: string): QuickLinkItem[] {
  const aiTo = aiRouteFor(role);
  switch (role) {
    case "Graduate":
      return [
        { to: "/graduate", label: "Career Overview", description: "Your job readiness & roadmap summary", icon: "house" },
        { to: "/graduate/cv", label: "CV & Resume Studio", description: "Upload your resume & update skills", icon: "file-text" },
        { to: "/graduate/interview", label: "AI Interview Coach", description: "Practice questions & get feedback", icon: "mic" },
        { to: aiTo, label: "AI Career Assistant", description: "Ask career, CV, or campus questions", icon: "sparkles" },
        { to: "/profile", label: "My Profile", description: "Photo, bio & password settings", icon: "user-round" },
        { to: "/settings", label: "Settings", description: "Theme & notification preferences", icon: "settings" },
      ];
    case "Doctor":
      return [
        { to: "/doctor", label: "Faculty Hub", description: "Courses, students & lecture uploads", icon: "house" },
        { to: "/doctor/exam-assistant", label: "Exam Assistant (EN/AR)", description: "Draft & publish course exams", icon: "languages" },
        { to: "/doctor/exam-results", label: "Exam Results & Grading", description: "Grade written answers & review scores", icon: "clipboard-check" },
        { to: aiTo, label: "AI Teaching Assistant", description: "Draft rubrics, summaries & quizzes", icon: "sparkles" },
        { to: "/profile", label: "My Profile", description: "Photo, contact info & password", icon: "user-round" },
        { to: "/settings", label: "Settings", description: "Appearance & workspace preferences", icon: "settings" },
      ];
    case "Admin":
      return [
        { to: "/admin", label: "Overview & Health", description: "Platform metrics & activity trends", icon: "shield-check" },
        { to: "/admin/users", label: "Users & Roles", description: "Create & manage user accounts", icon: "users" },
        { to: "/admin/announcements", label: "Announcements", description: "Post updates to campus users", icon: "megaphone" },
        { to: aiTo, label: "AI Assistant", description: "Get quick help & draft communications", icon: "sparkles" },
        { to: "/profile", label: "My Profile", description: "Photo & password settings", icon: "user-round" },
        { to: "/settings", label: "Settings", description: "Theme & workspace preferences", icon: "settings" },
      ];
    default:
      return [
        { to: "/dashboard", label: "Student Dashboard", description: "GPA, attendance & weekly progress", icon: "house" },
        { to: "/university", label: "My University", description: "Lecture notes & course materials", icon: "landmark" },
        { to: "/student/exams", label: "My Exams", description: "Take open exams & view results", icon: "clipboard-check" },
        { to: aiTo, label: "AI Study Assistant", description: "Ask about courses, notes & concepts", icon: "sparkles" },
        { to: "/profile", label: "My Profile", description: "Photo, bio & password settings", icon: "user-round" },
        { to: "/settings", label: "Settings", description: "Theme & notification preferences", icon: "settings" },
      ];
  }
}

const SUPPORT_CATEGORIES = [
  "Account & Sign-In",
  "Courses & Materials",
  "Exams & Grades",
  "CV & Career Services",
  "AI Assistant",
  "Technical Issue",
  "Other",
];

const fmtDate = (iso: string) => new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

export function Help() {
  const navigate = useNavigate();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  const [faqQuery, setFaqQuery] = useState("");
  const [openFaqId, setOpenFaqId] = useState<string | null>(null);

  const [subject, setSubject] = useState("");
  const [category, setCategory] = useState(SUPPORT_CATEGORIES[0]);
  const [message, setMessage] = useState("");
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  useEffect(() => {
    document.title = "Help & Support — UniSphere 360+";
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const meRes = await fetch(`${API_BASE_URL}/api/auth/me`, { headers: authHeaders() });
      if (meRes.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (meRes.ok) {
        const u = (await meRes.json()) as AuthUser;
        setUser(u);
        const firstRoleFaq = (ROLE_FAQS[u.role] ?? ROLE_FAQS.Student)[0];
        if (firstRoleFaq) setOpenFaqId((prev) => prev ?? firstRoleFaq.id);
      }
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  const role = user?.role ?? "Student";

  const faqs = useMemo(() => {
    const combined = [...(ROLE_FAQS[role] ?? ROLE_FAQS.Student), ...COMMON_FAQS];
    const q = faqQuery.trim().toLowerCase();
    if (!q) return combined;
    return combined.filter(
      (f) =>
        f.question.toLowerCase().includes(q) ||
        f.answer.toLowerCase().includes(q) ||
        f.category.toLowerCase().includes(q),
    );
  }, [role, faqQuery]);

  const quickLinks = useMemo(() => quickLinksFor(role), [role]);

  function submitTicket(e: FormEvent) {
    e.preventDefault();
    if (subject.trim().length < 3 || message.trim().length < 10) {
      setToast({ text: "Please provide a clear subject and at least 10 characters describing your issue.", isError: true });
      return;
    }
    const query = `subject=${encodeURIComponent(`[${category}] ${subject.trim()}`)}&body=${encodeURIComponent(message.trim())}`;
    window.location.href = `mailto:admin@unisphere.edu?${query}`;
    setToast({ text: "Opening your email app with the request filled in.", isError: false });
  }

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Help &amp; Support Center</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Answers, guides, and university support tailored for your {role.toLowerCase()} workspace.
          </p>
        </div>
        <Button size="sm" to={aiRouteFor(role)}>
          <UiIcon name="sparkles" />
          Ask AI Assistant
        </Button>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {loading ? (
        <LoadingBlock label="Loading Help Center…" />
      ) : (
        <>
          {/* Quick Helpful Links (Strictly scoped to current user's role) */}
          <div className="mb-6">
            <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-text-secondary">
              Quick Helpful Links
            </h2>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {quickLinks.map((item) => (
                <button
                  key={item.to}
                  type="button"
                  onClick={() => navigate(item.to)}
                  className="flex cursor-pointer items-center gap-3.5 rounded-card border border-border bg-card p-4 text-left transition hover:-translate-y-0.5 hover:border-primary/50"
                >
                  <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary-light text-primary">
                    <UiIcon name={item.icon} />
                  </span>
                  <div className="min-w-0">
                    <div className="truncate text-sm font-semibold text-text">{item.label}</div>
                    <div className="truncate text-xs text-text-secondary">{item.description}</div>
                  </div>
                </button>
              ))}
            </div>
          </div>

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
            {/* Searchable Expandable FAQ Accordion */}
            <Card className="overflow-hidden lg:col-span-7">
              <CardHeader
                title={`Frequently Asked Questions (${faqs.length})`}
                action={<span className="text-xs text-text-secondary">{role} Guide</span>}
              />
              <div className="border-b border-border p-4">
                <div className="relative">
                  <span className="pointer-events-none absolute top-1/2 left-3.5 -translate-y-1/2 text-text-secondary">
                    <UiIcon name="search" />
                  </span>
                  <input
                    type="search"
                    value={faqQuery}
                    onChange={(e) => setFaqQuery(e.target.value)}
                    placeholder="Search help topics (password, exams, CV, assistant, profile)…"
                    className="w-full rounded-control border border-border bg-bg py-2 pr-3.5 pl-10 text-sm text-text outline-none focus:border-primary"
                  />
                </div>
              </div>

              {faqs.length === 0 ? (
                <EmptyState
                  icon="circle-help"
                  message={`No FAQ entries matched “${faqQuery}”. Try another keyword or send us a message on the right.`}
                />
              ) : (
                <div className="divide-y divide-border">
                  {faqs.map((item) => {
                    const isOpen = openFaqId === item.id;
                    return (
                      <div key={item.id}>
                        <button
                          type="button"
                          aria-expanded={isOpen}
                          onClick={() => setOpenFaqId(isOpen ? null : item.id)}
                          className="flex w-full cursor-pointer items-center justify-between gap-3 border-0 bg-transparent px-5 py-4 text-left transition hover:bg-primary-light/25"
                        >
                          <div className="min-w-0">
                            <span className="mb-1 inline-block rounded bg-primary-light px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-primary">
                              {item.category}
                            </span>
                            <div className="text-sm font-semibold text-text">{item.question}</div>
                          </div>
                          <span
                            className={`shrink-0 text-text-secondary transition-transform duration-200 ${
                              isOpen ? "rotate-180 text-primary" : ""
                            }`}
                          >
                            <UiIcon name="chevron-down" />
                          </span>
                        </button>
                        {isOpen && (
                          <div className="bg-bg/50 px-5 pt-1 pb-4 text-sm leading-relaxed text-text-secondary">
                            {item.answer}
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </Card>

            {/* Contact Support Form & Official Contact Info */}
            <div className="flex flex-col gap-6 lg:col-span-5">
              <Card>
                <CardHeader title="Contact Support / Submit an Issue" />
                <form onSubmit={submitTicket} className="flex flex-col gap-3.5 p-5 text-sm">
                  <div>
                    <label className="mb-1 block font-medium text-text">Subject</label>
                    <input
                      type="text"
                      required
                      value={subject}
                      onChange={(e) => setSubject(e.target.value)}
                      placeholder="Brief summary of your question or issue"
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>

                  <div>
                    <label className="mb-1 block font-medium text-text">Category</label>
                    <select
                      value={category}
                      onChange={(e) => setCategory(e.target.value)}
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    >
                      {SUPPORT_CATEGORIES.map((cat) => (
                        <option key={cat} value={cat}>
                          {cat}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="mb-1 block font-medium text-text">Message</label>
                    <textarea
                      rows={4}
                      required
                      value={message}
                      onChange={(e) => setMessage(e.target.value)}
                      placeholder="Describe what happened or how our campus support team can help…"
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>

                  <button
                    type="submit"
                    className="btn-luminous inline-flex cursor-pointer items-center justify-center gap-2 rounded-control border-0 px-4 py-2.5 text-sm font-semibold disabled:opacity-60"
                  >
                    <UiIcon name="send" />
                    Email Support Request
                  </button>
                </form>
              </Card>

              <Card className="p-5">
                <div className="mb-3 flex items-center gap-2.5">
                  <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-primary-light text-primary">
                    <UiIcon name="headset" />
                  </span>
                  <div>
                    <div className="text-sm font-semibold text-text">Official University Support</div>
                    <div className="text-xs text-text-secondary">Direct help from the campus IT &amp; student services desk</div>
                  </div>
                </div>
                <div className="flex flex-col gap-2 text-xs text-text-secondary">
                  <div className="flex items-center justify-between rounded-control border border-border bg-bg/70 px-3.5 py-2.5">
                    <span>Support Email</span>
                    <a
                      href="mailto:admin@unisphere.edu"
                      className="font-semibold text-primary no-underline hover:underline"
                    >
                      admin@unisphere.edu
                    </a>
                  </div>
                  <div className="flex items-center justify-between rounded-control border border-border bg-bg/70 px-3.5 py-2.5">
                    <span>Support Hours</span>
                    <span className="font-medium text-text">Sun – Thu · 9:00 AM – 5:00 PM</span>
                  </div>
                  <div className="flex items-center justify-between rounded-control border border-border bg-bg/70 px-3.5 py-2.5">
                    <span>Typical Response</span>
                    <span className="font-medium text-text">Within 1 business day</span>
                  </div>
                </div>
              </Card>
            </div>
          </div>

        </>
      )}
    </div>
  );
}
