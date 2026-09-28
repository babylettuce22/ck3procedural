using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The 3D model a generated wonder wears on the map, together with the names and the icon that
/// belong to that particular silhouette.
///
/// CK3 renders a special building by looking up the <c>asset</c> block on the building definition
/// and drawing that mesh at the province's <c>special_building</c> locator (see
/// <see cref="Emit.LocatorWriter"/>, which emits one instance per land province with id = province
/// id). A building with no asset block is mechanically real and completely invisible, which is what
/// the generated wonders used to be.
///
/// The reason this is a catalogue rather than three independent random draws is that the meshes are
/// *recognisable*. Vanilla modelled actual buildings, so drawing the Giza mesh and then naming it
/// "The Colossus of Ardhan" puts a name on the map that visibly contradicts the pyramids standing
/// under it. So the mesh is picked first and everything the player reads — name, description,
/// build-menu icon — hangs off the mesh. Only the modifiers come from the archetype.
/// </summary>
/// <param name="Mesh">pdxmesh name, exactly as declared in gfx/models/buildings.</param>
/// <param name="Icon">A file in gfx/interface/icons/building_types, verified to exist.</param>
/// <param name="Blurb">Description, formatted with the county name.</param>
/// <param name="Names">Name candidates, formatted with the county name and a culture word.</param>
/// <param name="Encloses">
/// Zero for a wonder that stands beside the holding, which is nearly all of them. Otherwise the
/// mesh is a hollow ring built to go AROUND the holding — a wall circuit, a walled mound — and this
/// is the outer radius of its standing geometry in world units, so whatever else crowds the holding
/// knows to stay outside it. See <see cref="ProvinceAnchor.EncloseHoldings"/>.
/// </param>
/// <param name="ReplacesWalls">
/// The ring is the town's walls, so the holding's own wall ring must not be drawn inside it as well.
/// Vanilla switches the ring off for exactly these (Lugo, Toledo) in <c>walls_00</c>; see
/// <see cref="Emit.WonderWriter"/>.
/// </param>
/// <param name="Ladder">
/// One model per rung, lowest first, for a wonder vanilla modelled in stages — Canterbury's three,
/// Mont Saint-Michel's four, the mandala capital's five — so the building on the map grows as the
/// ladder is climbed instead of standing finished from the first day. Exactly
/// <see cref="GeneratedWonder.Tiers"/> entries, the first equal to <paramref name="Mesh"/>; where
/// vanilla has more stages than there are rungs, the first, a middle and the last are kept. Null
/// draws <paramref name="Mesh"/> on every rung, which is what a single-model wonder does.
/// </param>
/// <param name="IsEntity">
/// The names are entities rather than pdxmeshes. Only for the few wonders vanilla itself draws as
/// an entity, because the entity is what lights the braziers and the kiln smoke; the mesh alone is
/// the same model gone cold.
/// </param>
/// <param name="Dlc">
/// A <c>requires_dlc_flag</c> for the model, copied from the vanilla asset block that draws it. A
/// player without that DLC is shown <paramref name="Fallback"/> instead, exactly as vanilla shows
/// the plain cathedral in place of the Holy Buildings one.
/// </param>
/// <param name="Fallback">The unflagged pdxmesh drawn when <paramref name="Dlc"/> is not owned — a
/// base-game model of the same kind of building, so the name above it still reads true.</param>
/// <param name="Needs">What the county must have for the model to be honest there.</param>
/// <param name="Family">
/// Models that are one building in two states — the Parthenon and the church it became, Hagia
/// Sophia before and after its minarets — share a family, so one map never shows both.
/// </param>
public sealed record WonderAsset(string Mesh, string Icon, string Blurb, string[] Names,
    double Encloses = 0, bool ReplacesWalls = false,
    string[]? Ladder = null, bool IsEntity = false, string? Dlc = null, string? Fallback = null,
    WonderSite Needs = WonderSite.Any, string? Family = null)
{
    /// <summary>The key the one-per-map rule is kept on.</summary>
    public string Look => Family ?? Mesh;

    public bool Fits(WonderSite site) => (site & Needs) == Needs;
}

/// <summary>
/// What a wonder's county offers the model standing in it, and so what a <see cref="WonderAsset"/>
/// can ask for. Read off the county's baronies by <see cref="WorldCenterMap"/>.
/// </summary>
[Flags]
public enum WonderSite
{
    Any = 0,

    /// <summary>Hills or mountains somewhere in the county: a cliff monastery or a walled pass needs
    /// something to cling to.</summary>
    Relief = 1,

    /// <summary>The county reaches the sea: a tidal abbey on floodplains is a bug.</summary>
    Coast = 2,

    /// <summary>Most of the county is desert, drylands or desert mountains: the models painted in
    /// sand and ochre.</summary>
    Arid = 4,
}

/// <summary>
/// The architectural tradition a wonder's model was built in, on the same lines as vanilla's
/// <c>building_gfx</c> — which is what a culture's own castles and cities are drawn in, and so the
/// tone a wonder in that culture's county should share. Flags, because some models sit between two
/// (the Mezquita is Iberian and Moorish). <see cref="Any"/> is a model no tradition owns — a
/// mountain, a karst bay, a mine.
/// </summary>
[Flags]
public enum WonderStyle
{
    Any = 0,
    Western = 1 << 0,
    Norse = 1 << 1,
    Mediterranean = 1 << 2,
    Mena = 1 << 3,
    Iranian = 1 << 4,
    African = 1 << 5,
    Indian = 1 << 6,
    SoutheastAsian = 1 << 7,
    EastAsian = 1 << 8,
    Steppe = 1 << 9,
}

/// <summary>
/// Per-archetype pools of <see cref="WonderAsset"/>.
///
/// Every wonder here shows a player something without owning any DLC — its own model, or for the
/// two packs below a base-game stand-in. That is not the same as "is in the base game": the DLC folders under game/dlc ship only gfx, music and sound, and *all* the building
/// meshes — fp2, fp3, ep2, ep3, tgp, fp4 alike — live in the base game/gfx/models/buildings tree.
/// What gates them is the `requires_dlc_flag` field on the asset block that references them, and
/// vanilla does not set it on any of these (the Alhambra and the whole legendary set are plain
/// unflagged assets). The pools were filtered against that field rather than against the filename
/// prefix. The one flagged set, the Holy Buildings pack's (<c>holy_buildings</c>, the cp6 meshes),
/// is carried the way vanilla carries it: the flag on the model and a base-game
/// <see cref="WonderAsset.Fallback"/> after it, so a player without the pack sees a plain
/// cathedral where an owner sees the grand one. The East Asian Wonders pack's (cp8) models carry
/// the same pair, because vanilla gates those by where it places them rather than by flag — the
/// test is whether a non-owner ever sees the model in vanilla, and for cp8 they never do.
///
/// Natural features are included only where the archetype's modifiers still make sense of them — a
/// sacred peak is a Sanctuary because pilgrims climb it, a karst bay is a GreatHarbor because ships
/// shelter in it. The painted hills, the thousand haycock hills and the volcano are here on the same
/// terms, as sacred heights: holy ground a people makes offerings at, only offered on a county with
/// the relief to carry them.
///
/// Surveyed against the installed game on 2026-09-27: 146 special, legendary and great-building
/// meshes, of which these pools draw 128. The rest are left out on measurement, not taste — Hadrian's,
/// Gorgan's and the Great Wall's pieces are linear walls laid along real terrain (the Great Wall
/// segments run 180-350 units); the Constantinople and Chang'an blankets are whole cities with solid
/// middles, drawn under a capital rather than beside it; the Angkor temple field has standing
/// geometry 35 units out, wider than a barony; and four in-between construction stages of the
/// laddered wonders (Qutb Minar 2, Mont Saint-Michel 2, mandala capital 2 and 4) fall between the
/// rungs a three-tier ladder shows.
/// </summary>
public static class WonderAssets
{
    /// <summary>The Holy Buildings content pack's feature flag, as vanilla's cp6 asset blocks spell
    /// it (<c>has_cp6_dlc_trigger</c> tests the same name).</summary>
    private const string HolyBuildings = "holy_buildings";

    /// <summary>
    /// The East Asian Wonders content pack's feature flag (<c>has_cp8_dlc_trigger</c>). Vanilla never
    /// writes it on an asset block — it gates those wonders where they are PLACED instead, in
    /// game_start.txt, and draws each cp8 model from no other building — so a player without the
    /// pack never sees them in vanilla. Placed here unconditionally, they would be; the flag and a
    /// fallback keep it the way vanilla has it.
    /// </summary>
    private const string EastAsianWonders = "east_asian_wonders";

    private static readonly WonderAsset[] Sanctuary =
    [
        new("building_special_cathedral_generic_mesh", "icon_structure_cologne_cathedral.dds",
            "A temple raised over generations, its nave tall enough to swallow the rooftops of {0}.",
            ["The Great Nave of {0}", "The High Temple of {1}", "The Spired Temple of {0}"]),
        new("building_special_cathedral_pagan_mesh", "icon_structure_cathedral_pagan.dds",
            "A vast timber-and-stone sanctuary of the old rites, standing where {0} has always made its offerings.",
            ["The Grand Temple of {1}", "The Elder Sanctuary of {0}", "The Great Hallows of {1}"]),
        new("building_special_hagia_sophia_mesh", "icon_structure_hagia_sophia.dds",
            "An impossible dome floating on a ring of windows, the largest enclosed space anyone in {0} has stood beneath.",
            ["The Great Dome of {0}", "The Holy Dome of {1}", "The Domed Sanctuary of {0}"],
            Family: "hagia_sophia"),
        new("building_special_hagia_sophia_minarets_mesh", "icon_structure_holy_wisdom.dds",
            "A colossal domed sanctuary ringed by slender towers, rededicated by every faith that has held {0}.",
            ["The Great Dome of {0}", "The Crowned Sanctuary of {1}", "The Many-Towered Dome of {0}"],
            Family: "hagia_sophia"),
        new("building_special_notre_dame_mesh", "icon_structure_notre_dame.dds",
            "Flying buttresses and a forest of pinnacles carry the roof higher than any hall in {0}.",
            ["The Buttressed Temple of {1}", "The Grand Nave of {0}", "The Spires of {0}"]),
        new("ep2_building_special_canterbury_01_mesh", "icon_structure_canterbury_cathedral.dds",
            "The mother temple of the realm, where the high clergy of {0} are consecrated and buried.",
            ["The Mother Temple of {0}", "The High Priest's Seat of {1}", "The Tomb-Temple of {0}"],
            Ladder: ["ep2_building_special_canterbury_01_mesh", "ep2_building_special_canterbury_02_mesh",
                     "ep2_building_special_canterbury_03_mesh"]),
        new("fp2_building_special_basilica_santiago_mesh", "compostela.dds",
            "The end of a pilgrim road walked by thousands, whose hostels and shrines feed half of {0}.",
            ["The Pilgrims' Sanctuary of {0}", "The Journey's End of {1}", "The Wayfarers' Shrine of {0}"]),
        new("building_special_great_mosque_of_mecca_mesh", "icon_structure_great_mosque_of_mecca.dds",
            "A sacred precinct enclosing the holiest stone in {0}, circled day and night by the faithful.",
            ["The Holy Precinct of {0}", "The Sacred Precinct of {1}", "The Court of the Holy Stone of {0}"]),
        new("building_special_great_mosque_of_djenne_mesh", "icon_structure_great_mosque_of_djenne.dds",
            "A mountain of sun-dried brick bristling with palm scaffolding, replastered each year by all of {0}.",
            ["The Earthen Temple of {0}", "The Sunbaked Sanctuary of {1}", "The Great Mudbrick Temple of {0}"]),
        new("fp3_building_special_great_mosque_of_samarra_01_a_mesh", "icon_structure_great_mosque_of_samarra.dds",
            "A tower that climbs in a single outward spiral, visible from every road into {0}.",
            ["The Spiral Tower of {0}", "The Winding Tower of {1}", "The Coiled Tower of {0}"]),
        new("monument_mezquita_de_cordoba_mesh", "mezquita_cordoba.dds",
            "A hall of striped double arches receding further than the eye can follow, the pride of {0}.",
            ["The Pillared Hall of {0}", "The Forest of Arches of {1}", "The Great Prayer Hall of {0}"]),
        new("fp3_building_special_imam_reza_shrine_01_a_mesh", "icon_structure_imam_reza_shrine.dds",
            "A shrine under a dome of beaten gold, drawing mourners and petitioners from far beyond {0}.",
            ["The Golden Shrine of {0}", "The Gilded Sanctuary of {1}", "The Radiant Shrine of {0}"]),
        new("fp3_building_special_soltaniyeh_01_a_mesh", "icon_structure_soltaniyeh.dds",
            "A turquoise dome on an octagon of brick, raised as a tomb grand enough to shame kings of {0}.",
            ["The Turquoise Dome of {0}", "The Great Mausoleum of {1}", "The Azure Tomb of {0}"]),
        new("fp3_building_special_minaret_and_remains_of_jam_01_a_mesh", "icon_structure_minaret_and_remains_of_jam.dds",
            "A single carved tower left standing in an empty valley, all that remains of a capital of {0}.",
            ["The Lonely Tower of {0}", "The Tower of {1}", "The Last Tower of {0}"]),
        new("ep3_monument_parthenon_01_a_mesh", "icon_structure_parthenon.dds",
            "A marble temple on the height above {0}, its colonnade unchanged through every faith that has claimed it.",
            ["The Great Temple of {0}", "The Marble Temple of {1}", "The Columned Sanctuary of {0}"],
            Family: "parthenon"),
        new("building_special_stonehenge_mesh", "icon_structure_stonehenge.dds",
            "A ring of dressed sarsens set by hands nobody in {0} can name, still keeping the turn of the year.",
            ["The Standing Stones of {0}", "The Great Henge of {1}", "The Stone Circle of {0}"]),
        new("building_special_suwalesi_megaliths_01_mesh", "icon_structure_suwalesi_megaliths.dds",
            "Carved monoliths scattered across the upland, older than any lineage ruling {0}.",
            ["The Megaliths of {0}", "The Ancient Monoliths of {1}", "The Elder Stones of {0}"],
            Dlc: EastAsianWonders, Fallback: "building_special_stonehenge_mesh"),
        new("building_special_brihadeeswarar_temple_mesh", "icon_structure_brihadeeswarar_temple.dds",
            "A tapering tower of carved granite whose capstone was hauled up a ramp miles long, the wonder of {0}.",
            ["The Great Temple Tower of {0}", "The Towering Temple of {1}", "The Stone Temple of {0}"]),
        new("tgp_building_special_borudur_mesh", "icon_structure_borobudur.dds",
            "A stepped mountain of stone galleries, walked in widening circles by the pilgrims of {0}.",
            ["The Stepped Mountain of {0}", "The Terraced Sanctuary of {1}", "The Galleried Mount of {0}"]),
        new("tgp_building_special_angkorwat_mesh", "icon_structure_angkor_wat.dds",
            "A temple-city inside a moat wide enough to sail, the axis on which {0} was laid out.",
            ["The Moated Temple of {0}", "The Great Temple-City of {1}", "The Lotus Towers of {0}"]),
        new("building_special_pyramid_lingapura_01_mesh", "icon_structure_pyramid_lingapura.dds",
            "A stepped temple-mountain rising in sheer tiers from the plain of {0}.",
            ["The Step Pyramid of {0}", "The Temple-Mountain of {1}", "The Tiered Sanctuary of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_borudur_mesh"),
        new("tgp_building_special_leshan_buddha_mesh", "icon_structure_leshan_giant_buddha.dds",
            "A seated colossus carved from the living cliff, its feet level with the boats of {0}.",
            ["The Seated Colossus of {0}", "The Cliff Colossus of {1}", "The Carved Giant of {0}"]),
        new("building_special_maijishan_grottoes_01_mesh", "icon_structure_maijishan_grottoes.dds",
            "Shrines cut into a sheer rock face and reached by stairways pinned to the cliff above {0}.",
            ["The Cliff Grottoes of {0}", "The Carved Caves of {1}", "The Grotto Shrines of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_leshan_buddha_mesh"),
        new("tgp_building_special_itsukushima_mesh", "icon_structure_torii_gate.dds",
            "A shrine built out over the tideline, its great gate standing in open water at the flood of {0}.",
            ["The Floating Gate of {0}", "The Tidewater Shrine of {1}", "The Sea Gate of {0}"]),
        new("building_special_izumo_taisha_01_mesh", "icon_structure_izumo_taisha.dds",
            "A timber shrine on pillars taller than the trees, rebuilt unchanged for as long as {0} has records.",
            ["The Great Shrine of {0}", "The Timber Shrine of {1}", "The Elder Shrine of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_itsukushima_mesh"),
        new("tgp_building_special_hwangnyongsa_mesh", "icon_structure_stone_pagoda.dds",
            "A nine-storey wooden pagoda raised so that every neighbour of {0} might see it and think better of war.",
            ["The Nine-Storey Pagoda of {0}", "The Great Pagoda of {1}", "The Watch of {0}"]),
        new("building_special_three_pagodas_dali_01_mesh", "icon_structure_three_pagodas_dali.dds",
            "Three white pagodas standing in line against the mountains, the sign of {0} on every map.",
            ["The Three Pagodas of {0}", "The White Pagodas of {1}", "The Triple Spires of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_hwangnyongsa_mesh"),
        new("building_special_my_son_sanctuary_01_mesh", "icon_structure_my_son_sanctuary.dds",
            "A valley of brick towers swallowed by jungle, where the old kings of {0} still receive offerings.",
            ["The Jungle Sanctuary of {0}", "The Brick Towers of {1}", "The Hidden Temples of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_po_klong_temple_mesh"),
        new("fp4_legendary_building_norse_shrine_01_a_mesh", "icon_structure_temple_of_uppsala.dds",
            "A grove-shrine hung with offerings, where the great sacrifices of {0} are made.",
            ["The Great Grove of {0}", "The Hallowed Grove of {1}", "The Offering Place of {0}"]),
        new("fp4_legendary_dharmic_shrine_01_a_mesh", "icon_building_legendary_shrine.dds",
            "A shrine grown famous far past the borders of {0} for the miracles claimed there.",
            ["The Great Shrine of {0}", "The Blessed Shrine of {1}", "The Shrine of {1}"]),
        new("fp4_legendary_steppe_shrine_01_a_mesh", "icon_building_legendary_shrine.dds",
            "A cairn and standard on the open grass, the gathering place of every clan owing {0}.",
            ["The Sacred Cairn of {0}", "The Standing Shrine of {1}", "The Gathering Stone of {0}"]),

        // ---- Added 2026-09-27 from the unused-mesh survey. ----

        new("ep3_monument_parthenon_01_b_mesh", "icon_structure_parthenon_theotokos.dds",
            "An old marble temple walled in between its columns and roofed over for a newer rite, the first sanctuary of {0} under its present faith.",
            ["The Walled Temple of {0}", "The Marble Sanctuary of {1}", "The Rededicated Temple of {0}"],
            Family: "parthenon"),
        new("ep3_basilica_sant_apollinare_nuovo_mesh", "icon_structure_apollinare_nuovo.dds",
            "A long hall of plain brick outside and gold mosaic within, where the processions of {0} begin.",
            ["The Golden Hall of {0}", "The Mosaic Temple of {1}", "The Processional Hall of {0}"]),
        new("ep3_cattolica_di_stilo_mesh", "icon_structure_cattolica_stilo.dds",
            "A small square temple under five brick domes, copied in every village shrine of {0}.",
            ["The Five Domes of {0}", "The Domed Shrine of {1}", "The Little Temple of {0}"]),
        new("ep3_church_saint_lazarus_mesh", "icon_structure_saint_lazarus.dds",
            "A temple of pale stone over a revered tomb, its bell tower the first sight of {0} from the road.",
            ["The Tomb Temple of {0}", "The Bell Tower of {1}", "The Pale Shrine of {0}"]),
        new("ep3_church_saint_sophia_ohrid_01_a_mesh", "icon_structure_sofia_ohrid.dds",
            "A temple painted from floor to vault, where the high priests of {0} have been enthroned for generations.",
            ["The Painted Temple of {0}", "The Frescoed Sanctuary of {1}", "The Old Temple of {0}"]),
        new("ep3_hagios_demetrios_01_a_mesh", "icon_structure_hagios_demetrios.dds",
            "A five-aisled hall over a martyr's tomb, where all of {0} gathers on the martyr's feast.",
            ["The Martyr's Temple of {0}", "The Five-Aisled Hall of {1}", "The Feast Temple of {0}"]),
        new("ep3_etchmiadzin_cathedral_01_a_mesh", "icon_structure_etchmiadzin_cathedral.dds",
            "A domed temple of rose-coloured stone under a conical drum, the first house of worship ever raised in {0}.",
            ["The First Temple of {0}", "The Rose Stone Temple of {1}", "The Conical Dome of {0}"]),
        new("ep3_saint_catherine_monastery_mesh", "icon_structure_saint_catherine.dds",
            "A monastery behind fortress walls in a desert valley, its library older than any other in {0}.",
            ["The Desert Monastery of {0}", "The Walled Monastery of {1}", "The Monastery Under the Mountain of {0}"],
            Needs: WonderSite.Arid),
        new("tgp_building_special_po_klong_temple_mesh", "icon_structure_po_klong_garai.dds",
            "Three brick tower-shrines on a bare hilltop, lit at dusk so they can be seen across the plain of {0}.",
            ["The Hill Towers of {0}", "The Three Shrines of {1}", "The Sunset Towers of {0}"]),
        new("building_special_stone_pagoda_01_mesh", "icon_structure_stone_pagoda.dds",
            "A pagoda of dressed granite, storey stacked on narrowing storey, older than any timber hall in {0}.",
            ["The Stone Pagoda of {0}", "The Granite Pagoda of {1}", "The Stone Tower of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_hwangnyongsa_mesh"),
        new("building_special_buddha_kamakura_01_entity", "icon_structure_buddha_kamakura.dds",
            "A seated colossus cast in bronze and left under the open sky, with fires kept burning at its feet by the people of {0}.",
            ["The Bronze Colossus of {0}", "The Seated Giant of {1}", "The Great Bronze of {0}"],
            IsEntity: true, Dlc: EastAsianWonders, Fallback: "tgp_building_special_leshan_buddha_mesh"),
        new("fp4_legendary_building_christian_shrine_01_mesh", "icon_building_legendary_shrine.dds",
            "A small shrine grown famous for the cures claimed there, its walls hung with the offerings of {0}.",
            ["The Healing Shrine of {0}", "The Miracle Shrine of {1}", "The Wayside Shrine of {0}"]),
        new("fp4_legendary_islamic_shrine_01_a_mesh", "icon_building_legendary_shrine.dds",
            "The domed tomb of a holy man, kept by his descendants and visited by all of {0} on his day.",
            ["The Domed Tomb of {0}", "The Holy Man's Tomb of {1}", "The Tomb Shrine of {0}"]),

        // The Holy Buildings pack's. Each carries the pack's flag and a base-game model of the same
        // kind of building for a player without it; see the class summary.
        new("cp6_building_special_grand_cathedral_mesh", "icon_structure_cologne_cathedral.dds",
            "Twin spires over a nave a lifetime in the building, still rising under every new high priest of {0}.",
            ["The High Nave of {0}", "The Twin-Spired Temple of {1}", "The Unfinished Temple of {0}"],
            Dlc: HolyBuildings, Fallback: "building_special_cathedral_generic_mesh"),
        new("cp6_building_special_st_peters_basilica_mesh", "icon_structure_st_peters_basilica.dds",
            "A great domed temple over a founder's grave, its colonnaded square large enough to hold all of {0} at once.",
            ["The Founder's Temple of {0}", "The Colonnaded Dome of {1}", "The Great Square of {0}"],
            Dlc: HolyBuildings, Fallback: "building_special_hagia_sophia_mesh"),
        new("cp6_building_special_yazd_mosque_mesh", "icon_structure_yazd_mosque.dds",
            "A tiled gateway taller than any tower in {0}, flanked by twin slender towers and opening onto a great court.",
            ["The Tiled Temple of {0}", "The High Portal of {1}", "The Gateway Temple of {0}"],
            Dlc: HolyBuildings, Fallback: "fp3_building_special_imam_reza_shrine_01_a_mesh"),
        new("cp6_building_special_boudhanath_mesh", "icon_structure_boudhanath.dds",
            "A white dome under a gilded tower painted with watching eyes on every side, circled by the pilgrims of {0}.",
            ["The Watching Eyes of {0}", "The Gilded Spire of {1}", "The White Dome of {0}"],
            Dlc: HolyBuildings, Fallback: "tgp_building_special_borudur_mesh"),
        new("cp6_building_special_sanchi_stupa_mesh", "icon_structure_sanchi_stupa.dds",
            "A solid hemisphere of brick behind four carved stone gateways, raised over relics the kings of {0} still guard.",
            ["The Carved Gates of {0}", "The Relic Mound of {1}", "The Great Mound of {0}"],
            Dlc: HolyBuildings, Fallback: "tgp_building_special_borudur_mesh"),
        // Vanilla's four stages: the iron pillar in its court, then the tower rising over it.
        new("cp6_building_special_qutb_minar_01_mesh", "icon_structure_qutb_minar.dds",
            "A court around an iron pillar that has never rusted, where {0} raises a fluted victory tower storey by storey.",
            ["The Victory Tower of {0}", "The Red Tower of {1}", "The Fluted Tower of {0}"],
            Ladder: ["cp6_building_special_qutb_minar_01_mesh", "cp6_building_special_qutb_minar_03_mesh",
                     "cp6_building_special_qutb_minar_04_mesh"],
            Dlc: HolyBuildings, Fallback: "fp3_building_special_minaret_and_remains_of_jam_01_a_mesh"),
        new("cp6_building_special_mont_st_michel_01_mesh", "icon_structure_mont_st_michel.dds",
            "A monastery climbing a rock that the tide cuts off from the shore of {0} twice a day.",
            ["The Tidal Monastery of {0}", "The Monastery Rock of {1}", "The Mount of {0}"],
            Ladder: ["cp6_building_special_mont_st_michel_01_mesh", "cp6_building_special_mont_st_michel_03_mesh",
                     "cp6_building_special_mont_st_michel_04_mesh"],
            Dlc: HolyBuildings, Fallback: "ep3_athos_monasteries_01_b_mesh", Needs: WonderSite.Coast),
    ];

    // Thin on purpose: vanilla modelled almost no harbours. What is here reads as a coastal or
    // mercantile landmark, since PickArchetype only reaches for this pool on coastal counties.
    private static readonly WonderAsset[] GreatHarbor =
    [
        new("fp2_building_special_tower_of_hercules_mesh", "hercules.dds",
            "A lighthouse whose fire is banked at dusk and never allowed to go out, guiding every hull into {0}.",
            ["The Great Pharos of {0}", "The Beacon of {1}", "The Lighthouse of {0}"]),
        new("fp4_legendary_western_watchtower_01_a_mesh", "icon_building_legendary_watchtower.dds",
            "A signal tower on the headland, from which the whole approach to {0} can be read at a glance.",
            ["The Watch Tower of {0}", "The Seaward Tower of {1}", "The Signal Tower of {0}"]),
        new("building_special_ha_long_bay_01_mesh", "icon_structure_ha_long_bay.dds",
            "A drowned range of limestone towers, a thousand sheltered channels no fleet can blockade at {0}.",
            ["The Karst Isles of {0}", "The Thousand Isles of {1}", "The Dragon Isles of {0}"],
            Dlc: EastAsianWonders, Fallback: "fp2_building_special_rock_of_gibraltar_01_a_mesh"),
        new("fp2_building_special_rock_of_gibraltar_01_a_mesh", "gibraltar.dds",
            "A sheer rock standing over the narrows, so that nothing passes without the leave of {0}.",
            ["The Great Rock of {0}", "The Pillar of {1}", "The Guardian Rock of {0}"]),
        new("fp3_building_special_maharloo_lake_01_a_mesh", "icon_structure_maharloo_lake.dds",
            "A shallow lake that turns rose-red in the dry season, its salt pans worked by all of {0}.",
            ["The Rose Lake of {0}", "The Mirror Lake of {1}", "The Salt Mere of {0}"]),
        new("building_special_petra_mesh", "icon_structure_petra.dds",
            "Facades cut into a red gorge at the meeting of every caravan road that serves {0}.",
            ["The Rock-Carved City of {0}", "The Gorge City of {1}", "The Caravan City of {0}"]),
        new("building_special_mines_mesh", "icon_structure_mines.dds",
            "Galleries driven deep into the hillside, whose ore has paid for everything {0} owns.",
            ["The Great Mines of {0}", "The Deep Lodes of {1}", "The Silver Workings of {0}"]),

        // ---- Added 2026-09-27 from the unused-mesh survey. ----

        // Authored to stand in water, its rock 2.9 units below the origin; on the special-building
        // locator, which is always on land, that rock is simply underground.
        new("ep3_maidens_tower_01_a_mesh", "icon_structure_maiden_tower.dds",
            "A tower on a rock in the channel, from which a chain is stretched across the harbour mouth of {0}.",
            ["The Channel Tower of {0}", "The Rock Tower of {1}", "The Chain Tower of {0}"]),
        new("building_special_fanfang_guangzhou_01_entity", "icon_structure_fanfang_guangzhou.dds",
            "A walled quarter of foreign merchants with its own warehouses, judge and lamp tower, the richest street in {0}.",
            ["The Merchants' Quarter of {0}", "The Foreign Quarter of {1}", "The Traders' Ward of {0}"],
            IsEntity: true, Dlc: EastAsianWonders, Fallback: "fp2_building_special_aljaferia_mesh"),
        new("building_special_thuriang_kilns_01_entity", "icon_structure_thuriang_kilns.dds",
            "Rows of brick kilns smoking day and night, whose glazed wares leave {0} in every ship's hold.",
            ["The Great Kilns of {0}", "The Potters' Kilns of {1}", "The Glaze Works of {0}"],
            IsEntity: true, Dlc: EastAsianWonders, Fallback: "building_special_mines_mesh"),
    ];

    private static readonly WonderAsset[] GreatLibrary =
    [
        new("fp3_building_special_house_of_wisdom_01_a_mesh", "icon_structure_grand_library_of_baghdad.dds",
            "A court of translators, astronomers and copyists kept at the expense of {0}.",
            ["The House of Wisdom of {0}", "The Grand Library of {1}", "The Hall of Learning of {0}"]),
        new("building_special_yuelu_academy_01_mesh", "icon_structure_yuelu_academy.dds",
            "Lecture courts and dormitories under old trees, where the examined men of {0} are made.",
            ["The Great Academy of {0}", "The Academy of {1}", "The Scholars' Halls of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_heian_kyo_mesh"),
        new("building_special_confucius_temple_01_mesh", "icon_structure_confucius_temple.dds",
            "A temple to the sages doubling as the examination hall of {0}, its stelae listing every graduate.",
            ["The Temple of Learning of {0}", "The Sages' Temple of {1}", "The Hall of Sages of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_heian_kyo_mesh"),
        new("building_special_dengfeng_observatory_01_mesh", "icon_structure_dengfeng_observatory.dds",
            "A gnomon tower and a stone sighting-scale, from which the calendar of {0} is corrected.",
            ["The Star Observatory of {0}", "The Astronomers' Tower of {1}", "The Skywatch of {0}"],
            Dlc: EastAsianWonders, Fallback: "fp3_building_special_house_of_wisdom_01_a_mesh"),
        new("building_special_muara_takus_01_mesh", "icon_structure_muara_takus.dds",
            "A quiet brick precinct of domed shrines and cells where the manuscripts of {0} are copied and kept.",
            ["The Brick Precinct of {0}", "The Scriptorium of {1}", "The Cloister of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_borudur_mesh"),

        // ---- Added 2026-09-27 from the unused-mesh survey. ----

        new("cp6_building_special_sankore_university_mesh", "icon_structure_the_university_of_sankore.dds",
            "Mud-brick courts where scholars teach in the shade, holding more manuscripts than any other house in {0}.",
            ["The University of {0}", "The Mudbrick Academy of {1}", "The Hall of Manuscripts of {0}"],
            Dlc: HolyBuildings, Fallback: "building_special_great_mosque_of_djenne_mesh"),
    ];

    private static readonly WonderAsset[] Citadel =
    [
        new("building_special_tower_of_london_mesh", "icon_structure_tower_of_london.dds",
            "A pale stone keep inside two rings of wall, at once the armoury, the mint and the gaol of {0}.",
            ["The White Tower of {0}", "The Great Keep of {1}", "The Royal Fortress of {0}"]),
        new("building_special_trosky_castle_01_mesh", "icon_building_hill_forts.dds",
            "Two towers built on separate basalt spires, joined by a wall no siege engine can reach at {0}.",
            ["The Twin Crags of {0}", "The Cragfast Castle of {1}", "The Spire Fortress of {0}"]),
        new("fp3_building_special_alamut_castle_01_a_mesh", "icon_structure_alamut_castle.dds",
            "An eyrie on a knife-edge ridge, reached by one path wide enough for a single man out of {0}.",
            ["The Eagle's Nest of {0}", "The Mountain Hold of {1}", "The Eyrie of {0}"]),
        // The three rings below are the only meshes in the catalogue with an empty middle: no
        // standing geometry inside radius 3.8-5.3, walls out to 8.4-10.2, measured off the .mesh
        // files. Vanilla stands each of them within 0.7 units of its holding's locator where every
        // other special building sits 5-25 away. Heian-kyo and Angkor are co-located in vanilla too,
        // but their middles are solid — a holding there would stand inside the temple — so they
        // keep the ordinary offset.
        new("fp3_building_special_ark_of_bukhara_mesh", "icon_structure_ark_of_bukhara.dds",
            "A whole quarter raised on an artificial mound behind sloping walls — court, treasury and garrison of {0} together.",
            ["The Raised Citadel of {0}", "The Citadel of {1}", "The Walled Mount of {0}"],
            Encloses: 9.4),
        new("fp3_building_special_falak_ol_aflak_citadel_01_a_mesh", "icon_structure_falak_ol_aflak_citadel.dds",
            "A brick citadel of many towers on the rock above {0}, never yet carried by storm.",
            ["The Twelve Towers of {0}", "The Sky Citadel of {1}", "The High Citadel of {0}"]),
        new("fp2_building_special_toledo_city_walls_01_a_mesh", "toledo.dds",
            "A full circuit of curtain wall and barbican gates enclosing every roof in {0}.",
            ["The Great Walls of {0}", "The Ringwall of {1}", "The Gated Walls of {0}"],
            Encloses: 10.2, ReplacesWalls: true),
        new("fp2_building_special_roman_wall_of_lugo_01_a_mesh", "lugo_walls.dds",
            "An unbroken ancient circuit of bastioned wall, older than the walk along its top in {0}.",
            ["The Old Walls of {0}", "The Ancient Circuit of {1}", "The Bastioned Walls of {0}"],
            Encloses: 8.4, ReplacesWalls: true),
        new("fp2_building_special_alcazar_de_segovia_01_a_mesh", "alcazar_segovia.dds",
            "A castle on a spur of rock, its prow-shaped keep splitting the two rivers below {0}.",
            ["The Prow Fortress of {0}", "The Cliffside Castle of {1}", "The Stone Prow of {0}"]),
        new("building_special_citadel_linan_01_mesh", "icon_structure_citadel_linan.dds",
            "An inner city of rammed earth and gate-towers, holding the granaries and the arsenal of {0}.",
            ["The Imperial Citadel of {0}", "The Great Bastion of {1}", "The Inner City of {0}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_thang_long_palace_mesh"),
        new("fp4_legendary_western_watchtower_01_a_mesh", "icon_building_legendary_watchtower.dds",
            "A watchtower on the frontier ridge, the first place in {0} to know an army is coming.",
            ["The Great Watchtower of {0}", "The Warden's Tower of {1}", "The Beacon Tower of {0}"]),

        // ---- Added 2026-09-27 from the unused-mesh survey. ----

        new("ep3_patras_castle_01_a_mesh", "icon_structure_patras_castle.dds",
            "A castle on the height above the harbour, rebuilt and thickened by every lord who has held {0}.",
            ["The High Castle of {0}", "The Old Fortress of {1}", "The Upper Castle of {0}"]),
        new("ep3_cilician_gates_mesh", "icon_structure_cilician_gates.dds",
            "A narrow pass cut through the range and walled at its throat, the only road into {0} an army can take.",
            ["The Iron Gates of {0}", "The Gates of {1}", "The Walled Pass of {0}"],
            Needs: WonderSite.Relief),
        // Vanilla draws it only as a map object on the Great Wall; alone it is a gatehouse that
        // shuts a road, which needs high ground either side to mean anything.
        new("tgp_great_wall_gate_01_mesh", "icon_structure_the_great_wall.dds",
            "A double gate-tower of brick across the road, shutting the way into {0} at the sound of a horn.",
            ["The Great Gate of {0}", "The Gate Tower of {1}", "The Frontier Gate of {0}"],
            Needs: WonderSite.Relief),
    ];

    private static readonly WonderAsset[] ImperialPalace =
    [
        new("building_special_palace_of_aachen_mesh", "icon_structure_palace_of_achen.dds",
            "A palatine hall and shrine under one roof, where the rulers of {0} are crowned and hold court.",
            ["The Palatine Seat of {0}", "The Great Palace of {1}", "The Crowning Hall of {0}"]),
        new("fp2_building_special_alhambra_01_mesh", "icon_structure_alhambra.dds",
            "Courts of red stone opening onto water gardens and honeycombed vaults above {0}.",
            ["The Red Palace of {0}", "The Garden Palace of {1}", "The Vermilion Court of {0}"]),
        new("fp2_building_special_aljaferia_mesh", "aljaferia.dds",
            "A pleasure palace of interlaced arches and orange courts, built for no purpose but delight in {0}.",
            ["The Pleasure Palace of {0}", "The Ivory Court of {1}", "The Joyous Palace of {0}"]),
        new("fp3_building_special_palace_of_ctesiphon_01_a_mesh", "icon_structure_palace_of_ctesiphon.dds",
            "A single brick vault wider than any built since, throwing its shadow across the audience floor of {0}.",
            ["The Great Arch of {0}", "The Vaulted Palace of {1}", "The Great Vault of {0}"]),
        new("tgp_building_special_heian_kyo_mesh", "icon_structure_heian_palace.dds",
            "A walled compound of vermilion galleries and gravel courts where the court of {0} lives out of sight.",
            ["The Imperial Court of {0}", "The Cloistered Palace of {1}", "The Vermilion Palace of {0}"]),
        new("tgp_building_special_thang_long_palace_mesh", "icon_structure_citadel_thang_long.dds",
            "A royal citadel of tiered gates and dragon stairs at the heart of {0}.",
            ["The Dragon Court of {0}", "The Royal Citadel of {1}", "The Ascendant Palace of {0}"]),
        new("building_special_wilwatikta_palace_01_mesh", "icon_structure_wilwatikta_palace.dds",
            "Terraces of red brick, split gates and bathing pools laid out as a model of the order of {0}.",
            ["The Brick Palace of {0}", "The Terraced Palace of {1}", "The Court of {1}"],
            Dlc: EastAsianWonders, Fallback: "tgp_building_special_thang_long_palace_mesh"),
        new("fp4_legendary_western_palace_01_a_mesh", "icon_building_legendary_palace.dds",
            "A palace built to be seen from the road, so that no visitor mistakes the standing of {0}.",
            ["The Golden Palace of {0}", "The High Seat of {1}", "The Great Palace of {0}"]),
        new("fp4_legendary_islamic_palace_01_a_mesh", "icon_building_legendary_palace.dds",
            "Arcaded courts, fountains and shaded galleries, the summer seat of the rulers of {0}.",
            ["The Fountained Palace of {0}", "The Summer Court of {1}", "The Great Palace of {0}"]),
        new("fp4_legendary_india_palace_01_a_mesh", "icon_building_legendary_palace.dds",
            "A palace of carved balconies and lattice screens stepped up the slope above {0}.",
            ["The Carved Palace of {0}", "The Lattice Court of {1}", "The High Palace of {0}"]),
        new("fp4_legendary_building_norse_meadhall_01_a_mesh", "icon_building_longhouses.dds",
            "A hall long enough to seat every sworn man in {0}, its roof-tree black with hearthsmoke.",
            ["The Great Meadhall of {0}", "The Golden Hall of {1}", "The Long Hall of {0}"]),
        new("building_special_pyramids_giza_mesh", "icon_structure_the_pyramids.dds",
            "Three faced pyramids on the desert edge, raised as tombs by rulers of {0} whose names are half lost.",
            ["The Pyramids of {0}", "The Great Tombs of {1}", "The Royal Pyramids of {0}"]),
        new("fp3_building_special_tomb_of_cyrus_01_a_mesh", "icon_structure_tomb_of_cyrus.dds",
            "A plain stone chamber on six receding steps, the grave of the founder of {0}.",
            ["The Great Tomb of {0}", "The Stone Sepulchre of {1}", "The Founder's Tomb of {0}"]),
        new("building_special_colosseum_mesh", "icon_structure_colosseum.dds",
            "A tiered amphitheatre seating a crowd larger than most towns, the great spectacle of {0}.",
            ["The Great Arena of {0}", "The Amphitheatre of {1}", "The Grand Circus of {0}"]),
        new("fp4_legendary_mediterranean_monument_01_a_mesh", "icon_building_legendary_statue.dds",
            "A victory monument on a stepped plinth, raised where the fate of {0} was settled.",
            ["The Great Monument of {0}", "The Column of {1}", "The Victory Monument of {0}"]),
        new("fp4_legendary_western_hero_01_mesh", "icon_building_legendary_statue.dds",
            "An outsized statue of a founder whose deeds are recited to every child in {0}.",
            ["The Hero's Monument of {0}", "The Great Statue of {1}", "The Founder's Image of {0}"]),
        new("fp4_legendary_heroes_pillar_india_01_a_mesh", "icon_building_legendary_statue.dds",
            "A free-standing pillar cut with the victories of {0}, unrusted after centuries in the open.",
            ["The Pillar of Heroes of {0}", "The Victory Pillar of {1}", "The Standing Pillar of {0}"]),

        // ---- Added 2026-09-27 from the unused-mesh survey. ----

        new("ep3_despots_palace_mesh", "icon_structure_despot_palace.dds",
            "Halls stepped up the hillside above {0}, where the ruler's council sits under painted ceilings.",
            ["The Hillside Palace of {0}", "The Council Palace of {1}", "The Stepped Palace of {0}"]),
        // The All Under Heaven mandala capital's five stages; first, middle and last are the rungs.
        new("tgp_great_building_mandala_capital_01_mesh", "tgp_icon_building_mandala_capital_tier_05.dds",
            "A temple-palace inside rings of walls and tanks, from which the ruler of {0} claims the homage of every lesser court.",
            ["The Temple-Palace of {0}", "The Radiant Court of {1}", "The Ringed Capital of {0}"],
            Ladder: ["tgp_great_building_mandala_capital_01_mesh", "tgp_great_building_mandala_capital_03_mesh",
                     "tgp_great_building_mandala_capital_05_mesh"]),
        new("building_special_goguryeo_tomb_01_entity", "icon_structure_goguryeo_tomb.dds",
            "A stepped pyramid of cut granite over the grave of a conqueror of {0}, a brazier still lit at its door.",
            ["The Stepped Tomb of {0}", "The Granite Tomb of {1}", "The Conqueror's Tomb of {0}"],
            IsEntity: true, Dlc: EastAsianWonders, Fallback: "fp3_building_special_tomb_of_cyrus_01_a_mesh"),
        // A pure ground decal, no height at all: vanilla's own drawing of a moated keyhole mound.
        new("tgp_kofun_decal_mesh", "icon_structure_kofun.dds",
            "A keyhole-shaped burial mound ringed by moats, the resting place of an early ruler of {0}.",
            ["The Keyhole Mound of {0}", "The Royal Barrow of {1}", "The Moated Tomb of {0}"]),
        new("building_special_iron_lion_cangzhou_01_entity", "icon_structure_iron_lion_cangzhou.dds",
            "A lion of cast iron taller than a house, raised by the rulers of {0} to stand guard over the land.",
            ["The Iron Lion of {0}", "The Great Lion of {1}", "The Iron Guardian of {0}"],
            IsEntity: true, Dlc: EastAsianWonders, Fallback: "fp4_legendary_western_hero_01_mesh"),
        // Modelled for the Legends set and never placed by vanilla at all. The single stone is the
        // first rung; the fuller setting is the finished one.
        new("fp4_legendary_norse_runestone_01_b_mesh", "icon_building_legendary_statue.dds",
            "Standing stones carved with the deeds of the founders of {0}, one raised for every ruler worth remembering.",
            ["The Rune Stones of {0}", "The Carved Stones of {1}", "The Founders' Stones of {0}"],
            Ladder: ["fp4_legendary_norse_runestone_01_b_mesh", "fp4_legendary_norse_runestone_01_a_mesh",
                     "fp4_legendary_norse_runestone_01_a_mesh"]),
        new("fp4_legendary_hunting_lodge_01_a_mesh", "icon_building_legendary_hunting_grounds.dds",
            "A timber lodge on the edge of the royal forest, where the rulers of {0} hunt and hold their summer court.",
            ["The Royal Lodge of {0}", "The Hunting Lodge of {1}", "The Forest Court of {0}"]),
        new("fp4_legendary_desert_hunting_lodge_01_a_mesh", "icon_building_legendary_hunting_grounds.dds",
            "A pavilion at a desert spring where the rulers of {0} fly their falcons and receive envoys.",
            ["The Falconers' Pavilion of {0}", "The Desert Lodge of {1}", "The Spring Pavilion of {0}"],
            Needs: WonderSite.Arid),
    ];

    // Sacred peaks, and the holy places that cling to high ground — the painted hills, the volcano,
    // the monasteries on rock pillars and cliff faces. Kept apart because they are only honest on a
    // county that actually has the relief for them — a mountain mesh planted on floodplains reads as
    // a bug, not a wonder.
    private static readonly WonderAsset[] SacredPeaks =
    [
        new("fp3_building_special_mount_damavand_01_a_mesh", "icon_structure_mount_damavand.dds",
            "A snow-capped cone standing alone above the range, bound up with every old story told in {0}.",
            ["The Sacred Mount of {0}", "The Great Peak of {1}", "The Cloudpiercer of {0}"]),
        new("tgp_building_special_mt_fuji_mesh", "icon_structure_mount_apo.dds",
            "A symmetrical white peak visible for days' travel in every direction from {0}.",
            ["The Sacred Peak of {0}", "The Holy Mountain of {1}", "The White Mountain of {0}"]),
        new("mpo_building_special_burkhan_khaldun_mesh", "icon_structure_burkhan_khaldun.dds",
            "A forested holy mountain where the ancestors of {0} are said to be buried and no axe is permitted.",
            ["The Holy Mountain of {0}", "The Sacred Heights of {1}", "The Ancestral Peak of {0}"]),
        new("tgp_building_special_wudang_mountains_mesh", "icon_structure_wudang_mountain_temples.dds",
            "Monasteries pinned to a chain of peaks above the cloud line, the retreat of the ascetics of {0}.",
            ["The Mountain Temples of {0}", "The Cloud Monasteries of {1}", "The Peak Shrines of {0}"]),

        // ---- Added 2026-09-27 from the unused-mesh survey. ----

        new("building_special_gunung_api_01_mesh", "icon_structure_gunung_api.dds",
            "A smoking cone rising alone from the land, whose fires {0} feeds with offerings so that they stay below.",
            ["The Fire Mountain of {0}", "The Burning Peak of {1}", "The Smoking Mount of {0}"],
            Dlc: EastAsianWonders, Fallback: "fp3_building_special_mount_damavand_01_a_mesh"),
        new("fp3_building_special_rainbow_mountains_01_a_mesh", "icon_structure_ala_daghlar_mountains.dds",
            "Hills striped red, ochre and green like woven cloth, said in {0} to be the work of the gods' own dyers.",
            ["The Painted Hills of {0}", "The Striped Mountains of {1}", "The Rainbow Heights of {0}"],
            Needs: WonderSite.Arid),
        new("building_special_chocolate_hills_01_mesh", "icon_structure_chocolate_hills.dds",
            "Hundreds of rounded hills as alike as haycocks, which {0} holds to be the barrows of giants.",
            ["The Thousand Hills of {0}", "The Giants' Barrows of {1}", "The Haycock Hills of {0}"],
            Dlc: EastAsianWonders, Fallback: "fp3_building_special_rainbow_mountains_01_a_mesh"),
        new("ep3_fairy_chimneys_01_a_mesh", "icon_structure_fairy_chimneys.dds",
            "Cones of soft rock hollowed into shrines and cells, where the hermits of {0} live inside the hills.",
            ["The Hollow Hills of {0}", "The Rock Chimneys of {1}", "The Cave Shrines of {0}"]),
        new("ep3_meteora_01_mesh", "icon_structure_meteora.dds",
            "Monasteries set on the tops of sheer stone pillars, reached from {0} only by rope and net.",
            ["The Hanging Monasteries of {0}", "The Stone Pillars of {1}", "The Monasteries in the Air of {0}"],
            Ladder: ["ep3_meteora_01_mesh", "ep3_meteora_01_mesh", "ep3_meteora_02_mesh"]),
        new("ep3_sumela_monastery_01_a_mesh", "icon_structure_sumela_monastery.dds",
            "A monastery built into a cliff face above a forested gorge, its one door reached by a long stair from {0}.",
            ["The Cliff Monastery of {0}", "The Monastery of the Rock of {1}", "The Gorge Monastery of {0}"],
            Ladder: ["ep3_sumela_monastery_01_a_mesh", "ep3_sumela_monastery_01_a_mesh",
                     "ep3_sumela_monastery_01_b_mesh"]),
        // Vanilla's own ladder draws 01_a for its first two levels and 01_b for the third.
        new("ep3_athos_monasteries_01_a_mesh", "icon_structure_mount_athos.dds",
            "Fortified monasteries on the slopes of a holy mountain, where nobody from {0} may live but monks.",
            ["The Monastic Mountain of {0}", "The Monasteries of {1}", "The Mountain of Monks of {0}"],
            Ladder: ["ep3_athos_monasteries_01_a_mesh", "ep3_athos_monasteries_01_a_mesh",
                     "ep3_athos_monasteries_01_b_mesh"]),
        new("ep3_jvari_monastery_01_a_mesh", "icon_structure_jvari_monastery.dds",
            "A small domed temple alone on a hilltop over the meeting of two rivers, seen from everywhere in {0}.",
            ["The Hilltop Temple of {0}", "The Lone Dome of {1}", "The Watching Temple of {0}"]),
    ];

    /// <summary>
    /// Which tradition each model was built in, keyed by <see cref="WonderAsset.Mesh"/>. A table
    /// rather than a field on every entry so the whole judgement can be read in one place; a model
    /// missing from it counts as <see cref="WonderStyle.Any"/>.
    /// </summary>
    private static readonly Dictionary<string, WonderStyle> StyleOf = new(StringComparer.Ordinal)
    {
        // Latin Christendom and its castles.
        ["building_special_cathedral_generic_mesh"] = WonderStyle.Western,
        ["building_special_notre_dame_mesh"] = WonderStyle.Western,
        ["ep2_building_special_canterbury_01_mesh"] = WonderStyle.Western,
        ["cp6_building_special_grand_cathedral_mesh"] = WonderStyle.Western,
        ["cp6_building_special_st_peters_basilica_mesh"] = WonderStyle.Western | WonderStyle.Mediterranean,
        ["cp6_building_special_mont_st_michel_01_mesh"] = WonderStyle.Western,
        ["fp4_legendary_building_christian_shrine_01_mesh"] = WonderStyle.Western,
        ["building_special_tower_of_london_mesh"] = WonderStyle.Western,
        ["building_special_trosky_castle_01_mesh"] = WonderStyle.Western,
        ["building_special_palace_of_aachen_mesh"] = WonderStyle.Western,
        ["fp4_legendary_western_palace_01_a_mesh"] = WonderStyle.Western,
        ["fp4_legendary_western_hero_01_mesh"] = WonderStyle.Western,
        ["fp4_legendary_western_watchtower_01_a_mesh"] = WonderStyle.Western,
        ["fp4_legendary_hunting_lodge_01_a_mesh"] = WonderStyle.Western | WonderStyle.Norse,
        ["building_special_stonehenge_mesh"] = WonderStyle.Western | WonderStyle.Norse,

        // The north.
        ["building_special_cathedral_pagan_mesh"] = WonderStyle.Norse,
        ["fp4_legendary_building_norse_shrine_01_a_mesh"] = WonderStyle.Norse,
        ["fp4_legendary_building_norse_meadhall_01_a_mesh"] = WonderStyle.Norse,
        ["fp4_legendary_norse_runestone_01_b_mesh"] = WonderStyle.Norse,

        // Rome, Greece, Byzantium, Iberia and the Caucasus.
        ["building_special_hagia_sophia_mesh"] = WonderStyle.Mediterranean,
        ["building_special_hagia_sophia_minarets_mesh"] = WonderStyle.Mediterranean | WonderStyle.Mena,
        ["ep3_monument_parthenon_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_monument_parthenon_01_b_mesh"] = WonderStyle.Mediterranean,
        ["building_special_colosseum_mesh"] = WonderStyle.Mediterranean,
        ["fp4_legendary_mediterranean_monument_01_a_mesh"] = WonderStyle.Mediterranean,
        ["fp2_building_special_basilica_santiago_mesh"] = WonderStyle.Mediterranean | WonderStyle.Western,
        ["fp2_building_special_tower_of_hercules_mesh"] = WonderStyle.Mediterranean,
        ["fp2_building_special_roman_wall_of_lugo_01_a_mesh"] = WonderStyle.Mediterranean,
        ["fp2_building_special_toledo_city_walls_01_a_mesh"] = WonderStyle.Mediterranean,
        ["fp2_building_special_alcazar_de_segovia_01_a_mesh"] = WonderStyle.Mediterranean | WonderStyle.Western,
        ["ep3_basilica_sant_apollinare_nuovo_mesh"] = WonderStyle.Mediterranean,
        ["ep3_cattolica_di_stilo_mesh"] = WonderStyle.Mediterranean,
        ["ep3_church_saint_lazarus_mesh"] = WonderStyle.Mediterranean,
        ["ep3_church_saint_sophia_ohrid_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_hagios_demetrios_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_etchmiadzin_cathedral_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_jvari_monastery_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_meteora_01_mesh"] = WonderStyle.Mediterranean,
        ["ep3_sumela_monastery_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_athos_monasteries_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_fairy_chimneys_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_patras_castle_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_despots_palace_mesh"] = WonderStyle.Mediterranean,
        ["ep3_maidens_tower_01_a_mesh"] = WonderStyle.Mediterranean,
        ["ep3_cilician_gates_mesh"] = WonderStyle.Mediterranean | WonderStyle.Iranian,
        ["ep3_saint_catherine_monastery_mesh"] = WonderStyle.Mediterranean | WonderStyle.Mena,

        // Arabia, the Maghreb and al-Andalus.
        ["monument_mezquita_de_cordoba_mesh"] = WonderStyle.Mena | WonderStyle.Mediterranean,
        ["fp2_building_special_alhambra_01_mesh"] = WonderStyle.Mena | WonderStyle.Mediterranean,
        ["fp2_building_special_aljaferia_mesh"] = WonderStyle.Mena | WonderStyle.Mediterranean,
        ["building_special_great_mosque_of_mecca_mesh"] = WonderStyle.Mena,
        ["fp3_building_special_great_mosque_of_samarra_01_a_mesh"] = WonderStyle.Mena,
        ["fp3_building_special_house_of_wisdom_01_a_mesh"] = WonderStyle.Mena | WonderStyle.Iranian,
        ["fp4_legendary_islamic_shrine_01_a_mesh"] = WonderStyle.Mena,
        ["fp4_legendary_islamic_palace_01_a_mesh"] = WonderStyle.Mena,
        ["fp4_legendary_desert_hunting_lodge_01_a_mesh"] = WonderStyle.Mena,
        ["building_special_petra_mesh"] = WonderStyle.Mena,
        ["building_special_pyramids_giza_mesh"] = WonderStyle.Mena | WonderStyle.African,

        // Persia and the Iranian plateau.
        ["fp3_building_special_imam_reza_shrine_01_a_mesh"] = WonderStyle.Iranian,
        ["fp3_building_special_soltaniyeh_01_a_mesh"] = WonderStyle.Iranian,
        ["fp3_building_special_minaret_and_remains_of_jam_01_a_mesh"] = WonderStyle.Iranian,
        ["cp6_building_special_yazd_mosque_mesh"] = WonderStyle.Iranian,
        ["fp3_building_special_palace_of_ctesiphon_01_a_mesh"] = WonderStyle.Iranian | WonderStyle.Mena,
        ["fp3_building_special_tomb_of_cyrus_01_a_mesh"] = WonderStyle.Iranian,
        ["fp3_building_special_alamut_castle_01_a_mesh"] = WonderStyle.Iranian,
        ["fp3_building_special_ark_of_bukhara_mesh"] = WonderStyle.Iranian | WonderStyle.Steppe,
        ["fp3_building_special_falak_ol_aflak_citadel_01_a_mesh"] = WonderStyle.Iranian,
        ["fp3_building_special_maharloo_lake_01_a_mesh"] = WonderStyle.Iranian,
        ["fp3_building_special_mount_damavand_01_a_mesh"] = WonderStyle.Iranian,
        ["fp3_building_special_rainbow_mountains_01_a_mesh"] = WonderStyle.Iranian,

        // Africa south of the Sahara.
        ["building_special_great_mosque_of_djenne_mesh"] = WonderStyle.African,
        ["cp6_building_special_sankore_university_mesh"] = WonderStyle.African,

        // India and Tibet.
        ["building_special_brihadeeswarar_temple_mesh"] = WonderStyle.Indian,
        ["fp4_legendary_dharmic_shrine_01_a_mesh"] = WonderStyle.Indian,
        ["fp4_legendary_india_palace_01_a_mesh"] = WonderStyle.Indian,
        ["fp4_legendary_heroes_pillar_india_01_a_mesh"] = WonderStyle.Indian,
        ["cp6_building_special_qutb_minar_01_mesh"] = WonderStyle.Indian | WonderStyle.Iranian,
        ["cp6_building_special_boudhanath_mesh"] = WonderStyle.Indian,
        ["cp6_building_special_sanchi_stupa_mesh"] = WonderStyle.Indian,

        // The islands and the mainland south of China.
        ["tgp_building_special_borudur_mesh"] = WonderStyle.SoutheastAsian,
        ["tgp_building_special_angkorwat_mesh"] = WonderStyle.SoutheastAsian,
        ["building_special_pyramid_lingapura_01_mesh"] = WonderStyle.SoutheastAsian,
        ["building_special_my_son_sanctuary_01_mesh"] = WonderStyle.SoutheastAsian,
        ["tgp_building_special_po_klong_temple_mesh"] = WonderStyle.SoutheastAsian,
        ["building_special_muara_takus_01_mesh"] = WonderStyle.SoutheastAsian,
        ["building_special_wilwatikta_palace_01_mesh"] = WonderStyle.SoutheastAsian,
        ["building_special_suwalesi_megaliths_01_mesh"] = WonderStyle.SoutheastAsian,
        ["tgp_great_building_mandala_capital_01_mesh"] = WonderStyle.SoutheastAsian,
        ["building_special_thuriang_kilns_01_entity"] = WonderStyle.SoutheastAsian,
        ["building_special_ha_long_bay_01_mesh"] = WonderStyle.SoutheastAsian | WonderStyle.EastAsian,
        ["building_special_gunung_api_01_mesh"] = WonderStyle.SoutheastAsian,
        ["building_special_chocolate_hills_01_mesh"] = WonderStyle.SoutheastAsian,
        ["tgp_building_special_thang_long_palace_mesh"] = WonderStyle.SoutheastAsian | WonderStyle.EastAsian,

        // China, Korea and Japan.
        ["tgp_building_special_leshan_buddha_mesh"] = WonderStyle.EastAsian,
        ["building_special_maijishan_grottoes_01_mesh"] = WonderStyle.EastAsian,
        ["tgp_building_special_itsukushima_mesh"] = WonderStyle.EastAsian,
        ["building_special_izumo_taisha_01_mesh"] = WonderStyle.EastAsian,
        ["tgp_building_special_hwangnyongsa_mesh"] = WonderStyle.EastAsian,
        ["building_special_three_pagodas_dali_01_mesh"] = WonderStyle.EastAsian,
        ["building_special_stone_pagoda_01_mesh"] = WonderStyle.EastAsian,
        ["building_special_buddha_kamakura_01_entity"] = WonderStyle.EastAsian,
        ["building_special_fanfang_guangzhou_01_entity"] = WonderStyle.EastAsian,
        ["building_special_yuelu_academy_01_mesh"] = WonderStyle.EastAsian,
        ["building_special_confucius_temple_01_mesh"] = WonderStyle.EastAsian,
        ["building_special_dengfeng_observatory_01_mesh"] = WonderStyle.EastAsian,
        ["building_special_citadel_linan_01_mesh"] = WonderStyle.EastAsian,
        ["tgp_great_wall_gate_01_mesh"] = WonderStyle.EastAsian,
        ["tgp_building_special_heian_kyo_mesh"] = WonderStyle.EastAsian,
        ["building_special_goguryeo_tomb_01_entity"] = WonderStyle.EastAsian,
        ["tgp_kofun_decal_mesh"] = WonderStyle.EastAsian,
        ["building_special_iron_lion_cangzhou_01_entity"] = WonderStyle.EastAsian,
        ["tgp_building_special_mt_fuji_mesh"] = WonderStyle.EastAsian,
        ["tgp_building_special_wudang_mountains_mesh"] = WonderStyle.EastAsian,

        // The steppe.
        ["fp4_legendary_steppe_shrine_01_a_mesh"] = WonderStyle.Steppe,
        ["mpo_building_special_burkhan_khaldun_mesh"] = WonderStyle.Steppe,

        // Left as Any: the mines and the Rock — ore and a headland belong to nobody's architecture.
    };

    /// <summary>What each of vanilla's building_gfx tokens is drawn in, as a <see cref="WonderStyle"/>.</summary>
    private static readonly Dictionary<string, WonderStyle> StyleOfGfx = new(StringComparer.Ordinal)
    {
        ["western_building_gfx"] = WonderStyle.Western,
        ["east_slavic_building_gfx"] = WonderStyle.Western,
        ["norse_building_gfx"] = WonderStyle.Norse,
        ["mediterranean_building_gfx"] = WonderStyle.Mediterranean,
        ["iberian_building_gfx"] = WonderStyle.Mediterranean,
        ["byzantine_building_gfx"] = WonderStyle.Mediterranean,
        ["caucasian_building_gfx"] = WonderStyle.Mediterranean,
        ["mena_building_gfx"] = WonderStyle.Mena,
        ["arabic_group_building_gfx"] = WonderStyle.Mena,
        ["berber_group_building_gfx"] = WonderStyle.Mena,
        ["iranian_building_gfx"] = WonderStyle.Iranian,
        ["african_building_gfx"] = WonderStyle.African,
        ["indian_building_gfx"] = WonderStyle.Indian,
        ["tibetan_building_gfx"] = WonderStyle.Indian,
        ["southeast_asian_building_gfx"] = WonderStyle.SoutheastAsian,
        ["chinese_building_gfx"] = WonderStyle.EastAsian,
        ["japanese_building_gfx"] = WonderStyle.EastAsian,
        ["emishi_building_gfx"] = WonderStyle.EastAsian,
        ["steppe_building_gfx"] = WonderStyle.Steppe,
        ["amuric_building_gfx"] = WonderStyle.Steppe,
    };

    /// <summary>The traditions each one borders — where a culture's second-best wonder comes from.</summary>
    private static WonderStyle Near(WonderStyle style)
    {
        var near = WonderStyle.Any;
        if (style.HasFlag(WonderStyle.Western)) near |= WonderStyle.Norse | WonderStyle.Mediterranean;
        if (style.HasFlag(WonderStyle.Norse)) near |= WonderStyle.Western;
        if (style.HasFlag(WonderStyle.Mediterranean)) near |= WonderStyle.Western | WonderStyle.Mena | WonderStyle.Iranian;
        if (style.HasFlag(WonderStyle.Mena)) near |= WonderStyle.Mediterranean | WonderStyle.Iranian | WonderStyle.African;
        if (style.HasFlag(WonderStyle.Iranian)) near |= WonderStyle.Mena | WonderStyle.Mediterranean | WonderStyle.Indian | WonderStyle.Steppe;
        if (style.HasFlag(WonderStyle.African)) near |= WonderStyle.Mena;
        if (style.HasFlag(WonderStyle.Indian)) near |= WonderStyle.Iranian | WonderStyle.SoutheastAsian;
        if (style.HasFlag(WonderStyle.SoutheastAsian)) near |= WonderStyle.Indian | WonderStyle.EastAsian;
        if (style.HasFlag(WonderStyle.EastAsian)) near |= WonderStyle.SoutheastAsian | WonderStyle.Steppe;
        if (style.HasFlag(WonderStyle.Steppe)) near |= WonderStyle.Iranian | WonderStyle.EastAsian;
        return near & ~style;
    }

    /// <summary>
    /// A culture's building_gfx read as the traditions it is drawn in: the first token that means
    /// something is the culture's own, and any later ones in its fallback chain are kin — the same
    /// order the engine walks the chain in.
    /// </summary>
    private static (WonderStyle Own, WonderStyle Kin) StyleOfCulture(string? buildingGfx)
    {
        var own = WonderStyle.Any;
        var kin = WonderStyle.Any;
        if (buildingGfx is null) return (own, kin);

        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(buildingGfx, @"[a-z_]+_building_gfx"))
        {
            if (!StyleOfGfx.TryGetValue(m.Value, out var style)) continue;
            if (own == WonderStyle.Any) own = style;
            else kin |= style;
        }
        return (own, (kin | Near(own)) & ~own);
    }

    /// <summary>
    /// How strongly a model suits a culture. A model of the culture's own tradition is twelve times
    /// as likely as a stranger's, a kin or neighbouring tradition's four times, and so is one that
    /// belongs to nobody. The stranger keeps a weight of one on purpose: a world centre is exactly
    /// where a foreign builder's great work is plausible, and a culture drawn in a tradition the
    /// pool barely covers must still get variety rather than the same two models every time.
    /// </summary>
    private static int Suits(WonderAsset asset, WonderStyle own, WonderStyle kin)
    {
        if (own == WonderStyle.Any) return 1;
        var style = StyleOf.GetValueOrDefault(asset.Mesh);
        if (style == WonderStyle.Any) return 4;
        if ((style & own) != 0) return 12;
        if ((style & kin) != 0) return 4;
        return 1;
    }

    /// <summary>
    /// Choose the model for one wonder.
    ///
    /// <paramref name="used"/> holds the looks already handed out this world (<see
    /// cref="WonderAsset.Look"/>), so that two centres on the same map do not both get the pyramids.
    /// With a default of five centres against pools this size that constraint is easy to satisfy; if
    /// a pool is ever exhausted the draw falls back to the models the site allows rather than
    /// failing. What the site allows is never relaxed — every pool keeps models that need nothing.
    ///
    /// Among what is left, the county's culture leans the draw toward its own building tradition
    /// (<see cref="Suits"/>), so a western people's great work is most often a cathedral or a keep
    /// and a steppe people's a cairn or a holy mountain. <paramref name="buildingGfx"/> is the
    /// culture's building_gfx as written — one token or a fallback chain.
    /// </summary>
    public static WonderAsset Pick(WonderArchetype archetype, WonderSite site, string? buildingGfx,
        Rng rng, HashSet<string> used)
    {
        var pool = archetype switch
        {
            // Sacred peaks are Sanctuaries mechanically — the piety and pilgrimage modifiers are
            // exactly right for them — but they are only offered where there is a mountain to be.
            WonderArchetype.Sanctuary => site.HasFlag(WonderSite.Relief) ? [.. Sanctuary, .. SacredPeaks] : Sanctuary,
            WonderArchetype.GreatHarbor => GreatHarbor,
            WonderArchetype.GreatLibrary => GreatLibrary,
            WonderArchetype.Citadel => Citadel,
            _ => ImperialPalace
        };

        IReadOnlyList<WonderAsset> fitting = pool.Where(a => a.Fits(site)).ToList();
        IReadOnlyList<WonderAsset> free = fitting.Where(a => !Looks(a).Any(used.Contains)).ToList();
        var candidates = free.Count > 0 ? free : fitting;

        var (own, kin) = StyleOfCulture(buildingGfx);
        int index = rng.WeightedIndex(candidates, a => Suits(a, own, kin));
        var asset = candidates[index < 0 ? 0 : index];
        foreach (string look in Looks(asset)) used.Add(look);
        return asset;
    }

    /// <summary>
    /// Every look a wonder can show a player: its own, and — for a DLC model — the look of its
    /// fallback, which is what a player without the pack sees. Without the second, Boudhanath and
    /// Borobudur could share a map and a player without Holy Buildings would see two Borobudurs. The
    /// fallback is resolved to the look of the entry that draws it, so St Peter's (falling back to
    /// Hagia Sophia's dome) keeps both Hagia Sophias off the map, as their shared family already does.
    /// </summary>
    private static IEnumerable<string> Looks(WonderAsset asset)
    {
        yield return asset.Look;
        if (asset.Fallback is { } fallback)
            yield return LookOfMesh.Value.GetValueOrDefault(fallback, fallback);
    }

    /// <summary>Every model any entry draws, on any rung, to that entry's look. Lazy because the
    /// pools above are static fields and must be initialised first.</summary>
    private static readonly Lazy<Dictionary<string, string>> LookOfMesh = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in (WonderAsset[])[.. Sanctuary, .. SacredPeaks, .. GreatHarbor, .. GreatLibrary,
                                              .. Citadel, .. ImperialPalace])
            foreach (string mesh in asset.Ladder ?? [asset.Mesh])
                map.TryAdd(mesh, asset.Look);
        return map;
    });
}
