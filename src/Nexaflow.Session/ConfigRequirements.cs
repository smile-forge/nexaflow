using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Nexaflow.Features.Common;

namespace Nexaflow.Session;

/// <summary>
/// What a config's own attributes say of it, read by reflection: which of its settings are required and whether they are filled,
/// whether the setup wizard asks for it, which of its properties are settings a person edits, how an edited copy is put back, and
/// which setting greys out another. Feature configs and provider configs say these with attributes of the same names from their
/// own contracts; both are read.
/// </summary>
public static class ConfigRequirements
{
    /// <summary>Every <c>[Required]</c> setting of <paramref name="config"/> is filled — a provider whose key is entered is ready to
    /// use. True for a config that requires nothing.</summary>
    public static bool IsComplete(object config)
        => RequiredValues(config).All(IsFilled);

    /// <summary>
    /// The <c>[Required]</c> settings of <paramref name="config"/> are all filled or all empty: nothing entered yet is as good as
    /// all of it, while some but not all is an entry left half made. True for a config that requires nothing. Not
    /// <see cref="IsComplete"/>, which asks whether the config is ready to use.
    /// </summary>
    public static bool RequiredAllOrNone(object config)
    {
        var values = RequiredValues(config).ToList();
        return values.All(IsFilled) || values.All(v => !IsFilled(v));
    }

    /// <summary>Whether the setup wizard asks for <paramref name="config"/> when it is new: it says <c>[MandatorySetup]</c>.</summary>
    public static bool IsMandatory(object config)
        => config.GetType().GetCustomAttribute<MandatorySetupAttribute>() is not null
        || config.GetType().GetCustomAttribute<Nexaflow.Providers.Common.MandatorySetupAttribute>() is not null;

    /// <summary>
    /// The properties of <paramref name="configType"/> that are settings a person edits: public and read-write — only those are
    /// put back by <see cref="CopyInto"/> — other than the config's own identity, and of a type a setting can hold. A list or an
    /// object a feature persists for itself (the Solver's recent symbols) is state, not a setting.
    /// </summary>
    public static IEnumerable<PropertyInfo> EditableProperties(Type configType)
        => configType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite
                        && p.Name is not (nameof(IFeatureConfig.ConfigName) or nameof(IFeatureConfig.FriendlyName))
                        && IsSettingType(p.PropertyType));

    /// <summary>Puts what <paramref name="edited"/> holds into <paramref name="config"/>: every public read-write property, so an
    /// edited copy (<see cref="ConfigManager.Clone"/>) becomes the config.</summary>
    public static void CopyInto(object edited, object config)
    {
        foreach (var property in edited.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite))
            property.SetValue(config, property.GetValue(edited));
    }

    /// <summary>
    /// <paramref name="value"/> as a setting of <paramref name="type"/> holds it: an enum's name as the enum, words typed for a
    /// number as the number — none typed as zero. Anything else as it is.
    /// </summary>
    /// <exception cref="FormatException">The words are not a number of the setting's kind.</exception>
    /// <exception cref="ArgumentException">The name is not one of the enum's.</exception>
    public static object? Coerce(object? value, Type type)
    {
        if (value is null) return null;
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsEnum && value is string name) return Enum.Parse(target, name);

        if (value is string words && target != typeof(string) && target.IsValueType)
        {
            if (string.IsNullOrWhiteSpace(words)) return Activator.CreateInstance(target);
            if (target == typeof(int))     return int.Parse(words);
            if (target == typeof(long))    return long.Parse(words);
            if (target == typeof(double))  return double.Parse(words);
            if (target == typeof(decimal)) return decimal.Parse(words);
        }
        return value;
    }

    /// <summary>Whether a setting's value counts as set, for one that greys out another: on, words other than none or "0", a number
    /// other than zero.</summary>
    public static bool IsSet(object? value) => value switch
    {
        null     => false,
        bool on  => on,
        string s => !string.IsNullOrWhiteSpace(s) && s.Trim() != "0",
        _        => Convert.ToDouble(value) != 0,
    };

    /// <summary>The setting that greys out <paramref name="property"/>, and whether it does so while that setting is set
    /// (<c>[DisabledIfSet]</c>) or until it is (<c>[DisabledIfNotSet]</c>); null where none does.</summary>
    public static (string Sibling, bool WhileSet)? DisabledBy(PropertyInfo property)
    {
        if ((property.GetCustomAttribute<DisabledIfSetAttribute>()?.PropertyName
             ?? property.GetCustomAttribute<Nexaflow.Providers.Common.DisabledIfSetAttribute>()?.PropertyName) is { } whileSet)
            return (whileSet, true);
        if ((property.GetCustomAttribute<DisabledIfNotSetAttribute>()?.PropertyName
             ?? property.GetCustomAttribute<Nexaflow.Providers.Common.DisabledIfNotSetAttribute>()?.PropertyName) is { } untilSet)
            return (untilSet, false);
        return null;
    }

    /// <summary>Whether <paramref name="property"/> is a <c>[Required]</c> setting.</summary>
    public static bool IsRequired(PropertyInfo property) => property.GetCustomAttribute<RequiredAttribute>() is not null;

    private static IEnumerable<object?> RequiredValues(object config)
        => config.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(IsRequired).Select(p => p.GetValue(config));

    private static bool IsFilled(object? value) => !string.IsNullOrWhiteSpace(value as string);

    private static bool IsSettingType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(string) || t == typeof(bool) || t.IsEnum
            || t == typeof(int) || t == typeof(long) || t == typeof(double) || t == typeof(decimal);
    }
}
