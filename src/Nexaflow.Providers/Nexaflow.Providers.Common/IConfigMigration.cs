using System;
using System.Text.Json.Nodes;

namespace Nexaflow.Providers.Common;

/// <summary>
/// Optional opt-in upgrade path for a provider config. When the config registry loads a config
/// whose only on-disk file is from an <em>older</em> assembly version, it first carries over every
/// name-matching property (the automatic fallback), then — if the config implements this — calls
/// <see cref="MigrateFrom"/> so it can fix up shape changes the field copy can't express (renames,
/// restructures, value remaps). Most configs need no hook; the lenient carry-over handles additive
/// and removed fields on its own.
/// </summary>
public interface IConfigMigration
{
    /// <summary>
    /// Called after the lenient field-by-field carry-over when the loaded data came from
    /// <paramref name="previousVersion"/>. This instance's properties are already populated with all
    /// name-matching values; mutate it as needed using <paramref name="previous"/> (the raw JSON of
    /// the older file) to complete the migration.
    /// </summary>
    void MigrateFrom(JsonObject previous, Version previousVersion);
}
