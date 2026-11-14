// Shapes for endpoints that return anonymous objects (ActionResult<object>)
// instead of a named DTO. Field names follow the C# anonymous types in the
// controllers (camelCased by ASP.NET Core) and BlazorDtos.cs.

/** GET /api/announcements (AnnouncementsController.GetAll). */
export interface AnnouncementItem {
  id: number;
  title: string;
  body: string;
  category: string | null;
  createdAtUtc: string;
  universityId?: number | null;
  courseId: number | null;
  courseName: string | null;
}

/** GET /api/career/recommendations. */
export interface CareerRecommendation {
  title: string;
  description: string;
  requiredSkills: string;
  recommendedCourses: string;
  internshipTypes: string;
  matchConfidence: string;
}

/** GET /api/career/skill-gaps. */
export interface CareerSkillGaps {
  completedCourses: string[];
  pendingCourses: string[];
  recommendation: string;
}

/** GET /api/research/papers, /api/research/trending. */
export interface ResearchPaper {
  id: number;
  title: string;
  authors: string;
  abstract: string | null;
  url: string | null;
  source: string | null;
  publishedDate: string | null;
}

/** GET /api/research/summarize/{id}. */
export interface PaperSummary {
  id: number;
  title: string;
  summary: string;
  originalLength?: number;
  summaryLength?: number;
}

/** GET /api/admin/analytics. */
export interface AdminAnalytics {
  totalStudents: number;
  totalGraduates: number;
  totalDoctors: number;
  totalCourses: number;
  totalEnrollments: number;
  averageGpa: number;
  averageAttendance: number;
  gpaDistribution: Array<{ range: string; count: number }>;
  topCourses: Array<{ course: string; count: number }>;
  recentAuditLogs: Array<{ userId: number; method: string; path: string; statusCode: number; durationMs: number; timestamp: string }>;
}

/** GET /api/admin/announcements. */
export interface AdminAnnouncement {
  id: number;
  title: string;
  body: string;
  category: string | null;
  createdAtUtc: string;
  universityId?: number | null;
  courseId?: number | null;
}

/** GET /api/grad/roadmap/suggest. */
export interface RoadmapSuggestionList {
  suggestions: Array<{ title: string; category: string; notes: string | null; why: string | null }>;
  source: string;
}
