namespace Nexaflow.IO.Network.Probes;

/// <summary>The value kinds a probe setting can take. Mirrors the protocol-input type vocabulary so one
/// renderer and one validator serve both.</summary>
public enum ProbeSettingType { Text, Int, Bool, Enum, Duration, Cidr, Port }

/// <summary>
/// A setting a probe accepts, <b>described</b> rather than typed.
///
/// <para>
/// The host renders these generically, so adding a probe never means adding UI. And because the same
/// description is handed to the model, the AI can read and adjust a probe's configuration without a
/// bespoke tool per plugin — which is the difference between fifteen plugins being extensible and
/// fifteen plugins being fifteen integration jobs.
/// </para>
/// Deliberately the same shape as <c>ClientToolParameter</c>.
/// </summary>
/// <param name="Name">Key used to read the value back.</param>
/// <param name="Description">Written for the user and the model. Required — an undescribed setting is one
/// neither can use.</param>
/// <param name="Type">Value kind, driving both validation and the generated tool parameter type.</param>
/// <param name="Default">Default as text; parsed per <paramref name="Type"/>.</param>
/// <param name="OneOf">Permitted values when <paramref name="Type"/> is <see cref="ProbeSettingType.Enum"/>.</param>
/// <param name="Min">Optional inclusive lower bound for numeric/duration settings.</param>
/// <param name="Max">Optional inclusive upper bound. A probe that lets the user raise its own packet
/// budget without limit is a probe that defeats the guard, so bound it here.</param>
public sealed record ProbeSetting(
    string Name,
    string Description,
    ProbeSettingType Type,
    string Default = "",
    IReadOnlyList<string>? OneOf = null,
    double? Min = null,
    double? Max = null);
