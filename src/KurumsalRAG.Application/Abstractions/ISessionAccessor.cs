namespace KurumsalRAG.Application.Abstractions;

/// <summary>
/// Geçerli isteğin anonim session kimliğini sağlar. Web katmanında httpOnly cookie'den
/// okunur; use-case servisleri bu port üzerinden session'ı öğrenir (HttpContext'e bağımlı olmaz).
/// </summary>
public interface ISessionAccessor
{
    /// <summary>Geçerli session id (cookie'den). Yoksa boş olmayan bir değer garanti edilir.</summary>
    string SessionId { get; }
}
