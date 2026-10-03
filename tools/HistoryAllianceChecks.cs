using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

// The fixture has access to the same rules as Tick; no alternate simulation or copied arithmetic.
public sealed partial class HistorySim
{
    internal static int VerifyAlliances(string? output)
    {
        string root = output ?? Path.Combine(Path.GetTempPath(), "ck3-alliance-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }

            HistorySim Fixture(int seed = 42)
            {
                var heritage = new Heritage { Key = "heritage_test", Name = "Test", Language = null!, Look = default! };
                var culture = new Culture
                {
                    Key = "test",
                    Name = "Test",
                    Heritage = heritage,
                    Tongue = null!,
                    MeanDevelopment = 5,
                    Color = (100, 100, 100),
                    Ethos = "ethos_communal",
                    MartialCustom = "martial_custom_male_only",
                    HeadDetermination = "heritage",
                    Traditions = [],
                    CoaGfx = "",
                    BuildingGfx = "",
                    ClothingGfx = "",
                    UnitGfx = "",
                    MaleNames = ["Adam", "Bert", "Carl"],
                    FemaleNames = ["Ada", "Bea"],
                    DynastyNames = ["Oak", "Elm", "Ash"],
                    PatronymSuffixMale = "",
                    PatronymSuffixFemale = "",
                    LocationPrefix = "",
                    AlwaysUsePatronym = false,
                };
                var counties = Enumerable.Range(0, 18).Select(i => new Title
                {
                    Tier = "c",
                    Index = i,
                    Key = $"c_test_{i}",
                    Name = $"County {i}",
                }).ToList();
                var polities = Enumerable.Range(0, 6).Select(i => new Polity
                {
                    Id = i,
                    Capital = counties[i * 3],
                    Culture = culture,
                    Founded = 800,
                    Peak = 3,
                }).ToList();
                foreach (var p in polities) p.Counties.UnionWith(counties.Skip(p.Id * 3).Take(3));
                var sim = new Formation.Sim
                {
                    Polities = polities,
                    Owner = polities.SelectMany(p => p.Counties.Select(c => (c, p))).ToDictionary(x => x.c, x => x.p),
                    Adjacent = counties.ToDictionary(c => c, c => counties.Where(n => n != c).ToHashSet()),
                    Development = counties.ToDictionary(c => c, _ => 5),
                    CountyCulture = counties.ToDictionary(c => c, _ => culture),
                    Events = [],
                    AvgKingdom = 12,
                    Reach = 4,
                    Aggression = 1,
                    Turbulence = 1,
                    NextId = 6,
                    Year = 900,
                    TickYears = 1,
                };
                var history = new HistorySim(sim, seed, 900);
                foreach (var p in polities)
                {
                    history._government[p.Id] = GovernmentMap.Feudal;
                    history._law[p.Id] = SuccessionLaw.Partition;
                    history._femaleShare[p.Id] = 0;
                    history.Seat(p, new SimRuler
                    {
                        Id = history._nextRuler++,
                        Name = $"Ruler {p.Id}",
                        Born = 870,
                        Female = false,
                        Crowned = 895,
                        House = new SimHouse { Id = history._nextHouse++, Name = $"House {p.Id}", Culture = culture, Founded = 800 },
                    });
                    foreach (var c in p.Counties)
                    {
                        history._landLaw[c] = SuccessionLaw.Partition;
                        history._landGovernment[c] = GovernmentMap.Feudal;
                        history._landFemaleShare[c] = 0;
                        history._diplomaticFaith[c] = "catholic";
                    }
                }
                history.SeatWars(null, null);
                return history;
            }

            var h = Fixture();
            var realms = h._sim.Polities;
            var a = realms[0]; var b = realms[1]; var c = realms[2]; var d = realms[3];
            h._government[c.Id] = GovernmentMap.Theocracy;
            h._law[c.Id] = SuccessionLaw.Elective;
            Require(h.CompatibleTreaty(a, c), "Independent same-faith theocrats must be eligible for political agreements.");
            h._diplomaticFaith[c.Capital] = "orthodox";
            Require(!h.CompatibleTreaty(a, c), "Clerical alliances must respect religious compatibility.");
            h._diplomaticFaith[c.Capital] = "catholic";
            h.Agree(a, c, 900, 912, "mutual protection", false);
            h.Agree(c, d, 900, 912, "good relations", false);
            Require(h.ExpectedStrength(a, b) > Formation.Strength(h._sim, a), "Expected allied strength must deter an attack.");
            h.Declare(a, c, c.Capital);
            Require(h.Wars.Count == 0, "An active alliance must block attacks between its parties.");

            bool joined = false, refused = false;
            for (int seed = 0; seed < 128; seed++)
            {
                var trial = Fixture(seed);
                var parties = trial._sim.Polities;
                trial.Agree(parties[0], parties[2], 900, 912, "mutual protection", false);
                trial.Agree(parties[2], parties[3], 900, 912, "good relations", false);
                trial.Declare(parties[0], parties[1], parties[1].Capital);
                var war = trial.Wars.Single();
                Require(!war.AttackingAllies.Contains(parties[3]), "Calls must not propagate to allies of allies.");
                if (war.AttackingAllies.Contains(parties[2]))
                {
                    joined = true;
                    Require(trial.SideStrength(war, true) > Formation.Strength(trial._sim, parties[0]), "Accepted support must affect fighting.");
                    var second = new SimWar
                    {
                        Id = 100,
                        Attacker = parties[4],
                        Defender = parties[5],
                        Beneficiary = parties[4],
                        Target = parties[5].Capital,
                        Goal = [parties[5].Capital],
                        Started = 900
                    };
                    double before = trial.SideStrength(war, true);
                    second.AttackingAllies.Add(parties[2]); trial._wars.Add(second);
                    Require(trial.SideStrength(war, true) < before, "An ally's strength must be divided across commitments.");
                }
                else
                {
                    refused = true;
                    Require(!trial.AlliesOf(parties[0]).Contains(parties[2]), "Refusing an eligible call must break the commitment.");
                }
            }
            Require(joined && refused, "Fixtures must exercise acceptance and refusal.");

            var expired = Fixture(); var ep = expired._sim.Polities;
            expired.Agree(ep[0], ep[2], 888, 900, "good relations", false);
            Require(!expired.AlliesOf(ep[0]).Any(), "Expired agreements must not influence a new declaration.");
            var ongoing = new SimWar { Id = 1, Attacker = ep[0], Defender = ep[1], Beneficiary = ep[0],
                Target = ep[1].Capital, Goal = [ep[1].Capital], Started = 899 };
            ongoing.AttackingAllies.Add(ep[2]); expired._wars.Add(ongoing);
            Require(expired.SideStrength(ongoing, true) > Formation.Strength(expired._sim, ep[0]),
                "Treaty expiry must not silently erase already accepted military participation.");

            var neutral = Fixture(); var np = neutral._sim.Polities;
            neutral.Agree(np[0], np[2], 900, 912, "good relations", false);
            neutral.Agree(np[1], np[2], 900, 912, "good relations", false);
            neutral.Declare(np[0], np[1], np[1].Capital);
            Require(neutral.Wars.Single().AttackingAllies.Count + neutral.Wars.Single().DefendingAllies.Count == 0,
                "A mutual ally must remain neutral rather than join both sides.");

            h._sim.Year++;
            var predecessor = h.RulerOf(c)!;
            h.Succeed(c, predecessor, new Rng(123), succession: false);
            var cleric = h.RulerOf(c)!;
            Require(cleric.Parent is null && h.Year - cleric.Born >= 30 && !cleric.Female && c.Counties.Count == 3,
                "Clerical succession must appoint an adult of the clergy's sex without partition or a child heir.");
            h.AlliancesYear();
            Require(h.Alliances.Where(x => x.A == c || x.B == c).All(x => x.A == c ? x.CrownedA == cleric.Crowned : x.CrownedB == cleric.Crowned),
                "A clerical successor must renegotiate agreements in their own right.");
            c.Suzerain = a; h.PruneDiplomacy();
            Require(!h.Alliances.Any(x => x.A == c || x.B == c), "Losing independence must remove external commitments.");

            var forming = Fixture();
            foreach (var county in forming._sim.Polities[5].Counties) forming._sim.Development[county] = 40;
            bool formed = false;
            for (int i = 0; i < 150; i++)
            {
                forming._sim.Year++;
                forming.AlliancesYear();
                formed |= forming.Alliances.Any();
                var errors = new List<string>(); forming.CheckAlliances(errors);
                Require(errors.Count == 0, string.Join("; ", errors));
            }
            Require(formed, "Shared threats must produce alliances during yearly history.");

            // Exercise the actual yearly ordering, including wars, succession, homage and
            // collapse. Two independent runs must agree and every tick must keep valid parties.
            for (int seed = 0; seed < 8; seed++)
            {
                var first = Fixture(seed); var second = Fixture(seed);
                first._government[2] = second._government[2] = GovernmentMap.Theocracy;
                first._law[2] = second._law[2] = SuccessionLaw.Elective;
                for (int year = 0; year < 200; year++)
                {
                    first.Tick(); second.Tick();
                    var errors = first.Check();
                    Require(errors.Count == 0, $"Seed {seed}, year {first.Year}: {string.Join("; ", errors)}");
                    Require(System.Text.Json.JsonSerializer.Serialize(AppliedHistory.Capture(first, first._sim.Development.Keys))
                        == System.Text.Json.JsonSerializer.Serialize(AppliedHistory.Capture(second, second._sim.Development.Keys)),
                        $"Deterministic replay diverged at seed {seed}, year {first.Year}.");
                }
            }

            var persist = Fixture(); var pp = persist._sim.Polities;
            persist.Agree(pp[0], pp[2], 900, 912, "mutual protection", false);
            persist._allianceOffers[(1, 3)] = 905;
            persist._truces[(3, 4)] = 905;
            persist._claims[pp[1].Capital] = (pp[0], 950);
            var carriedWar = new SimWar
            {
                Id = 7,
                Attacker = pp[0],
                Defender = pp[1],
                Beneficiary = pp[0],
                Target = pp[1].Capital,
                Goal = [pp[1].Capital],
                Started = 899,
                Score = 30
            };
            carriedWar.AttackingAllies.Add(pp[2]); persist._wars.Add(carriedWar); persist._nextWar = 9;
            var captured = AppliedHistory.Capture(persist, persist._sim.Owner.Keys);
            captured.Save(root);
            var loaded = AppliedHistory.Load(root)!;
            var resumed = Fixture(); resumed.SeatAlliances(null, loaded); resumed.SeatWars(null, loaded);
            Require(resumed.Alliances.Count() == 1 && resumed.Wars.Single().AttackingAllies.Single().Id == 2
                && resumed.Wars.Single().Score == 30 && resumed.NextWarId == 9
                && resumed.TruceUntil(resumed._sim.Polities[3], resumed._sim.Polities[4]) == 905
                && resumed._allianceOffers[(1, 3)] == 905 && resumed.Claims.Count() == 1,
                "Save/resume must retain treaties, calls, cooldowns, wars, claims, truces and war ids.");
            persist.WarsYear(); resumed.WarsYear();
            Require(persist.Wars.Single().Score == resumed.Wars.Single().Score, "Resuming must retain deterministic battle results.");
            var shifted = loaded.ShiftedBy(100);
            Require(shifted.Alliances!.Single().Since == 1000 && shifted.Alliances!.Single().Until == 1012
                && shifted.AllianceOffers.Single().Until == 1005 && shifted.Wars.Single().Started == 999,
                "Moving the start year must shift all diplomatic dates.");

            var empty = Fixture();
            var emptyCapture = AppliedHistory.Capture(empty, empty._sim.Owner.Keys);
            var seats = empty._sim.Polities.ToDictionary(p => p.Id, p => p.Capital);
            var diplomacy = emptyCapture.DiplomacyFor(seats, empty._sim.Owner.Keys)!;
            Require(diplomacy.Alliances is { Count: 0 } && diplomacy.CarriesWars,
                "An explicitly peaceful history must suppress invented alliances and wars at export.");
            var off = Fixture(); var op = off._sim.Polities;
            off.Agree(op[0], op[2], 900, 912, "good relations", false);
            off.Rules &= ~RealmRules.Alliances;
            Require(off.ExpectedStrength(op[0], op[1]) == Formation.Strength(off._sim, op[0]), "The off switch must remove deterrence.");
            off.Declare(op[0], op[1], op[1].Capital);
            Require(off.Wars.Single().AttackingAllies.Count == 0, "The off switch must disable war calls.");

            // Exercise the real emitter with accepted participants, including a clerical ally.
            var prehistory = new PrehistoryMap();
            prehistory.ActiveWars.Add(new ActiveWar
            {
                StartDate = "899.1.1",
                TargetTitle = pp[1].Capital,
                CasusBelli = "claim_cb",
                AttackerCounty = pp[0].Capital,
                DefenderCounty = pp[1].Capital,
                ClaimantCounty = pp[0].Capital,
                Description = "Alliance fixture",
                AttackingAllies = [pp[2].Capital],
                DefendingAllies = [pp[3].Capital]
            });
            WarWriter.WriteAll(root, prehistory, new MapConfig());

            // A small standalone validation mod around the real emitted starting-war script.
            // It lives outside launcher playsets and does not alter the user's generated world.
            Io.ParadoxText.WriteBom(Path.Combine(root, "descriptor.mod"), $"name=\"Alliance checks\"\npath=\"{root.Replace('\\', '/')}\"\n");
            Directory.CreateDirectory(Path.Combine(root, "history", "characters"));
            Directory.CreateDirectory(Path.Combine(root, "common", "landed_titles"));
            Directory.CreateDirectory(Path.Combine(root, "history", "titles"));
            var people = new Io.JominiBuilder(); var titles = new Io.JominiBuilder(); var historyTitles = new Io.JominiBuilder();
            foreach (var p in pp)
            {
                using (people.Block(HistoryWriter.CharacterId(p.Capital)))
                {
                    people.Field("name", $"\"Ruler {p.Id}\""); people.Field("culture", "anglo_saxon"); people.Field("religion", "catholic");
                    if (p.Id == 2) people.Field("trait", "devoted");
                    people.Inline("870.1.1", "birth = yes");
                    if (p.Id == 0)
                        using (people.Block("900.1.1"))
                        using (people.Block("effect"))
                            people.Field("create_alliance", $"character:{HistoryWriter.CharacterId(pp[2].Capital)}");
                }
                using (titles.Block(p.Capital.Key))
                using (titles.Block($"b_alliance_check_{p.Id}"))
                    titles.Field("province", p.Id + 1);
                using (historyTitles.Block(p.Capital.Key))
                using (historyTitles.Block("895.1.1"))
                {
                    historyTitles.Field("holder", HistoryWriter.CharacterId(p.Capital));
                    historyTitles.Field("government", p.Id == 2 ? GovernmentMap.Theocracy : GovernmentMap.Feudal);
                }
            }
            Io.ParadoxText.WriteBom(Path.Combine(root, "history", "characters", "gen_alliance_checks.txt"), people.ToString());
            Io.ParadoxText.WriteBom(Path.Combine(root, "common", "landed_titles", "gen_alliance_checks.txt"), titles.ToString());
            Io.ParadoxText.WriteBom(Path.Combine(root, "history", "titles", "gen_alliance_checks.txt"), historyTitles.ToString());
            Console.WriteLine($"History alliance checks passed. Export fixture: {root}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Console.Error.WriteLine($"Alliance fixture retained: {root}");
            return 1;
        }
    }
}
