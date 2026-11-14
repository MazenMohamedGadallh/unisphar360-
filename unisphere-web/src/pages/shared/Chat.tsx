import { ChatPanel } from '../../components/ChatPanel';
import { PageHeader } from '../../components/ui';
import { usePageTitle } from '../../lib/hooks';
import { useAuth } from '../../lib/auth';

export default function Chat() {
  usePageTitle('AI Assistant');
  const { role } = useAuth();
  return (
    <>
      <PageHeader
        title="AI Assistant"
        subtitle={role === 'Doctor' ? 'Chat with UniSphere Assistant about your teaching' : 'Chat with UniSphere Assistant about your courses and studies'}
      />
      <ChatPanel />
    </>
  );
}
