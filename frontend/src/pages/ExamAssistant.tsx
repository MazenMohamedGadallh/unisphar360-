import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, errorMessage } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

type QuestionType = "MCQ" | "TrueFalse" | "Essay" | "Definition";

interface CourseOption {
  id: number;
  code: string;
  title: string;
  department: string;
  academicYear: number;
}

interface MaterialOption {
  id: number;
  title: string;
  type: string;
  description: string;
}

interface DraftQuestion {
  orderIndex: number;
  questionType: QuestionType;
  questionText: string;
  options: string[];
  correctOption: number | null;
  modelAnswer: string | null;
  marks: number;
  explanation?: string | null;
  sourceMaterial?: string | null;
}

interface SavedExamRow {
  id: number;
  courseId: number;
  courseCode: string;
  courseTitle: string;
  title: string;
  status: string;
  questionCount: number;
  marks?: number | null;
  createdAtUtc: string;
  publishedAtUtc: string | null;
}

const COPY = {
  en: {
    pageTitle: "AI Exam Assistant",
    pageSubtitle: "Build mixed-format exams (MCQ, True/False, Essay, Definition) from course notes, translate between English & Arabic, and publish directly to students.",
    tabBuilder: "Exam Builder",
    tabDrafts: "My Exams & Drafts",
    switchLang: "العربية",
    course: "Course",
    materials: "Source Course Materials",
    allMaterialsHint: "Leave unchecked to use all materials for this course.",
    mixHeading: "Question Mix",
    mcq: "MCQ",
    trueFalse: "True / False",
    essay: "Essay",
    definition: "Definition",
    difficulty: "Difficulty",
    examLang: "Question Language",
    duration: "Duration (min)",
    generate: "Generate Mixed Exam with AI",
    generating: "Generating with AI…",
    translate: "Translate to Arabic",
    translating: "Translating…",
    addManual: "Add Question Manually",
    examTitle: "Exam Title",
    saveDraft: "Save as Draft",
    publishNow: "Save & Publish to Students",
    saving: "Saving…",
    previewTitle: "Generated & Editable Questions",
    emptyPreview: "Choose a course and question mix, then click Generate Mixed Exam with AI — or add questions manually.",
    modelAnswer: "Model Answer (for Doctor grading)",
    publishBtn: "Publish Now",
    viewResults: "View Results",
  },
  ar: {
    pageTitle: "مساعد إعداد الامتحانات الذكي",
    pageSubtitle: "أنشئ امتحانات متنوعة (اختيار من متعدد، صح وخطأ، مقالي، تعريفات) من محاضرات المقرر، وترجمها بين العربية والإنجليزية وانشرها للطلاب.",
    tabBuilder: "منشئ الامتحان",
    tabDrafts: "امتحاناتي والمسودات",
    switchLang: "English",
    course: "المقرر الدراسي",
    materials: "مواد ومحاضرات المقرر",
    allMaterialsHint: "اتركها غير محددة لاستخدام جميع محاضرات المقرر.",
    mixHeading: "توزيع أنواع الأسئلة",
    mcq: "اختيار من متعدد",
    trueFalse: "صح / خطأ",
    essay: "سؤال مقالي",
    definition: "تعريف مصطلح",
    difficulty: "مستوى الصعوبة",
    examLang: "لغة الأسئلة",
    duration: "المدة (دقيقة)",
    generate: "توليد الامتحان بالذكاء الاصطناعي",
    generating: "جاري توليد الأسئلة…",
    translate: "ترجمة الأسئلة للإنجليزية",
    translating: "جاري الترجمة…",
    addManual: "إضافة سؤال يدويًا",
    examTitle: "عنوان الامتحان",
    saveDraft: "حفظ كمسودة",
    publishNow: "حفظ ونشر للطلاب",
    saving: "جاري الحفظ…",
    previewTitle: "الأسئلة المُولّدة والقابلة للتعديل",
    emptyPreview: "اختر المقرر وتوزيع الأسئلة ثم اضغط توليد الامتحان، أو أضف سؤالاً يدويًا.",
    modelAnswer: "الإجابة النموذجية (لتصحيح الدكتور)",
    publishBtn: "نشر الآن",
    viewResults: "عرض النتائج",
  },
} as const;

/* The API's question shape: choices as a JSON string and the correct one stored as its text. */
interface ApiQuestion {
  questionText: string;
  questionType: QuestionType;
  correctAnswer: string | null;
  explanation: string | null;
  sourceMaterial: string | null;
  optionsJson: string | null;
}

const isWrittenType = (type: QuestionType) => type === "Essay" || type === "Definition";

function fromApi(q: ApiQuestion, orderIndex: number): DraftQuestion {
  const written = isWrittenType(q.questionType);
  const options: string[] = !written && q.optionsJson ? JSON.parse(q.optionsJson) : [];
  const correct = options.findIndex((o) => o.trim().toLowerCase() === q.correctAnswer?.trim().toLowerCase());
  return {
    orderIndex,
    questionType: q.questionType,
    questionText: q.questionText,
    options,
    correctOption: written || correct < 0 ? null : correct,
    modelAnswer: written ? (q.correctAnswer ?? "") : null,
    marks: written ? 5 : 2,
    explanation: q.explanation,
    sourceMaterial: q.sourceMaterial,
  };
}

function toApi(q: DraftQuestion): ApiQuestion {
  const written = isWrittenType(q.questionType);
  return {
    questionText: q.questionText,
    questionType: q.questionType,
    correctAnswer: written ? q.modelAnswer : q.correctOption === null ? null : q.options[q.correctOption],
    explanation: q.explanation ?? null,
    sourceMaterial: q.sourceMaterial ?? null,
    optionsJson: written ? null : JSON.stringify(q.options),
  };
}

const languageCode = (language: "English" | "Arabic") => (language === "Arabic" ? "ar" : "en");

export function ExamAssistant() {
  const navigate = useNavigate();
  const [uiLang, setUiLang] = useState<"en" | "ar">("en");
  const [tab, setTab] = useState<"builder" | "drafts">("builder");
  const [courses, setCourses] = useState<CourseOption[] | null>(null);
  const [selectedCourseId, setSelectedCourseId] = useState<number>(0);
  const [materials, setMaterials] = useState<MaterialOption[]>([]);
  const [selectedMaterialIds, setSelectedMaterialIds] = useState<number[]>([]);
  const [savedExams, setSavedExams] = useState<SavedExamRow[]>([]);

  const [mcqCount, setMcqCount] = useState(2);
  const [trueFalseCount, setTrueFalseCount] = useState(1);
  const [essayCount, setEssayCount] = useState(1);
  const [definitionCount, setDefinitionCount] = useState(1);
  const [difficulty, setDifficulty] = useState("Medium");
  const [questionLang, setQuestionLang] = useState<"English" | "Arabic">("English");

  const [examTitle, setExamTitle] = useState("");
  const [questions, setQuestions] = useState<DraftQuestion[]>([]);
  const [busy, setBusy] = useState<"generate" | "translate" | "save" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const t = COPY[uiLang];

  const req = useCallback(
    async (path: string, init?: RequestInit) => {
      const res = await fetch(`${API_BASE_URL}/api/doctor/exam-assistant${path}`, {
        ...init,
        headers: { ...authHeaders(), ...(init?.body ? { "Content-Type": "application/json" } : {}) },
      });
      if (res.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        throw new DOMException("expired", "AbortError");
      }
      return res;
    },
    [navigate],
  );

  const loadMaterials = useCallback(
    async (courseId: number) => {
      if (!courseId) return;
      try {
        const res = await req(`/courses/${courseId}/materials`);
        if (res.ok) {
          setMaterials((await res.json()) as MaterialOption[]);
          setSelectedMaterialIds([]);
        }
      } catch {
        // ignore
      }
    },
    [req],
  );

  const loadInitial = useCallback(async () => {
    setError(null);
    try {
      const [cRes, eRes] = await Promise.all([req("/courses"), req("/exams")]);
      if (cRes.status === 403) {
        setError("Exam Assistant is only available to doctors.");
        return;
      }
      if (!cRes.ok) throw new Error(`HTTP ${cRes.status}`);
      const courseList = (await cRes.json()) as CourseOption[];
      setCourses(courseList);
      if (courseList.length > 0) {
        setSelectedCourseId(courseList[0].id);
        void loadMaterials(courseList[0].id);
      }
      if (eRes.ok) setSavedExams((await eRes.json()) as SavedExamRow[]);
    } catch (e) {
      if ((e as Error).name !== "AbortError") setError("Could not load Exam Assistant data.");
    }
  }, [req, loadMaterials]);

  useEffect(() => {
    void loadInitial();
  }, [loadInitial]);

  function toggleUiLang() {
    const next = uiLang === "en" ? "ar" : "en";
    setUiLang(next);
    setQuestionLang(next === "ar" ? "Arabic" : "English");
  }

  function toggleMaterial(id: number) {
    setSelectedMaterialIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function generateExam() {
    if (!selectedCourseId) return;
    setBusy("generate");
    setToast(null);
    try {
      const course = courses?.find((c) => c.id === selectedCourseId);
      const res = await req("/generate", {
        method: "POST",
        body: JSON.stringify({
          courseId: selectedCourseId,
          // Nothing ticked means "use all materials", but the API wants the ids spelled out.
          materialIds: selectedMaterialIds.length ? selectedMaterialIds : materials.map((m) => m.id),
          questionCount: mcqCount + trueFalseCount + essayCount + definitionCount,
          mcqCount,
          trueFalseCount,
          essayCount,
          definitionCount,
          difficulty: difficulty.toLowerCase(),
          language: languageCode(questionLang),
        }),
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Failed to generate exam."), isError: true });
        return;
      }
      const body = (await res.json()) as { questions: ApiQuestion[]; ragWarning?: string | null };
      setExamTitle(course ? `${course.code} — ${course.title} Assessment` : "Generated University Exam");
      setQuestions(body.questions.map(fromApi));
      setToast({
        text:
          body.ragWarning ??
          (uiLang === "ar"
            ? `تم توليد ${body.questions.length} سؤالاً بنجاح.`
            : `Generated ${body.questions.length} mixed questions. Review or edit before publishing.`),
        isError: false,
      });
    } catch (e) {
      if ((e as Error).name !== "AbortError") {
        setToast({ text: "Could not reach AI Exam Generator.", isError: true });
      }
    } finally {
      setBusy(null);
    }
  }

  async function translateQuestions() {
    if (questions.length === 0) return;
    const targetLanguage = questionLang === "Arabic" ? "English" : "Arabic";
    setBusy("translate");
    setToast(null);
    try {
      const res = await req("/translate", {
        method: "POST",
        body: JSON.stringify({ questions: questions.map(toApi), language: languageCode(targetLanguage) }),
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Translation failed."), isError: true });
        return;
      }
      const translated = (await res.json()) as ApiQuestion[];
      setQuestions((prev) => translated.map((q, i) => ({ ...fromApi(q, i), marks: prev[i]?.marks ?? 2 })));
      setQuestionLang(targetLanguage);
      setUiLang(targetLanguage === "Arabic" ? "ar" : "en");
      setToast({ text: `Translated exam into ${targetLanguage}.`, isError: false });
    } catch (e) {
      if ((e as Error).name !== "AbortError") setToast({ text: "Translation request failed.", isError: true });
    } finally {
      setBusy(null);
    }
  }

  function addManualQuestion(type: QuestionType) {
    const isWritten = type === "Essay" || type === "Definition";
    setQuestions((prev) => [
      ...prev,
      {
        orderIndex: prev.length,
        questionType: type,
        questionText: "",
        options: isWritten ? [] : type === "TrueFalse" ? (questionLang === "Arabic" ? ["صح", "خطأ"] : ["True", "False"]) : ["Option A", "Option B", "Option C", "Option D"],
        correctOption: isWritten ? null : 0,
        modelAnswer: isWritten ? "" : null,
        marks: isWritten ? 5 : 2,
      },
    ]);
    if (!examTitle && courses) {
      const c = courses.find((x) => x.id === selectedCourseId);
      if (c) setExamTitle(`${c.code} — ${c.title} Assessment`);
    }
  }

  async function saveExam(publishNow: boolean) {
    if (!selectedCourseId || !examTitle.trim() || questions.length === 0) {
      setToast({ text: "Provide an exam title and at least one question before saving.", isError: true });
      return;
    }
    setBusy("save");
    setToast(null);
    try {
      const title = examTitle.trim();
      const res = await req("/exams", {
        method: "POST",
        body: JSON.stringify({
          courseId: selectedCourseId,
          title,
          questionCount: questions.length,
          questionTypes: [...new Set(questions.map((q) => q.questionType))].join(","),
          difficulty: difficulty.toLowerCase(),
          language: languageCode(questionLang),
          materialsUsed: materials
            .filter((m) => !selectedMaterialIds.length || selectedMaterialIds.includes(m.id))
            .map((m) => m.title)
            .join(", "),
          questions: questions.map((q, i) => ({ ...toApi(q), id: 0, examId: 0, orderIndex: i, marks: q.marks })),
        }),
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Could not save exam."), isError: true });
        return;
      }
      if (publishNow) {
        const saved = (await res.json()) as { id: number };
        const published = await publishExam(saved.id, title, questions.reduce((sum, q) => sum + q.marks, 0));
        if (!published.ok) {
          const reason = await errorMessage(published, "Publishing failed.");
          setQuestions([]); // saved already: retrying from the builder would duplicate it
          setSavedExams(await (await req("/exams")).json());
          setTab("drafts");
          setToast({ text: `Saved as a draft, but not published: ${reason}`, isError: true });
          return;
        }
      }
      setToast({
        text: publishNow
          ? "Exam published! Students can now see and take it in My Exams."
          : "Exam saved to My Drafts.",
        isError: false,
      });
      const eRes = await req("/exams");
      if (eRes.ok) setSavedExams((await eRes.json()) as SavedExamRow[]);
      setTab("drafts");
    } catch (e) {
      if ((e as Error).name !== "AbortError") setToast({ text: "Failed to save exam.", isError: true });
    } finally {
      setBusy(null);
    }
  }

  const publishExam = (id: number, title: string, marks: number | null) =>
    req(`/exams/${id}/publish`, { method: "POST", body: JSON.stringify({ examId: id, title, marks }) });

  async function publishExisting(id: number) {
    try {
      // An empty title keeps the saved one; marks come from the questions.
      const detail = await req(`/exams/${id}`);
      if (!detail.ok) throw new Error(await errorMessage(detail, "Could not load the draft."));
      const exam = (await detail.json()) as { title: string; questions: { marks: number | null }[] };
      const res = await publishExam(id, exam.title, exam.questions.reduce((sum, q) => sum + (q.marks ?? 0), 0) || null);
      if (!res.ok) throw new Error(await errorMessage(res, "Could not publish draft."));
      const eRes = await req("/exams");
      if (eRes.ok) setSavedExams((await eRes.json()) as SavedExamRow[]);
      setToast({ text: "Draft published to students.", isError: false });
    } catch (e) {
      setToast({ text: e instanceof Error ? e.message : "Could not publish draft.", isError: true });
    }
  }

  const totalSelectedQuestions = mcqCount + trueFalseCount + essayCount + definitionCount;

  return (
    <div className="dashboard" dir={uiLang === "ar" ? "rtl" : "ltr"}>
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">{t.pageTitle}</h1>
          <p className="mt-1 max-w-2xl text-sm text-text-secondary">{t.pageSubtitle}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" size="sm" onClick={toggleUiLang}>
            <UiIcon name="globe" />
            {t.switchLang}
          </Button>
          <div className="flex rounded-control border border-border bg-card p-1">
            <button
              type="button"
              onClick={() => setTab("builder")}
              className={`cursor-pointer rounded-md border-0 px-3 py-1.5 text-xs font-semibold transition ${
                tab === "builder" ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
              }`}
            >
              {t.tabBuilder}
            </button>
            <button
              type="button"
              onClick={() => setTab("drafts")}
              className={`cursor-pointer rounded-md border-0 px-3 py-1.5 text-xs font-semibold transition ${
                tab === "drafts" ? "bg-primary text-white" : "bg-transparent text-text-secondary hover:text-text"
              }`}
            >
              {t.tabDrafts} ({savedExams.length})
            </button>
          </div>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card><ErrorState message={error} onRetry={() => void loadInitial()} /></Card>
      ) : courses === null ? (
        <LoadingBlock label="Loading Exam Assistant…" />
      ) : tab === "drafts" ? (
        <Card className="overflow-hidden">
          <CardHeader title={`${t.tabDrafts} (${savedExams.length})`} />
          {savedExams.length === 0 ? (
            <EmptyState icon="clipboard-check" message="No exams created yet." />
          ) : (
            <div className="dash-table-wrap">
              <table className="dash-table">
                <thead>
                  <tr>
                    <th>Exam</th>
                    <th>Course</th>
                    <th>Questions</th>
                    <th>Status</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {savedExams.map((ex) => (
                    <tr key={ex.id}>
                      <td className="font-medium text-text">{ex.title}</td>
                      <td>
                        <span className="font-semibold text-primary">{ex.courseCode}</span>
                        <span className="ml-1.5 text-text-secondary">{ex.courseTitle}</span>
                      </td>
                      <td className="dash-num">
                        {ex.questionCount}
                        {ex.marks != null ? ` · ${ex.marks} pts` : ""}
                      </td>
                      <td>
                        <StatusBadge status={ex.status === "Published" ? "Passed" : "In progress"} />
                      </td>
                      <td className="text-right">
                        {ex.status === "Draft" ? (
                          <Button size="sm" onClick={() => void publishExisting(ex.id)}>
                            {t.publishBtn}
                          </Button>
                        ) : (
                          <Button size="sm" variant="secondary" to="/doctor/exam-results">
                            {t.viewResults}
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
          {/* Left config panel */}
          <Card className="lg:col-span-5">
            <CardHeader title={t.mixHeading} />
            <div className="flex flex-col gap-4 p-5 text-sm">
              <div>
                <label className="mb-1.5 block font-medium text-text">{t.course}</label>
                <select
                  className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  value={selectedCourseId}
                  onChange={(e) => {
                    const id = Number(e.target.value);
                    setSelectedCourseId(id);
                    void loadMaterials(id);
                  }}
                >
                  {courses.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.code} — {c.title}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <div className="mb-1 flex items-center justify-between">
                  <span className="font-medium text-text">{t.materials}</span>
                  <span className="text-xs text-text-secondary">{materials.length} available</span>
                </div>
                <p className="m-0 mb-2 text-xs text-text-secondary">{t.allMaterialsHint}</p>
                <div className="flex max-h-40 flex-col gap-1.5 overflow-y-auto rounded-control border border-border bg-bg/60 p-2.5">
                  {materials.length === 0 ? (
                    <span className="text-xs text-text-secondary">No materials uploaded for this course yet.</span>
                  ) : (
                    materials.map((m) => (
                      <label key={m.id} className="flex cursor-pointer items-center gap-2 text-xs text-text">
                        <input
                          type="checkbox"
                          checked={selectedMaterialIds.includes(m.id)}
                          onChange={() => toggleMaterial(m.id)}
                        />
                        <span className="truncate font-medium">{m.title}</span>
                        <span className="ml-auto rounded bg-primary-light px-1.5 py-0.5 text-[10px] font-semibold text-primary">
                          {m.type}
                        </span>
                      </label>
                    ))
                  )}
                </div>
              </div>

              <div>
                <div className="mb-2 flex items-center justify-between">
                  <span className="font-medium text-text">{t.mixHeading}</span>
                  <span className="dash-num text-xs font-semibold text-primary">{totalSelectedQuestions} total</span>
                </div>
                <div className="grid grid-cols-2 gap-3">
                  {[
                    { label: t.mcq, value: mcqCount, set: setMcqCount },
                    { label: t.trueFalse, value: trueFalseCount, set: setTrueFalseCount },
                    { label: t.essay, value: essayCount, set: setEssayCount },
                    { label: t.definition, value: definitionCount, set: setDefinitionCount },
                  ].map((item) => (
                    <label key={item.label} className="flex flex-col gap-1 rounded-control border border-border bg-bg/60 p-2.5">
                      <span className="text-xs font-medium text-text-secondary">{item.label}</span>
                      <input
                        type="number"
                        min={0}
                        max={20}
                        value={item.value}
                        onChange={(e) => item.set(Math.max(0, Math.min(20, Number(e.target.value) || 0)))}
                        className="dash-num w-full rounded border border-border bg-card px-2.5 py-1 text-sm font-semibold text-text outline-none focus:border-primary"
                      />
                    </label>
                  ))}
                </div>
              </div>

              <div className="grid grid-cols-3 gap-2.5">
                <label className="flex flex-col gap-1">
                  <span className="text-xs font-medium text-text-secondary">{t.difficulty}</span>
                  <select
                    value={difficulty}
                    onChange={(e) => setDifficulty(e.target.value)}
                    className="rounded-control border border-border bg-bg px-2.5 py-2 text-xs text-text outline-none focus:border-primary"
                  >
                    <option value="Easy">Easy</option>
                    <option value="Medium">Medium</option>
                    <option value="Hard">Hard</option>
                  </select>
                </label>
                <label className="flex flex-col gap-1">
                  <span className="text-xs font-medium text-text-secondary">{t.examLang}</span>
                  <select
                    value={questionLang}
                    onChange={(e) => setQuestionLang(e.target.value as "English" | "Arabic")}
                    className="rounded-control border border-border bg-bg px-2.5 py-2 text-xs text-text outline-none focus:border-primary"
                  >
                    <option value="English">English</option>
                    <option value="Arabic">العربية</option>
                  </select>
                </label>
                
              </div>

              <Button onClick={() => void generateExam()} disabled={busy !== null || totalSelectedQuestions === 0}>
                <UiIcon name="sparkles" />
                {busy === "generate" ? t.generating : t.generate}
              </Button>

              <div className="flex flex-wrap gap-2 border-t border-border pt-3">
                <span className="w-full text-xs font-medium text-text-secondary">{t.addManual}:</span>
                {(["MCQ", "TrueFalse", "Essay", "Definition"] as QuestionType[]).map((qt) => (
                  <button
                    key={qt}
                    type="button"
                    onClick={() => addManualQuestion(qt)}
                    className="cursor-pointer rounded-control border border-border bg-bg px-2.5 py-1 text-xs font-medium text-text transition hover:border-primary hover:text-primary"
                  >
                    + {qt}
                  </button>
                ))}
              </div>
            </div>
          </Card>

          {/* Right editor & publish panel */}
          <Card className="lg:col-span-7">
            <CardHeader
              title={t.previewTitle}
              action={
                questions.length > 0 ? (
                  <Button
                    size="sm"
                    variant="secondary"
                    disabled={busy !== null}
                    onClick={() => void translateQuestions()}
                  >
                    <UiIcon name="globe" />
                    {busy === "translate"
                      ? t.translating
                      : questionLang === "Arabic"
                        ? "Translate to English"
                        : "ترجمة للعربية"}
                  </Button>
                ) : undefined
              }
            />
            <div className="flex flex-col gap-4 p-5">
              {questions.length === 0 ? (
                <EmptyState icon="sparkles" message={t.emptyPreview} />
              ) : (
                <>
                  <div>
                    <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-text-secondary">
                      {t.examTitle}
                    </label>
                    <input
                      type="text"
                      value={examTitle}
                      onChange={(e) => setExamTitle(e.target.value)}
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm font-medium text-text outline-none focus:border-primary"
                    />
                  </div>

                  <div className="flex flex-col gap-3">
                    {questions.map((q, idx) => {
                      const isWritten = q.questionType === "Essay" || q.questionType === "Definition";
                      return (
                        <div key={idx} className="rounded-control border border-border bg-bg/60 p-4 text-sm">
                          <div className="mb-2 flex items-center justify-between gap-2">
                            <span className="rounded bg-primary-light px-2 py-0.5 text-[11px] font-semibold text-primary">
                              #{idx + 1} · {q.questionType} · {q.marks} pts
                            </span>
                            <button
                              type="button"
                              onClick={() => setQuestions((prev) => prev.filter((_, i) => i !== idx))}
                              className="cursor-pointer border-0 bg-transparent text-xs font-medium text-danger hover:underline"
                            >
                              Remove
                            </button>
                          </div>
                          <textarea
                            rows={2}
                            value={q.questionText}
                            onChange={(e) =>
                              setQuestions((prev) =>
                                prev.map((item, i) => (i === idx ? { ...item, questionText: e.target.value } : item)),
                              )
                            }
                            placeholder="Question text…"
                            className="mb-2.5 w-full rounded-control border border-border bg-card px-3 py-2 text-sm text-text outline-none focus:border-primary"
                          />
                          {isWritten ? (
                            <div>
                              <label className="mb-1 block text-xs font-medium text-primary">{t.modelAnswer}</label>
                              <textarea
                                rows={2}
                                value={q.modelAnswer ?? ""}
                                onChange={(e) =>
                                  setQuestions((prev) =>
                                    prev.map((item, i) => (i === idx ? { ...item, modelAnswer: e.target.value } : item)),
                                  )
                                }
                                className="w-full rounded-control border border-primary/25 bg-card px-3 py-2 text-xs text-text outline-none focus:border-primary"
                              />
                            </div>
                          ) : (
                            <div className="flex flex-col gap-1.5">
                              {q.options.map((opt, oi) => (
                                <div key={oi} className="flex items-center gap-2">
                                  <input
                                    type="radio"
                                    name={`correct_${idx}`}
                                    checked={q.correctOption === oi}
                                    onChange={() =>
                                      setQuestions((prev) =>
                                        prev.map((item, i) => (i === idx ? { ...item, correctOption: oi } : item)),
                                      )
                                    }
                                    title="Mark as correct option"
                                  />
                                  <input
                                    type="text"
                                    value={opt}
                                    onChange={(e) =>
                                      setQuestions((prev) =>
                                        prev.map((item, i) =>
                                          i === idx
                                            ? {
                                                ...item,
                                                options: item.options.map((o, j) => (j === oi ? e.target.value : o)),
                                              }
                                            : item,
                                        ),
                                      )
                                    }
                                    className="flex-1 rounded border border-border bg-card px-2.5 py-1 text-xs text-text outline-none focus:border-primary"
                                  />
                                </div>
                              ))}
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>

                  <div className="flex flex-wrap items-center justify-end gap-2.5 border-t border-border pt-4">
                    <Button variant="secondary" disabled={busy !== null} onClick={() => void saveExam(false)}>
                      {busy === "save" ? t.saving : t.saveDraft}
                    </Button>
                    <Button disabled={busy !== null} onClick={() => void saveExam(true)}>
                      <UiIcon name="check-circle" />
                      {busy === "save" ? t.saving : t.publishNow}
                    </Button>
                  </div>
                </>
              )}
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
