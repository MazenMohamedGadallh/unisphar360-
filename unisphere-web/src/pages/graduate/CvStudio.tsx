import { useEffect, useState } from 'react';
import { Icon } from '../../components/Icon';
import { Alert, Async, ConfirmDialog, ErrorBox, PageHeader, Progress } from '../../components/ui';
import { api, errorMessage } from '../../lib/api';
import { useAction, useApi, useFlash, usePageTitle } from '../../lib/hooks';
import { fmtDateTime } from '../../lib/format';
import type { CvAnalysisDto, CvDocumentDto } from '../../types/dto';

function parseLabel(status: string): string {
  switch (status) {
    case 'Parsed': return 'text extracted';
    case 'Failed': return 'needs manual text';
    case 'Manual': return 'manual text';
    case 'UploadOnly': return 'stored, text pending';
    default: return status;
  }
}

function AnalysisView({ a }: { a: CvAnalysisDto }) {
  return (
    <div className="analysis">
      {a.scorePercent !== null && (
        <>
          <Progress value={a.scorePercent} tone={a.scorePercent >= 70 ? 'success' : a.scorePercent >= 50 ? 'warning' : 'danger'} />
          <p>
            <strong>ATS score: {a.scorePercent}%</strong>
          </p>
        </>
      )}
      {a.overallAssessment && <p>{a.overallAssessment}</p>}
      <p className="muted small-print">
        Provider: {a.providerName} / {a.model} · {fmtDateTime(a.createdAtUtc)}
      </p>
      {a.strengths.length > 0 && (
        <>
          <h4>Strengths</h4>
          <ul className="grad-list">
            {a.strengths.map((s, sIdx) => (
              <li key={`${s}-${sIdx}`}>
                <Icon name="circle-check" className="success" /> {s}
              </li>
            ))}
          </ul>
        </>
      )}
      {a.weaknesses.length > 0 && (
        <>
          <h4>Weaknesses</h4>
          <ul className="grad-list">
            {a.weaknesses.map((s, sIdx) => (
              <li key={`${s}-${sIdx}`}>
                <Icon name="circle-x" className="danger" /> {s}
              </li>
            ))}
          </ul>
        </>
      )}
      {a.suggestions.length > 0 && (
        <>
          <h4>Suggestions</h4>
          <ul className="grad-list">
            {a.suggestions.map((s, sIdx) => (
              <li key={`${s}-${sIdx}`}>
                <Icon name="lightbulb" className="info" /> {s}
              </li>
            ))}
          </ul>
        </>
      )}
      {a.keywords.length > 0 && (
        <>
          <h4>Top ATS keywords</h4>
          <div className="tag-list">
            {a.keywords.map((k, kIdx) => (
              <span className="grad-badge" key={`${k}-${kIdx}`}>
                {k}
              </span>
            ))}
          </div>
        </>
      )}
      {a.rewrites.length > 0 && (
        <>
          <h4>Rewrites</h4>
          <div className="table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Original</th>
                  <th>Rewritten</th>
                  <th>Why</th>
                </tr>
              </thead>
              <tbody>
                {a.rewrites.map((r, i) => (
                  <tr key={i}>
                    <td>{r.original}</td>
                    <td>{r.rewritten}</td>
                    <td className="muted">{r.why}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}

function CvManager({ cv, onChanged }: { cv: CvDocumentDto; onChanged: (cv: CvDocumentDto) => void }) {
  const analyses = useApi<CvAnalysisDto[]>(`/api/grad/profile/cv/${cv.id}/analyses`);
  const [manual, setManual] = useState('');
  const [targetRole, setTargetRole] = useState('');
  const [current, setCurrent] = useState<CvAnalysisDto | null>(null);
  const [flash, setFlash] = useFlash();
  const action = useAction();
  const thin = cv.parseStatus === 'Failed' || cv.parseStatus === 'UploadOnly' || cv.textChars < 80;

  useEffect(() => {
    const list = analyses.data ?? [];
    if (list.length > 0) setCurrent([...list].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))[0]);
  }, [analyses.data]);

  const saveText = () =>
    action.run(async () => {
      const updated = await api.put<CvDocumentDto>(`/api/grad/profile/cv/${cv.id}/manual-text`, { text: manual });
      if (updated) onChanged(updated);
      setFlash('Text saved. You can now run the analysis.');
    });

  const analyze = () =>
    action.run(async () => {
      const res = await api.post<CvAnalysisDto>(`/api/grad/profile/cv/${cv.id}/analyze`, { targetRole: targetRole.trim() || null });
      setCurrent(res);
      setFlash('Analysis ready.');
      void analyses.reload();
    });

  return (
    <div className="card section-gap">
      <div className="card-header">
        <h3>{cv.fileName}</h3>
        <span className="muted">{parseLabel(cv.parseStatus)}</span>
      </div>
      <ErrorBox error={action.error} />
      {flash && <Alert kind="success">{flash}</Alert>}
      <div className="grad-two-col">
        <div>
          {thin ? (
            <div className="form-group">
              <label htmlFor="grCvManual">Extracted text missing or thin — paste your CV text</label>
              <textarea id="grCvManual" rows={8} value={manual} onChange={(e) => setManual(e.target.value)} placeholder="Paste the full text of your CV here…" />
            </div>
          ) : (
            <>
              <p>
                Extracted text: <strong>{cv.textChars} characters</strong>. You can regenerate the analysis or paste refined text below.
              </p>
              <div className="form-group">
                <label htmlFor="grCvManual2">Manual text override</label>
                <textarea id="grCvManual2" rows={6} value={manual} onChange={(e) => setManual(e.target.value)} placeholder="Optional: paste refined CV text to analyze instead" />
              </div>
            </>
          )}
          {cv.parseError && <p className="muted small-print">Extraction note: {cv.parseError}</p>}
          <button className="btn btn-secondary" onClick={() => void saveText()} disabled={action.busy || !manual.trim()}>
            {thin ? 'Save text' : 'Save text override'}
          </button>
        </div>
        <div>
          <h3>AI analysis</h3>
          <div className="form-inline">
            <div className="form-group">
              <label htmlFor="grTarget">Target role (optional)</label>
              <input id="grTarget" value={targetRole} onChange={(e) => setTargetRole(e.target.value)} placeholder="e.g. Backend Developer" maxLength={100} />
            </div>
            <button className="btn btn-primary" onClick={() => void analyze()} disabled={action.busy}>
              <Icon name="sparkles" /> {action.busy ? 'Analyzing…' : 'Analyze with AI'}
            </button>
          </div>
          {action.busy && <p className="muted">Analyzing with AI… this can take up to a minute.</p>}
          {current ? (
            <AnalysisView a={current} />
          ) : analyses.loading ? (
            <p className="muted">Loading previous analyses…</p>
          ) : (
            <p className="muted">No analysis yet for this CV.</p>
          )}
          {(analyses.data?.length ?? 0) > 1 && (
            <div className="form-group">
              <label>Previous analyses</label>
              <select value={current?.id ?? ''} onChange={(e) => setCurrent(analyses.data!.find((a) => a.id === Number(e.target.value)) ?? null)}>
                {[...analyses.data!]
                  .sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc))
                  .map((a) => (
                    <option key={a.id} value={a.id}>
                      {fmtDateTime(a.createdAtUtc)} · {a.scorePercent ?? '—'}%
                    </option>
                  ))}
              </select>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default function CvStudio() {
  usePageTitle('CV & Resume Studio');
  const cvs = useApi<CvDocumentDto[]>('/api/grad/profile/cv');
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [deleting, setDeleting] = useState<CvDocumentDto | null>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [flash, setFlash] = useFlash();

  const selected = cvs.data?.find((c) => c.id === selectedId) ?? null;

  const upload = async (file: File | undefined) => {
    if (!file) return;
    setError(null);
    if (file.size > 10 * 1024 * 1024) {
      setError('File is larger than 10 MB.');
      return;
    }
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append('file', file, file.name);
      const created = await api.upload<CvDocumentDto>('/api/grad/profile/cv', fd);
      setFlash('CV uploaded.');
      await cvs.reload();
      if (created?.id) setSelectedId(created.id);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setUploading(false);
    }
  };

  const remove = async () => {
    if (!deleting) return;
    try {
      await api.del(`/api/grad/profile/cv/${deleting.id}`);
      if (selectedId === deleting.id) setSelectedId(null);
      await cvs.reload();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setDeleting(null);
    }
  };

  return (
    <>
      <PageHeader title="CV & Resume Studio" subtitle="Upload your CV, review the AI analysis and refine your draft" />
      <ErrorBox error={error} />
      <div className="grad-two-col">
        <div className="card">
          <div className="card-header">
            <h3>Upload a CV</h3>
          </div>
          <p className="muted">Accepted: .pdf, .docx, .txt (max 10 MB). The text is extracted automatically; if extraction fails you can paste the text manually.</p>
          <label className="file-button">
            <input
              type="file"
              accept=".pdf,.docx,.txt"
              disabled={uploading}
              onChange={(e) => {
                void upload(e.target.files?.[0]);
                e.target.value = '';
              }}
            />
            <span className="btn btn-primary">
              <Icon name="upload" /> {uploading ? 'Uploading…' : 'Choose file'}
            </span>
          </label>
          {flash && <div className="grad-badge flash-badge">{flash}</div>}
        </div>
        <div className="card">
          <div className="card-header">
            <h3>Your CVs ({cvs.data?.length ?? 0})</h3>
          </div>
          <Async res={cvs} loadingLabel="Loading CVs…" emptyIcon="file-text" empty="No CVs uploaded yet.">
            {(list) => (
              <ul className="grad-list grad-cv-list">
                {list.map((cv) => (
                  <li key={cv.id} className={`row-between ${cv.id === selectedId ? 'selected' : ''}`}>
                    <div>
                      <strong>{cv.fileName}</strong>
                      <div className="muted">
                        {Math.round(cv.sizeBytes / 1024)} KB · {parseLabel(cv.parseStatus)} · {fmtDateTime(cv.uploadedAtUtc)}
                        {cv.hasAnalysis && <span className="grad-badge">analyzed</span>}
                      </div>
                    </div>
                    <div className="row-actions">
                      <button className="btn btn-sm btn-secondary" onClick={() => setSelectedId(cv.id)}>
                        Manage
                      </button>
                      <button className="btn btn-sm btn-danger" onClick={() => setDeleting(cv)}>
                        Delete
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </Async>
        </div>
      </div>

      {selected && <CvManager key={selected.id} cv={selected} onChanged={(c) => cvs.setData((l) => l?.map((x) => (x.id === c.id ? c : x)))} />}

      {deleting && (
        <ConfirmDialog
          title="Delete CV?"
          message={
            <>
              Delete <strong>{deleting.fileName}</strong> and its analyses?
            </>
          }
          onConfirm={() => void remove()}
          onCancel={() => setDeleting(null)}
        />
      )}
    </>
  );
}
