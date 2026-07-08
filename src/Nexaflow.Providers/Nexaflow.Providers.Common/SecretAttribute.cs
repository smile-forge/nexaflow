namespace Nexaflow.Providers.Common;

/// <summary>
/// Marks a string config property as a secret (API key, token…). <c>ConfigManager</c> encrypts it
/// at rest with DPAPI (current user) — the JSON on disk holds <c>enc:&lt;base64&gt;</c> instead of
/// plaintext — and decrypts on load (legacy plaintext values pass through and are encrypted on the
/// next save). Mirrored in <c>Nexaflow.Features.Common</c> so feature configs can use it too (same
/// precedent as <c>IConfigMigration</c>); Core matches by attribute name.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute { }
