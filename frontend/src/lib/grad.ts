/* The graduate's profile, latest CV and latest analysis, shared by Career Overview and CV Studio. */
import { API_BASE_URL, authHeaders, errorMessage } from "./api";

export interface GradProfile {
  userId: number;
  headline: string | null;
  careerSummary: string | null;
  linkedInUrl: string | null;
  gitHubUrl: string | null;
  portfolioUrl: string | null;
  preferredJobTitles: string | null;
  preferredIndustries: string | null;
  preferredLocations: string | null;
  workModes: string;
  employmentType: string;
  experienceLevel: string;
  languages: string | null;
  careerGoal: string | null;
  targetRole: string | null;
  primaryStruggle: string | null;
  completionPercent: number;
  skills: { id: number; name: string }[];
}

export interface CvDocument {
  id: number;
  fileName: string;
  sizeBytes: number;
  parseStatus: string;
  parseError: string | null;
  uploadedAtUtc: string;
  textChars: number;
}

export interface CvAnalysis {
  overallAssessment: string | null;
  scorePercent: number | null;
  strengths: string[];
  suggestions: string[];
  technicalSkills: string[] | null;
}

/** fetch against the API with the bearer token; throws the server's message on failure. */
export async function gradFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const json: Record<string, string> = typeof init.body === "string" ? { "Content-Type": "application/json" } : {};
  const res = await fetch(`${API_BASE_URL}/api/${path}`, { ...init, headers: { ...authHeaders(), ...json } });
  if (!res.ok) {
    const error = new Error(await errorMessage(res, `Request failed (HTTP ${res.status}).`));
    error.name = res.status === 401 ? "Unauthorized" : "ApiError";
    throw error;
  }
  return (res.status === 204 ? undefined : await res.json()) as T;
}

export async function loadGradCareer() {
  const [profile, documents] = await Promise.all([
    gradFetch<GradProfile>("grad/profile"),
    gradFetch<CvDocument[]>("grad/profile/cv"),
  ]);
  const cv = [...documents].sort((a, b) => b.uploadedAtUtc.localeCompare(a.uploadedAtUtc))[0] ?? null;
  const analyses = cv ? await gradFetch<CvAnalysis[]>(`grad/profile/cv/${cv.id}/analyses`) : [];
  return { profile, cv, analysis: analyses[0] ?? null };
}

export const onboardingSkippedKey = (userId: number) => `unisphere.onboarding.skipped.${userId}`;
