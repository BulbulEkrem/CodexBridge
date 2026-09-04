namespace CodexBridge.Core.Security;

/// <summary>
/// Sır deposu. Token gibi hassas dizeleri saklar. <b>Yalnızca bizim ürettiğimiz sırlar</b>
/// buraya yazılır — kullanıcının CLI kimlik dosyalarına asla dokunulmaz.
/// </summary>
public interface ISecretStore
{
    /// <summary>Sırrı okur; yoksa veya çözülemezse <c>null</c>.</summary>
    string? Read(string key);

    /// <summary>Sırrı atomik olarak yazar.</summary>
    void Write(string key, string value);

    /// <summary>Sırrı siler. Yoksa sessizce geçer.</summary>
    void Delete(string key);
}
