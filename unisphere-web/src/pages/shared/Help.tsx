import { PageHeader } from '../../components/ui';
import { usePageTitle } from '../../lib/hooks';
import { useAuth, type Role } from '../../lib/auth';

const COMMON = [
  {
    q: 'How do I navigate UniSphere?',
    a: 'Use the sidebar on the left to move between sections. The search bar at the top (Ctrl+K or "/") finds courses, events, materials and pages; press Enter for the full results page.',
  },
  { q: 'How do I switch between light and dark mode?', a: 'Use the sun/moon button in the top bar, or pick a theme on the Settings page.' },
];

const BY_ROLE: Record<Role, Array<{ q: string; a: string }>> = {
  Student: [
    { q: 'How do I track my academic progress?', a: 'Visit the Academic Advisor page to see your GPA, completed courses, attendance rate, graduation audit and personalized insights.' },
    { q: 'How do I plan my study schedule?', a: 'Use the Study Planner to create and manage weekly study activities. Mark them as complete as you finish them.' },
    { q: 'How do I find career opportunities?', a: 'Visit the Career Assistant page to explore career paths, skill gaps, and internship recommendations based on your academic profile.' },
    { q: 'How do I search for research papers?', a: 'Go to the Research Hub and use the search bar to find academic papers. You can also view trending papers on the main page.' },
  ],
  Graduate: [
    { q: 'How do I get better job matches?', a: 'Complete your Career Profile and add your skills; upload your CV in the CV & Resume Studio. Matches and the skill-gap report update from that data.' },
    { q: 'How do the AI tools work?', a: 'CV analysis, interview feedback and roadmap suggestions use the AI provider configured on the server. If none is configured you will see a clear message instead of made-up output.' },
  ],
  Doctor: [
    { q: 'Which courses can I manage?', a: 'Only courses an administrator assigned to you. Materials, announcements, analytics and exams are all limited to those courses.' },
    { q: 'How does the Exam Assistant work?', a: 'Pick a course and the materials to use, generate questions, review and edit them, save a draft and publish it when you are ready. Students never see correct answers.' },
  ],
  Admin: [
    { q: 'How do I give a doctor access to a course?', a: 'Open the Admin Panel → Assignments tab and create an instructor assignment for the course and term.' },
    { q: 'How do I review graduation applications?', a: 'Open the Admin Panel → Graduation tab. Approving an application confers the degree and notifies the student.' },
  ],
};

export default function Help() {
  usePageTitle('Help & Support');
  const { role } = useAuth();
  const faqs = [...(role ? BY_ROLE[role] : []), ...COMMON];
  return (
    <>
      <PageHeader title="Help & Support" subtitle="Get help with using UniSphere 360+." />
      <div className="card narrow-card">
        <div className="card-header">
          <h3>Frequently Asked Questions</h3>
        </div>
        <div className="faq-list">
          {faqs.map((f, fIdx) => (
            <div className="faq-item" key={`${f.q}-${fIdx}`}>
              <h4>{f.q}</h4>
              <p>{f.a}</p>
            </div>
          ))}
        </div>
      </div>
      <div className="card narrow-card section-gap-top">
        <div className="card-header">
          <h3>Contact Support</h3>
        </div>
        <div className="contact-info">
          <p>
            UniSphere 360+ is a demo platform, so this page does not point to a real help desk. For issues with your account, courses, or grades, contact your
            university&apos;s registrar or program coordinator through the channels your institution provides.
          </p>
          <p>Technical problems with the app itself can be reported to your on-campus IT service desk, using the same channel you use for other edu-tech tools.</p>
        </div>
      </div>
    </>
  );
}
