namespace Ck3MapGen.Magic;

/// <summary>How far an effect reaches. Multiplies power, and gates on the world's ceiling.</summary>
public enum EffectScope
{
    Self,
    Character,
    Court,
    Province,
    Title,
    Realm,
    World,
}

/// <summary>
/// Whether an effect helps, hurts, or merely reveals.
///
/// Load-bearing for two things that are easy to forget until they are wrong: the AI weight (an AI
/// will not cast a boon on a rival) and the exposure rule (being seen doing a harm costs
/// differently from being seen doing a kindness).
/// </summary>
public enum EffectPolarity
{
    Boon,
    Harm,
    Neutral,
}

/// <summary>
/// One indivisible thing magic can do.
///
/// The palette is fixed and hand-audited; the generation is in which atoms a world may use, which
/// of them combine into a spell, and at what magnitude. That is the central scoping decision of
/// the whole feature: assembling script from a known vocabulary is verifiable, and generating
/// script from nothing is not — an invented effect cannot be power-weighted, cannot be checked
/// against a ceiling, and fails silently in CK3 rather than loudly.
///
/// <see cref="ScriptHint"/> carries no weight at runtime. It is the note to the emitter stage
/// about what this atom is expected to compile to, written now while the reasoning is fresh, so
/// that the emitter is a translation exercise rather than a second design exercise.
/// </summary>
public sealed record EffectAtom(
    string Key,
    MagicDomain Domain,
    EffectScope Scope,
    EffectPolarity Polarity,
    MagicCeiling MinCeiling,
    double Weight,
    double MinMagnitude,
    double MaxMagnitude,
    string Phrasing,
    string ScriptHint)
{
    /// <summary>
    /// The atom's own number at a normalised strength in [0, 1].
    ///
    /// Everything upstream works in normalised strength rather than in the atom's own units,
    /// because the units are not comparable: 400 stress and 3 health are both "a lot", and a
    /// grammar that multiplied raw magnitudes by a weight would price the stress atom two hundred
    /// times the health one for no reason other than the scale it happens to be measured on.
    /// </summary>
    public double Magnitude(double strength) =>
        MinMagnitude + Math.Clamp(strength, 0, 1) * (MaxMagnitude - MinMagnitude);

    /// <summary>
    /// Power contributed at a normalised strength. Affine rather than linear: an atom at its
    /// floor is still worth something, because being able to do the thing at all is most of the
    /// value and the last 40% of the number is rarely what decides a campaign.
    /// </summary>
    public double PowerAt(double strength) =>
        Weight * (0.4 + 1.2 * Math.Clamp(strength, 0, 1)) * EffectAtoms.ScopeMultiplier(Scope);

    /// <summary>The report line: the atom said in English at a chosen strength.</summary>
    public string Describe(double strength)
    {
        double magnitude = Magnitude(strength);
        return string.Format(Phrasing, magnitude < 10
            ? magnitude.ToString("0.#")
            : magnitude.ToString("0"));
    }
}

/// <summary>
/// The palette. Forty-five atoms, six or seven per domain.
///
/// Two invariants hold it together, and both were learned by breaking them.
///
/// Roughly equal counts per domain is not cosmetic symmetry: it means forbidding a domain removes
/// a comparable amount of capability whichever domain got forbidden, which is what keeps the
/// prohibition meaningful rather than accidentally toothless.
///
/// Every domain also has at least one atom at <see cref="MagicCeiling.Personal"/>. Without that,
/// "personal ceiling" quietly means "only the four domains that happen to have a personal-scale
/// atom", and a world that forbids two of those four has a practice with nothing in it — which is
/// exactly the error a two-hundred-seed sweep turned up twelve times before Death, Mind and Nature
/// were given one each.
/// </summary>
public static class EffectAtoms
{
    /// <summary>
    /// What a step up in reach is worth. Superlinear on purpose: touching a province is worth more
    /// than four times touching a person, because it persists, it compounds, and nobody has to
    /// consent to it.
    /// </summary>
    public static double ScopeMultiplier(EffectScope scope) => scope switch
    {
        EffectScope.Self => 1.0,
        EffectScope.Character => 1.3,
        EffectScope.Court => 2.0,
        EffectScope.Province => 3.2,
        EffectScope.Title => 3.6,
        EffectScope.Realm => 5.0,
        EffectScope.World => 9.0,
        _ => 1.0,
    };

    public static IReadOnlyList<EffectAtom> All { get; } =
    [
        // ---------------------------------------------------------------- Life
        new("life_mend", MagicDomain.Life, EffectScope.Character, EffectPolarity.Boon,
            MagicCeiling.Court, 1.0, 1, 3,
            "closes wounds that should have taken months ({0} step)",
            "remove_trait = wounded_1/2/3; add_character_modifier health"),

        new("life_cure", MagicDomain.Life, EffectScope.Character, EffectPolarity.Boon,
            MagicCeiling.Court, 1.2, 1, 2,
            "drives out an illness ({0} severity)",
            "remove_trait = ill / pneumonic / cancer; health modifier"),

        new("life_quicken", MagicDomain.Life, EffectScope.Character, EffectPolarity.Boon,
            MagicCeiling.Personal, 0.8, 10, 40,
            "makes a barren union fruitful (+{0}% fertility)",
            "add_character_modifier = { fertility = X years = Y }"),

        new("life_vigour", MagicDomain.Life, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Personal, 0.9, 1, 4,
            "holds off age and exhaustion (+{0} health)",
            "add_character_modifier = { health = X }"),

        new("life_twinned", MagicDomain.Life, EffectScope.Character, EffectPolarity.Boon,
            MagicCeiling.Court, 0.7, 1, 1,
            "sees a pregnancy doubled",
            "on_pregnancy: set_pregnancy_twin"),

        new("life_ward_plague", MagicDomain.Life, EffectScope.Province, EffectPolarity.Boon,
            MagicCeiling.Realm, 1.4, 10, 50,
            "turns a sickness away from a county ({0}% resistance)",
            "add_county_modifier epidemic resistance; epidemic_outbreak_chance"),

        // ---------------------------------------------------------------- Death
        new("death_raise", MagicDomain.Death, EffectScope.Realm, EffectPolarity.Boon,
            MagicCeiling.Realm, 2.2, 1, 3,
            "calls up soldiers who were buried ({0} regiment)",
            "create_men_at_arms of generated undead type; upkeep modifier"),

        new("death_waste", MagicDomain.Death, EffectScope.Character, EffectPolarity.Harm,
            MagicCeiling.Court, 1.5, 1, 4,
            "sets a wasting in a rival (-{0} health)",
            "add_character_modifier = { health = -X }; hidden"),

        new("death_barren", MagicDomain.Death, EffectScope.Character, EffectPolarity.Harm,
            MagicCeiling.Court, 1.3, 20, 80,
            "closes a bloodline's womb (-{0}% fertility)",
            "add_character_modifier fertility negative, long duration"),

        new("death_speak", MagicDomain.Death, EffectScope.Self, EffectPolarity.Neutral,
            MagicCeiling.Court, 1.1, 1, 2,
            "asks the dead what they knew ({0} secret)",
            "random dead relative -> random_secret -> reveal_to = root"),

        new("death_deathless", MagicDomain.Death, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.World, 4.0, 1, 1,
            "stops the caster ageing altogether",
            "immortality trait or repeated age modifier; needs a succession counter-pressure"),

        new("death_plaguebearer", MagicDomain.Death, EffectScope.Province, EffectPolarity.Harm,
            MagicCeiling.Realm, 2.6, 1, 2,
            "seeds a sickness in a county ({0} virulence)",
            "start_epidemic in province; couples to the generated epidemic if one exists"),

        new("death_undying_will", MagicDomain.Death, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Personal, 1.0, 10, 40,
            "refuses the wound that should have finished it (+{0}% to survive)",
            "on_death interception, or a health modifier plus a survival trigger"),

        // ---------------------------------------------------------------- War
        new("war_bless_levy", MagicDomain.War, EffectScope.Realm, EffectPolarity.Boon,
            MagicCeiling.Realm, 1.8, 5, 25,
            "hardens an army against harm (+{0}% toughness)",
            "add_character_modifier = { men_at_arms_toughness_mult = X }"),

        new("war_terror", MagicDomain.War, EffectScope.Realm, EffectPolarity.Harm,
            MagicCeiling.Realm, 1.9, 5, 20,
            "unnerves an enemy host (-{0}% their advantage)",
            "opposing army modifier; enemy_advantage"),

        new("war_fog", MagicDomain.War, EffectScope.Province, EffectPolarity.Neutral,
            MagicCeiling.Realm, 1.4, 10, 40,
            "hides a county under weather that will not lift ({0}% siege slowdown)",
            "add_county_modifier = { siege_speed = -X defender_advantage = Y }"),

        new("war_ironskin", MagicDomain.War, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Personal, 1.0, 2, 8,
            "turns blades from the caster's skin (+{0} prowess)",
            "add_character_modifier = { prowess = X }"),

        new("war_beasts", MagicDomain.War, EffectScope.Realm, EffectPolarity.Boon,
            MagicCeiling.Realm, 2.0, 1, 2,
            "brings something out of the wild to fight ({0} regiment)",
            "create_men_at_arms of generated beast type"),

        new("war_rally", MagicDomain.War, EffectScope.Realm, EffectPolarity.Boon,
            MagicCeiling.Court, 1.2, 10, 40,
            "puts the fallen back on their feet (+{0}% reinforcement)",
            "add_character_modifier = { men_at_arms_recovery? / levy_reinforcement_rate = X }"),

        // ---------------------------------------------------------------- Mind
        new("mind_sway", MagicDomain.Mind, EffectScope.Character, EffectPolarity.Boon,
            MagicCeiling.Court, 0.9, 15, 60,
            "turns a mind warm towards the caster (+{0} opinion)",
            "add_opinion = { modifier = gen_magic_swayed opinion = X }"),

        new("mind_compel", MagicDomain.Mind, EffectScope.Character, EffectPolarity.Harm,
            MagicCeiling.Court, 1.6, 1, 2,
            "leaves an instruction a target cannot argue with ({0} hook)",
            "add_hook = { type = favor/strong }"),

        new("mind_read", MagicDomain.Mind, EffectScope.Character, EffectPolarity.Neutral,
            MagicCeiling.Court, 1.2, 1, 2,
            "takes what a target is hiding ({0} secret)",
            "random_secret -> reveal_to = root"),

        new("mind_dread", MagicDomain.Mind, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Court, 1.1, 5, 25,
            "makes the caster something to be afraid of (+{0} dread)",
            "add_dread / add_character_modifier = { dread = X }"),

        new("mind_break", MagicDomain.Mind, EffectScope.Character, EffectPolarity.Harm,
            MagicCeiling.Court, 1.7, 100, 400,
            "unmakes a mind that was in the way (+{0} stress)",
            "add_stress = X; at the top of the range, add_trait = lunatic"),

        new("mind_forget", MagicDomain.Mind, EffectScope.Character, EffectPolarity.Neutral,
            MagicCeiling.Court, 1.3, 1, 2,
            "takes a memory out of the world ({0} secret or hook removed)",
            "remove_secret / remove_hook; also clears opinion modifiers"),

        new("mind_tongue", MagicDomain.Mind, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Personal, 0.8, 2, 6,
            "hears the meaning under any speech (+{0} diplomacy)",
            "add_character_modifier = { diplomacy = X }; language barrier suppression"),

        // ---------------------------------------------------------------- Nature
        new("nature_bloom", MagicDomain.Nature, EffectScope.Province, EffectPolarity.Boon,
            MagicCeiling.Realm, 1.6, 10, 50,
            "makes a county grow past what its soil allows (+{0}% development growth)",
            "add_county_modifier = { development_growth_factor = X }"),

        new("nature_blight", MagicDomain.Nature, EffectScope.Province, EffectPolarity.Harm,
            MagicCeiling.Realm, 1.7, 10, 50,
            "sours the ground of a county (-{0}% supply and growth)",
            "add_county_modifier = { supply_limit_mult = -X development_growth_factor = -Y }"),

        new("nature_calm", MagicDomain.Nature, EffectScope.Province, EffectPolarity.Boon,
            MagicCeiling.Realm, 1.2, 20, 60,
            "quiets the roads and the weather ({0}% safer travel)",
            "add_county_modifier travel danger; travel_speed"),

        new("nature_stoneshape", MagicDomain.Nature, EffectScope.Title, EffectPolarity.Boon,
            MagicCeiling.Realm, 1.5, 10, 40,
            "raises walls in a season instead of a decade (-{0}% build time)",
            "add_character_modifier = { build_speed = -X build_gold_cost = -Y }"),

        new("nature_partwater", MagicDomain.Nature, EffectScope.Province, EffectPolarity.Neutral,
            MagicCeiling.World, 3.0, 1, 1,
            "opens a road where there was water",
            "temporary adjacency / army movement; the most map-invasive atom in the palette"),

        new("nature_seasonturn", MagicDomain.Nature, EffectScope.World, EffectPolarity.Neutral,
            MagicCeiling.World, 4.5, 1, 1,
            "pushes the world's weather off its course",
            "advance or delay a generated situation phase; the keystone atom"),

        new("nature_beastkin", MagicDomain.Nature, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Personal, 0.9, 15, 45,
            "goes unmolested by anything that runs on four legs ({0}% safer abroad)",
            "travel danger modifier; hunt activity outcomes"),

        // ---------------------------------------------------------------- Fate
        new("fate_foresee", MagicDomain.Fate, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Personal, 1.1, 10, 40,
            "shows the caster the shape of what is coming (+{0}% scheme success)",
            "add_character_modifier scheme_success_chance; reveal hidden info"),

        new("fate_illfortune", MagicDomain.Fate, EffectScope.Character, EffectPolarity.Harm,
            MagicCeiling.Court, 1.5, 10, 40,
            "puts a rival on the wrong side of every chance (-{0}% their odds)",
            "add_character_modifier broad negative; scheme resistance down"),

        new("fate_bind_oath", MagicDomain.Fate, EffectScope.Character, EffectPolarity.Neutral,
            MagicCeiling.Court, 1.4, 1, 2,
            "makes a promise that keeps itself ({0} binding)",
            "add_hook = { type = strong }; on break, fire the price"),

        new("fate_sever", MagicDomain.Fate, EffectScope.Character, EffectPolarity.Harm,
            MagicCeiling.Realm, 3.4, 1, 1,
            "cuts a life short at a distance",
            "death = { killer = root death_reason = gen_magic_death }; the single most "
            + "dangerous atom to price wrongly"),

        new("fate_mark_heir", MagicDomain.Fate, EffectScope.Character, EffectPolarity.Boon,
            MagicCeiling.Realm, 2.0, 10, 40,
            "settles a succession before it is contested (+{0} legitimacy)",
            "add_legitimacy / succession weight; title claim strengthening"),

        new("fate_omen", MagicDomain.Fate, EffectScope.Realm, EffectPolarity.Neutral,
            MagicCeiling.Realm, 1.3, 1, 1,
            "reads what the realm is heading towards",
            "reveal a pending prophecy predicate to the caster"),

        // ---------------------------------------------------------------- Craft
        new("craft_enchant", MagicDomain.Craft, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Court, 1.8, 1, 3,
            "puts something into an object that stays there ({0} quality)",
            "create_artifact with a generated modifier; hooks the existing artifact writer"),

        new("craft_mend", MagicDomain.Craft, EffectScope.Title, EffectPolarity.Boon,
            MagicCeiling.Court, 1.0, 20, 60,
            "restores what was ruined (-{0}% repair cost)",
            "building repair; add_character_modifier build_gold_cost"),

        new("craft_transmute", MagicDomain.Craft, EffectScope.Self, EffectPolarity.Boon,
            MagicCeiling.Personal, 1.2, 50, 300,
            "makes gold out of something that was not gold (+{0})",
            "add_gold = X; watch the economy — this is the easiest degenerate atom"),

        new("craft_tower", MagicDomain.Craft, EffectScope.Title, EffectPolarity.Boon,
            MagicCeiling.Realm, 1.9, 1, 1,
            "raises a seat that could not otherwise stand there",
            "unlock a generated building in a barony; ties to the ley field"),

        new("craft_bind_servant", MagicDomain.Craft, EffectScope.Court, EffectPolarity.Boon,
            MagicCeiling.Court, 1.6, 1, 2,
            "binds something into service ({0} servant)",
            "create_character with a generated trait, added to the court"),

        new("craft_warforge", MagicDomain.Craft, EffectScope.Realm, EffectPolarity.Boon,
            MagicCeiling.Realm, 1.7, 5, 20,
            "arms a host with work no smith did (+{0}% damage)",
            "add_character_modifier = { men_at_arms_damage_mult = X }"),
    ];

    /// <summary>Atoms this world may use at all: allowed domain, and within the ceiling.</summary>
    public static IReadOnlyList<EffectAtom> Available(Cosmology myth) =>
        All.Where(a => myth.Domains.Allows(a.Domain) && a.MinCeiling <= myth.Ceiling).ToList();

    public static EffectAtom ByKey(string key) => All.First(a => a.Key == key);
}
