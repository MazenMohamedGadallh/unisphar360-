import { ChatPanel } from '../../components/ChatPanel';
import { PageHeader } from '../../components/ui';
import { usePageTitle } from '../../lib/hooks';

export default function GradAssistant() {
  usePageTitle('AI Career Assistant');
  return (
    <>
      <PageHeader title="AI Career Assistant" subtitle="Chat about your career — it knows your profile, target roles and job activity" />
      <ChatPanel
        placeholder="Ask about your job search, CV or interviews…"
        emptyText="Ask about roles that fit you, how to improve your CV, or how to prepare for an interview."
      />
    </>
  );
}
