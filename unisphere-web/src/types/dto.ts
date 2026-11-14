/* Generated from the backend C# DTO records (Dtos/*.cs, request records in Controllers/).
   ASP.NET Core serialises with camelCase property names; DateTime values arrive as ISO strings. */

export interface UserDto {
  id: number;
  fullName: string;
  firstName: string;
  avatarUrl: string | null;
  major: string;
}

export interface MeDto {
  id: number;
  fullName: string;
  email: string;
  avatarUrl: string | null;
  major: string;
  roleId: number;
  role: string;
}

export interface ModuleDto {
  key: string;
  title: string;
  subtitle: string;
  icon: string;
  backgroundColor: string;
  accentColor: string;
}

export interface EventDto {
  id: number;
  title: string;
  location: string;
  startsAtUtc: string;
  imageUrl: string | null;
  description: string | null;
}

export interface NotificationDto {
  id: number;
  title: string;
  body: string;
  createdAtUtc: string;
  isRead: boolean;
  category: string;
}

export interface RecommendationDto {
  headline: string;
  message: string;
  route: string;
}

export interface HomeResponse {
  user: UserDto;
  greeting: string;
  unreadNotifications: number;
  modules: ModuleDto[];
  upcomingEvents: EventDto[];
  recommendation: RecommendationDto;
}

export interface SearchItemDto {
  id: number | null;
  title: string;
  subtitle: string;
  route: string;
  icon: string;
}

export interface SearchGroupDto {
  type: string;
  label: string;
  items: SearchItemDto[];
}

export interface SearchResponseDto {
  groups: SearchGroupDto[];
}

export interface CourseDto {
  id: number;
  code: string;
  title: string;
  description: string;
  track: string;
  credits: number;
  instructors: string;
}

export interface UpdateUserRequest {
  fullName: string;
  major: string;
  avatarUrl: string | null;
}

export interface AcademicStats {
  gpa: number;
  gpaDelta: number;
  maxGpa: number;
  completedCourses: number;
  totalCourses: number;
  completedPercent: number;
  attendanceRate: number;
  attendanceDelta: number;
  subjectsToImprove: number;
  recommendedCourses: number;
}

export interface GradeRowDto {
  courseId: number;
  course: string;
  gradeLetter: string;
  gradePoints: number;
  gradeDisplay: string;
  attendance: number;
  status: string;
}

export interface StudyDayDto {
  id: number;
  date: string;
  dayLabel: string;
  dayNumber: number;
  title: string;
  hours: number;
  note: string | null;
  isDone: boolean;
}

export interface StudyPlannerResponse {
  weekLabel: string;
  weekStart: string;
  days: StudyDayDto[];
}

export interface InsightDto {
  kind: string;
  title: string;
  message: string;
  actionLabel: string | null;
  actionRoute: string | null;
}

export interface QuickActionDto {
  key: string;
  title: string;
  icon: string;
  route: string;
  backgroundColor: string;
  accentColor: string;
}

export interface ActivityDto {
  id: number;
  kind: string;
  message: string;
  createdAtUtc: string;
  timeAgo: string;
}

export interface CreateStudyItemRequest {
  date: string;
  title: string;
  hours: number;
  note: string | null;
}

export interface AcademicDashboardResponse {
  user: UserDto;
  greeting: string;
  tagline: string;
  unreadNotifications: number;
  stats: AcademicStats;
  performance: GradeRowDto[];
  studyPlanner: StudyPlannerResponse;
  insights: InsightDto[];
  recommendedCourses: CourseDto[];
  quickActions: QuickActionDto[];
  recentActivity: ActivityDto[];
}

export interface DashboardSummaryDto {
  fullName: string;
  firstName: string;
  greeting: string;
  currentGpa: number;
  gpaDeltaVsLastSemester: number;
  completedCourses: number;
  totalCourses: number;
  completionPercent: number;
  attendanceRate: number;
  attendanceDelta: number;
  subjectsToImprove: number;
  recommendedCoursesCount: number;
  unreadNotifications: number;
}

export interface DashboardPerformanceRowDto {
  courseId: number;
  courseName: string;
  gradeLetter: string;
  gradePoints: number;
  attendancePercent: number;
  status: string;
}

export interface DashboardStudyDayDto {
  id: number;
  date: string;
  dayLabel: string;
  subject: string;
  hours: number;
  isCompleted: boolean;
  isFreeDay: boolean;
}

export interface DashboardStudyPlanDto {
  weekLabel: string;
  weekStart: string;
  days: DashboardStudyDayDto[];
}

export interface DashboardInsightDto {
  type: string;
  title: string;
  message: string;
  actionLabel: string | null;
  actionRoute: string | null;
}

export interface DashboardRecommendedCourseDto {
  id: number;
  name: string;
  track: string;
  credits: number;
  tag: string;
}

export interface DashboardActivityDto {
  id: number;
  text: string;
  createdAt: string;
}

export interface UnreadCountDto {
  unreadCount: number;
}

export interface FactorContributionDto {
  factor: string;
  weight: number;
  value: number;
  contribution: number;
  description: string;
}

export interface PerformanceCourseRiskDto {
  course: string;
  riskLevel: string;
  reason: string;
}

export interface PerformanceAnalysisDto {
  riskLevel: string;
  predictedGpa: number;
  confidence: number;
  score: number;
  strengths: string[];
  weaknesses: string[];
  recommendations: string[];
  courseRisks: PerformanceCourseRiskDto[];
  factors: FactorContributionDto[];
}

export interface GpaTrendPointDto {
  label: string;
  gpa: number;
}

export interface DailyCountDto {
  date: string;
  count: number;
}

export interface AdminOverviewDto {
  totalStudents: number;
  totalDoctors: number;
  totalGraduates: number;
  totalAdmins: number;
  newRegistrations7d: number;
  newRegistrations30d: number;
  activeUsersToday: number;
  loginsPerDay: DailyCountDto[];
  onTrackStudents: number;
  needsAttentionStudents: number;
  atRiskStudents: number;
  averageGpa: number;
  averageAttendance: number;
  coursesCount: number;
}

export interface AdminUserDto {
  id: number;
  fullName: string;
  email: string;
  role: string;
  roleId: number;
  isActive: boolean;
  isDeleted: boolean;
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
  major: string;
}

export interface PagedResultDto<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface PatchUserStatusRequest {
  isActive: boolean;
}

export interface PatchUserRoleRequest {
  role: string;
}

export interface DepartmentStatsDto {
  department: string;
  students: number;
  averageGpa: number;
  averageAttendance: number;
}

export interface AtRiskStudentDto {
  id: number;
  fullName: string;
  email: string;
  department: string;
  riskLevel: string;
  score: number;
  predictedGpa: number;
}

export interface AuditLogDto {
  id: number;
  userId: number;
  method: string;
  path: string;
  statusCode: number;
  durationMs: number;
  timestamp: string;
  ipAddress: string;
}

export interface SystemHealthDto {
  status: string;
  database: string;
  uptimeSeconds: number;
  version: string;
  serverTimeUtc: string;
}

export interface UniversityDto {
  id: number;
  name: string;
  logo: string | null;
}

export interface MyUniversityDto {
  universityName: string | null;
  department: string | null;
  academicYear: number | null;
  coursesCount: number;
  materialsCount: number;
}

export interface UniversityCourseDto {
  id: number;
  code: string;
  title: string;
  department: string;
  academicYear: number | null;
  semester: string;
  materialsCount: number;
  latestMaterialDate: string | null;
  instructors: string;
  status: string;
  matchesStudentDepartmentYear: boolean;
}

export interface MaterialDto {
  id: number;
  courseId: number;
  courseName: string;
  title: string;
  type: string;
  description: string;
  content: string;
  fileUrl: string | null;
  createdAt: string;
  instructorName: string;
  hasUploadedFile: boolean;
  uploadedFileName: string | null;
  uploadedFileSizeBytes: number | null;
}

export interface UpcomingItemDto {
  id: number;
  title: string;
  courseName: string;
  type: string;
  date: string;
  description: string | null;
}

export interface CreateMaterialRequest {
  courseId: number;
  title: string;
  type: string;
  description: string | null;
  content: string | null;
  fileUrl: string | null;
}

export interface ChatProviderDto {
  name: string;
  displayName: string;
  model: string;
}

export interface ChatProviderStatusDto {
  name: string;
  displayName: string;
  model: string;
  available: boolean;
  reason: string;
}

export interface ChatConversationDto {
  id: number;
  title: string;
  provider: string;
  updatedAtUtc: string;
  messageCount: number;
}

export interface ChatMessageDto {
  id: number;
  role: string;
  content: string;
  provider: string;
  model: string;
  createdAtUtc: string;
}

export interface ChatConversationDetailDto {
  id: number;
  title: string;
  provider: string;
  updatedAtUtc: string;
  messages: ChatMessageDto[];
}

export interface SendChatMessageRequest {
  provider: string;
  content: string;
}

export interface RenameConversationRequest {
  title: string;
}

export interface RegisterRequest {
  firstName: string;
  middleName: string | null;
  lastName: string;
  phoneNumber: string;
  email: string;
  gender: string;
  age: number;
  role: string;
  password: string;
  confirmPassword: string;
  academicYear: number | null;
  department: string | null;
  specialization: string | null;
  graduatedFrom: string | null;
  graduationYear: number | null;
  universityId: number | null;
  address: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
  roleId: number | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface AuthUserDto {
  id: number;
  fullName: string;
  email: string;
  roleId: number;
  role: string;
  major: string;
  avatarUrl: string | null;
}

export interface RoleDto {
  id: number;
  name: string;
}

export interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  user: AuthUserDto;
}

export interface InstructorAssignmentDto {
  id: number;
  courseId: number;
  courseCode: string;
  courseTitle: string;
  universityId: number | null;
  universityName: string;
  doctorUserId: number;
  doctorName: string;
  term: string;
  createdAtUtc: string;
}

export interface CreateInstructorAssignmentRequest {
  courseId: number;
  doctorUserId: number;
  term: string;
}

export interface AdminDoctorDto {
  id: number;
  fullName: string;
  email: string;
  universityId: number | null;
  universityName: string;
  specialization: string;
  isActive: boolean;
}

export interface AdminCourseDto {
  id: number;
  code: string;
  title: string;
  universityId: number | null;
}

export interface DoctorCourseDto {
  id: number;
  code: string;
  title: string;
  department: string;
  academicYear: number | null;
  semester: string;
  credits: number;
  prerequisites: string;
  studentsCount: number;
  materialsCount: number;
  terms: string;
  displayTitle: string | null;
  /** Serialized alias of id. */
  courseId: number;
  /** Serialized alias of studentsCount. */
  studentCount: number;
}

export interface CreateDoctorAnnouncementRequest {
  courseId: number;
  title: string;
  body: string;
  category: string | null;
}

export interface DoctorSummaryDto {
  coursesCount: number;
  studentsCount: number;
  materialsCount: number;
  announcementsCount: number;
  latestMaterials: MaterialDto[];
  latestAnnouncements: DoctorAnnouncementDto[];
  latestByMe: DoctorAnnouncementDto[];
  upcoming: MaterialDto[];
}

export interface UpdateDoctorCourseRequest {
  displayTitle: string;
}

export interface AdminUserDetailDto {
  id: number;
  firstName: string;
  middleName: string | null;
  lastName: string;
  fullName: string;
  email: string;
  address: string;
  phoneNumber: string;
  universityId: number | null;
  universityName: string;
  department: string;
  role: string;
  roleId: number;
  isActive: boolean;
  isDeleted: boolean;
}

export interface AdminUpdateUserRequest {
  firstName: string;
  middleName: string | null;
  lastName: string;
  email: string;
  phoneNumber: string;
  address: string | null;
  department: string | null;
  universityId: number | null;
}

export interface DoctorResearchDto {
  id: number;
  title: string;
  abstract: string;
  authors: string;
  keywords: string;
  year: number;
  field: string;
  doiOrUrl: string | null;
  isPublished: boolean;
  hasPdf: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ResearchBrowseItemDto {
  id: number;
  title: string;
  authors: string;
  keywords: string;
  year: number;
  field: string;
  authorName: string;
  universityName: string;
  hasPdf: boolean;
  createdAtUtc: string;
}

export interface ResearchDetailDto {
  id: number;
  title: string;
  abstract: string;
  authors: string;
  keywords: string;
  year: number;
  field: string;
  doiOrUrl: string | null;
  authorName: string;
  authorDepartment: string;
  universityName: string;
  hasPdf: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ResearchUniversityOptionDto {
  id: number;
  name: string;
}

export interface ResearchFiltersDto {
  universities: ResearchUniversityOptionDto[];
  fields: string[];
  years: number[];
}

export interface GraduationChecklistItemDto {
  key: string;
  title: string;
  detail: string;
  isMet: boolean;
}

export interface GraduationApplicationSummaryDto {
  id: number;
  status: string;
  submittedAtUtc: string;
  reviewedAtUtc: string | null;
  decisionNote: string | null;
  conferredGraduationYear: number | null;
}

export interface GraduationAlumniDto {
  graduatedFrom: string;
  graduationYear: number | null;
  finalGpa: number;
  honors: string;
  hasCertificate: boolean;
}

export interface GraduationOverviewResponse {
  role: string;
  isGraduated: boolean;
  degree: string;
  department: string;
  universityName: string;
  academicYear: number | null;
  gpa: number;
  minGpaRequired: number;
  coursesCompleted: number;
  coursesRequired: number;
  completionPercent: number;
  creditsCompleted: number;
  creditsInProgress: number;
  failedCourses: string[];
  isEligible: boolean;
  blockingReasons: string[];
  checklist: GraduationChecklistItemDto[];
  applicationStatus: string;
  application: GraduationApplicationSummaryDto | null;
  alumni: GraduationAlumniDto | null;
}

export interface GraduationApplyRequest {
  note: string | null;
  expectedGraduationYear: number | null;
}

export interface GraduationCeremonyDto {
  id: number;
  title: string;
  location: string;
  startsAtUtc: string;
  description: string | null;
}

export interface GraduationCertificateDto {
  certificateId: string;
  holder: string;
  degree: string;
  university: string;
  graduationYear: number;
  finalGpa: number;
  honors: string;
  conferredOnUtc: string | null;
}

export interface GraduationApplicationAdminDto {
  id: number;
  userId: number;
  studentName: string;
  email: string;
  degree: string;
  gpaAtApplication: number;
  coursesCompleted: number;
  coursesRequired: number;
  status: string;
  submittedAtUtc: string;
  reviewedAtUtc: string | null;
  decisionNote: string | null;
  conferredGraduationYear: number | null;
}

export interface ReviewGraduationApplicationRequest {
  approve: boolean;
  note: string | null;
  graduationYear: number | null;
}

export interface ExamGenerationRequest {
  courseId: number;
  questionCount: number;
  questionTypes: string;
  difficulty: string;
  language: string;
}

export interface GeneratedExamQuestionDto {
  questionText: string;
  questionType: string;
  correctAnswer: string | null;
  explanation: string | null;
  sourceMaterial: string | null;
  optionsJson: string | null;
}

export interface ExamGenerationResponse {
  provider: string;
  model: string;
  questions: GeneratedExamQuestionDto[];
  courseId: number;
  courseCode: string;
  courseTitle: string;
  ragWarning: string | null;
}

export interface ExamQuestionDto {
  id: number;
  examId: number;
  questionText: string;
  questionType: string;
  correctAnswer: string | null;
  explanation: string | null;
  sourceMaterial: string | null;
  optionsJson: string | null;
  orderIndex: number;
  marks: number | null;
}

export interface ExamDto {
  id: number;
  doctorUserId: number;
  courseId: number;
  courseCode: string;
  courseTitle: string;
  title: string;
  questionCount: number;
  questionTypes: string;
  difficulty: string;
  language: string;
  materialsUsed: string | null;
  createdAtUtc: string;
  status: string;
  publishedAtUtc: string | null;
  marks: number | null;
  questions: ExamQuestionDto[];
}

export interface ExamListDto {
  id: number;
  courseId: number;
  courseCode: string;
  courseTitle: string;
  title: string;
  questionCount: number;
  difficulty: string;
  language: string;
  createdAtUtc: string;
  status: string;
  publishedAtUtc: string | null;
}

export interface UpdateExamRequest {
  courseId: number;
  title: string;
  questionCount: number;
  questionTypes: string;
  difficulty: string;
  language: string;
  materialsUsed: string | null;
  questions: ExamQuestionDto[];
}

export interface UpdateExamQuestionRequest {
  questionText: string;
  questionType: string;
  correctAnswer: string | null;
  explanation: string | null;
  sourceMaterial: string | null;
  optionsJson: string | null;
  orderIndex: number;
}

export interface ExamAssistantGenerationRequest {
  courseId: number;
  materialIds: number;
  questionCount: number;
  difficulty: string;
  language: string;
}

export interface ExamAssistantPublishRequest {
  examId: number;
  title: string;
  marks: number | null;
}

export interface StudentExamDto {
  id: number;
  courseId: number;
  courseCode: string;
  courseTitle: string;
  title: string;
  questionCount: number;
  marks: number | null;
  createdAtUtc: string;
  publishedAtUtc: string | null;
}

export interface StudentExamQuestionDto {
  id: number;
  examId: number;
  questionText: string;
  questionType: string;
  optionsJson: string | null;
  orderIndex: number;
  marks: number | null;
}

export interface UserNotificationSettingDto {
  userId: number;
  preference: string;
  mutedUntilUtc: string | null;
}

export interface UpdateUserNotificationSettingRequest {
  preference: string;
  mutedUntilUtc: string | null;
}

export interface DoctorResearchUploadRequest {
  title: string;
  abstract: string;
  authors: string;
  keywords: string;
  year: number;
  field: string;
  doiOrUrl: string | null;
  isPublished: boolean;
  pdf: File | null;
}

export interface DoctorProfileDto {
  id: number;
  firstName: string;
  middleName: string | null;
  lastName: string;
  fullName: string;
  universityId: number | null;
  email: string;
  address: string | null;
  phoneNumber: string;
  universityName: string | null;
  department: string;
  specialization: string | null;
  avatarUrl: string | null;
  courses: DoctorProfileCourseDto[];
}

export interface DoctorProfileCourseDto {
  courseId: number;
  code: string;
  catalogTitle: string;
  displayTitle: string | null;
  semester: string;
  enrolledCount: number;
}

export interface UpdateDoctorProfileRequest {
  avatarUrl: string | null;
}

export interface SetCourseDisplayTitleRequest {
  displayTitle: string | null;
}

export interface DoctorDashboardDto {
  doctorName: string;
  courseCount: number;
  studentCount: number;
  averageGradePoints: number | null;
  averageAttendance: number | null;
  courses: DoctorCourseSummaryDto[];
  alerts: DoctorAlertDto[];
  recentAnnouncements: DoctorAnnouncementDto[];
}

export interface DoctorCourseSummaryDto {
  courseId: number;
  code: string;
  title: string;
  track: string;
  credits: number;
  semester: string;
  studentCount: number;
  averageGradePoints: number | null;
  averageAttendance: number | null;
}

export interface DoctorCourseDetailDto {
  id: number;
  code: string;
  title: string;
  description: string;
  track: string;
  credits: number;
  semester: string;
  department: string;
  academicYear: number | null;
}

export interface DoctorStudentDto {
  enrollmentId: number;
  studentId: number;
  fullName: string;
  email: string;
  department: string;
  academicYear: number | null;
  gradeLetter: string;
  gradePoints: number;
  attendancePercent: number;
  status: string;
  major: string;
}

export interface GradeBucketDto {
  label: string;
  count: number;
}

export interface AttendanceBucketDto {
  label: string;
  count: number;
}

export interface DoctorAlertDto {
  studentId: number;
  enrollmentId: number | null;
  studentName: string;
  reason: string;
  severity: string;
}

export interface DoctorCourseAnalyticsDto {
  courseId: number;
  code: string;
  title: string;
  enrollmentCount: number;
  gradedCount: number;
  averageGradePoints: number | null;
  averageAttendance: number | null;
  gradeDistribution: GradeBucketDto[];
  attendanceDistribution: AttendanceBucketDto[];
  alerts: DoctorAlertDto[];
}

export interface DoctorMaterialDto {
  id: number;
  courseId: number;
  title: string;
  type: string;
  description: string;
  content: string;
  fileUrl: string | null;
  deadlineUtc: string | null;
  createdAtUtc: string;
  hasUploadedFile: boolean;
  uploadedFileName: string | null;
  uploadedFileSizeBytes: number | null;
}

export interface CreateDoctorMaterialRequest {
  title: string;
  type: string;
  description: string | null;
  content: string | null;
  fileUrl: string | null;
  deadlineUtc: string | null;
  courseId?: number;
}

export interface UpdateDoctorMaterialRequest {
  title: string;
  type: string;
  description: string | null;
  content: string | null;
  fileUrl: string | null;
  deadlineUtc: string | null;
}

export interface MaterialUploadResultDto {
  fileUrl: string;
  fileName: string;
  sizeBytes: number;
}

export interface DoctorAnnouncementDto {
  id: number;
  courseId: number | null;
  courseName: string | null;
  title: string;
  body: string;
  category: string | null;
  createdAtUtc: string;
  isMine: boolean;
}

export interface CreateCourseAnnouncementRequest {
  title: string;
  body: string;
  category: string | null;
}

export interface CourseAssignmentDto {
  id: number;
  courseId: number;
  courseCode: string;
  courseTitle: string;
  doctorUserId: number;
  doctorName: string;
  assignedAtUtc: string;
}

export interface CreateCourseAssignmentRequest {
  doctorUserId: number;
  courseId: number;
}

export interface QuizGenerationRequest {
  courseId: number;
  materialId: number | null;
  questionCount: number;
}

export interface GeneratedQuestionDto {
  question: string;
  options: string[];
  correctIndex: number;
  explanation: string;
  reference: string | null;
}

export interface QuizGenerationResponse {
  provider: string;
  model: string;
  questions: GeneratedQuestionDto[];
  isDraft: boolean;
}

export interface SummarizeRequest {
  courseId: number;
  materialId: number;
}

export interface LectureSummaryDto {
  materialId: number;
  title: string;
  summary: string;
  provider: string;
  model: string;
}

export interface LectureQuestionRequest {
  courseId: number;
  question: string;
}

export interface LectureAnswerDto {
  answer: string;
  sources: string[];
  grounded: boolean;
  provider: string;
  model: string;
}

export interface GraduateSkillDto {
  id: number;
  name: string;
  level: string | null;
  source: string;
}

export interface GraduateSkillCreateRequest {
  name: string;
  level: string | null;
}

export interface ProfileEntryDto {
  id: number;
  kind: string;
  title: string;
  organization: string | null;
  description: string | null;
  period: string | null;
}

export interface ProfileEntryCreateRequest {
  kind: string;
  title: string;
  organization: string | null;
  description: string | null;
  period: string | null;
}

export interface GraduateProfileDto {
  userId: number;
  fullName: string;
  firstName: string;
  avatarUrl: string | null;
  major: string;
  department: string;
  universityName: string | null;
  graduationYear: number | null;
  graduatedFrom: string | null;
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
  completionPercent: number;
  missingFields: string[];
  skills: GraduateSkillDto[];
  entries: ProfileEntryDto[];
}

export interface GraduateProfileUpdateRequest {
  headline: string | null;
  careerSummary: string | null;
  linkedInUrl: string | null;
  gitHubUrl: string | null;
  portfolioUrl: string | null;
  preferredJobTitles: string | null;
  preferredIndustries: string | null;
  preferredLocations: string | null;
  workModes: string | null;
  employmentType: string | null;
  experienceLevel: string | null;
  languages: string | null;
  careerGoal: string | null;
}

export interface ProfileCompletionDto {
  percent: number;
  missingFields: string[];
  nextActions: string[];
}

export interface CvDocumentDto {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  parseStatus: string;
  parseError: string | null;
  uploadedAtUtc: string;
  hasAnalysis: boolean;
  analysisId: number | null;
  textChars: number;
}

export interface CvAnalysisDto {
  id: number;
  cvDocumentId: number;
  overallAssessment: string | null;
  scorePercent: number | null;
  strengths: string[];
  weaknesses: string[];
  suggestions: string[];
  rewrites: KeywordRewrite[];
  keywords: string[];
  providerName: string;
  model: string;
  createdAtUtc: string;
}

export interface KeywordRewrite {
  original: string;
  rewritten: string;
  why: string;
}

export interface CvAnalyzeRequest {
  targetRole: string | null;
}

export interface JobListingDto {
  id: number;
  source: string;
  title: string;
  companyName: string;
  location: string;
  remote: boolean;
  jobType: string;
  category: string;
  salary: string | null;
  url: string;
  publishedAtUtc: string;
  keySkills: string[];
  isSaved: boolean;
  isApplied: boolean;
  snippet: string | null;
}

export interface JobSearchRequest {
  search: string | null;
  location: string | null;
  industry: string | null;
  employmentType: string | null;
  experienceLevel: string | null;
  remote: boolean | null;
  sort: string | null;
  page: number;
  pageSize: number;
}

export interface PagedJobsDto {
  jobs: JobListingDto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface JobMatchDto {
  job: JobListingDto;
  scorePercent: number;
  matchedSkills: string[];
  missingSkills: string[];
  explanation: string;
  actions: string[];
}

export interface SkillGapReportDto {
  targetRole: string;
  coveragePercent: number;
  matched: SkillGapItem[];
  missing: SkillGapItem[];
  recommendedSteps: string[];
}

export interface SkillGapItem {
  skill: string;
  level: string | null;
  priority: string;
  jobsRequiring: string;
}

export interface CvParseResponse {
  parseStatus: string;
  parseError: string | null;
  textChars: number;
}

export interface ApplicationDto {
  id: number;
  jobListingId: number | null;
  companyName: string;
  jobTitle: string;
  sourceUrl: string | null;
  status: string;
  notes: string | null;
  followUpDate: string | null;
  appliedAtUtc: string;
  updatedAtUtc: string;
}

export interface ApplicationCreateRequest {
  jobListingId: number | null;
  companyName: string | null;
  jobTitle: string | null;
  sourceUrl: string | null;
  status: string | null;
  notes: string | null;
  followUpDate: string | null;
  appliedAtUtc: string | null;
}

export interface ApplicationUpdateRequest {
  status: string | null;
  notes: string | null;
  followUpDate: string | null;
}

export interface ApplicationSummaryDto {
  total: number;
  byStatus: StatusCount[];
  upcomingFollowUps: number;
  interviews: number;
}

export interface StatusCount {
  status: string;
  count: number;
}

export interface RoadmapItemDto {
  id: number;
  title: string;
  category: string | null;
  status: string;
  notes: string | null;
  targetDate: string | null;
  sortOrder: number;
  origin: string;
  completedAtUtc: string | null;
}

export interface RoadmapItemCreateRequest {
  title: string;
  category: string | null;
  status: string | null;
  notes: string | null;
  targetDate: string | null;
  sortOrder: number | null;
}

export interface RoadmapItemUpdateRequest {
  title: string | null;
  category: string | null;
  status: string;
  notes: string | null;
  targetDate: string | null;
  sortOrder: number | null;
}

export interface RoadmapProgressDto {
  total: number;
  notStarted: number;
  inProgress: number;
  completed: number;
  percent: number;
}

export interface RoadmapSuggestionDto {
  title: string;
  category: string;
  notes: string | null;
  why: string | null;
}

export interface InterviewQuestionDto {
  id: number;
  questionText: string;
  answerText: string | null;
  feedback: string | null;
  feedbackScore: number | null;
}

export interface InterviewSessionDto {
  id: number;
  targetRole: string;
  jobListingId: number | null;
  questionSource: string;
  providerName: string | null;
  model: string | null;
  createdAtUtc: string;
  answeredCount: number;
  totalCount: number;
  questions: InterviewQuestionDto[] | null;
}

export interface InterviewSessionCreateRequest {
  targetRole: string | null;
  jobListingId: number | null;
  useAi: boolean | null;
}

export interface InterviewAnswerRequest {
  answerText: string;
}

export interface GraduateOverviewDto {
  firstName: string;
  fullName: string;
  profileCompletionPercent: number;
  profileMissingFields: string[];
  cvStatus: string | null;
  cvUploadedAtUtc: string | null;
  cvAnalysisScore: number | null;
  matchingJobsCount: number;
  matchingJobsAboveThreshold: number;
  savedJobsCount: number;
  applicationsCount: number;
  applicationsInProgress: number;
  upcomingFollowUpsCount: number;
  roadmapProgressPercent: number;
  topSkillGaps: SkillGapItem[];
  nextActions: string[];
  upcomingEvents: GradEventDto[];
  recentActivity: GradActivityDto[];
  unreadNotifications: number;
  previousAnalysisCount: number;
}

export interface GradEventDto {
  id: number;
  title: string;
  location: string;
  startsAtUtc: string;
  description: string | null;
}

export interface GradActivityDto {
  id: number;
  kind: string;
  message: string;
  createdAtUtc: string;
}

export interface GradAnnouncementDto {
  id: number;
  title: string;
  body: string;
  category: string | null;
  createdAtUtc: string;
}

export interface GradAlumniDto {
  universityName: string | null;
  department: string;
  graduationYear: string | null;
  degree: string | null;
  careerEvents: GradEventDto[];
  announcements: GradAnnouncementDto[];
  careerCenterTip: string;
  contacts: string[];
}

export interface GradInsightsDto {
  jobsIndexed: number;
  lastSyncUtc: string | null;
  topSkills: NameCount[];
  jobsByIndustry: NameCount[];
  jobsByWorkType: NameCount[];
  recentTitles: string[];
}

export interface NameCount {
  name: string;
  count: number;
}

export interface GradSettingsDto {
  notifyNewMatches: boolean;
  notifyFollowUps: boolean;
  matchAlertThresholdPercent: number;
  showGpaOnProfile: boolean;
  themePreference: string;
  alertKeywords: string | null;
  alertLocations: string | null;
}

export interface ResearchHubItemDto {
  id: number;
  title: string;
  abstract: string;
  authors: string;
  keywords: string;
  year: number;
  field: string;
  doiOrUrl: string | null;
  isPublished: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  authorDoctorUserId: number;
  authorName: string;
  authorUniversity: string;
  authorDepartment: string;
  hasPdf: boolean;
}

export interface ResearchHubFiltersDto {
  universities: UniversityDto[];
  fields: string[];
  years: number[];
}

export interface ResearchHubPageDto {
  items: ResearchHubItemDto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ResearchUploadRequest {
  title: string;
  abstract: string;
  authors: string;
  keywords: string;
  year: number;
  field: string;
  doiOrUrl: string | null;
  isPublished: boolean;
}

export interface SetResearchPublishRequest {
  isPublished: boolean;
}

export interface SendNotificationRequest {
  title: string;
  body: string;
  userId: number | null;
  roleId: number | null;
}

export interface CreateAnnouncementRequest {
  title: string;
  body: string;
  category: string | null;
  universityId: number | null;
}

export interface CreateEnrollmentRequest {
  courseId: number;
}

export interface UpdateGradeRequest {
  gradeLetter: string | null;
  gradePoints: number | null;
  attendancePercent: number | null;
  status: string | null;
}

export interface EnrollmentDto {
  id: number;
  courseId: number;
  courseCode: string;
  courseTitle: string;
  semester: string;
  gradeLetter: string;
  gradePoints: number;
  attendancePercent: number;
  status: string;
}

export interface CreateEventRequest {
  title: string;
  location: string;
  startsAtUtc: string;
  imageUrl: string | null;
  description: string | null;
  universityId: number | null;
}

export interface GradSettingsUpdateRequest {
  notifyNewMatches: boolean;
  notifyFollowUps: boolean;
  matchAlertThresholdPercent: number;
  showGpaOnProfile: boolean;
  themePreference: string;
  alertKeywords: string | null;
  alertLocations: string | null;
}

export interface CvManualTextRequest {
  text: string;
}

export interface CreatePaperRequest {
  title: string;
  authors: string | null;
  abstract: string | null;
  url: string | null;
  source: string | null;
  publishedDate: string | null;
}

export interface ExamCatalogCourseDto {
  id: number;
  code: string;
  title: string;
  department: string;
  academicYear: number | null;
  semester: string;
}
