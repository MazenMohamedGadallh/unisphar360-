namespace UniSphere.Api.Services;

public class ChatOptions
{
    public Dictionary<string, ChatProviderOptions> Providers { get; set; } = new();

    public string DefaultProvider { get; set; } = "";

    public int MaxTokens { get; set; } = 1024;
    public int TimeoutSeconds { get; set; } = 60;
    public int DailyMessageLimit { get; set; } = 100;
    public int PerMinuteLimit { get; set; } = 20;
}

public class ChatProviderOptions
{
    public string Model { get; set; } = "";

    public string BaseUrl { get; set; } = "";

    public string ApiKey { get; set; } = "";
}

public class ChatTurn
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public class ChatRequest
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public List<ChatTurn> Messages { get; set; } = new();
    public int MaxTokens { get; set; } = 1024;

    public List<RagDocument> RagDocuments { get; set; } = new();
}

public class ChatProviderException : Exception
{
    public ChatProviderException(string message) : base(message) { }
}

public class ChatLimitExceededException : Exception
{
    public ChatLimitExceededException(string message) : base(message) { }
}
