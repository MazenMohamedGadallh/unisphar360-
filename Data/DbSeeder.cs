using UniSphere.Api.Models;
using UniSphere.Api.Services;

namespace UniSphere.Api.Data;

public static class DbSeeder
{
    public const string DemoStudentOldEmail = "gehad@unisphere.edu";
    public const string DemoStudentEmail = "mazenmohamed@unisphere.edu";

    public static void Seed(AppDbContext db)
    {
        // In-place rename for existing databases: the demo student was
        // created as gehad@unisphere.edu in older seeds. Update that row
        // (same Id, so grades/attendance/plans/notifications stay linked)
        // instead of inserting a duplicate.
        var legacy = db.Users.FirstOrDefault(u => u.Email == DemoStudentOldEmail);
        if (legacy is not null && !db.Users.Any(u => u.Email == DemoStudentEmail))
        {
            legacy.Email = DemoStudentEmail;
            legacy.FirstName = "Mazen";
            legacy.LastName = "Mohamed";
            legacy.FullName = "Mazen Mohamed";
            db.SaveChanges();
        }

        if (db.Users.Any()) return;

        // Roles (AppDbContext.OnModelCreating seeds these via HasData on
        // EnsureCreated, so only insert when the table is still empty —
        // otherwise a fresh database crashes with UNIQUE constraint on Roles.Id).
        if (!db.Roles.Any())
        {
            db.Roles.AddRange(
                new RoleItem { Id = 1, Name = "Student" },
                new RoleItem { Id = 2, Name = "Graduate" },
                new RoleItem { Id = 3, Name = "Doctor" },
                new RoleItem { Id = 4, Name = "Admin" }
            );
        }

        // Universities (seeded before users/courses so ids are known).
        if (!db.Universities.Any())
        {
            db.Universities.AddRange(
                new University { Name = "El Shorouk Academy" },
                new University { Name = "Cairo University" },
                new University { Name = "Ain Shams University" },
                new University { Name = "Other" }
            );
            db.SaveChanges();
        }
        var universities = db.Universities.ToDictionary(u => u.Name);
        var elShorouk = universities["El Shorouk Academy"];

        // Demo accounts
        var user = new User
        {
            FirstName = "Mazen",
            LastName = "Mohamed",
            FullName = "Mazen Mohamed",
            PhoneNumber = "01012345678",
            Email = "mazenmohamed@unisphere.edu",
            Gender = "Male",
            Age = 20,
            Major = "Computer Engineering",
            Department = "Computer Engineering",
            AcademicYear = 3,
            UniversityId = elShorouk.Id,
            Role = UserRole.Student,
            PasswordHash = PasswordService.Hash("Password123!")
        };
        var admin = new User
        {
            FirstName = "UniSphere",
            LastName = "Admin",
            FullName = "UniSphere Admin",
            PhoneNumber = "01000000001",
            Email = "admin@unisphere.edu",
            Gender = "Male",
            Age = 35,
            Major = "Administration",
            Department = "Administration",
            Role = UserRole.Admin,
            PasswordHash = PasswordService.Hash("Admin123!")
        };
        var graduate = new User
        {
            FirstName = "Omar",
            LastName = "Hassan",
            FullName = "Omar Hassan",
            PhoneNumber = "01112345678",
            Email = "graduate@unisphere.edu",
            Gender = "Male",
            Age = 24,
            Major = "Computer Engineering",
            Department = "Computer Science",
            GraduatedFrom = "Cairo University, Faculty of Engineering",
            GraduationYear = 2023,
            Role = UserRole.Graduate,
            PasswordHash = PasswordService.Hash("Graduate123!")
        };
        var doctor = new User
        {
            FirstName = "Khaled",
            LastName = "Mansour",
            FullName = "Dr. Khaled Mansour",
            PhoneNumber = "01212345678",
            Email = "doctor@unisphere.edu",
            Gender = "Male",
            Age = 45,
            Major = "Computer Engineering",
            Department = "Computer Science",
            Specialization = "Artificial Intelligence",
            UniversityId = elShorouk.Id,
            Role = UserRole.Doctor,
            PasswordHash = PasswordService.Hash("Doctor123!")
        };
        db.Users.AddRange(user, admin, graduate, doctor);

        // Modules
        db.Modules.AddRange(
            new ModuleItem { Key = "academic-advisor", Title = "Academic Advisor", Subtitle = "Plan - Analyze - Improve", Icon = "school", BackgroundColor = "#EDEBFF", AccentColor = "#4A3FD0", SortOrder = 1 },
            new ModuleItem { Key = "career-assistant", Title = "Career Assistant", Subtitle = "CV - Jobs - Skills", Icon = "work", BackgroundColor = "#E6F8EE", AccentColor = "#1E9E5E", SortOrder = 2 },
            new ModuleItem { Key = "research-hub", Title = "Research Hub", Subtitle = "Search - Summarize - Ask", Icon = "search", BackgroundColor = "#FFE8E6", AccentColor = "#E0524A", SortOrder = 3 },
            new ModuleItem { Key = "student-services", Title = "Student Services", Subtitle = "FAQs - Events - Support", Icon = "account_balance", BackgroundColor = "#E5F0FF", AccentColor = "#2F7BE0", SortOrder = 4 },
            new ModuleItem { Key = "analytics-admin", Title = "Analytics & Admin", Subtitle = "Insights - Reports - Trends", Icon = "bar_chart", BackgroundColor = "#FFF4D9", AccentColor = "#E09A1A", SortOrder = 5 }
        );

        // Events
        var now = DateTime.UtcNow.Date;
        db.Events.AddRange(
            new EventItem { Title = "AI Workshop", Location = "Faculty of Engineering", StartsAtUtc = now.AddDays(3).AddHours(10), Description = "Hands-on workshop on machine learning basics." },
            new EventItem { Title = "Tech Club Meeting", Location = "Student Activities Center", StartsAtUtc = now.AddDays(5).AddHours(16), Description = "Weekly meeting of the Tech Club." },
            new EventItem { Title = "Career Fair", Location = "Main Campus", StartsAtUtc = now.AddDays(10).AddHours(9), Description = "Meet employers and apply for internships." }
        );

        // Courses
        db.Courses.AddRange(
            new Course { Code = "CS350", Title = "Machine Learning", Description = "Supervised and unsupervised learning.", Track = "AI", Credits = 3 },
            new Course { Code = "CS351", Title = "Deep Learning", Description = "Neural networks, CNNs and transformers.", Track = "AI", Credits = 3 },
            new Course { Code = "CS362", Title = "Natural Language Processing", Description = "Text processing, LLMs and RAG.", Track = "AI", Credits = 3 },
            new Course { Code = "CS340", Title = "Mobile App Development", Description = "Building apps with Flutter.", Track = "Software", Credits = 3 },
            new Course { Code = "CS240", Title = "Web Development", Description = "HTML, CSS, JavaScript and backend basics.", Track = "Software", Credits = 3 },
            new Course { Code = "CS330", Title = "Database Systems", Description = "SQL, PostgreSQL and data modeling.", Track = "CS", Credits = 3 },
            new Course { Code = "CS430", Title = "Advanced Databases", Description = "Query optimization, NoSQL and distributed data.", Track = "CS", Credits = 3 },
            new Course { Code = "CS210", Title = "Data Structures & Algorithms", Description = "Lists, trees, graphs and complexity.", Track = "CS", Credits = 3 },
            new Course { Code = "CS120", Title = "Discrete Mathematics", Description = "Logic, sets, combinatorics and graphs.", Track = "CS", Credits = 3 },
            new Course { Code = "CS320", Title = "Computer Networks", Description = "TCP/IP, routing and network design.", Track = "IT", Credits = 3 },
            new Course { Code = "CS420", Title = "Cloud Computing", Description = "Virtualization, containers and cloud services.", Track = "IT", Credits = 3 }
        );

        db.SaveChanges();
        var c = db.Courses.ToDictionary(x => x.Code);

        // University scope for Mazen's 6 courses (El Shorouk Academy,
        // Computer Engineering department, year 3).
        foreach (var code in new[] { "CS350", "CS210", "CS330", "CS320", "CS240", "CS120" })
        {
            c[code].UniversityId = elShorouk.Id;
            c[code].Department = "Computer Engineering";
            c[code].AcademicYear = 3;
            c[code].Semester = "Fall 2025";
        }
        db.SaveChanges();

        // Notifications
        db.Notifications.AddRange(
            new Notification { UserId = user.Id, Title = "Registration open", Body = "Course registration for next term is now open." },
            new Notification { UserId = user.Id, Title = "AI Workshop reminder", Body = "Your AI Workshop starts in 3 days." },
            new Notification { UserId = user.Id, Title = "New job match", Body = "A new internship matches your profile." }
        );

        // Enrollments
        db.Enrollments.AddRange(
            new Enrollment { UserId = user.Id, CourseId = c["CS350"].Id, GradeLetter = "B+", GradePoints = 3.3, AttendancePercent = 95, Status = "Good" },
            new Enrollment { UserId = user.Id, CourseId = c["CS210"].Id, GradeLetter = "B", GradePoints = 3.0, AttendancePercent = 88, Status = "Needs Attention" },
            new Enrollment { UserId = user.Id, CourseId = c["CS330"].Id, GradeLetter = "A-", GradePoints = 3.7, AttendancePercent = 96, Status = "Excellent" },
            new Enrollment { UserId = user.Id, CourseId = c["CS320"].Id, GradeLetter = "C+", GradePoints = 2.3, AttendancePercent = 78, Status = "Needs Attention" },
            new Enrollment { UserId = user.Id, CourseId = c["CS240"].Id, GradeLetter = "B+", GradePoints = 3.4, AttendancePercent = 90, Status = "Good" },
            new Enrollment { UserId = user.Id, CourseId = c["CS120"].Id, GradeLetter = "B", GradePoints = 3.0, AttendancePercent = 85, Status = "Needs Attention" }
        );

        // Semester records
        db.SemesterRecords.AddRange(
            new SemesterRecord { UserId = user.Id, Label = "2 semesters ago", IsCurrent = false, Gpa = 2.78, AttendanceRate = 82, CompletedCourses = 6, TotalCourses = 26 },
            new SemesterRecord { UserId = user.Id, Label = "Last semester", IsCurrent = false, Gpa = 2.96, AttendanceRate = 87, CompletedCourses = 12, TotalCourses = 26 },
            new SemesterRecord { UserId = user.Id, Label = "Current semester", IsCurrent = true, Gpa = 3.16, AttendanceRate = 92, CompletedCourses = 18, TotalCourses = 26 }
        );
        db.SemesterRecords.Add(new SemesterRecord { UserId = graduate.Id, Label = "Final", IsCurrent = true, Gpa = 3.4, AttendanceRate = 94, CompletedCourses = 26, TotalCourses = 26 });

        // Study planner
        var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        var template = new (int Day, string Title, int Hours, string? Note)[]
        {
            (0, "Data Structures", 2, null),
            (1, "Machine Learning", 3, null),
            (2, "Database Systems", 2, null),
            (3, "Web Development", 2, null),
            (4, "Computer Networks", 2, null),
            (5, "Revision & practice", 3, null),
            (6, "Free Day", 0, "Rest & Recharge"),
        };
        for (var week = -1; week <= 2; week++)
        {
            foreach (var t in template)
            {
                db.StudyPlanItems.Add(new StudyPlanItem
                {
                    UserId = user.Id,
                    Date = weekStart.AddDays(week * 7 + t.Day),
                    Title = t.Title,
                    Hours = t.Hours,
                    Note = t.Note,
                    IsDone = week < 0 || (week == 0 && t.Day == 0)
                });
            }
        }

        // Activities
        var utc = DateTime.UtcNow;
        db.Activities.AddRange(
            new ActivityItem { UserId = user.Id, Kind = "quiz", Message = "You completed the quiz for Machine Learning", CreatedAtUtc = utc.AddHours(-2) },
            new ActivityItem { UserId = user.Id, Kind = "recommendation", Message = "New recommendation available: Deep Learning", CreatedAtUtc = utc.AddHours(-5) },
            new ActivityItem { UserId = user.Id, Kind = "attendance", Message = "Your attendance has been updated", CreatedAtUtc = utc.AddHours(-25) },
            new ActivityItem { UserId = user.Id, Kind = "announcement", Message = "New announcement: Exam schedule released", CreatedAtUtc = utc.AddHours(-49) }
        );

        // Announcements
        db.Announcements.AddRange(
            new Announcement { Title = "Welcome to UniSphere 360+", Body = "Your academic journey starts here. Explore your dashboard and connect with your advisors.", Category = "general", CreatedByUserId = admin.Id },
            new Announcement { Title = "Midterm Exam Schedule", Body = "Midterm exams will begin next month. Check your schedule for exact dates and times.", Category = "academic", CreatedByUserId = admin.Id },
            new Announcement { Title = "Career Fair Registration Open", Body = "Register now for the annual career fair. Meet top employers and explore internship opportunities.", Category = "career", CreatedByUserId = admin.Id }
        );

        // Research papers
        db.ResearchPapers.AddRange(
            new ResearchPaper { Title = "Attention Is All You Need", Authors = "Vaswani et al.", Abstract = "The dominant sequence transduction models are based on complex recurrent or convolutional neural networks. We propose a new simple network architecture, the Transformer, based solely on attention mechanisms.", Url = "https://arxiv.org/abs/1706.03762", Source = "arxiv", PublishedDate = new DateTime(2017, 6, 12) },
            new ResearchPaper { Title = "BERT: Pre-training of Deep Bidirectional Transformers", Authors = "Devlin et al.", Abstract = "We introduce a new language representation model called BERT, which stands for Bidirectional Encoder Representations from Transformers.", Url = "https://arxiv.org/abs/1810.04805", Source = "arxiv", PublishedDate = new DateTime(2018, 10, 11) },
            new ResearchPaper { Title = "Deep Residual Learning for Image Recognition", Authors = "He et al.", Abstract = "We present a residual learning framework to ease the training of networks that are substantially deeper than those used previously.", Url = "https://arxiv.org/abs/1512.03385", Source = "arxiv", PublishedDate = new DateTime(2015, 12, 10) }
        );

        // Course materials for Mazen's university courses (3-4 per course,
        // mixed types) plus 3 upcoming items (exams/announcement).
        var nowUtc = DateTime.UtcNow;
        void Material(string code, string title, string type, string description, string content, int daysAgo = 0)
        {
            db.CourseMaterials.Add(new CourseMaterial
            {
                CourseId = c[code].Id,
                Title = title,
                Type = type,
                Description = description,
                Content = content,
                UploadedByUserId = doctor.Id,
                CreatedAtUtc = nowUtc.AddDays(-daysAgo)
            });
        }

        Material("CS350", "Supervised Learning Summary", "Summary", "Key concepts of regression and classification.", "Supervised learning maps inputs to labeled outputs.\n\nKey points:\n- Linear and logistic regression\n- Decision trees and forests\n- Train/validation/test splits", 6);
        Material("CS350", "Lecture 4: Neural Network Basics", "Lecture", "Perceptrons, activation functions and backpropagation.", "A perceptron computes w.x + b followed by a non-linearity.\nBackpropagation applies the chain rule to update weights.", 4);
        Material("CS350", "Assignment 2: Build a Classifier", "Assignment", "Train and evaluate a classifier on the provided dataset.", "Tasks:\n1. Load the dataset\n2. Train logistic regression and k-NN\n3. Report accuracy and confusion matrix", 2);
        Material("CS210", "Data Structures Cheat Sheet", "Summary", "Lists, trees, graphs and complexity in one page.", "Arrays O(1) access.\nLinked lists O(n) traversal.\nBST average O(log n).\nGraphs: BFS vs DFS.", 7);
        Material("CS210", "Lecture 6: Graph Traversals", "Lecture", "BFS and DFS with worked examples.", "BFS explores level by level using a queue.\nDFS dives deep using a stack or recursion.", 3);
        Material("CS210", "Assignment 3: Shortest Paths", "Assignment", "Implement Dijkstra on the campus graph.", "Submit code plus a short report comparing runtimes.", 1);
        Material("CS330", "SQL Joins Summary", "Summary", "Inner, left, right and full joins visualized.", "INNER JOIN keeps matches.\nLEFT JOIN keeps all left rows.\nAggregation with GROUP BY and HAVING.", 5);
        Material("CS330", "Lecture 5: Indexing", "Lecture", "B-trees and query planning.", "An index trades write cost for read speed.\nCheck EXPLAIN plans before optimizing.", 2);
        Material("CS330", "Midterm Exam: Databases", "Exam", "Covers SQL, normalization and indexing. Bring your ID.", "Chapters 1-6.\nDuration: 90 minutes.\nNo phones allowed.", 0);
        Material("CS320", "OSI Model Summary", "Summary", "All seven layers with examples.", "7 Application, 6 Presentation, 5 Session, 4 Transport, 3 Network, 2 Data Link, 1 Physical.", 6);
        Material("CS320", "Lecture 7: TCP vs UDP", "Lecture", "Reliability vs speed trade-offs.", "TCP: ordered, reliable, slower handshake.\nUDP: fire-and-forget, ideal for streaming.", 3);
        Material("CS320", "Assignment 4: Subnetting", "Assignment", "Subnet the lab network into 6 subnets.", "Show your work for full credit.", 1);
        Material("CS240", "Flexbox & Grid Summary", "Summary", "Modern CSS layout in one page.", "Flexbox for one dimension.\nGrid for two dimensions.\nMedia queries for responsiveness.", 5);
        Material("CS240", "Lecture 8: REST APIs", "Lecture", "Resources, verbs and status codes.", "GET reads, POST creates, PUT replaces, PATCH updates, DELETE removes.", 2);
        Material("CS240", "Project Milestone 2", "Assignment", "Client-side routing plus API integration due Friday.", "Deliverable: working demo plus README.", 0);
        Material("CS120", "Logic & Proofs Summary", "Summary", "Propositional logic and proof techniques.", "Direct proof, contrapositive, contradiction, induction.", 4);
        Material("CS120", "Lecture 5: Combinatorics", "Lecture", "Counting principles with examples.", "Permutations P(n,k). Combinations C(n,k). Pigeonhole principle.", 1);
        Material("CS120", "Final Exam: Discrete Math", "Exam", "Cumulative final covering logic, sets and graphs.", "Chapters 1-9.\nDuration: 120 minutes.", 0);
        Material("CS330", "Lab Sessions Moved to Hall B", "Announcement", "All database labs move to Hall B starting Monday.", "Please check the new seating plan on the department board.", 0);

        db.SaveChanges();
    }
}
