namespace PipelogiqSDK.Contracts;

public class LogDto
{
    public string ApiKey { get; set; } = null!;
    public DateTime? Created { get; set; }
    public string? Message { get; set; }
    public string? LogLevel { get; set; }
    public List<KeywordDto>? Keywords { get; set; }
}
