namespace UniSphere.Api.Services;

public class DoctorAiOptions
{
    public string PreferredProvider { get; set; } = "";
    public int MaxTokens { get; set; } = 4096;
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxContextChars { get; set; } = 12000;
}

public class AiUnavailableException : Exception
{
    public AiUnavailableException(string message) : base(message) { }
}

public class AiInvalidResponseException : Exception
{
    public AiInvalidResponseException(string message) : base(message) { }
}
