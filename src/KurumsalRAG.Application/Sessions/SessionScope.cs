namespace KurumsalRAG.Application.Sessions;

/// <summary>
/// Retrieval'ın görebileceği session kümesini hesaplar: kullanıcının kendi session'ı + seed
/// (örnek doküman). Kendi session'ı seed ile aynıysa tekilleştirir. Saf (test edilebilir) mantık.
/// </summary>
public static class SessionScope
{
    public static string[] Allowed(string selfSessionId, string seedSessionId)
        => string.Equals(selfSessionId, seedSessionId, StringComparison.Ordinal)
            ? [seedSessionId]
            : [selfSessionId, seedSessionId];
}
