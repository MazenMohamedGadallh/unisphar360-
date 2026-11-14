// Exam Assistant (/api/doctor/exam-assistant):
//   courses → courses/{id}/materials → generate (RAG-backed, may return ragWarning)
//   → review/edit → POST exams (draft) → PUT exams/{id} → POST exams/{id}/publish.

import { useState } from 'react';
import { Icon } from '../../components/Icon';
import { Alert, Async, ConfirmDialog, ErrorBox, PageHeader, StatusBadge, Tabs } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, useFlash, usePageTitle } from '../../lib/hooks';
import { fmtDateTime } from '../../lib/format';
import type { ExamCatalogCourseDto, ExamDto, ExamGenerationResponse, ExamListDto, GeneratedExamQuestionDto, MaterialDto } from '../../types/dto';

type View = 'setup' | 'review' | 'drafts';
const DIFFICULTIES = ['easy', 'medium', 'hard', 'mixed'];

interface Question {
  questionText: string;
  questionType: string;
  correctAnswer: string;
  explanation: string;
  sourceMaterial: string;
  options: string[];
}

function parseOptions(json: string | null): string[] {
  if (!json) return [];
  try {
    const v = JSON.parse(json) as unknown;
    return Array.isArray(v) ? v.map(String) : [];
  } catch {
    return [];
  }
}

const fromGenerated = (q: GeneratedExamQuestionDto): Question => ({
  questionText: q.questionText,
  questionType: q.questionType,
  correctAnswer: q.correctAnswer ?? '',
  explanation: q.explanation ?? '',
  sourceMaterial: q.sourceMaterial ?? '',
  options: parseOptions(q.optionsJson),
});

function QuestionEditor({ q, onSave, onCancel }: { q: Question; onSave: (q: Question) => void; onCancel: () => void }) {
  const [d, setD] = useState<Question>({ ...q, options: [...q.options] });
  return (
    <div className="edit-form">
      <div className="form-group">
        <label>Question</label>
        <textarea rows={3} value={d.questionText} onChange={(e) => setD({ ...d, questionText: e.target.value })} />
      </div>
      {d.questionType === 'MCQ' && (
        <div className="form-group">
          <label>Options</label>
          {d.options.map((o, i) => (
            <div className="option-row" key={i}>
              <input value={o} onChange={(e) => setD({ ...d, options: d.options.map((x, j) => (j === i ? e.target.value : x)) })} />
              <button type="button" className="icon-btn icon-btn-danger" aria-label="Remove option" onClick={() => setD({ ...d, options: d.options.filter((_, j) => j !== i) })}>
                <Icon name="x" />
              </button>
            </div>
          ))}
          <button type="button" className="btn btn-sm btn-secondary" onClick={() => setD({ ...d, options: [...d.options, ''] })}>
            <Icon name="plus" /> Add option
          </button>
        </div>
      )}
      <div className="form-row">
        <div className="form-group">
          <label>Correct answer</label>
          <input value={d.correctAnswer} onChange={(e) => setD({ ...d, correctAnswer: e.target.value })} />
        </div>
        <div className="form-group">
          <label>Source material</label>
          <input value={d.sourceMaterial} onChange={(e) => setD({ ...d, sourceMaterial: e.target.value })} />
        </div>
      </div>
      <div className="form-group">
        <label>Explanation</label>
        <input value={d.explanation} onChange={(e) => setD({ ...d, explanation: e.target.value })} />
      </div>
      <div className="row-actions">
        <button className="btn btn-sm btn-primary" onClick={() => onSave({ ...d, options: d.options.map((o) => o.trim()).filter(Boolean) })} disabled={!d.questionText.trim()}>
          Save
        </button>
        <button className="btn btn-sm btn-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </div>
  );
}

function QuestionList({ questions, setQuestions, showAnswers, readOnly }: { questions: Question[]; setQuestions: (q: Question[]) => void; showAnswers: boolean; readOnly?: boolean }) {
  const [editing, setEditing] = useState<number | null>(null);
  return (
    <div className="questions-list">
      {questions.map((q, i) => (
        <div className={`question-card ${editing === i ? 'editing' : ''}`} key={i}>
          <div className="question-header">
            <span className="question-number">{i + 1}</span>
            <span className="grad-badge">{q.questionType}</span>
            {!readOnly && editing !== i && (
              <div className="row-actions">
                <button className="btn btn-sm btn-secondary" onClick={() => setEditing(i)}>
                  Edit
                </button>
                <button className="btn btn-sm btn-danger" onClick={() => setQuestions(questions.filter((_, j) => j !== i))}>
                  Remove
                </button>
              </div>
            )}
          </div>
          {editing === i ? (
            <QuestionEditor
              q={q}
              onCancel={() => setEditing(null)}
              onSave={(nq) => {
                setQuestions(questions.map((x, j) => (j === i ? nq : x)));
                setEditing(null);
              }}
            />
          ) : (
            <>
              <div className="question-text" dir="auto">
                {q.questionText}
              </div>
              {q.options.length > 0 && (
                <ol className="mcq-options" type="A">
                  {q.options.map((o, j) => (
                    <li key={j} dir="auto">
                      {o}
                    </li>
                  ))}
                </ol>
              )}
              {showAnswers && (
                <div className="answer-block">
                  <p>
                    <strong>Answer:</strong> {q.correctAnswer || '—'}
                  </p>
                  {q.explanation && (
                    <p>
                      <strong>Explanation:</strong> {q.explanation}
                    </p>
                  )}
                  {q.sourceMaterial && (
                    <p>
                      <strong>Source:</strong> {q.sourceMaterial}
                    </p>
                  )}
                </div>
              )}
            </>
          )}
        </div>
      ))}
    </div>
  );
}

export default function ExamAssistant() {
  usePageTitle('Exam Assistant');
  const [view, setView] = useState<View>('setup');
  const courses = useApi<ExamCatalogCourseDto[]>('/api/doctor/exam-assistant/courses');
  const [courseId, setCourseId] = useState<number | null>(null);
  const materials = useApi<MaterialDto[]>(courseId ? `/api/doctor/exam-assistant/courses/${courseId}/materials` : null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [count, setCount] = useState(10);
  const [difficulty, setDifficulty] = useState('mixed');
  const [language, setLanguage] = useState('en');
  const [questions, setQuestions] = useState<Question[]>([]);
  const [ragWarning, setRagWarning] = useState<string | null>(null);
  const [provider, setProvider] = useState('');
  const [showAnswers, setShowAnswers] = useState(true);
  const [title, setTitle] = useState('');
  const [examId, setExamId] = useState<number | null>(null);
  const [examStatus, setExamStatus] = useState('');
  const [marks, setMarks] = useState('');
  const drafts = useApi<ExamListDto[]>(view === 'drafts' ? '/api/doctor/exam-assistant/exams' : null);
  const [deleting, setDeleting] = useState<ExamListDto | null>(null);
  const [flash, setFlash] = useFlash(5000);
  const action = useAction();

  const pickCourse = (id: number | null) => {
    setCourseId(id);
    setSelected(new Set());
  };

  const generate = () =>
    action.run(async () => {
      const res = await api.post<ExamGenerationResponse>('/api/doctor/exam-assistant/generate', {
        courseId,
        materialIds: Array.from(selected),
        questionCount: count,
        difficulty,
        language,
      });
      setQuestions(res.questions.map(fromGenerated));
      setRagWarning(res.ragWarning ?? null);
      setProvider(`${res.provider} / ${res.model}`);
      setTitle(`${res.courseCode} exam — ${new Date().toLocaleDateString()}`);
      setExamId(null);
      setExamStatus('');
      setMarks('');
      setView('review');
    });

  const toRequest = () => ({
    courseId,
    title: title.trim(),
    questionCount: questions.length,
    questionTypes: Array.from(new Set(questions.map((q) => q.questionType))).join(',') || 'MCQ',
    difficulty,
    language,
    materialsUsed: materials.data?.filter((m) => selected.has(m.id)).map((m) => m.title).join(', ') || null,
    questions: questions.map((q, i) => ({
      id: 0,
      examId: examId ?? 0,
      questionText: q.questionText,
      questionType: q.questionType,
      correctAnswer: q.correctAnswer || null,
      explanation: q.explanation || null,
      sourceMaterial: q.sourceMaterial || null,
      optionsJson: q.options.length ? JSON.stringify(q.options) : null,
      orderIndex: i,
      marks: null,
    })),
  });

  const save = () =>
    action.run(async () => {
      if (!title.trim()) throw new Error('Give the exam a title first.');
      const saved = examId
        ? await api.put<ExamDto>(`/api/doctor/exam-assistant/exams/${examId}`, toRequest())
        : await api.post<ExamDto>('/api/doctor/exam-assistant/exams', toRequest());
      setExamId(saved.id);
      setExamStatus(saved.status);
      setFlash(examId ? 'Draft updated.' : 'Draft saved. You can publish it now or later from My Drafts.');
    });

  const publish = () =>
    action.run(async () => {
      if (!examId) throw new Error('Save the draft before publishing.');
      const res = await api.post<ExamDto>(`/api/doctor/exam-assistant/exams/${examId}/publish`, {
        examId,
        title: title.trim(),
        marks: marks ? Number(marks) : null,
      });
      setExamStatus(res.status);
      setFlash('Exam published. Students registered in this course can now see it (answers are never shown to them).');
    });

  const open = (id: number) =>
    action.run(async () => {
      const exam = await api.get<ExamDto>(`/api/doctor/exam-assistant/exams/${id}`);
      setCourseId(exam.courseId);
      setSelected(new Set());
      setDifficulty(exam.difficulty);
      setLanguage(exam.language);
      setQuestions(
        [...exam.questions]
          .sort((a, b) => a.orderIndex - b.orderIndex)
          .map((q) => fromGenerated({ ...q })),
      );
      setTitle(exam.title);
      setExamId(exam.id);
      setExamStatus(exam.status);
      setMarks(exam.marks !== null ? String(exam.marks) : '');
      setRagWarning(null);
      setProvider('');
      setView('review');
    });

  const remove = () =>
    action.run(async () => {
      if (!deleting) return;
      await api.del(`/api/doctor/exam-assistant/exams/${deleting.id}`);
      if (examId === deleting.id) {
        setExamId(null);
        setQuestions([]);
      }
      setDeleting(null);
      await drafts.reload();
    });

  const published = examStatus === 'Published';

  return (
    <>
      <PageHeader title="Exam Assistant" subtitle="Generate exams from your own course materials, review them, then publish to your students." />
      <Tabs
        tabs={[
          { key: 'setup', label: 'Generate Exam' },
          { key: 'review', label: `Review (${questions.length})` },
          { key: 'drafts', label: 'My Exams' },
        ]}
        value={view}
        onChange={setView}
      />
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}

      {view === 'setup' && (
        <div className="card">
          <div className="card-header">
            <h3>Setup Exam</h3>
          </div>
          <Async res={courses} loadingLabel="Loading courses…" emptyIcon="book-open" empty="You are not assigned to any course yet.">
            {(list) => (
              <>
                <div className="form-group">
                  <label>Course</label>
                  <select value={courseId ?? ''} onChange={(e) => pickCourse(e.target.value ? Number(e.target.value) : null)}>
                    <option value="">-- Select a course --</option>
                    {list.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.code} — {c.title}
                      </option>
                    ))}
                  </select>
                </div>
                {courseId && (
                  <div className="form-group">
                    <div className="row-between label-row">
                      <label>Materials</label>
                      {materials.data && materials.data.length > 0 && (
                        <button
                          type="button"
                          className="btn-link"
                          onClick={() => setSelected(selected.size === materials.data!.length ? new Set() : new Set(materials.data!.map((m) => m.id)))}
                        >
                          {selected.size === materials.data.length ? 'Clear all' : 'Select all'}
                        </button>
                      )}
                    </div>
                    <Async res={materials} loadingLabel="Loading materials…" emptyIcon="file-text" empty="This course has no materials yet. Add some on the Materials page first.">
                      {(ms) => (
                        <div className="material-checklist">
                          {ms.map((m) => (
                            <label className="check-row" key={m.id}>
                              <input
                                type="checkbox"
                                checked={selected.has(m.id)}
                                onChange={() => {
                                  const n = new Set(selected);
                                  if (n.has(m.id)) n.delete(m.id);
                                  else n.add(m.id);
                                  setSelected(n);
                                }}
                              />
                              <span>{m.title}</span>
                              <span className="grad-badge">{m.type}</span>
                              {m.hasUploadedFile && <span className="muted">· {m.uploadedFileName}</span>}
                            </label>
                          ))}
                        </div>
                      )}
                    </Async>
                  </div>
                )}
                <div className="form-inline">
                  <div className="form-group">
                    <label>Number of questions</label>
                    <input type="number" min={1} max={30} value={count} onChange={(e) => setCount(Math.max(1, Math.min(30, Number(e.target.value) || 1)))} />
                  </div>
                  <div className="form-group">
                    <label>Difficulty</label>
                    <select value={difficulty} onChange={(e) => setDifficulty(e.target.value)}>
                      {DIFFICULTIES.map((d, dIdx) => (
                        <option key={`${d}-${dIdx}`} value={d}>
                          {d[0].toUpperCase() + d.slice(1)}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="form-group">
                    <label>Language</label>
                    <select value={language} onChange={(e) => setLanguage(e.target.value)}>
                      <option value="en">English</option>
                      <option value="ar">العربية (Arabic)</option>
                    </select>
                  </div>
                </div>
                <div className="form-actions">
                  <button className="btn btn-primary" onClick={() => void generate()} disabled={action.busy || !courseId || selected.size === 0}>
                    <Icon name="sparkles" /> {action.busy ? 'Generating…' : 'Generate Questions'}
                  </button>
                  {courseId && selected.size === 0 && <span className="field-hint">Select at least one material to enable generation.</span>}
                </div>
              </>
            )}
          </Async>
        </div>
      )}

      {view === 'review' &&
        (questions.length === 0 ? (
          <div className="card">
            <div className="empty-state">Nothing to review yet. Generate questions or open a saved exam.</div>
          </div>
        ) : (
          <div className="card">
            <div className="card-header">
              <h3>
                Review Questions ({questions.length}) {examStatus && <StatusBadge status={published ? 'passed' : 'in-progress'} label={examStatus} />}
              </h3>
              <label className="check-row inline">
                <input type="checkbox" checked={showAnswers} onChange={(e) => setShowAnswers(e.target.checked)} /> Show answers
              </label>
            </div>
            {ragWarning && <div className="alert alert-warning">{ragWarning}</div>}
            {provider && <p className="rec-track">Generated by {provider} — review every question before publishing.</p>}
            <div className="form-inline">
              <div className="form-group grow">
                <label>Exam title</label>
                <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} disabled={published} />
              </div>
              <div className="form-group">
                <label>Total marks</label>
                <input type="number" min={0} value={marks} onChange={(e) => setMarks(e.target.value)} disabled={published} />
              </div>
            </div>
            <QuestionList questions={questions} setQuestions={setQuestions} showAnswers={showAnswers} readOnly={published} />
            <div className="form-actions">
              {!published && (
                <button className="btn btn-secondary" onClick={() => void save()} disabled={action.busy || questions.length === 0}>
                  {examId ? 'Save changes' : 'Save Draft'}
                </button>
              )}
              {!published && (
                <button className="btn btn-primary" onClick={() => void publish()} disabled={action.busy || !examId} title={examId ? '' : 'Save the draft first'}>
                  Publish Exam
                </button>
              )}
              {published && <span className="muted">This exam is published and can no longer be edited.</span>}
            </div>
          </div>
        ))}

      {view === 'drafts' && (
        <div className="card">
          <div className="card-header">
            <h3>My Exams</h3>
          </div>
          <Async res={drafts} loadingLabel="Loading…" emptyIcon="clipboard-list" empty="No saved exams yet.">
            {(list) => (
              <div className="table-wrapper">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Title</th>
                      <th>Course</th>
                      <th>Questions</th>
                      <th>Difficulty</th>
                      <th>Status</th>
                      <th>Created</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {[...list]
                      .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
                      .map((d) => (
                        <tr key={d.id}>
                          <td>
                            <strong>{d.title}</strong>
                          </td>
                          <td>
                            {d.courseCode} — {d.courseTitle}
                          </td>
                          <td>{d.questionCount}</td>
                          <td>{d.difficulty}</td>
                          <td>
                            <StatusBadge status={d.status === 'Published' ? 'passed' : 'in-progress'} label={d.status} />
                            {d.publishedAtUtc && <div className="muted small-print">{fmtDateTime(d.publishedAtUtc)}</div>}
                          </td>
                          <td>{fmtDateTime(d.createdAtUtc)}</td>
                          <td>
                            <div className="row-actions">
                              <button className="btn btn-sm btn-secondary" onClick={() => void open(d.id)}>
                                Open
                              </button>
                              <button className="btn btn-sm btn-danger" onClick={() => setDeleting(d)}>
                                Delete
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
            )}
          </Async>
        </div>
      )}

      {deleting && <ConfirmDialog title="Delete exam?" message={`Delete “${deleting.title}”? This cannot be undone.`} busy={action.busy} onConfirm={() => void remove()} onCancel={() => setDeleting(null)} />}
    </>
  );
}
