namespace UniSphere.Api.Models;

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

public class ExamAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public ExamAttempt? Attempt { get; set; }
    public int QuestionId { get; set; }
    public ExamQuestion? Question { get; set; }
    public int? SelectedOption { get; set; }
    public bool IsCorrect { get; set; }
    public string? TextAnswer { get; set; }       // essay / definition answers
    public DateTime? GradedAtUtc { get; set; }    // set when the doctor grades a written answer
}
