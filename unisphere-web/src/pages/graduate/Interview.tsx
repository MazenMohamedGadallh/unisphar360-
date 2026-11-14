import { useState } from 'react';
import { Async, ConfirmDialog, ErrorBox, PageHeader } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, usePageTitle } from '../../lib/hooks';
import { fmtDate } from '../../lib/format';
import type { InterviewQuestionDto, InterviewSessionDto } from '../../types/dto';

export default function Interview() {
  usePageTitle('AI Interview Coach');
  const sessions = useApi<InterviewSessionDto[]>('/api/grad/interview/sessions');
  const [role, setRole] = useState('Backend Developer');
  const [useAi, setUseAi] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const selected = useApi<InterviewSessionDto>(selectedId ? `/api/grad/interview/sessions/${selectedId}` : null);
  const [drafts, setDrafts] = useState<Record<number, string>>({});
  const [busyQ, setBusyQ] = useState<number | null>(null);
  const [deleting, setDeleting] = useState<InterviewSessionDto | null>(null);
  const action = useAction();

  const start = () =>
    action.run(async () => {
      const s = await api.post<InterviewSessionDto>('/api/grad/interview/sessions', { targetRole: role.trim() || null, jobListingId: null, useAi });
      await sessions.reload();
      if (s?.id) {
        setDrafts({});
        setSelectedId(s.id);
      }
    });

  const draftFor = (q: InterviewQuestionDto) => drafts[q.id] ?? q.answerText ?? '';

  const answer = (q: InterviewQuestionDto) =>
    action.run(async () => {
      if (!selectedId) return;
      setBusyQ(q.id);
      try {
        const updated = await api.post<InterviewQuestionDto>(`/api/grad/interview/sessions/${selectedId}/questions/${q.id}/answer`, { answerText: draftFor(q) });
        if (updated)
          selected.setData((s) => (s ? { ...s, questions: (s.questions ?? []).map((x) => (x.id === updated.id ? updated : x)) } : s));
        void sessions.reload();
      } finally {
        setBusyQ(null);
      }
    });

  const remove = () =>
    action.run(async () => {
      if (!deleting) return;
      await api.del(`/api/grad/interview/sessions/${deleting.id}`);
      if (selectedId === deleting.id) setSelectedId(null);
      setDeleting(null);
      await sessions.reload();
    });

  return (
    <>
      <PageHeader title="AI Interview Coach" subtitle="Practice with AI-generated questions and get instant feedback on your answers" />
      <div className="card section-gap">
        <div className="card-header">
          <h3>Start a session</h3>
        </div>
        <div className="form-inline">
          <div className="form-group">
            <label htmlFor="grIntRole">Target role</label>
            <input id="grIntRole" value={role} onChange={(e) => setRole(e.target.value)} placeholder="Backend Engineer" maxLength={100} />
          </div>
          <div className="form-group">
            <label htmlFor="grIntMode">Question source</label>
            <select id="grIntMode" value={String(useAi)} onChange={(e) => setUseAi(e.target.value === 'true')}>
              <option value="false">Curated bank (no AI key needed)</option>
              <option value="true">AI-generated (requires AI key)</option>
            </select>
          </div>
          <button className="btn btn-primary" onClick={() => void start()} disabled={action.busy}>
            {action.busy && busyQ === null ? 'Preparing…' : 'Start session'}
          </button>
        </div>
        <p className="muted small-print">Without an AI key configured, choose the curated bank: real questions, standard grading.</p>
      </div>

      <ErrorBox error={action.error} />

      <div className="card section-gap">
        <div className="card-header">
          <h3>Practice sessions</h3>
        </div>
        <Async res={sessions} loadingLabel="Loading sessions…" emptyIcon="message-square" empty="No practice sessions yet.">
          {(list) => (
            <ul className="grad-list">
              {list.map((s) => (
                <li key={s.id} className="row-between">
                  <div>
                    <strong>{s.targetRole}</strong> <span className="grad-badge">{s.questionSource}</span>
                    <span className="muted">
                      {' '}
                      · {s.answeredCount}/{s.totalCount} answered · {fmtDate(s.createdAtUtc)}
                    </span>
                  </div>
                  <div className="row-actions">
                    <button className={`btn btn-sm ${selectedId === s.id ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setSelectedId(s.id)}>
                      Open
                    </button>
                    <button className="btn btn-sm btn-danger" onClick={() => setDeleting(s)}>
                      Delete
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Async>
      </div>

      {selectedId && (
        <div className="card">
          <Async res={selected} loadingLabel="Loading session…">
            {(s) => (
              <>
                <div className="card-header">
                  <h3>
                    Session: {s.targetRole} <span className="grad-badge">{s.questionSource}</span>
                  </h3>
                  {s.providerName && (
                    <span className="muted">
                      {s.providerName} / {s.model}
                    </span>
                  )}
                </div>
                {(s.questions ?? []).map((q, i) => (
                  <div className="interview-qa" key={q.id}>
                    <p>
                      <strong>Q{i + 1}:</strong> {q.questionText}
                    </p>
                    <textarea rows={3} value={draftFor(q)} onChange={(e) => setDrafts({ ...drafts, [q.id]: e.target.value })} placeholder="Type your answer…" maxLength={4000} />
                    <div className="row-actions">
                      <button className="btn btn-primary btn-sm" onClick={() => void answer(q)} disabled={draftFor(q).trim().length <= 8 || busyQ !== null}>
                        {busyQ === q.id ? 'Grading…' : q.answerText ? 'Re-grade' : 'Submit answer'}
                      </button>
                      {q.feedbackScore !== null && <span className="grad-badge">Score {q.feedbackScore}/10</span>}
                    </div>
                    {q.feedback && (
                      <div className="coach-feedback">
                        <strong>Coach feedback</strong>
                        <p>{q.feedback}</p>
                      </div>
                    )}
                  </div>
                ))}
              </>
            )}
          </Async>
        </div>
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete session?"
          message={`Delete the "${deleting.targetRole}" practice session and its answers?`}
          busy={action.busy}
          onConfirm={() => void remove()}
          onCancel={() => setDeleting(null)}
        />
      )}
    </>
  );
}
