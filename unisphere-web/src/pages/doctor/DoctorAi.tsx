import { useState } from 'react';
import { Async, ErrorBox, PageHeader, Tabs } from '../../components/ui';
import { api } from '../../lib/api';
import { useAction, useApi, usePageTitle } from '../../lib/hooks';
import type { DoctorCourseDto, DoctorMaterialDto, LectureAnswerDto, LectureSummaryDto, QuizGenerationResponse } from '../../types/dto';

interface AiStatus {
  available: boolean;
  providers: unknown[];
  message: string;
}

type Tab = 'quiz' | 'summarize' | 'ask';

export default function DoctorAi() {
  usePageTitle('AI Teaching Assistant');
  const status = useApi<AiStatus>('/api/doctor/ai/status');
  const courses = useApi<DoctorCourseDto[]>('/api/doctor/courses');
  const [courseId, setCourseId] = useState(0);
  const materials = useApi<DoctorMaterialDto[]>(courseId > 0 ? `/api/doctor/courses/${courseId}/materials` : null);
  const [tab, setTab] = useState<Tab>('quiz');
  const [quizMaterial, setQuizMaterial] = useState(0);
  const [count, setCount] = useState(5);
  const [summaryMaterial, setSummaryMaterial] = useState(0);
  const [question, setQuestion] = useState('');
  const [quiz, setQuiz] = useState<QuizGenerationResponse | null>(null);
  const [summary, setSummary] = useState<LectureSummaryDto | null>(null);
  const [answer, setAnswer] = useState<LectureAnswerDto | null>(null);
  const action = useAction();
  const available = status.data?.available ?? false;
  const noProvider = status.data && !status.data.available;

  const changeCourse = (id: number) => {
    setCourseId(id);
    setQuizMaterial(0);
    setSummaryMaterial(0);
    setQuiz(null);
    setSummary(null);
    setAnswer(null);
  };

  const generate = () =>
    action.run(async () => {
      setQuiz(await api.post<QuizGenerationResponse>('/api/doctor/ai/quiz', { courseId, materialId: quizMaterial || null, questionCount: count }));
    });
  const summarize = () =>
    action.run(async () => {
      setSummary(await api.post<LectureSummaryDto>('/api/doctor/ai/summarize', { courseId, materialId: summaryMaterial }));
    });
  const ask = () =>
    action.run(async () => {
      setAnswer(await api.post<LectureAnswerDto>('/api/doctor/ai/ask', { courseId, question: question.trim() }));
    });

  return (
    <>
      <PageHeader
        title="AI Teaching Assistant"
        subtitle="Draft quizzes, summarize lecture materials and ask grounded questions. Generated content is always a draft and must be reviewed by you before use."
      />
      {noProvider && (
        <div className="alert alert-error alert-row">
          <span>{status.data?.message} Set OPENAI_API_KEY, ANTHROPIC_API_KEY, GEMINI_API_KEY or OPENROUTER_API_KEY (or run Ollama) on the server, then retry.</span>
          <button className="btn btn-sm btn-secondary" onClick={() => void status.reload()}>
            Retry
          </button>
        </div>
      )}
      <ErrorBox error={action.error} />

      <Async res={courses} loadingLabel="Loading your courses…" emptyIcon="book-open" empty="You are not assigned to any course yet. Contact an administrator.">
        {(list) => (
          <div className="form-inline card filter-card">
            <div className="form-group">
              <label htmlFor="aiCourse">Course</label>
              <select id="aiCourse" value={courseId} onChange={(e) => changeCourse(Number(e.target.value))}>
                <option value={0}>Select a course…</option>
                {list.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.code} — {c.displayTitle || c.title}
                  </option>
                ))}
              </select>
            </div>
            {courseId > 0 && materials.data && <span className="muted">{materials.data.length} material(s) available as context</span>}
          </div>
        )}
      </Async>

      <Tabs
        tabs={[
          { key: 'quiz', label: 'Generate Quiz' },
          { key: 'summarize', label: 'Summarize Lecture' },
          { key: 'ask', label: 'Ask a Question' },
        ]}
        value={tab}
        onChange={setTab}
      />

      {tab === 'quiz' && (
        <div className="card">
          <div className="card-header">
            <h3>Generate a Quiz</h3>
          </div>
          <div className="form-inline">
            <div className="form-group">
              <label>Source material (optional)</label>
              <select value={quizMaterial} onChange={(e) => setQuizMaterial(Number(e.target.value))} disabled={courseId <= 0}>
                <option value={0}>Any course material</option>
                {(materials.data ?? []).map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.title}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label>Number of questions</label>
              <input type="number" min={1} max={15} value={count} onChange={(e) => setCount(Math.max(1, Math.min(15, Number(e.target.value) || 1)))} />
            </div>
            <button className="btn btn-primary" onClick={() => void generate()} disabled={action.busy || !available || courseId <= 0}>
              {action.busy ? 'Generating…' : 'Generate Quiz'}
            </button>
          </div>
          {quiz && (
            <>
              <p className="rec-track">
                Provider: {quiz.provider} / {quiz.model} — <strong>draft content, review before use</strong>.
              </p>
              <ol className="quiz-list">
                {quiz.questions.map((q, i) => (
                  <li key={i}>
                    <div className="rec-title">{q.question}</div>
                    <ul>
                      {q.options.map((o, j) => (
                        <li key={j}>
                          {o} {j === q.correctIndex && <span className="status-badge excellent">Correct</span>}
                        </li>
                      ))}
                    </ul>
                    {q.explanation && <div className="rec-track">{q.explanation}</div>}
                    {q.reference && <div className="rec-track">Source: {q.reference}</div>}
                  </li>
                ))}
              </ol>
            </>
          )}
        </div>
      )}

      {tab === 'summarize' && (
        <div className="card">
          <div className="card-header">
            <h3>Summarize a Lecture</h3>
          </div>
          <div className="form-inline">
            <div className="form-group">
              <label>Material</label>
              <select value={summaryMaterial} onChange={(e) => setSummaryMaterial(Number(e.target.value))} disabled={courseId <= 0}>
                <option value={0}>Select a material…</option>
                {(materials.data ?? []).map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.title} ({m.type})
                  </option>
                ))}
              </select>
            </div>
            <button className="btn btn-primary" onClick={() => void summarize()} disabled={action.busy || !available || summaryMaterial <= 0}>
              {action.busy ? 'Summarizing…' : 'Summarize'}
            </button>
          </div>
          {summary && (
            <div className="chart-container">
              <h4>{summary.title}</h4>
              <p className="pre-wrap">{summary.summary}</p>
              <p className="rec-track">
                Provider: {summary.provider} / {summary.model}
              </p>
            </div>
          )}
        </div>
      )}

      {tab === 'ask' && (
        <div className="card">
          <div className="card-header">
            <h3>Ask a Question About Your Course</h3>
          </div>
          <div className="form-group">
            <label>Your question</label>
            <textarea rows={3} value={question} onChange={(e) => setQuestion(e.target.value)} maxLength={1000} placeholder="e.g. What does the course material say about normalization?" />
          </div>
          <button className="btn btn-primary" onClick={() => void ask()} disabled={action.busy || !available || courseId <= 0 || !question.trim()}>
            {action.busy ? 'Thinking…' : 'Ask'}
          </button>
          {answer && (
            <div className="chart-container">
              <p className="pre-wrap">{answer.answer}</p>
              {answer.grounded && answer.sources.length > 0 ? (
                <p className="rec-track">Grounded in: {answer.sources.join(', ')}</p>
              ) : (
                <p className="rec-track">
                  <span className="status-badge needs-attention">Not grounded</span> This answer was not based on course materials — verify before using it.
                </p>
              )}
              <p className="rec-track">
                Provider: {answer.provider} / {answer.model}
              </p>
            </div>
          )}
        </div>
      )}
    </>
  );
}
