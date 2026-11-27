import { useCallback, useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Alert, Button, Card, CardHeader, ConfirmDialog, ErrorState, LoadingBlock } from "../components/ui";
import { UserAvatar } from "../components/UserAvatar";
import { clearToken, notifyUserUpdated, type AuthUser } from "../lib/api";
import { gradFetch, loadGradCareer } from "../lib/grad";
import { UiIcon } from "../UiIcon";
import "./dashboard.css";

interface GraduateProfile {
  userId: number;
  headline: string;
  summary: string;
  targetRole: string;
  location: string;
  linkedinUrl: string;
  githubUrl: string;
  skills: string[];
  cvFileName: string | null;
  cvUploadedAtUtc: string | null;
  cvSummary: string | null;
  cvExtractedText?: string | null;
  cvScore?: number | null;
  cvStrengths?: string[];
  cvImprovements?: string[];
  cvDetectedSkills?: string[];
}

type CvStatus = "empty" | "uploading" | "uploaded" | "analyzed" | "error";

interface CvMetadata {
  fileName: string;
  uploadedAtUtc: string;
  fileSizeBytes: number | null;
  extractedChars: number;
  score: number | null;
}

const fmtDate = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

const fmtFileSize = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(2)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;

const MAX_CV_BYTES = 10 * 1024 * 1024; // 10 MB

export function GraduateCvStudio() {
  const navigate = useNavigate();
  const [authUser, setAuthUser] = useState<AuthUser | null>(null);
  const [profile, setProfile] = useState<GraduateProfile | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ text: string; isError: boolean } | null>(null);

  const [headline, setHeadline] = useState("");
  const [targetRole, setTargetRole] = useState("");
  const [location, setLocation] = useState("");
  const [linkedinUrl, setLinkedinUrl] = useState("");
  const [githubUrl, setGithubUrl] = useState("");
  const [skillsText, setSkillsText] = useState("");
  const [summary, setSummary] = useState("");
  const [saving, setSaving] = useState(false);

  const [photoBusy, setPhotoBusy] = useState(false);
  const [cvBusy, setCvBusy] = useState(false);
  const [cvStatus, setCvStatus] = useState<CvStatus>("empty");
  const [cvMetadata, setCvMetadata] = useState<CvMetadata | null>(null);
  const [textSaving, setTextSaving] = useState(false);
  const [manualText, setManualText] = useState("");
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  const photoInputRef = useRef<HTMLInputElement | null>(null);
  const cvInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    document.title = "CV & Resume Studio — UniSphere 360+";
  }, []);

  // Server values the form does not edit; PUT /api/grad/profile replaces every field, so they are sent back.
  const career = useRef<Awaited<ReturnType<typeof loadGradCareer>> | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [me, next] = await Promise.all([gradFetch<AuthUser>("auth/me"), loadGradCareer()]);
      career.current = next;
      const { profile: server, cv, analysis } = next;
      const view: GraduateProfile = {
        userId: server.userId,
        headline: server.headline ?? "",
        summary: server.careerSummary ?? "",
        targetRole: server.preferredJobTitles ?? "",
        location: server.preferredLocations ?? "",
        linkedinUrl: server.linkedInUrl ?? "",
        githubUrl: server.gitHubUrl ?? "",
        skills: server.skills.map((s) => s.name),
        cvFileName: cv?.fileName ?? null,
        cvUploadedAtUtc: cv?.uploadedAtUtc ?? null,
        cvSummary: analysis?.overallAssessment ?? cv?.parseError ?? null,
        cvScore: analysis?.scorePercent ?? null,
        cvStrengths: analysis?.strengths ?? [],
        cvImprovements: analysis?.suggestions ?? [],
        cvDetectedSkills: analysis?.technicalSkills ?? [],
      };
      setAuthUser(me);
      setProfile(view);
      setCvMetadata(
        cv && {
          fileName: cv.fileName,
          uploadedAtUtc: cv.uploadedAtUtc,
          fileSizeBytes: cv.sizeBytes,
          extractedChars: cv.textChars,
          score: view.cvScore ?? null,
        },
      );
      setCvStatus(!cv ? "empty" : analysis ? "analyzed" : "uploaded");
      setHeadline(view.headline);
      setTargetRole(view.targetRole);
      setLocation(view.location);
      setLinkedinUrl(view.linkedinUrl);
      setGithubUrl(view.githubUrl);
      setSummary(view.summary);
      setSkillsText(view.skills.join(", "));
    } catch (e) {
      if ((e as Error).name === "Unauthorized") {
        clearToken();
        navigate("/login?expired=1");
        return;
      }
      setError("Could not load CV & Resume Studio.");
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  /** Writes the career form, then reconciles the skill list one add/remove at a time. */
  async function persistProfile(skills: string[]) {
    const server = career.current!.profile;
    await gradFetch("grad/profile", {
      method: "PUT",
      body: JSON.stringify({
        ...server,
        headline,
        careerSummary: summary,
        preferredJobTitles: targetRole,
        preferredLocations: location,
        linkedInUrl: linkedinUrl,
        gitHubUrl: githubUrl,
      }),
    });
    const wanted = new Map(skills.map((s) => [s.toLowerCase(), s]));
    const have = new Set(server.skills.map((s) => s.name.toLowerCase()));
    for (const [key, name] of wanted) {
      if (!have.has(key)) await gradFetch("grad/profile/skills", { method: "POST", body: JSON.stringify({ name }) });
    }
    for (const skill of server.skills) {
      if (!wanted.has(skill.name.toLowerCase())) {
        await gradFetch(`grad/profile/skills/${skill.id}`, { method: "DELETE" });
      }
    }
  }

  const reason = (e: unknown, fallback: string) => (e instanceof Error ? e.message : fallback);

  async function handlePhotoSelected(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;
    if (!file.type.startsWith("image/")) {
      setToast({ text: "Please select a PNG, JPG, or WebP image.", isError: true });
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      setToast({ text: "Profile photo must be smaller than 5 MB.", isError: true });
      return;
    }
    setPhotoBusy(true);
    setToast(null);
    try {
      const form = new FormData();
      form.append("file", file);
      const saved = await gradFetch<Partial<AuthUser>>(`users/${authUser!.id}/avatar`, { method: "POST", body: form });
      const updated = { ...authUser!, ...saved };
      setAuthUser(updated);
      notifyUserUpdated(updated);
      setToast({ text: "Profile photo updated and synced across UniSphere.", isError: false });
    } catch {
      setToast({ text: "Could not upload profile photo.", isError: true });
    } finally {
      setPhotoBusy(false);
    }
  }

  async function handleRemovePhoto() {
    setPhotoBusy(true);
    setToast(null);
    try {
      const saved = await gradFetch<Partial<AuthUser>>(`users/${authUser!.id}/avatar`, { method: "DELETE" });
      const updated = { ...authUser!, ...saved };
      setAuthUser(updated);
      notifyUserUpdated(updated);
      setToast({ text: "Profile photo removed.", isError: false });
    } catch {
      setToast({ text: "Could not remove profile photo.", isError: true });
    } finally {
      setPhotoBusy(false);
    }
  }

  async function saveProfile(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setToast(null);
    try {
      await persistProfile(skillsText.split(",").map((s) => s.trim()).filter(Boolean));
      await load();
      setToast({ text: "Career profile & skills saved.", isError: false });
    } catch (e) {
      setToast({ text: reason(e, "Failed to save career profile."), isError: true });
    } finally {
      setSaving(false);
    }
  }

  const handleUploadSuccess = useCallback(
    async (file: File) => {
      if (file.size === 0) {
        setCvStatus("error");
        setToast({ text: "The selected CV file is empty. Please choose a valid .pdf, .docx, or .txt file.", isError: true });
        return;
      }
      if (!/\.(pdf|docx|txt)$/i.test(file.name)) {
        setCvStatus("error");
        setToast({ text: "Unsupported file format. Please upload a .pdf, .docx, or .txt resume.", isError: true });
        return;
      }
      if (file.size > MAX_CV_BYTES) {
        const sizeMb = (file.size / (1024 * 1024)).toFixed(1);
        setCvStatus("error");
        setToast({
          text: `CV file is ${sizeMb} MB — maximum allowed size is 10 MB (.pdf, .docx, .txt).`,
          isError: true,
        });
        return;
      }

      setCvBusy(true);
      setCvStatus("uploading");
      setToast(null);
      try {
        const formData = new FormData();
        formData.append("file", file, file.name);
        await gradFetch("grad/profile/cv", { method: "POST", body: formData });
        await load();
        setManualText("");
        setToast({ text: `Uploaded "${file.name}". Run Analyze CV to get your ATS score.`, isError: false });
      } catch (e) {
        setCvStatus("error");
        setToast({ text: reason(e, "Could not upload your CV. Please try again."), isError: true });
      } finally {
        setCvBusy(false);
      }
    },
    [load],
  );

  async function handleCvUpload(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    e.target.value = "";
    if (!file) return;
    await handleUploadSuccess(file);
  }

  async function saveManualTextOverride() {
    const cv = career.current?.cv;
    if (!cv || !manualText.trim()) {
      setToast({ text: "Upload a CV and enter your resume text before saving.", isError: true });
      return;
    }
    setTextSaving(true);
    setToast(null);
    try {
      await gradFetch(`grad/profile/cv/${cv.id}/manual-text`, { method: "PUT", body: JSON.stringify({ text: manualText }) });
      await load();
      setToast({ text: "Manual CV text override saved. Click \"Analyze CV\" to refresh your ATS score.", isError: false });
    } catch (e) {
      setToast({ text: reason(e, "Could not save CV text override."), isError: true });
    } finally {
      setTextSaving(false);
    }
  }

  async function analyzeCv() {
    const cv = career.current?.cv;
    if (!cv) return;
    setCvBusy(true);
    setToast(null);
    try {
      if (manualText.trim()) {
        await gradFetch(`grad/profile/cv/${cv.id}/manual-text`, { method: "PUT", body: JSON.stringify({ text: manualText }) });
      }
      const result = await gradFetch<{ scorePercent: number | null }>(`grad/profile/cv/${cv.id}/analyze`, {
        method: "POST",
        body: JSON.stringify({ targetRole: targetRole || null }),
      });
      await load();
      setToast({ text: `CV analysis completed — ATS Readiness Score: ${result.scorePercent ?? "—"}/100.`, isError: false });
    } catch (e) {
      setToast({ text: reason(e, "CV analysis failed. Please try again."), isError: true });
    } finally {
      setCvBusy(false);
    }
  }

  async function deleteCv() {
    const cv = career.current?.cv;
    if (!cv) return;
    setCvBusy(true);
    setToast(null);
    try {
      await gradFetch(`grad/profile/cv/${cv.id}`, { method: "DELETE" });
      await load();
      setManualText("");
      setToast({ text: "Uploaded CV and extracted text deleted.", isError: false });
    } catch (e) {
      setToast({ text: reason(e, "Could not delete CV."), isError: true });
    } finally {
      setCvBusy(false);
    }
  }

  async function syncDetectedSkills() {
    if (!profile?.cvDetectedSkills?.length) return;
    const merged = Array.from(
      new Set([...profile.skills, ...profile.cvDetectedSkills].map((s) => s.trim()).filter(Boolean)),
    );
    setSaving(true);
    try {
      await persistProfile(merged);
      await load();
      setToast({ text: "Detected CV skills merged into your profile.", isError: false });
    } catch (e) {
      setToast({ text: reason(e, "Could not sync detected skills."), isError: true });
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="dashboard">
      <div className="mb-6 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="m-0 text-2xl font-semibold text-text">CV &amp; Resume Studio</h1>
          <p className="mt-1 text-sm text-text-secondary">
            Upload your CV (.pdf, .docx, .txt up to 10 MB), refine extracted text, run ATS analysis, and manage your career profile.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="secondary" to="/graduate/jobs">
            <UiIcon name="briefcase" />
            See Matched Jobs
          </Button>
          <Button size="sm" to="/graduate/roadmap">
            <UiIcon name="map" />
            Skills &amp; Roadmap
          </Button>
        </div>
      </div>

      {toast && <Alert kind={toast.isError ? "error" : "success"}>{toast.text}</Alert>}

      {error ? (
        <Card>
          <ErrorState message={error} onRetry={() => void load()} />
        </Card>
      ) : !profile ? (
        <LoadingBlock label="Loading CV & Resume Studio…" />
      ) : (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
          {/* Left column: Photo + Profile Form */}
          <div className="flex flex-col gap-6 lg:col-span-6">
            <Card className="p-5">
              <div className="flex flex-wrap items-center justify-between gap-4">
                <div className="flex items-center gap-4">
                  <UserAvatar
                    fullName={authUser?.fullName ?? "Graduate"}
                    avatarUrl={authUser?.avatarUrl}
                    size={72}
                  />
                  <div>
                    <div className="text-base font-semibold text-text">{authUser?.fullName ?? "Graduate Profile"}</div>
                    <div className="text-xs text-text-secondary">
                      {authUser?.email} · Circular avatar synced across your workspace
                    </div>
                  </div>
                </div>
                <div className="flex flex-wrap gap-2">
                  <input
                    ref={photoInputRef}
                    type="file"
                    accept="image/png,image/jpeg,image/webp"
                    className="hidden"
                    onChange={(e) => void handlePhotoSelected(e)}
                  />
                  <Button
                    size="sm"
                    variant="secondary"
                    disabled={photoBusy}
                    onClick={() => photoInputRef.current?.click()}
                  >
                    <UiIcon name="camera" />
                    {photoBusy ? "Uploading…" : "Upload Photo"}
                  </Button>
                  {authUser?.avatarUrl && (
                    <Button size="sm" variant="secondary" disabled={photoBusy} onClick={() => void handleRemovePhoto()}>
                      Remove
                    </Button>
                  )}
                </div>
              </div>
            </Card>

            <Card>
              <CardHeader title="Career Profile & Core Skills" />
              <form onSubmit={(e) => void saveProfile(e)} className="flex flex-col gap-4 p-5 text-sm">
                <div>
                  <label className="mb-1 block font-medium text-text">Professional Headline</label>
                  <input
                    type="text"
                    value={headline}
                    onChange={(e) => setHeadline(e.target.value)}
                    placeholder="e.g., Computer Engineering Graduate · Full-Stack & AI Engineer"
                    className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                </div>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <div>
                    <label className="mb-1 block font-medium text-text">Target Role</label>
                    <input
                      type="text"
                      value={targetRole}
                      onChange={(e) => setTargetRole(e.target.value)}
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block font-medium text-text">Preferred Location</label>
                    <input
                      type="text"
                      value={location}
                      onChange={(e) => setLocation(e.target.value)}
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                </div>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <div>
                    <label className="mb-1 block font-medium text-text">LinkedIn URL</label>
                    <input
                      type="url"
                      value={linkedinUrl}
                      onChange={(e) => setLinkedinUrl(e.target.value)}
                      placeholder="https://linkedin.com/in/..."
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block font-medium text-text">GitHub / Portfolio URL</label>
                    <input
                      type="url"
                      value={githubUrl}
                      onChange={(e) => setGithubUrl(e.target.value)}
                      placeholder="https://github.com/..."
                      className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                    />
                  </div>
                </div>
                <div>
                  <label className="mb-1 block font-medium text-text">Skills (comma-separated — powers live Job Matching)</label>
                  <input
                    type="text"
                    value={skillsText}
                    onChange={(e) => setSkillsText(e.target.value)}
                    className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                </div>
                <div>
                  <label className="mb-1 block font-medium text-text">Executive Summary</label>
                  <textarea
                    rows={3}
                    value={summary}
                    onChange={(e) => setSummary(e.target.value)}
                    className="w-full rounded-control border border-border bg-bg px-3.5 py-2 text-sm text-text outline-none focus:border-primary"
                  />
                </div>
                <div>
                  <button
                    type="submit"
                    disabled={saving}
                    className="btn-luminous inline-flex cursor-pointer items-center gap-2 rounded-control border-0 px-5 py-2.5 text-sm font-semibold disabled:opacity-60"
                  >
                    <UiIcon name="check" />
                    {saving ? "Saving…" : "Save Career Profile"}
                  </button>
                </div>
              </form>
            </Card>
          </div>

          {/* Right column: CV Upload, Manage/Delete, Manual Text Override & Analyze CV */}
          <div className="flex flex-col gap-6 lg:col-span-6">
            <Card>
              <CardHeader
                title="CV Upload & Management"
                action={
                  cvMetadata || profile.cvFileName ? (
                    <Button
                      size="sm"
                      variant="secondary"
                      disabled={cvBusy}
                      onClick={() => setConfirmDeleteOpen(true)}
                    >
                      <UiIcon name="trash-2" />
                      Delete CV
                    </Button>
                  ) : null
                }
              />
              <div className="flex flex-col gap-4 p-5 text-sm">
                <div className="rounded-control border border-dashed border-primary/45 bg-primary-light/30 p-5 text-center">
                  <div className="mx-auto mb-2 flex h-12 w-12 items-center justify-center rounded-full bg-primary-light text-primary">
                    <UiIcon name="upload" />
                  </div>
                  <div className="flex flex-wrap items-center justify-center gap-2">
                    <span className="font-semibold text-text">
                      {cvMetadata?.fileName ?? profile.cvFileName ?? "No CV uploaded yet"}
                    </span>
                    {cvStatus !== "empty" && (
                      <span
                        className={`rounded-md px-2 py-0.5 text-[11px] font-semibold ${
                          cvStatus === "analyzed" || cvStatus === "uploaded"
                            ? "bg-success-light text-success"
                            : cvStatus === "uploading"
                              ? "bg-primary-light text-primary"
                              : "bg-danger-light text-danger"
                        }`}
                      >
                        {cvStatus === "analyzed"
                          ? "Uploaded & Analyzed"
                          : cvStatus === "uploaded"
                            ? "Uploaded"
                            : cvStatus === "uploading"
                              ? "Uploading…"
                              : "Upload Failed"}
                      </span>
                    )}
                  </div>
                  {(cvMetadata?.uploadedAtUtc ?? profile.cvUploadedAtUtc) && (
                    <div className="mt-0.5 text-[11px] text-text-muted">
                      Last updated {fmtDate((cvMetadata?.uploadedAtUtc ?? profile.cvUploadedAtUtc)!)}
                      {cvMetadata?.fileSizeBytes ? ` · ${fmtFileSize(cvMetadata.fileSizeBytes)}` : ""}
                    </div>
                  )}
                  <p className="mt-1.5 text-xs text-text-secondary">
                    Supports <strong>.pdf</strong>, <strong>.docx</strong>, and <strong>.txt</strong> up to <strong>10 MB</strong> with automatic text extraction.
                  </p>
                  <input
                    ref={cvInputRef}
                    type="file"
                    accept=".pdf,.docx,.txt,.md"
                    disabled={cvBusy}
                    className="hidden"
                    onChange={(e) => void handleCvUpload(e)}
                  />
                  <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
                    <Button
                      size="sm"
                      disabled={cvBusy}
                      onClick={() => cvInputRef.current?.click()}
                    >
                      <UiIcon name="upload" />
                      {cvBusy ? "Processing…" : cvMetadata || profile.cvFileName ? "Replace CV File" : "Upload CV"}
                    </Button>
                    <Button
                      size="sm"
                      variant="secondary"
                      disabled={cvBusy}
                      onClick={() => void analyzeCv()}
                    >
                      <UiIcon name="sparkles" />
                      {cvBusy ? "Analyzing…" : "Analyze CV"}
                    </Button>
                  </div>
                </div>

                {/* Extracted / Manual Text Override */}
                <div className="rounded-control border border-border bg-bg/60 p-4">
                  <div className="mb-1.5 flex items-center justify-between gap-2">
                    <label htmlFor="cv-manual-text" className="text-xs font-semibold text-text">
                      Extracted CV Text / Manual Override
                    </label>
                    <span className="text-[11px] text-text-muted">{manualText.length} chars</span>
                  </div>
                  <textarea
                    id="cv-manual-text"
                    rows={6}
                    value={manualText}
                    onChange={(e) => setManualText(e.target.value)}
                    placeholder="Extracted text from your uploaded CV appears here. You can also paste or refine your resume text directly and save it…"
                    className="w-full rounded-control border border-border bg-bg px-3 py-2 text-xs leading-relaxed text-text outline-none focus:border-primary"
                  />
                  <div className="mt-2.5 flex flex-wrap gap-2">
                    <Button
                      size="sm"
                      variant="secondary"
                      disabled={textSaving}
                      onClick={() => void saveManualTextOverride()}
                    >
                      <UiIcon name="save" />
                      {textSaving ? "Saving…" : "Save Text Override"}
                    </Button>
                    <button
                      type="button"
                      disabled={cvBusy}
                      onClick={() => void analyzeCv()}
                      className="btn-luminous inline-flex cursor-pointer items-center gap-1.5 rounded-control border-0 px-3.5 py-1.5 text-xs font-semibold disabled:opacity-60"
                    >
                      <UiIcon name="sparkles" />
                      {cvBusy ? "Analyzing…" : "Analyze CV"}
                    </button>
                  </div>
                </div>

                {/* ATS Analysis Results */}
                {profile.cvScore !== null && profile.cvScore !== undefined && (
                  <div className="flex items-center justify-between rounded-control border border-border bg-bg/70 p-4">
                    <div>
                      <div className="text-xs font-semibold uppercase tracking-wide text-primary">ATS Readiness Score</div>
                      <div className="text-xs text-text-secondary">Tailored for {profile.targetRole}</div>
                    </div>
                    <div className="dash-num text-2xl font-bold text-primary">{profile.cvScore}/100</div>
                  </div>
                )}

                {profile.cvSummary && (
                  <div className="rounded-control border border-border bg-bg/70 p-4">
                    <div className="mb-1 text-xs font-semibold uppercase tracking-wide text-primary">Analysis Summary</div>
                    <p className="m-0 text-xs leading-relaxed text-text">{profile.cvSummary}</p>
                  </div>
                )}

                {profile.cvStrengths && profile.cvStrengths.length > 0 && (
                  <div>
                    <div className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-success">Strengths</div>
                    <ul className="m-0 list-disc space-y-1 pl-4 text-xs text-text-secondary">
                      {profile.cvStrengths.map((s, idx) => (
                        <li key={idx}>{s}</li>
                      ))}
                    </ul>
                  </div>
                )}

                {profile.cvImprovements && profile.cvImprovements.length > 0 && (
                  <div>
                    <div className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-warning">
                      Recommendations
                    </div>
                    <ul className="m-0 list-disc space-y-1 pl-4 text-xs text-text-secondary">
                      {profile.cvImprovements.map((s, idx) => (
                        <li key={idx}>{s}</li>
                      ))}
                    </ul>
                  </div>
                )}

                {profile.cvDetectedSkills && profile.cvDetectedSkills.length > 0 && (
                  <div className="rounded-control border border-border bg-bg/70 p-4">
                    <div className="mb-2 flex items-center justify-between gap-2">
                      <span className="text-xs font-semibold uppercase tracking-wide text-text-secondary">
                        Detected Skills
                      </span>
                      <button
                        type="button"
                        onClick={() => void syncDetectedSkills()}
                        className="cursor-pointer border-0 bg-transparent text-xs font-semibold text-primary hover:underline"
                      >
                        + Sync to Profile Skills
                      </button>
                    </div>
                    <div className="flex flex-wrap gap-1.5">
                      {profile.cvDetectedSkills.map((sk) => (
                        <span key={sk} className="rounded-md bg-primary-light px-2 py-0.5 text-[11px] font-medium text-primary">
                          {sk}
                        </span>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            </Card>
          </div>
        </div>
      )}

      <ConfirmDialog
        open={confirmDeleteOpen}
        title="Delete Uploaded CV?"
        confirmLabel="Delete CV"
        danger
        onCancel={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void deleteCv();
        }}
      >
        This will permanently remove <strong>{profile?.cvFileName}</strong>, its extracted text, and its current ATS analysis from your profile.
      </ConfirmDialog>
    </div>
  );
}
