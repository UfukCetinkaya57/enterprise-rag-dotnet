namespace KurumsalRAG.Domain.ValueObjects;

public enum ChatRole
{
    System,
    User,
    Assistant
}

/// <summary>Provider-agnostik tek bir sohbet mesajı.</summary>
public sealed record ChatMessage(ChatRole Role, string Content)
{
    public static ChatMessage System(string content) => new(ChatRole.System, content);
    public static ChatMessage User(string content) => new(ChatRole.User, content);
    public static ChatMessage Assistant(string content) => new(ChatRole.Assistant, content);
}
