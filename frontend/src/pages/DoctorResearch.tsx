import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, EmptyState, ErrorState, LoadingBlock, StatusBadge } from "../components/ui";
import { API_BASE_URL, authHeaders, clearToken, errorMessage } from "../lib/api";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

/* GET /api/doctor/research */
interface ResearchItem {
  id: number;
  title: string;
  field: string;
  authors: string;
  year: number;
  isPublished: boolean;
  hasPdf: boolean;
  abstract: string;
}

export function DoctorResearch() {
  const navigate = useNavigate();
  const [data, setData] = useState<ResearchItem[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const [title, setTitle] = useState("");
  const [field, setField] = useState("");
  const [authors, setAuthors] = useState("");
  const [pdf, setPdf] = useState<File | null>(null);
  const [year, setYear] = useState<number>(new Date().getFullYear());
  const [published, setPublished] = useState(true);
  const [abstract, setAbstract] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    document.title = "Faculty Research Hub — UniSphere 360+";
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/doctor/research`, { headers: authHeaders() });
      if (res.status === 401) {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      if (res.status === 403) {
        setError("Research Hub is available for Doctors.");
        return;
      }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setData((await res.json()) as ResearchItem[]);
    } catch {
      setError("Could not load Faculty Research Hub.");
    } finally {
      setLoading(false);
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  async function addResearch(e: FormEvent) {
    e.preventDefault();
    if (!title.trim() || !authors.trim() || !field.trim() || !abstract.trim() || !pdf) {
      setToast({ text: "Please provide a title, authors, field, abstract, and a PDF file.", isError: true });
      return;
    }
    setSaving(true);
    setToast(null);
    const formElement = e.target as HTMLFormElement;
    try {
      const form = new FormData();
      form.append("title", title.trim());
      form.append("authors", authors.trim());
      form.append("field", field.trim());
      form.append("abstract", abstract.trim());
      form.append("year", String(year || new Date().getFullYear()));
      form.append("isPublished", String(published));
      form.append("pdf", pdf);
      const res = await fetch(`${API_BASE_URL}/api/doctor/research`, {
        method: "POST",
        headers: authHeaders(),
        body: form,
      });
      if (!res.ok) {
        setToast({ text: await errorMessage(res, "Failed to save research record."), isError: true });
        return;
      }
      setTitle("");
      setAuthors("");
      setField("");
      setAbstract("");
      setPdf(null);
      formElement.reset();
      setToast({ text: "Paper added to your Faculty Research portfolio.", isError: false });
      await load();
    } catch {
      setToast({ text: "Failed to save research record.", isError: true });
    } finally {
      setSaving(false);
    }
  }

  async function removeItem(id: number) {
    setToast(null);
    try {
      const res = await fetch(`${API_BASE_URL}/api/doctor/research/${id}`, {
        method: "DELETE",
        headers: authHeaders(),
      });
      if (!res.ok) throw new Error();
      setToast({ text: "Research record removed.", isError: false });
      await load();
    } catch {
      setToast({ text: "Could not delete research record.", isError: true });
    }
  }

  const pubCount = data?.filter((i) => i.isPublished).length ?? 0;
  const draftCount = data?.filter((i) => !i.isPublished).length ?? 0;
  const pdfCount = data?.filter((i) => i.hasPdf).length ?? 0;

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">Faculty Research Hub</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Upload your research papers and control which ones are published.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" to="/doctor">
            <UiIcon name="house" />
            Faculty Hub
          </Button>
          <Button size="sm" to="/doctor/ai">
            <UiIcon name="sparkles" />
            AI Research Assistant
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : loading || !data ? (
        <LoadingBlock label="Loading Faculty Research Hub…" />
      ) : (
        <>
          <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div className="dash-stat gpa">
              <div className="dash-stat-icon"><UiIcon name="file-text" /></div>
              <span className="dash-stat-label">Published</span>
              <span className="dash-stat-value">{pubCount}</span>
            </div>
            <div className="dash-stat courses">
              <div className="dash-stat-icon"><UiIcon name="microscope" /></div>
              <span className="dash-stat-label">Drafts</span>
              <span className="dash-stat-value">{draftCount}</span>
            </div>
            <div className="dash-stat attendance">
              <div className="dash-stat-icon"><UiIcon name="graduation-cap" /></div>
              <span className="dash-stat-label">With PDF</span>
              <span className="dash-stat-value">{pdfCount}</span>
            </div>
          </div>

          <div className="mb-6 grid grid-cols-1 gap-6 lg:grid-cols-12">
            {/* Add Research Form */}
            <Card className="lg:col-span-5">
              <CardHeader title="Register Research Record" />
              <form onSubmit={(e) => void addResearch(e)} className="flex flex-col gap-3.5 p-5 text-sm">
                <div>
                  <label className="mb-1 block font-medium text-text">Title</label>
                  <input
                    type="text"
                    required
                    value={title}
                    onChange={(e) => setTitle(e.target.value)}
                    placeholder="Paper, grant, or capstone project title"
                    className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                </div>
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="mb-1 block font-medium text-text">Field</label>
                    <input
                      type="text"
                      required
                      value={field}
                      onChange={(e) => setField(e.target.value)}
                      placeholder="e.g., Machine Learning"
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block font-medium text-text">Visibility</label>
                    <select
                      value={published ? "published" : "draft"}
                      onChange={(e) => setPublished(e.target.value === "published")}
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    >
                      <option value="published">Published</option>
                      <option value="draft">Draft</option>
                    </select>
                  </div>
                </div>
                <div className="grid grid-cols-3 gap-3">
                  <div className="col-span-2">
                    <label className="mb-1 block font-medium text-text">Authors</label>
                    <input
                      type="text"
                      required
                      value={authors}
                      onChange={(e) => setAuthors(e.target.value)}
                      placeholder="e.g., A. Hassan, M. Ali"
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block font-medium text-text">Year</label>
                    <input
                      type="number"
                      min="2000"
                      max="2035"
                      value={year}
                      onChange={(e) => setYear(Number(e.target.value))}
                      className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                </div>
                <div>
                  <label className="mb-1 block font-medium text-text">Abstract</label>
                  <textarea
                    rows={3}
                    value={abstract}
                    onChange={(e) => setAbstract(e.target.value)}
                    placeholder="Key research scope and methodology…"
                    className="w-full rounded-control border border-border bg-bg px-3 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                </div>
                <label className="block font-medium text-text">
                  PDF File
                  <input
                    type="file"
                    accept="application/pdf"
                    required
                    onChange={(e) => setPdf(e.target.files?.[0] ?? null)}
                    className="mt-1 block w-full text-xs text-text-secondary"
                  />
                </label>
                <button
                  type="submit"
                  disabled={saving}
                  className="btn-luminous inline-flex cursor-pointer items-center justify-center gap-2 rounded-control border-0 px-4 py-2.5 text-sm font-semibold disabled:opacity-60"
                >
                  <UiIcon name="plus" />
                  {saving ? "Saving…" : "Add to Research Portfolio"}
                </button>
              </form>
            </Card>

            {/* Research Portfolio List */}
            <Card className="overflow-hidden lg:col-span-7">
              <CardHeader title={`Research Portfolio (${data.length})`} />
              {data.length === 0 ? (
                <EmptyState icon="microscope" message="No research papers added yet." />
              ) : (
                <div className="divide-y divide-border">
                  {data.map((item) => (
                    <div key={item.id} className="px-5 py-4">
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="rounded bg-primary-light px-2 py-0.5 text-[11px] font-semibold text-primary">
                            {item.field}
                          </span>
                          <span className="dash-num text-xs text-text-secondary">{item.year}</span>
                          <StatusBadge status={item.isPublished ? "Passed" : "In progress"} />
                        </div>
                        <Button size="sm" variant="secondary" onClick={() => void removeItem(item.id)}>
                          <UiIcon name="trash-2" />
                          Remove
                        </Button>
                      </div>
                      <h3 className="m-0 mt-2 text-sm font-semibold text-text">{item.title}</h3>
                      <div className="mt-0.5 text-xs font-medium text-primary">{item.authors}</div>
                      <p className="m-0 mt-1.5 text-xs leading-relaxed text-text-secondary">{item.abstract}</p>
                    </div>
                  ))}
                </div>
              )}
            </Card>
          </div>

        </>
      )}
    </div>
  );
}
