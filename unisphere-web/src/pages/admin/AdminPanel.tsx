import { PageHeader, Tabs } from '../../components/ui';
import { usePageTitle } from '../../lib/hooks';
import { useSearchParams } from '../../lib/router';
import { AssignmentsTab, AuditTab, CommunicationTab, DepartmentsTab, GraduationTab, OverviewTab, RiskTab, SystemTab, UsersTab } from './AdminTabs';

const TABS = [
  { key: 'overview', label: 'Overview' },
  { key: 'users', label: 'Users' },
  { key: 'instructors', label: 'Assignments' },
  { key: 'graduation', label: 'Graduation' },
  { key: 'communication', label: 'Notifications & Events' },
  { key: 'risk', label: 'At-risk students' },
  { key: 'departments', label: 'Departments' },
  { key: 'audit', label: 'Audit log' },
  { key: 'system', label: 'System' },
] as const;

type TabKey = (typeof TABS)[number]['key'];

export default function AdminPanel() {
  usePageTitle('Admin Panel');
  const [params, setParams] = useSearchParams();
  const raw = params.get('tab');
  const tab: TabKey = TABS.some((t) => t.key === raw) ? (raw as TabKey) : 'overview';

  return (
    <>
      <PageHeader title="Admin Panel" subtitle="System analytics, users and control." />
      <Tabs tabs={[...TABS]} value={tab} onChange={(k) => setParams({ tab: k === 'overview' ? null : k })} />
      {tab === 'overview' && <OverviewTab />}
      {tab === 'users' && <UsersTab />}
      {tab === 'instructors' && <AssignmentsTab />}
      {tab === 'graduation' && <GraduationTab />}
      {tab === 'communication' && <CommunicationTab />}
      {tab === 'risk' && <RiskTab />}
      {tab === 'departments' && <DepartmentsTab />}
      {tab === 'audit' && <AuditTab />}
      {tab === 'system' && <SystemTab />}
    </>
  );
}
