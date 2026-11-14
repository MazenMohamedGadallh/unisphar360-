import { useState } from 'react';
import { Icon } from '../../components/Icon';
import { Async, ErrorBox, Modal, PageHeader } from '../../components/ui';
import { api, errorMessage } from '../../lib/api';
import { useApi, usePageTitle } from '../../lib/hooks';
import { fmtDateTime } from '../../lib/format';
import type { StudentExamDto } from '../../types/dto';

export default function StudentExams() {
  usePageTitle('My Exams');
  const exams = useApi<StudentExamDto[]>('/api/student/exams');
  const [selected, setSelected] = useState<StudentExamDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [opening, setOpening] = useState<number | null>(null);

  const open = async (id: number) => {
    setOpening(id);
    setError(null);
    try {
      setSelected(await api.get<StudentExamDto>(`/api/student/exams/${id}`));
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setOpening(null);
    }
  };

  return (
    <>
      <PageHeader title="My Exams" subtitle="Published exams for your currently registered courses." />
      <ErrorBox error={error} />
      <div className="card">
        <Async res={exams} loadingLabel="Loading exams…" emptyIcon="clipboard-check" empty="No exams available yet.">
          {(list) => (
            <div className="table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Exam</th>
                    <th>Course</th>
                    <th>Questions</th>
                    <th>Marks</th>
                    <th>Published</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {[...list]
                    .sort((a, b) => (b.publishedAtUtc ?? b.createdAtUtc).localeCompare(a.publishedAtUtc ?? a.createdAtUtc))
                    .map((e) => (
                      <tr key={e.id}>
                        <td>
                          <strong>{e.title}</strong>
                        </td>
                        <td>
                          {e.courseTitle} <span className="muted">({e.courseCode})</span>
                        </td>
                        <td>{e.questionCount}</td>
                        <td>{e.marks ?? '—'}</td>
                        <td>{fmtDateTime(e.publishedAtUtc ?? e.createdAtUtc)}</td>
                        <td>
                          <button className="btn btn-sm btn-secondary" disabled={opening === e.id} onClick={() => void open(e.id)}>
                            {opening === e.id ? 'Opening…' : 'Open'}
                          </button>
                        </td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          )}
        </Async>
      </div>

      {selected && (
        <Modal title={selected.title} onClose={() => setSelected(null)} footer={<button className="btn btn-primary" onClick={() => setSelected(null)}>Back</button>}>
          <ul className="plain-list detail-list">
            <li>
              <Icon name="book-open" /> {selected.courseTitle} ({selected.courseCode})
            </li>
            <li>
              <Icon name="clipboard-list" /> {selected.questionCount} questions
            </li>
            {selected.marks !== null && (
              <li>
                <Icon name="award" /> Marks: {selected.marks}
              </li>
            )}
            <li>
              <Icon name="calendar-days" /> Published {fmtDateTime(selected.publishedAtUtc ?? selected.createdAtUtc)}
            </li>
          </ul>
          <p className="muted">Your doctor will share how and when the exam is taken. Correct answers are never shown here.</p>
        </Modal>
      )}
    </>
  );
}
