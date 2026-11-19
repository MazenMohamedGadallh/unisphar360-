namespace UniSphere.Api.Models;

/// <summary>One student's single attempt at a published exam. One row per
/// (exam, student) pair — enforced by the unique index
/// IX_ExamAttempts_ExamId_StudentUserId so a second attempt is impossible
/// at the database level too. Scores are written ONLY by the server when
/// the attempt is submitted; the client can never supply them.</summary>
public class ExamAttempt
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
    public int StudentUserId { get; set; }
    public User? StudentUser { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public int? Score { get; set; }               // server-graded at submit: # of correct answers
    public int? MaxScore { get; set; }            // exam.QuestionCount, frozen at submit
    public ICollection<ExamAnswer> Answers { get; set; } = new List<ExamAnswer>();

    public bool IsSubmitted => SubmittedAtUtc is not null;
}

/// <summary>One saved student answer inside an attempt: which option the
/// student picked (0-based index into the question's OptionsJson; null =
/// unanswered) and whether the SERVER graded it correct at submit time.
/// IsCorrect is a persisted record of the grading decision, never a
/// client-supplied flag.</summary>
public class ExamAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public ExamAttempt? Attempt { get; set; }
    public int QuestionId { get; set; }
    public ExamQuestion? Question { get; set; }
    public int? SelectedOption { get; set; }
    public bool IsCorrect { get; set; }
}
