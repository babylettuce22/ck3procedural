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
public sealed record WonderAsset(string Mesh, string Icon, string Blurb, string[] Names);

/// <summary>
/// Per-archetype pools of <see cref="WonderAsset"/>.
///
/// Every mesh here is reachable without owning any DLC. That is not the same as "is in the base
/// game": the DLC folders under game/dlc ship only gfx, music and sound, and *all* the building
/// meshes — fp2, fp3, ep2, ep3, tgp, fp4 alike — live in the base game/gfx/models/buildings tree.
/// What gates them is the `requires_dlc_flag` field on the asset block that references them, and
/// vanilla does not set it on any of these (the Alhambra and the whole legendary set are plain
/// unflagged assets). The pools were filtered against that field rather than against the filename
/// prefix, so a mesh that is only ever referenced from behind a flag is not in here.
///
/// Natural features are included only where the archetype's modifiers still make sense of them — a
/// sacred peak is a Sanctuary because pilgrims climb it, a karst bay is a GreatHarbor because ships
/// shelter in it. The ones that fit nothing (rainbow mountains, chocolate hills, a volcano) are left
/// out rather than mislabelled.
/// </summary>
public static class WonderAssets
{
    private static readonly WonderAsset[] Sanctuary =
    [
        new("building_special_cathedral_generic_mesh", "icon_structure_cologne_cathedral.dds",
            "A cathedral raised over generations, its nave tall enough to swallow the rooftops of {0}.",
            ["The Great Cathedral of {0}", "The Cathedral of {1}", "The High Minster of {0}"]),
        new("building_special_cathedral_pagan_mesh", "icon_structure_cathedral_pagan.dds",
            "A vast timber-and-stone sanctuary of the old rites, standing where {0} has always made its offerings.",
            ["The Grand Temple of {1}", "The Elder Sanctuary of {0}", "The Great Hallows of {1}"]),
        new("building_special_hagia_sophia_mesh", "icon_structure_hagia_sophia.dds",
            "An impossible dome floating on a ring of windows, the largest enclosed space anyone in {0} has stood beneath.",
            ["The Great Dome of {0}", "The Basilica of Holy {1}", "The Domed Sanctuary of {0}"]),
        new("building_special_hagia_sophia_minarets_mesh", "icon_structure_holy_wisdom.dds",
            "A colossal domed sanctuary ringed by slender towers, rededicated by every faith that has held {0}.",
            ["The Great Dome of {0}", "The Crowned Sanctuary of {1}", "The Many-Towered Dome of {0}"]),
        new("building_special_notre_dame_mesh", "icon_structure_notre_dame.dds",
            "Flying buttresses and a forest of pinnacles carry the roof higher than any hall in {0}.",
            ["The Cathedral of {1}", "The Grand Minster of {0}", "The Spires of {0}"]),
        new("ep2_building_special_canterbury_01_mesh", "icon_structure_canterbury_cathedral.dds",
            "The mother church of the realm, where the high clergy of {0} are consecrated and buried.",
            ["The Metropolitan Cathedral of {0}", "The Primate's Seat of {1}", "The Mother Church of {0}"]),
        new("fp2_building_special_basilica_santiago_mesh", "compostela.dds",
            "The end of a pilgrim road walked by thousands, whose hostels and shrines feed half of {0}.",
            ["The Pilgrims' Basilica of {0}", "The Great Basilica of {1}", "The Wayfarers' Shrine of {0}"]),
        new("building_special_great_mosque_of_mecca_mesh", "icon_structure_great_mosque_of_mecca.dds",
            "A sacred precinct enclosing the holiest stone in {0}, circled day and night by the faithful.",
            ["The Grand Mosque of {0}", "The Sacred Precinct of {1}", "The Great Haram of {0}"]),
        new("building_special_great_mosque_of_djenne_mesh", "icon_structure_great_mosque_of_djenne.dds",
            "A mountain of sun-dried brick bristling with palm scaffolding, replastered each year by all of {0}.",
            ["The Earthen Mosque of {0}", "The Sunbaked Mosque of {1}", "The Great Mudbrick Mosque of {0}"]),
        new("fp3_building_special_great_mosque_of_samarra_01_a_mesh", "icon_structure_great_mosque_of_samarra.dds",
            "A minaret that climbs in a single outward spiral, visible from every road into {0}.",
            ["The Spiral Minaret of {0}", "The Great Mosque of {1}", "The Coiled Tower of {0}"]),
        new("monument_mezquita_de_cordoba_mesh", "mezquita_cordoba.dds",
            "A hall of striped double arches receding further than the eye can follow, the pride of {0}.",
            ["The Pillared Mosque of {0}", "The Forest of Arches of {1}", "The Great Mosque of {0}"]),
        new("fp3_building_special_imam_reza_shrine_01_a_mesh", "icon_structure_imam_reza_shrine.dds",
            "A shrine under a dome of beaten gold, drawing mourners and petitioners from far beyond {0}.",
            ["The Golden Shrine of {0}", "The Gilded Sanctuary of {1}", "The Radiant Shrine of {0}"]),
        new("fp3_building_special_soltaniyeh_01_a_mesh", "icon_structure_soltaniyeh.dds",
            "A turquoise dome on an octagon of brick, raised as a tomb grand enough to shame kings of {0}.",
            ["The Turquoise Dome of {0}", "The Great Mausoleum of {1}", "The Azure Tomb of {0}"]),
        new("fp3_building_special_minaret_and_remains_of_jam_01_a_mesh", "icon_structure_minaret_and_remains_of_jam.dds",
            "A single carved tower left standing in an empty valley, all that remains of a capital of {0}.",
            ["The Lonely Minaret of {0}", "The Tower of {1}", "The Last Tower of {0}"]),
        new("ep3_monument_parthenon_01_a_mesh", "icon_structure_parthenon.dds",
            "A marble temple on the height above {0}, its colonnade unchanged through every faith that has claimed it.",
            ["The Great Temple of {0}", "The Marble Temple of {1}", "The Columned Sanctuary of {0}"]),
        new("building_special_stonehenge_mesh", "icon_structure_stonehenge.dds",
            "A ring of dressed sarsens set by hands nobody in {0} can name, still keeping the turn of the year.",
            ["The Standing Stones of {0}", "The Great Henge of {1}", "The Stone Circle of {0}"]),
        new("building_special_suwalesi_megaliths_01_mesh", "icon_structure_suwalesi_megaliths.dds",
            "Carved monoliths scattered across the upland, older than any lineage ruling {0}.",
            ["The Megaliths of {0}", "The Ancient Monoliths of {1}", "The Elder Stones of {0}"]),
        new("building_special_brihadeeswarar_temple_mesh", "icon_structure_brihadeeswarar_temple.dds",
            "A tapering tower of carved granite whose capstone was hauled up a ramp miles long, the wonder of {0}.",
            ["The Great Vimana of {0}", "The Towering Temple of {1}", "The Stone Temple of {0}"]),
        new("tgp_building_special_borudur_mesh", "icon_structure_borobudur.dds",
            "A stepped mountain of stone galleries, walked in widening circles by the pilgrims of {0}.",
            ["The Stepped Stupa of {0}", "The Terraced Sanctuary of {1}", "The Great Stupa of {0}"]),
        new("tgp_building_special_angkorwat_mesh", "icon_structure_angkor_wat.dds",
            "A temple-city inside a moat wide enough to sail, the axis on which {0} was laid out.",
            ["The Moated Temple of {0}", "The Great Temple-City of {1}", "The Lotus Towers of {0}"]),
        new("building_special_pyramid_lingapura_01_mesh", "icon_structure_pyramid_lingapura.dds",
            "A stepped temple-mountain rising in sheer tiers from the plain of {0}.",
            ["The Step Pyramid of {0}", "The Temple-Mountain of {1}", "The Tiered Sanctuary of {0}"]),
        new("tgp_building_special_leshan_buddha_mesh", "icon_structure_leshan_giant_buddha.dds",
            "A seated colossus carved from the living cliff, its feet level with the boats of {0}.",
            ["The Colossal Buddha of {0}", "The Cliff Colossus of {1}", "The Great Carved Buddha of {0}"]),
        new("building_special_maijishan_grottoes_01_mesh", "icon_structure_maijishan_grottoes.dds",
            "Shrines cut into a sheer rock face and reached by stairways pinned to the cliff above {0}.",
            ["The Cliff Grottoes of {0}", "The Carved Caves of {1}", "The Grotto Shrines of {0}"]),
        new("tgp_building_special_itsukushima_mesh", "icon_structure_torii_gate.dds",
            "A shrine built out over the tideline, its great gate standing in open water at the flood of {0}.",
            ["The Floating Gate of {0}", "The Tidewater Shrine of {1}", "The Sea Gate of {0}"]),
        new("building_special_izumo_taisha_01_mesh", "icon_structure_izumo_taisha.dds",
            "A timber shrine on pillars taller than the trees, rebuilt unchanged for as long as {0} has records.",
            ["The Great Shrine of {0}", "The Timber Shrine of {1}", "The Elder Shrine of {0}"]),
        new("tgp_building_special_hwangnyongsa_mesh", "icon_structure_stone_pagoda.dds",
            "A nine-storey wooden pagoda raised so that every neighbour of {0} might see it and think better of war.",
            ["The Nine-Storey Pagoda of {0}", "The Great Pagoda of {1}", "The Watch of {0}"]),
        new("building_special_three_pagodas_dali_01_mesh", "icon_structure_three_pagodas_dali.dds",
            "Three white pagodas standing in line against the mountains, the sign of {0} on every map.",
            ["The Three Pagodas of {0}", "The White Pagodas of {1}", "The Triple Spires of {0}"]),
        new("building_special_my_son_sanctuary_01_mesh", "icon_structure_my_son_sanctuary.dds",
            "A valley of brick towers swallowed by jungle, where the old kings of {0} still receive offerings.",
            ["The Jungle Sanctuary of {0}", "The Brick Towers of {1}", "The Hidden Temples of {0}"]),
        new("fp4_legendary_building_norse_shrine_01_a_mesh", "icon_structure_temple_of_uppsala.dds",
            "A grove-shrine hung with offerings, where the great sacrifices of {0} are made.",
            ["The Great Grove of {0}", "The Hallowed Grove of {1}", "The Offering Place of {0}"]),
        new("fp4_legendary_dharmic_shrine_01_a_mesh", "icon_building_legendary_shrine.dds",
            "A shrine grown famous far past the borders of {0} for the miracles claimed there.",
            ["The Great Shrine of {0}", "The Blessed Shrine of {1}", "The Shrine of {1}"]),
        new("fp4_legendary_steppe_shrine_01_a_mesh", "icon_building_legendary_shrine.dds",
            "A cairn and standard on the open grass, the gathering place of every clan owing {0}.",
            ["The Sacred Cairn of {0}", "The Standing Shrine of {1}", "The Gathering Stone of {0}"]),
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
            ["The Karst Isles of {0}", "The Thousand Isles of {1}", "The Dragon Isles of {0}"]),
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
    ];

    private static readonly WonderAsset[] GreatLibrary =
    [
        new("fp3_building_special_house_of_wisdom_01_a_mesh", "icon_structure_grand_library_of_baghdad.dds",
            "A court of translators, astronomers and copyists kept at the expense of {0}.",
            ["The House of Wisdom of {0}", "The Grand Library of {1}", "The Hall of Learning of {0}"]),
        new("building_special_yuelu_academy_01_mesh", "icon_structure_yuelu_academy.dds",
            "Lecture courts and dormitories under old trees, where the examined men of {0} are made.",
            ["The Great Academy of {0}", "The Academy of {1}", "The Scholars' Halls of {0}"]),
        new("building_special_confucius_temple_01_mesh", "icon_structure_confucius_temple.dds",
            "A temple to the sages doubling as the examination hall of {0}, its stelae listing every graduate.",
            ["The Temple of Learning of {0}", "The Sages' Temple of {1}", "The Hall of Sages of {0}"]),
        new("building_special_dengfeng_observatory_01_mesh", "icon_structure_dengfeng_observatory.dds",
            "A gnomon tower and a stone sighting-scale, from which the calendar of {0} is corrected.",
            ["The Star Observatory of {0}", "The Astronomers' Tower of {1}", "The Skywatch of {0}"]),
        new("building_special_muara_takus_01_mesh", "icon_structure_muara_takus.dds",
            "A quiet brick precinct of stupas and cells where the manuscripts of {0} are copied and kept.",
            ["The Great Vihara of {0}", "The Scriptorium of {1}", "The Cloister of {0}"]),
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
        new("fp3_building_special_ark_of_bukhara_mesh", "icon_structure_ark_of_bukhara.dds",
            "A whole quarter raised on an artificial mound behind sloping walls — court, treasury and garrison of {0} together.",
            ["The Great Ark of {0}", "The Citadel of {1}", "The Walled Mount of {0}"]),
        new("fp3_building_special_falak_ol_aflak_citadel_01_a_mesh", "icon_structure_falak_ol_aflak_citadel.dds",
            "A brick citadel of many towers on the rock above {0}, never yet carried by storm.",
            ["The Twelve Towers of {0}", "The Sky Citadel of {1}", "The High Citadel of {0}"]),
        new("fp2_building_special_toledo_city_walls_01_a_mesh", "toledo.dds",
            "A full circuit of curtain wall and barbican gates enclosing every roof in {0}.",
            ["The Great Walls of {0}", "The Ringwall of {1}", "The Gated Walls of {0}"]),
        new("fp2_building_special_roman_wall_of_lugo_01_a_mesh", "lugo_walls.dds",
            "An unbroken ancient circuit of bastioned wall, older than the walk along its top in {0}.",
            ["The Old Walls of {0}", "The Ancient Circuit of {1}", "The Bastioned Walls of {0}"]),
        new("fp2_building_special_alcazar_de_segovia_01_a_mesh", "alcazar_segovia.dds",
            "A castle on a spur of rock, its prow-shaped keep splitting the two rivers below {0}.",
            ["The Prow Fortress of {0}", "The Cliffside Castle of {1}", "The Stone Prow of {0}"]),
        new("building_special_citadel_linan_01_mesh", "icon_structure_citadel_linan.dds",
            "An inner city of rammed earth and gate-towers, holding the granaries and the arsenal of {0}.",
            ["The Imperial Citadel of {0}", "The Great Bastion of {1}", "The Inner City of {0}"]),
        new("fp4_legendary_western_watchtower_01_a_mesh", "icon_building_legendary_watchtower.dds",
            "A watchtower on the frontier ridge, the first place in {0} to know an army is coming.",
            ["The Great Watchtower of {0}", "The Warden's Tower of {1}", "The Beacon Tower of {0}"]),
    ];

    private static readonly WonderAsset[] ImperialPalace =
    [
        new("building_special_palace_of_aachen_mesh", "icon_structure_palace_of_achen.dds",
            "A palatine hall and chapel under one roof, where the rulers of {0} are crowned and hold court.",
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
            ["The Brick Palace of {0}", "The Terraced Palace of {1}", "The Court of {1}"]),
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
    ];

    // Sacred peaks. Kept apart because they are only honest on a county that actually has the
    // relief for them — a mountain mesh planted on floodplains reads as a bug, not a wonder.
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
    ];

    /// <summary>
    /// Choose the model for one wonder.
    ///
    /// <paramref name="used"/> holds the meshes already handed out this world, so that two centres
    /// on the same map do not both get the pyramids. With a default of five centres against pools
    /// this size that constraint is easy to satisfy; if a pool is ever exhausted the draw falls back
    /// to the full pool rather than failing.
    /// </summary>
    public static WonderAsset Pick(WonderArchetype archetype, bool mountainous, Rng rng,
        HashSet<string> used)
    {
        var pool = archetype switch
        {
            // Sacred peaks are Sanctuaries mechanically — the piety and pilgrimage modifiers are
            // exactly right for them — but they are only offered where there is a mountain to be.
            WonderArchetype.Sanctuary => mountainous ? [.. Sanctuary, .. SacredPeaks] : Sanctuary,
            WonderArchetype.GreatHarbor => GreatHarbor,
            WonderArchetype.GreatLibrary => GreatLibrary,
            WonderArchetype.Citadel => Citadel,
            _ => ImperialPalace
        };

        IReadOnlyList<WonderAsset> free = pool.Where(a => !used.Contains(a.Mesh)).ToList();
        var asset = rng.Pick(free.Count > 0 ? free : pool);
        used.Add(asset.Mesh);
        return asset;
    }
}
