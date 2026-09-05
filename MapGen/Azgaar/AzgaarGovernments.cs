using Ck3MapGen.Config;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Reads each Azgaar state's own constitution and turns it into a CK3 government.
///
/// This exists because the generative path in <see cref="Governments"/> is the wrong tool for an
/// imported map. That path infers a government from terrain, development and start year, because on
/// a map the tool invented itself there is nothing else to infer it from — and its inference is
/// coarse: a mean aridity a hair over its clan threshold turns a whole heritage into clans, which on
/// the first real export made two thirds of the map a clan and every one of their kings a
/// "Patriarch". The export does not need inferring. Azgaar states carry a <c>form</c>, a
/// <c>formName</c> and a <c>type</c>, and between them those three say what the country is.
///
/// The default is deliberately <see cref="GovernmentMap.Feudal"/>. Azgaar's monarchies are the
/// overwhelming majority of any export and a monarchy is a feudal realm unless something in the
/// export says otherwise; everything below is that "otherwise", and a form word nobody recognises
/// falls through to feudal rather than to a guess.
/// </summary>
public static class AzgaarGovernments
{
    /// <summary>
    /// Form words that name a government outright, whatever <c>form</c> they sit under.
    ///
    /// Checked before <see cref="ByForm"/> because <c>form</c> is the coarse field: Azgaar files
    /// Khanates and Sultanates alike under Monarchy, and a Council under Anarchy, and the specific
    /// word is the one carrying the meaning. Drawn from Azgaar's own form tables plus the near
    /// neighbours a user is likely to have typed in by hand in the states editor, since these are
    /// free-text once edited.
    /// </summary>
    private static readonly Dictionary<string, string> ByFormName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Steppe polities. Azgaar hands these to Nomadic cultures.
            ["Khanate"] = GovernmentMap.Nomad,
            ["Great Khanate"] = GovernmentMap.Nomad,
            ["Khaganate"] = GovernmentMap.Nomad,
            ["Ulus"] = GovernmentMap.Nomad,
            ["Horde"] = GovernmentMap.Nomad,
            ["Orda"] = GovernmentMap.Nomad,

            // Houses and dynastic confederations — CK3's clan, not its tribe.
            ["Sultanate"] = GovernmentMap.Clan,
            ["Emirate"] = GovernmentMap.Clan,
            ["Caliphate"] = GovernmentMap.Clan,
            ["Imamah"] = GovernmentMap.Clan,
            ["Imamate"] = GovernmentMap.Clan,
            ["Sheikhdom"] = GovernmentMap.Clan,
            ["Beylik"] = GovernmentMap.Clan,
            ["Clan"] = GovernmentMap.Clan,

            ["Tribe"] = GovernmentMap.Tribal,
            ["Tribes"] = GovernmentMap.Tribal,
            ["Chiefdom"] = GovernmentMap.Tribal,
            ["Chieftaincy"] = GovernmentMap.Tribal,

            ["Republic"] = GovernmentMap.Republic,
            ["Most Serene Republic"] = GovernmentMap.Republic,
            ["Federation"] = GovernmentMap.Republic,
            ["Trade Company"] = GovernmentMap.Republic,
            ["Oligarchy"] = GovernmentMap.Republic,
            ["Tetrarchy"] = GovernmentMap.Republic,
            ["Triumvirate"] = GovernmentMap.Republic,
            ["Diarchy"] = GovernmentMap.Republic,
            ["Junta"] = GovernmentMap.Republic,
            ["League"] = GovernmentMap.Republic,
            ["Hanseatic League"] = GovernmentMap.Republic,
            ["Commune"] = GovernmentMap.Republic,
            ["Council"] = GovernmentMap.Republic,
            ["Community"] = GovernmentMap.Republic,
            ["Confederation"] = GovernmentMap.Republic,

            ["Theocracy"] = GovernmentMap.Theocracy,
            ["Thearchy"] = GovernmentMap.Theocracy,
            ["Hierocracy"] = GovernmentMap.Theocracy,
            ["Brotherhood"] = GovernmentMap.Theocracy,
            ["See"] = GovernmentMap.Theocracy,
            ["Holy See"] = GovernmentMap.Theocracy,
            ["Holy State"] = GovernmentMap.Theocracy,
            ["Diocese"] = GovernmentMap.Theocracy,
            ["Archdiocese"] = GovernmentMap.Theocracy,
            ["Bishopric"] = GovernmentMap.Theocracy,
            ["Archbishopric"] = GovernmentMap.Theocracy,
            ["Eparchy"] = GovernmentMap.Theocracy,
            ["Exarchate"] = GovernmentMap.Theocracy,
            ["Patriarchate"] = GovernmentMap.Theocracy,
            ["Papacy"] = GovernmentMap.Theocracy,

            // Deliberately absent: Duchy, Grand Duchy, Principality, Kingdom, Empire, Despotate,
            // Shogunate, Union, Commonwealth, United Kingdom, United Provinces, Heptarchy. Every one
            // of those is a feudal realm in CK3 terms and reaching the default is the right answer.
        };

    /// <summary>The coarse fallback, for a form word this does not know.</summary>
    private static readonly Dictionary<string, string> ByForm =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Theocracy"] = GovernmentMap.Theocracy,
            ["Republic"] = GovernmentMap.Republic,

            // Anarchy is Azgaar's word for a country with no sovereign — a council of communes or a
            // confederation of tribes. Tribal is the closer of CK3's two readings: a republic is a
            // *government*, and the point of an Anarchy is that there is not one.
            ["Anarchy"] = GovernmentMap.Tribal,

            // Union and Monarchy both fall through to feudal, which is what they are.
        };

    /// <summary>
    /// The government for one state, before any of CK3's own eligibility rules are applied.
    ///
    /// Order matters. <see cref="AzgaarState.StateType"/> comes first for the two types CK3 has a
    /// distinct government for, because it is the only field that separates a horde or a band of
    /// hunters from a settled realm — Azgaar calls all three a Monarchy, and a Hunting state whose
    /// form name is "Principality" is still a Hunting state. Everything else is decided by the words
    /// the country uses for itself.
    /// </summary>
    public static string For(AzgaarState state)
    {
        if (state.StateType.Equals("Nomadic", StringComparison.OrdinalIgnoreCase))
            return GovernmentMap.Nomad;

        if (state.StateType.Equals("Hunting", StringComparison.OrdinalIgnoreCase))
            return GovernmentMap.Tribal;

        if (state.FormName is { Length: > 0 } formName
            && ByFormName.TryGetValue(formName.Trim(), out string? byName))
            return byName;

        if (state.Form is { Length: > 0 } form
            && ByForm.TryGetValue(form.Trim(), out string? byForm))
            return byForm;

        return GovernmentMap.Feudal;
    }

    /// <summary>
    /// Every state's government, keyed by state id, with the settings that can veto one applied.
    ///
    /// A pure function of the export and the config — no titles, no terrain, no realms — which is
    /// what lets it run early enough for naming to read it. Both the government pass and the two
    /// naming passes need the same answer, and a state's government decides which vocabulary its
    /// title and its ruler draw from, so it cannot wait until governments are assigned.
    /// </summary>
    public static Dictionary<int, string> ByState(AzgaarImport azgaar, MapConfig cfg)
    {
        var governments = new Dictionary<int, string>();

        foreach (var state in azgaar.World.RealStates)
        {
            string government = For(state);

            // A horde on a map that has hordes switched off is still not a feudal realm; clan is
            // what SafeFallback already calls the settled reading of one, and tribal the unsettled.
            if (government == GovernmentMap.Nomad && !cfg.EnableNomadHordes)
                government = GovernmentMap.Tribal;

            governments[state.I] = government;
        }

        return governments;
    }

    /// <summary>A tally for the console, most common first.</summary>
    public static IEnumerable<(string Government, int Count)> Tally(Dictionary<int, string> byState)
        => byState.GroupBy(kv => kv.Value)
                  .Select(g => (g.Key, g.Count()))
                  .OrderByDescending(g => g.Item2)
                  .ThenBy(g => g.Key, StringComparer.Ordinal);
}
