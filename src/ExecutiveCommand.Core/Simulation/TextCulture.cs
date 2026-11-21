using System.Globalization;

namespace ExecutiveCommand.Core.Simulation;

/// <summary>
/// Engine-generated text (inbox messages, decision summaries) is part of the saved state, so it must be
/// formatted identically on every machine or state hashes would differ between locales. Until text is
/// moved to keyed localization templates, hosts call <see cref="Apply"/> at startup to pin en-US formatting.
/// </summary>
public static class TextCulture
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static void Apply()
    {
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }
}
