import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, errorMessage } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface ResearchPaper {
  id: number;
  title: string;
  authors: string;
  venue: string;
  year: number;
  category: string;
  abstract: string;
  url: string;
  trending: boolean;
}

interface ResearchHubResponse {
  papers: ResearchPaper[];
  categories: string[];
}

interface PaperSummary {
  title: string;
  summary: string;
}

interface QuizCourse {
  courseId: number;
  code: string;
  title: string;
}

interface PracticeQuizQuestion {
  id: number;
  question: string;
  options: string[];
  correctIndex: number;
  explanation: string;
  sourceTitle: string;
}

interface PracticeQuizPayload {
  courseLabel: string;
  questions: PracticeQuizQuestion[];
}

export function ResearchHub() {
  const navigate = useNavigate();
  const [tab, setTab] = useState<"papers" | "quiz">("papers");

  // Research Papers state
  const [data, setData] = useState<ResearchHubResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [category, setCategory] = useState("all");
  const [trendingOnly, setTrendingOnly] = useState(false);
  const [summaries, setSummaries] = useState<Record<number, PaperSummary>>({});
  const [summarizingId, setSummarizingId] = useState<number | null>(null);

  // Student AI Practice Quiz state
  const [courses, setCourses] = useState<QuizCourse[]>([]);
  const [selectedCourseId, setSelectedCourseId] = useState<string>("");
  const [materials, setMaterials] = useState<{ id: number; title: string }[]>([]);
  const [materialId, setMaterialId] = useState("");
  const [questionCount, setQuestionCount] = useState(5);
  const [quizLoading, setQuizLoading] = useState(false);
  const [quizError, setQuizError] = useState<string | null>(null);
  const [quiz, setQuiz] = useState<PracticeQuizPayload | null>(null);
  const [answers, setAnswers] = useState<Record<number, number>>({});
  const [submittedQuiz, setSubmittedQuiz] = useState(false);

  useEffect(() => {
    document.title = "Research Hub & Practice Quiz — UniSphere 360+";
  }, []);

  const loadPapers = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const get = (path: string) => fetch(`${API_BASE_URL}/api/research/${path}`, { headers: authHeaders() });
      const [res, trendRes] = await Promise.all([
        get(`papers?q=${encodeURIComponent(query.trim())}`),
        get("trending"),
      ]);
      if (res.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const trendingIds = new Set<number>(trendRes.ok ? (await trendRes.json()).map((t: any) => t.id) : []);
      const papers: ResearchPaper[] = (await res.json()).map((x: any) => ({
        id: x.id,
        title: x.title,
        authors: x.authors ?? "",
        venue: x.source ?? "",
        category: x.source ?? "other",
        year: x.publishedDate ? new Date(x.publishedDate).getFullYear() : 0,
        abstract: x.abstract ?? "",
        url: x.url ?? "",
        trending: trendingIds.has(x.id),
      }));
      setData({
        papers: papers.filter(
          (x) => (category === "all" || x.category === category) && (!trendingOnly || x.trending),
        ),
        categories: [...new Set(papers.map((x) => x.category))],
      });
    } catch {
      setError("Could not load Research Hub papers.");
    } finally {
      setLoading(false);
    }
  }, [navigate, query, category, trendingOnly]);

  useEffect(() => {
    void loadPapers();
  }, [loadPapers]);

  useEffect(() => {
    void (async () => {
      try {
        const res = await fetch(`${API_BASE_URL}/api/student/ai/courses`, { headers: authHeaders() });
        if (!res.ok) return;
        const list = (await res.json()) as QuizCourse[];
        setCourses(list);
        if (list.length) setSelectedCourseId(String(list[0].courseId));
      } catch {
        // Optional helper dropdown
      }
    })();
  }, []);

  useEffect(() => {
    setMaterials([]);
    setMaterialId("");
    if (!selectedCourseId) return;
    void (async () => {
      try {
        const res = await fetch(`${API_BASE_URL}/api/student/ai/courses/${selectedCourseId}/materials`, {
          headers: authHeaders(),
        });
        if (res.ok) setMaterials(await res.json());
      } catch {
        // The material picker is optional.
      }
    })();
  }, [selectedCourseId]);

  async function summarizePaper(paperId: number) {
    setSummarizingId(paperId);
    try {
      const res = await fetch(`${API_BASE_URL}/api/research/summarize/${paperId}`, { headers: authHeaders() });
      if (!res.ok) throw new Error();
      const summary = (await res.json()) as PaperSummary;
      setSummaries((prev) => ({ ...prev, [paperId]: summary }));
    } catch {
      // Ignore quietly
    } finally {
      setSummarizingId(null);
    }
  }

  async function generatePracticeQuiz(e: FormEvent) {
    e.preventDefault();
    setQuizLoading(true);
    setQuizError(null);
    setSubmittedQuiz(false);
    setAnswers({});
    try {
      const res = await fetch(`${API_BASE_URL}/api/student/ai/quiz`, {
        method: "POST",
        headers: { ...authHeaders(), "Content-Type": "application/json" },
        body: JSON.stringify({
          courseId: Number(selectedCourseId),
          materialId: materialId ? Number(materialId) : null,
          questionCount,
        }),
      });
      if (!res.ok) {
        setQuizError(await errorMessage(res, "Could not generate practice quiz."));
        return;
      }
      const body = await res.json();
      const course = courses.find((c) => String(c.courseId) === selectedCourseId);
      setQuiz({
        courseLabel: course ? `${course.code} — ${course.title}` : "Practice Quiz",
        questions: body.questions.map((q: any, id: number) => ({ ...q, id, sourceTitle: q.reference ?? "" })),
      });
    } catch {
      setQuizError("Could not generate practice quiz.");
    } finally {
      setQuizLoading(false);
    }
  }

  const correctCount =
    quiz && submittedQuiz
      ? quiz.questions.filter((q) => answers[q.id] === q.correctIndex).length
      : 0;

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Research Hub &amp; AI Practice Quiz</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Discover trending computer science &amp; engineering papers with 3-sentence AI summaries, or generate an instant MCQ practice quiz from your enrolled course materials.
          </p>
        </div>
        <div className="flex rounded-control border border-border bg-card p-1">
          <button
            type="button"
            onClick={() => setTab("papers")}
            className={`cursor-pointer rounded-md border-0 px-3.5 py-1.5 text-xs font-semibold transition ${
              tab === "papers" ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
            }`}
          >
            Research Papers
          </button>
          <button
            type="button"
            onClick={() => setTab("quiz")}
            className={`cursor-pointer rounded-md border-0 px-3.5 py-1.5 text-xs font-semibold transition ${
              tab === "quiz" ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
            }`}
          >
            AI Practice Quiz
          </button>
        </div>
      </div>

      {tab === "papers" && (
        <>
          <Card className="mb-6 p-4">
            <div className="flex flex-wrap items-center gap-3">
              <div className="relative min-w-[220px] flex-1">
                <span className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-text-secondary">
                  <UiIcon name="search" />
                </span>
                <input
                  type="search"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Search papers by title, author, venue, or topic (RAG, Raft, LoRA)…"
                  className="w-full rounded-control border border-border bg-bg py-2 pr-3 pl-9 text-sm text-text outline-none focus:border-primary"
                />
              </div>
              <select
                value={category}
                onChange={(e) => setCategory(e.target.value)}
                className="rounded-control border border-border bg-bg px-3 py-2 text-xs font-medium text-text outline-none focus:border-primary"
              >
                <option value="all">All Categories</option>
                {(data?.categories ?? []).map((cat) => (
                  <option key={cat} value={cat}>
                    {cat}
                  </option>
                ))}
              </select>
              <button
                type="button"
                onClick={() => setTrendingOnly((v) => !v)}
                className={`cursor-pointer rounded-control border px-3 py-2 text-xs font-semibold transition ${
                  trendingOnly
                    ? "border-primary bg-primary text-white"
                    : "border-border bg-bg text-text-secondary hover:text-text"
                }`}
              >
                Trending Only
              </button>
            </div>
          </Card>

          {error ? (
            <Card>
              <ErrorState message={error} onRetry={() => void loadPapers()} />
            </Card>
          ) : loading || !data ? (
            <LoadingBlock label="Loading Research Hub…" />
          ) : data.papers.length === 0 ? (
            <Card>
              <EmptyState icon="microscope" message="No research papers match your current search filters." />
            </Card>
          ) : (
            <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
              {data.papers.map((paper) => {
                const summary = summaries[paper.id];
                return (
                  <Card key={paper.id} className="flex flex-col justify-between p-5">
                    <div>
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="rounded bg-primary-light px-2 py-0.5 text-xs font-bold text-primary">
                            {paper.category}
                          </span>
                          <span className="text-xs text-text-secondary">
                            {paper.venue}{paper.year ? ` · ${paper.year}` : ""}
                          </span>
                        </div>
                        <div className="flex items-center gap-2 text-xs text-text-secondary">
                          {paper.trending && (
                            <span className="rounded bg-success-light px-2 py-0.5 text-[11px] font-semibold text-success">
                              Trending
                            </span>
                          )}
                        </div>
                      </div>

                      <h3 className="m-0 mt-2.5 text-base font-semibold text-text">{paper.title}</h3>
                      <div className="mt-1 text-xs text-text-muted">{paper.authors}</div>
                      <p className="m-0 mt-2.5 text-xs leading-relaxed text-text-secondary">{paper.abstract}</p>

                      {summary && (
                        <div className="mt-3.5 rounded-control border border-primary/25 bg-primary-light/30 p-3.5 text-xs">
                          <div className="mb-1 flex items-center gap-1.5 font-semibold text-primary">
                            <UiIcon name="sparkles" />
                            3-Sentence Summary
                          </div>
                          <p className="m-0 leading-relaxed text-text">{summary.summary}</p>
                        </div>
                      )}
                    </div>

                    <div className="mt-4 flex flex-wrap items-center justify-between gap-2 border-t border-border pt-3.5">
                      <Button
                        size="sm"
                        variant={summary ? "secondary" : "primary"}
                        disabled={summarizingId === paper.id}
                        onClick={() => void summarizePaper(paper.id)}
                      >
                        <UiIcon name="sparkles" />
                        {summarizingId === paper.id
                          ? "Summarizing…"
                          : summary
                            ? "Refresh Summary"
                            : "Summarize (3 Sentences)"}
                      </Button>
                      <a
                        href={paper.url}
                        target="_blank"
                        rel="noreferrer"
                        className="inline-flex items-center gap-1 text-xs font-semibold text-primary hover:underline"
                      >
                        Open Paper
                        <UiIcon name="external-link" />
                      </a>
                    </div>
                  </Card>
                );
              })}
            </div>
          )}
        </>
      )}

      {tab === "quiz" && (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
          <Card className="lg:col-span-4">
            <CardHeader title="Generate Practice Quiz" />
            <form onSubmit={(e) => void generatePracticeQuiz(e)} className="flex flex-col gap-3.5 p-5 text-sm">
              <div>
                <label className="mb-1 block text-xs font-semibold text-text-secondary">Course</label>
                <select
                  value={selectedCourseId}
                  onChange={(e) => setSelectedCourseId(e.target.value)}
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                >
                  {courses.map((c) => (
                    <option key={c.courseId} value={c.courseId}>
                      {c.code} — {c.title}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold text-text-secondary">Lecture Material</label>
                <select
                  value={materialId}
                  onChange={(e) => setMaterialId(e.target.value)}
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                >
                  <option value="">First available material</option>
                  {materials.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.title}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1 block text-xs font-semibold text-text-secondary">
                  Questions ({questionCount})
                </label>
                <input
                  type="range"
                  min={3}
                  max={8}
                  value={questionCount}
                  onChange={(e) => setQuestionCount(Number(e.target.value))}
                  className="w-full accent-primary"
                />
              </div>
              <button
                type="submit"
                disabled={quizLoading}
                className="btn-luminous inline-flex cursor-pointer items-center justify-center gap-2 rounded-control border-0 px-4 py-2.5 text-sm font-semibold disabled:opacity-60"
              >
                <UiIcon name="sparkles" />
                {quizLoading ? "Generating from Course Notes…" : "Generate Practice Quiz"}
              </button>
              {quizError && <Alert kind="error">{quizError}</Alert>}
            </form>
          </Card>

          <Card className="overflow-hidden lg:col-span-8">
            <CardHeader
              title={quiz ? quiz.courseLabel : "Interactive Practice Questions"}
              action={
                quiz && submittedQuiz ? (
                  <span className="dash-num rounded-md bg-primary-light px-2.5 py-1 text-xs font-bold text-primary">
                    Score: {correctCount} / {quiz.questions.length} (
                    {Math.round((correctCount / Math.max(1, quiz.questions.length)) * 100)}%)
                  </span>
                ) : undefined
              }
            />
            {quizLoading ? (
              <LoadingBlock label="Building course-grounded MCQs…" />
            ) : !quiz ? (
              <EmptyState
                icon="clipboard-check"
                message="Pick one of your courses on the left and click Generate Practice Quiz to test your mastery."
              />
            ) : (
              <div className="divide-y divide-border">
                {quiz.questions.map((q, qIdx) => {
                  const chosen = answers[q.id];
                  const isCorrect = submittedQuiz && chosen === q.correctIndex;
                  return (
                    <div key={q.id} className="p-5 text-sm">
                      <div className="flex items-start justify-between gap-2">
                        <div className="font-semibold text-text">
                          {qIdx + 1}. {q.question}
                        </div>
                        <span className="shrink-0 rounded bg-bg px-2 py-0.5 text-[11px] text-text-muted">
                          {q.sourceTitle}
                        </span>
                      </div>
                      <div className="mt-3 grid grid-cols-1 gap-2">
                        {q.options.map((opt, optIdx) => {
                          const selected = chosen === optIdx;
                          let tone = "border-border bg-bg text-text hover:border-primary/60";
                          if (submittedQuiz) {
                            if (optIdx === q.correctIndex) {
                              tone = "border-success bg-success-light/40 text-text font-semibold";
                            } else if (selected && optIdx !== q.correctIndex) {
                              tone = "border-danger bg-danger-light/40 text-text";
                            }
                          } else if (selected) {
                            tone = "border-primary bg-primary-light/40 text-text font-semibold";
                          }
                          return (
                            <button
                              key={optIdx}
                              type="button"
                              disabled={submittedQuiz}
                              onClick={() => setAnswers((prev) => ({ ...prev, [q.id]: optIdx }))}
                              className={`flex cursor-pointer items-center gap-2.5 rounded-control border px-3.5 py-2.5 text-left text-xs transition ${tone}`}
                            >
                              <span className="inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full border border-current text-[11px] font-bold">
                                {String.fromCharCode(65 + optIdx)}
                              </span>
                              <span>{opt}</span>
                            </button>
                          );
                        })}
                      </div>
                      {submittedQuiz && (
                        <div
                          className={`mt-3 rounded-md p-2.5 text-xs ${
                            isCorrect ? "bg-success-light/40 text-success" : "bg-warning-light/40 text-text"
                          }`}
                        >
                          <strong>{isCorrect ? "Correct!" : "Review:"}</strong> {q.explanation}
                        </div>
                      )}
                    </div>
                  );
                })}
                <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-4">
                  <span className="text-xs text-text-secondary">
                    Answered {Object.keys(answers).length} of {quiz.questions.length} questions
                  </span>
                  <div className="flex gap-2">
                    {submittedQuiz ? (
                      <Button
                        size="sm"
                        variant="secondary"
                        onClick={() => {
                          setAnswers({});
                          setSubmittedQuiz(false);
                        }}
                      >
                        Retry Quiz
                      </Button>
                    ) : (
                      <Button
                        size="sm"
                        disabled={Object.keys(answers).length === 0}
                        onClick={() => setSubmittedQuiz(true)}
                      >
                        Check Answers
                      </Button>
                    )}
                  </div>
                </div>
              </div>
            )}
          </Card>
        </div>
      )}
    </div>
  );
}
