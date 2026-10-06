using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

// Secondary technologies earned from Neolithic practice and the planner's herd-versus-field valuation
// (Docs/HusbandrySettling.md). Owns its worlds and counters (lanes run concurrently).
internal static class SettlingChecks
{
    private static void Check(ref int checks, bool ok, string message) { checks++; if (!ok) throw new Exception("Settling: " + message); }
    private static bool Info => Environment.GetEnvironmentVariable("SETTLING_INFO") == "1";
    private static bool Near(double a, double b, double tolerance = 1e-9) => Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    // Value-rule camp with both capabilities and identical physical state;
    // `cultivationPrimary` changes only the historical first-breakthrough label.
    private static WorldState Camp(int population, int animals, bool cultivationPrimary = false, double pasture = 1, double fieldIntensity = 0)
    {
        var w = NeolithicChecks.Fixture();
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
        var c = w.Camps[0]; int tile = c.Tile;
        w.Resources.Stone[tile] = 1000; w.Resources.Clay[tile] = 1000; w.Resources.Wood[tile] = 1000;
        var t = w.Tiles[tile]; t.CultivationCapital = .1; t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, .1); t.CultivationOperator = c.Id;
        c.Inventory.HandTools = 1000; c.Population = population; c.DemographicGrowthMultiplier = 1; w.Config.AnnualHumanGrowthRate = new SimulationConfig().AnnualHumanGrowthRate;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        c.Technology = cultivationPrimary ? Technology.Cultivation : Technology.AnimalHusbandry;
        c.Technologies[(int)Technology.Cultivation] = true; c.Technologies[(int)Technology.AnimalHusbandry] = true;
        w.Config.Neolithic.PlannerFulfilment = "Value";
        c.FoodNeedsMetRatio = .2; c.QoL = .8; w.Config.QoLMortalityEnabled = false; /* these fixtures price growth at the unreduced ceiling; QoLMortalityChecks covers the lever */
        if (fieldIntensity > 0)
            foreach (int id in w.Map.Radius(tile, 1).Where(id => w.Tiles[id].TraversableLand))
            {
                double capital = Math.Pow(fieldIntensity / w.Config.ReferenceIntensity, 2);
                w.Tiles[id].CultivationCapital = capital; w.Tiles[id].Cultivation = (float)fieldIntensity; w.Tiles[id].CultivationOperator = c.Id; w.Tiles[id].CultivationOperatingFraction = 1;
            }
        if (pasture != 1) foreach (var tl in w.Tiles) { tl.PotentialFood *= (float)pasture; for (int s = 0; s < 4; s++) tl.Seasons[s].MaxPotentialFood *= (float)pasture; }
        if (animals > 0) w.Herds.Add(new Herd { Id = w.NextId++, Tile = tile, Species = "goat", Animals = animals, Capacity = 1, DomesticCamp = c.Id, OwnerSociety = c.SocietyId });
        return w;
    }

    private static string Sig(EconomicIntent e) => string.Join(",", e.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).Select(a => a.Role + a.Population)) + "|" + Math.Round(e.Labor?.MaterialGatherers ?? 0, 9) + "|" + Math.Round(e.Labor?.ExpansionFarmers ?? 0, 9);
    private static double Expansion(EconomicIntent plan) => plan.Labor?.ExpansionFarmers ?? 0;

    // ---- Part A: secondary technologies are earned from Neolithic practice; one practice API with a trade source.
    private static void Practice(ref int checks)
    {
        var w = NeolithicChecks.Fixture(false); var society = w.Camps[0].People; var cfg = w.Config;
        Check(ref checks, cfg.SecondaryTechnologyThreshold == cfg.ExperienceThreshold || cfg.ExperienceThreshold != 225 && cfg.SecondaryTechnologyThreshold == 225, "the secondary threshold defaults to the 225-point experience threshold");
        Check(ref checks, new SimulationConfig().SecondaryTechnologyThreshold == new SimulationConfig().ExperienceThreshold && new SimulationConfig().TradePracticePerValue == 0 && new SimulationConfig().SettledHerdGrowthPenalty == .35, "new configuration defaults");
        // Paleolithic credits accrue to lifetime practice only.
        TechnologyDiscovery.AddPractice(society, Domain.Gathering, 40, PracticeSource.Gathering);
        TechnologyDiscovery.AddPractice(society, Domain.Hunting, 7.5, PracticeSource.Hunting);
        Check(ref checks, society.Experience[(int)Domain.Gathering] == 40 && society.Experience[(int)Domain.Hunting] == 7.5 && society.NeolithicExperience.All(x => x == 0), "Paleolithic practice credits lifetime experience only");
        // After the breakthrough the same credit also accrues to Neolithic practice, whatever its source.
        society.Progression.Era = Era.Neolithic;
        foreach (var source in new[] { PracticeSource.Gathering, PracticeSource.Hunting, PracticeSource.Fishing, PracticeSource.Trade })
            TechnologyDiscovery.AddPractice(society, Domain.Fishing, 3, source);
        Check(ref checks, society.Experience[(int)Domain.Fishing] == 12 && society.NeolithicExperience[(int)Domain.Fishing] == 12 && society.NeolithicExperience[(int)Domain.Gathering] == 0, "Neolithic practice credits both lifetime and Neolithic experience, independent of source");
        TechnologyDiscovery.AddPractice(society, Domain.Fishing, 0, PracticeSource.Fishing); TechnologyDiscovery.AddPractice(society, Domain.Fishing, double.NaN, PracticeSource.Fishing);
        Check(ref checks, society.Experience[(int)Domain.Fishing] == 12, "an empty or invalid credit changes nothing");

        // The real accrual path: a resolved Paleolithic turn credits the same totals as before and stays out of Neolithic practice;
        // a resolved Neolithic turn credits the same Experience as its Neolithic practice increment.
        var paleo = NeolithicChecks.Fixture(false); paleo.Config.ExperienceThreshold = 100000;
        new TurnResolver().Resolve(paleo, new Dictionary<int, List<Order>>());
        var ps = paleo.Camps[0].People;
        Check(ref checks, ps.Experience.Sum() > 0 && ps.NeolithicExperience.All(x => x == 0), "a resolved Paleolithic turn credits lifetime practice and no Neolithic practice");
        var neo = NeolithicChecks.Fixture(false); neo.Config.ExperienceThreshold = 100000; neo.Camps[0].People.Progression.Era = Era.Neolithic;
        neo.Camps[0].Technology = Technology.AnimalHusbandry; neo.Camps[0].Technologies[(int)Technology.AnimalHusbandry] = true;
        new TurnResolver().Resolve(neo, new Dictionary<int, List<Order>>());
        var ns = neo.Camps[0].People;
        Check(ref checks, ns.Experience.Sum() > 0 && Enumerable.Range(0, 3).All(i => Near(ns.Experience[i], ns.NeolithicExperience[i], 1e-12)), "a resolved Neolithic turn credits Neolithic practice the same amounts");

        // Discovery: Paleolithic experience alone never grants; Neolithic practice does, exactly at the threshold.
        var d = NeolithicChecks.Fixture(false); var camp = d.Camps[0]; var soc = camp.People;
        soc.Progression.Era = Era.Neolithic; soc.Technology = Technology.AnimalHusbandry; soc.Technologies[(int)Technology.AnimalHusbandry] = true;
        soc.Experience = new[] { 900.0, 900.0, 900.0 };
        TechnologyDiscovery.Resolve(d, new TurnRecord());
        Check(ref checks, !soc.Technologies[(int)Technology.Cultivation] && !soc.Technologies[(int)Technology.Watercraft], "any amount of Paleolithic experience grants no secondary technology");
        var status = TechnologyDiscovery.Statuses(d, soc);
        Check(ref checks, status[(int)Domain.Gathering].Progress == 0 && status[(int)Domain.Gathering].Experience == 0 && status[(int)Domain.Gathering].Threshold == d.Config.SecondaryTechnologyThreshold && !status[(int)Domain.Gathering].Eligible
            && status[(int)Domain.Gathering].BlockedReason == "More Gathering practice required", "status shows the Neolithic progress of an unacquired secondary technology");
        soc.NeolithicExperience[(int)Domain.Gathering] = d.Config.SecondaryTechnologyThreshold - 1e-6;
        TechnologyDiscovery.Resolve(d, new TurnRecord());
        Check(ref checks, !soc.Technologies[(int)Technology.Cultivation] && !TechnologyDiscovery.Statuses(d, soc)[(int)Domain.Gathering].Eligible, "just below the threshold grants nothing");
        Check(ref checks, TechnologyDiscovery.Statuses(d, soc)[(int)Domain.Hunting].Acquired && TechnologyDiscovery.Statuses(d, soc)[(int)Domain.Hunting].Experience == 900 && TechnologyDiscovery.Statuses(d, soc)[(int)Domain.Hunting].Progress == 900, "the acquired first technology keeps showing lifetime experience and progress");
        soc.NeolithicExperience[(int)Domain.Gathering] = d.Config.SecondaryTechnologyThreshold;
        Check(ref checks, TechnologyDiscovery.Statuses(d, soc)[(int)Domain.Gathering].Eligible, "at the threshold the status is eligible");
        var record = new TurnRecord(); TechnologyDiscovery.Resolve(d, record);
        Check(ref checks, soc.Technologies[(int)Technology.Cultivation] && record.Events.Any(e => e.Contains("discovered Cultivation through Gathering")), "Neolithic practice at the threshold grants the technology");
        // A different threshold is honoured, and a technology already held is kept whatever the practice.
        var t2 = NeolithicChecks.Fixture(false); var s2 = t2.Camps[0].People; s2.Progression.Era = Era.Neolithic; s2.Technology = Technology.Cultivation; s2.Technologies[(int)Technology.Cultivation] = true;
        t2.Config.SecondaryTechnologyThreshold = 50; s2.NeolithicExperience[(int)Domain.Fishing] = 49;
        TechnologyDiscovery.Resolve(t2, new TurnRecord()); Check(ref checks, !s2.Technologies[(int)Technology.Watercraft], "a configured threshold applies: below it nothing");
        s2.NeolithicExperience[(int)Domain.Fishing] = 50; TechnologyDiscovery.Resolve(t2, new TurnRecord()); Check(ref checks, s2.Technologies[(int)Technology.Watercraft], "a configured threshold applies: at it the technology is granted");
        s2.NeolithicExperience[(int)Domain.Fishing] = 0; TechnologyDiscovery.Resolve(t2, new TurnRecord()); Check(ref checks, s2.Technologies[(int)Technology.Watercraft], "a technology already held is kept");

        // The forced-transition catch-up is unchanged: the secondary domain carries lifetime practice x 1.25 (floor 100).
        var f = NeolithicChecks.Fixture(false); var fc = f.Camps[0];
        fc.Experience = new[] { 200.0, 180.0, 0.0 }; f.Days = 240 * f.Config.DaysPerYear;
        TurnResolver.Breakthrough(f, fc, new TurnRecord());
        Check(ref checks, fc.ForcedTransition && fc.SecondaryDomain == (int)Domain.Gathering && fc.People.NeolithicExperience.All(x => x == 0), "a forced transition starts with no Neolithic practice");
        Check(ref checks, TechnologyDiscovery.EffectiveProgress(fc.People, Domain.Gathering, f.Config) == 225 && TechnologyDiscovery.EffectiveProgress(fc.People, Domain.Fishing, f.Config) == 0, "forced carryover: secondary progress 180 x 1.25 = 225, other domains start at zero");
        TechnologyDiscovery.Resolve(f, new TurnRecord());
        Check(ref checks, fc.Technologies[(int)Technology.Cultivation] && !fc.Technologies[(int)Technology.Watercraft], "the forced Society's secondary domain is granted at its first Close");
        var g = NeolithicChecks.Fixture(false); var gc = g.Camps[0]; gc.Experience = new[] { 200.0, 180.0, 0.0 }; g.Days = 200 * g.Config.DaysPerYear;
        TurnResolver.Breakthrough(g, gc, new TurnRecord()); TechnologyDiscovery.Resolve(g, new TurnRecord());
        Check(ref checks, !gc.ForcedTransition && !gc.Technologies[(int)Technology.Cultivation], "a non-forced transition gets no carryover grant");

        // Saves: Neolithic practice round-trips; a save without the field loads zeros; the new configuration defaults for older saves.
        var sv = NeolithicChecks.Fixture(false); sv.Camps[0].People.Progression.Era = Era.Neolithic; sv.Camps[0].People.NeolithicExperience = new[] { 1.5, 2.5, 3.5 };
        sv.Config.SecondaryTechnologyThreshold = 77; sv.Config.TradePracticePerValue = .25; sv.Config.SettledHerdGrowthPenalty = .5;
        var back = SaveCodec.Load(SaveCodec.Save(sv));
        Check(ref checks, back.Camps[0].People.NeolithicExperience.SequenceEqual(new[] { 1.5, 2.5, 3.5 }) && back.Config.SecondaryTechnologyThreshold == 77 && back.Config.TradePracticePerValue == .25 && back.Config.SettledHerdGrowthPenalty == .5, "Neolithic practice and the new configuration round-trip");
        Check(ref checks, SaveCodec.Fingerprint(sv) == SaveCodec.Fingerprint(back), "the round trip preserves the authoritative fingerprint");
        sv.Camps[0].People.NeolithicExperience = null;
        var legacy = SaveCodec.Load(SaveCodec.Save(sv));
        Check(ref checks, legacy.Camps[0].People.NeolithicExperience != null && legacy.Camps[0].People.NeolithicExperience.Length == 3 && legacy.Camps[0].People.NeolithicExperience.All(x => x == 0), "an older save without Neolithic practice loads zeros");
        var serializer = new System.Runtime.Serialization.DataContractSerializer(typeof(SimulationConfig)); string xml;
        using (var stream = new System.IO.MemoryStream()) { serializer.WriteObject(stream, new SimulationConfig { SecondaryTechnologyThreshold = 91, TradePracticePerValue = 3, SettledHerdGrowthPenalty = .6 }); xml = System.Text.Encoding.UTF8.GetString(stream.ToArray()); }
        string old = xml.Replace("<SecondaryTechnologyThreshold>91</SecondaryTechnologyThreshold>", "").Replace("<TradePracticePerValue>3</TradePracticePerValue>", "").Replace("<SettledHerdGrowthPenalty>0.6</SettledHerdGrowthPenalty>", "");
        Check(ref checks, old.Length < xml.Length - 60, "the legacy configuration fixture omits the three fields");
        using (var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(old)))
        { var loaded = (SimulationConfig)serializer.ReadObject(stream); Check(ref checks, loaded.SecondaryTechnologyThreshold == 225 && loaded.TradePracticePerValue == 0 && Near(loaded.SettledHerdGrowthPenalty, .35), "older saves load the defaults (225, inert trade, 0.35)"); }
        foreach (var bad in new Action<SimulationConfig>[] { c => c.SecondaryTechnologyThreshold = 0, c => c.TradePracticePerValue = -1, c => c.SettledHerdGrowthPenalty = 1.5, c => c.SettledHerdGrowthPenalty = double.NaN })
        { var cfgBad = new SimulationConfig(); bad(cfgBad); bool rejected = false; try { cfgBad.Validate(); } catch (ArgumentException) { rejected = true; } Check(ref checks, rejected, "invalid secondary, trade or settling configuration is rejected"); }
        var json = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(FindConfig()), "\"SecondaryTechnologyThreshold\":\\s*225,\\s*\"TradePracticePerValue\":\\s*0,\\s*\"SettledHerdGrowthPenalty\":\\s*0\\.35");
        Check(ref checks, json.Success, "OpeningConfig.json carries the three defaults");

        // Trade hook: inert at weight 0, credits goods value x weight through the same API otherwise.
        var tw = NeolithicChecks.Fixture(false); var tr = tw.Camps[0].People; tr.Progression.Era = Era.Neolithic;
        Check(ref checks, TechnologyDiscovery.RecordTradePractice(tw.Config, tr, Domain.Gathering, 500) == 0 && tr.Experience.All(x => x == 0) && tr.NeolithicExperience.All(x => x == 0), "trade practice is inert at weight 0");
        tw.Config.TradePracticePerValue = .1;
        double credited = TechnologyDiscovery.RecordTradePractice(tw.Config, tr, Domain.Gathering, 500);
        Check(ref checks, Near(credited, 50) && Near(tr.Experience[(int)Domain.Gathering], 50) && Near(tr.NeolithicExperience[(int)Domain.Gathering], 50) && tr.Experience[(int)Domain.Hunting] == 0, "trade practice credits goods value x weight to the receiver's domain");
        TechnologyDiscovery.RecordTradePractice(tw.Config, tr, Domain.Fishing, -10); TechnologyDiscovery.RecordTradePractice(tw.Config, tr, Domain.Fishing, 0);
        Check(ref checks, tr.Experience[(int)Domain.Fishing] == 0, "no goods, no practice");
        tw.Config.TradePracticePerValue = 1; TechnologyDiscovery.RecordTradePractice(tw.Config, tr, Domain.Fishing, tw.Config.SecondaryTechnologyThreshold);
        tr.Technology = Technology.AnimalHusbandry; tr.Technologies[(int)Technology.AnimalHusbandry] = true;
        TechnologyDiscovery.Resolve(tw, new TurnRecord());
        Check(ref checks, tr.Technologies[(int)Technology.Watercraft], "imported practice can earn a secondary technology");
        var pw = NeolithicChecks.Fixture(false); pw.Config.TradePracticePerValue = 1;
        TechnologyDiscovery.RecordTradePractice(pw.Config, pw.Camps[0].People, Domain.Hunting, 10);
        Check(ref checks, pw.Camps[0].People.Experience[(int)Domain.Hunting] == 10 && pw.Camps[0].People.NeolithicExperience[(int)Domain.Hunting] == 0, "before the breakthrough trade practice counts toward it like any practice");
    }
    private static string FindConfig()
    {
        for (var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        { string path = System.IO.Path.Combine(dir.FullName, "Assets", "_Anthropocene", "Resources", "OpeningConfig.json"); if (System.IO.File.Exists(path)) return path; }
        throw new System.IO.FileNotFoundException("OpeningConfig.json");
    }

    // ---- Part B: herds weighed against fields.
    private static void Herds(ref int checks)
    {
        var cfg = new SimulationConfig();
        // Growth forecast: independent arithmetic, purity, and the resolver's own outcome.
        var w = Camp(200, 160, false, 1, .24); var c = w.Camps[0]; double years = w.Config.TurnDays / w.Config.DaysPerYear, intervals = w.Config.GenerationIntervals;
        string before = SaveCodec.Fingerprint(w);
        var grown = TurnResolver.ForecastGrownManagedAnimalFood(w, c, c.ManagedBreedingIntensity, intervals, years, c.Tile);
        var current = TurnResolver.ForecastManagedAnimalFood(w, c, HerdingTooling.RequiredHerders(w, c, years), intervals, years, c.Tile);
        Check(ref checks, SaveCodec.Fingerprint(w) == before, "the grown-herd forecast is pure");
        double expected = Math.Floor(160 * Math.Exp(w.Config.GetSpecies("goat").GrowthRate * c.ManagedBreedingIntensity * years));
        Check(ref checks, grown.Animals == expected && expected > 160, "a nourished herd grows by the breeding exponential (" + grown.Animals + " animals)");
        Check(ref checks, grown.AnimalFood > current.AnimalFood && Near(grown.AnimalFood, expected * w.Config.AnimalFoodPerManagedAnimal * w.Config.Consumption * intervals, 1e-9), "a nourished, fully herded grown herd yields its full animal food");
        var world = Camp(200, 160, false, 1, .24); var record = new TurnResolver().Resolve(world, new Dictionary<int, List<Order>>());
        Check(ref checks, world.Herds.Single(h => h.DomesticCamp == world.Camps[0].Id).Animals == (int)grown.Animals, "the resolver's newborns equal the forecast (" + world.Herds.Single(h => h.DomesticCamp == world.Camps[0].Id).Animals + ")");
        var none = Camp(200, 0, false, 1, .24);
        Check(ref checks, TurnResolver.ForecastGrownManagedAnimalFood(none, none.Camps[0], .55, intervals, years, none.Camps[0].Tile).Animals == 0, "no herd, no growth forecast");
        var stop = Camp(200, 160, false, 1, .24); stop.Camps[0].ManagedBreedingIntensity = 0;
        Check(ref checks, TurnResolver.ForecastGrownManagedAnimalFood(stop, stop.Camps[0], 0, intervals, years, stop.Camps[0].Tile).Animals == 160, "breeding intensity 0 forecasts no growth");
        // Water that cannot carry the herd: no demonstrated support, so no growth (the resolver breeds only fully nourished herds).
        var dry = Camp(200, 160, false, 1, .24);
        foreach (var t in dry.Tiles) { t.Freshwater = 1; for (int s = 0; s < 4; s++) t.Seasons[s].MaxFreshwater = 1; }
        var dryGrown = TurnResolver.ForecastGrownManagedAnimalFood(dry, dry.Camps[0], .55, intervals, years, dry.Camps[0].Tile);
        Check(ref checks, dryGrown.Animals == 160, "a herd short of water is forecast not to grow");

        // Demand pass: herd growth closes the gap before fields (Value rule, both technologies).
        var d = CampProductionDemand.Build(w, c, EconomicIntent.From(c));
        Check(ref checks, d.HerdGrowthFood > 0 && Near(d.HerdGrowthFood, (grown.AnimalFood - d.ManagedFood.AnimalFood) - (grown.HumanFoodDisplaced - d.ManagedFood.HumanFoodDisplaced), 1e-9) && Near(d.GrownHerdFood, grown.AnimalFood, 1e-9), "the demand pass carries the net growth food and the grown herd's food");
        var dd = CampProductionDemand.Build(dry, dry.Camps[0], EconomicIntent.From(dry.Camps[0]));
        Check(ref checks, dd.HerdGrowthFood == 0, "no growth food without support");
        // The gap a ladder prices is the forecast gap less the growth credit.
        var cropOnly = Camp(200, 160, true, 1, .24); cropOnly.Camps[0].ManagedBreedingIntensity = 0;
        var e0 = EconomicIntent.From(cropOnly.Camps[0]); e0.ManagedBreedingIntensity = 0;
        var d0 = CampProductionDemand.Build(cropOnly, cropOnly.Camps[0], e0);
        Check(ref checks, d0.HerdGrowthFood == 0 && d.GapTotalPerYear < d0.GapTotalPerYear && Near(d0.GapTotalPerYear - d.GapTotalPerYear, d.HerdGrowthFood / years, 1e-9), "the growth credit shortens the food gap by exactly the net growth food per year");

        // Planner: herds with pasture keep spare labour off fields; the same camp without growth builds fields.
        var grow = Camp(100, 50, true, 1, 0); var stay = Camp(100, 50, true, 1, 0);
        var eStay = EconomicIntent.From(stay.Camps[0]); eStay.ManagedBreedingIntensity = 0; stay.Camps[0].ManagedBreedingIntensity = 0;
        // Shrink the herd's unassisted food so only growth makes the difference.
        var pGrow = EconomicPlanner.Plan(grow, grow.Camps[0], EconomicIntent.From(grow.Camps[0])); var pStay = EconomicPlanner.Plan(stay, stay.Camps[0], eStay);
        if (Info) Console.WriteLine("SETTLING planner growth exp=" + Expansion(pGrow) + " nogrowth exp=" + Expansion(pStay));
        Check(ref checks, Expansion(pGrow) < Expansion(pStay), "growing herds with pasture take labour off field expansion (" + Expansion(pGrow) + " < " + Expansion(pStay) + ")");
        // A herd that cannot be carried (no water) expands fields like the camp with no herd growth.
        var dryCamp = Camp(100, 50, true, 1, 0);
        foreach (var t in dryCamp.Tiles) { t.Freshwater = 1; for (int s = 0; s < 4; s++) t.Seasons[s].MaxFreshwater = 1; }
        var dryPlan = EconomicPlanner.Plan(dryCamp, dryCamp.Camps[0], EconomicIntent.From(dryCamp.Camps[0]));
        var noHerd = Camp(100, 0, true, 1, 0); var noHerdPlan = EconomicPlanner.Plan(noHerd, noHerd.Camps[0], EconomicIntent.From(noHerd.Camps[0]));
        Check(ref checks, Expansion(dryPlan) > Expansion(pGrow), "the same herd without water (no growth) leaves more labour for fields (" + Expansion(dryPlan) + ")");
        Check(ref checks, Expansion(noHerdPlan) > 0 && Expansion(noHerdPlan) > Expansion(pGrow), "a camp with no herd expands fields (" + Expansion(noHerdPlan) + ")");
        // Uniform is unchanged: no growth credit, no settling cost.
        var uniform = Camp(200, 160, false, 1, .24); uniform.Config.Neolithic.PlannerFulfilment = "Uniform";
        var ud = CampProductionDemand.Build(uniform, uniform.Camps[0], EconomicIntent.From(uniform.Camps[0]));
        Check(ref checks, ud.HerdGrowthFood == 0 && ud.GrownHerdFood == 0 && ud.SettlingCost == 0 && double.IsPositiveInfinity(ud.ExpansionEffortCap), "the Uniform rule keeps no herd credit and no settling cost");
        // Purity and determinism of the planner, and continuation across a save.
        string fp = SaveCodec.Fingerprint(w);
        var p1 = EconomicPlanner.Plan(w, c, EconomicIntent.From(c)); var p2 = EconomicPlanner.Plan(w, c, EconomicIntent.From(c));
        var loaded = SaveCodec.Load(SaveCodec.Save(w)); var p3 = EconomicPlanner.Plan(loaded, loaded.Camps[0], EconomicIntent.From(loaded.Camps[0]));
        Check(ref checks, SaveCodec.Fingerprint(w) == fp && Sig(p1) == Sig(p2) && Sig(p1) == Sig(p3), "planning with the herd credit is pure, repeatable and save-stable");
    }

    // ---- Settling cost of a physically herd-bearing mobile camp.
    private static void Settling(ref int checks)
    {
        // Near the village threshold with many spare residents and a herd worth more than the fields would add.
        var h = Camp(400, 160, false, 1, .24); var k = Camp(400, 160, true, 1, .24);
        var dh = CampProductionDemand.Build(h, h.Camps[0], EconomicIntent.From(h.Camps[0])); var dk = CampProductionDemand.Build(k, k.Camps[0], EconomicIntent.From(k.Camps[0]));
        double years = h.Config.TurnDays / h.Config.DaysPerYear;
        Check(ref checks, dh.SettlingCost > 0 && !dh.SettlesByExpansion && dh.ExpansionEffortCap < double.PositiveInfinity, "a mobile herd-bearing camp near the threshold is charged a settling cost and capped (" + dh.SettlingCost.ToString("F0") + ", cap " + dh.ExpansionEffortCap.ToString("F0") + " worker-years)");
        Check(ref checks, Near(dk.SettlingCost, dh.SettlingCost) &&
            dk.SettlesByExpansion == dh.SettlesByExpansion &&
            Near(dk.ExpansionEffortCap, dh.ExpansionEffortCap),
            "the same physical transition cost is independent of primary technology");
        var ph = EconomicPlanner.Plan(h, h.Camps[0], EconomicIntent.From(h.Camps[0])); var pk = EconomicPlanner.Plan(k, k.Camps[0], EconomicIntent.From(k.Camps[0]));
        Check(ref checks, Expansion(ph) <= Math.Floor(dh.ExpansionEffortCap / years + 1e-9) &&
            Expansion(pk) == Expansion(ph),
            "equivalent primary-technology labels produce the same capped field plan (" + Expansion(ph) + " vs " + Expansion(pk) + ")");
        Check(ref checks, dh.Expansion == null || dh.Expansion.TotalWorkerYears <= dh.ExpansionEffortCap + 1e-9, "the valued ladder stops before the crossing step");
        // The capped plan really leaves the footprint below the village threshold at the turn's end.
        var after = Camp(400, 160, false, 1, .245); after.Camps[0].EconomyAutomationTurns = -1; new TurnResolver().Resolve(after, new Dictionary<int, List<Order>>());
        var kept = Camp(400, 160, true, 1, .245); kept.Camps[0].EconomyAutomationTurns = -1; new TurnResolver().Resolve(kept, new Dictionary<int, List<Order>>());
        double Average(WorldState x) { var camp = x.Camps[0]; var land = x.Map.Radius(camp.Tile, 1).Where(id => x.Tiles[id].TraversableLand).ToArray(); return land.Sum(id => x.Tiles[id].CultivationOperator == camp.Id ? CultivationEconomy.Intensity(x.Config, x.Tiles[id].CultivationCapital) : 0) / land.Length; }
        Check(ref checks, Average(after) < CultivationEconomy.VillageIntensity &&
            !after.Camps[0].IsVillage && Near(Average(after), Average(kept)) &&
            kept.Camps[0].IsVillage == after.Camps[0].IsVillage,
            "resolved automation gives equivalent physical camps the same transition outcome (" + Average(after).ToString("F3") + " vs " + Average(kept).ToString("F3") + ")");
        if (Info) Console.WriteLine("SETTLING resolved husbandry avg=" + Average(after).ToString("F3") + " village=" + after.Camps[0].IsVillage + " | cultivator avg=" + Average(kept).ToString("F3") + " village=" + kept.Camps[0].IsVillage);
        // A zero penalty restores the no-cost behaviour; existing villages are unaffected.
        var free = Camp(400, 160, false, 1, .24); free.Config.SettledHerdGrowthPenalty = 0;
        var df = CampProductionDemand.Build(free, free.Camps[0], EconomicIntent.From(free.Camps[0]));
        Check(ref checks, df.SettlingCost == 0 && double.IsPositiveInfinity(df.ExpansionEffortCap), "a zero settling penalty restores the uncapped ladder");
        var village = Camp(400, 160, false, 1, .24); village.Camps[0].IsVillage = true;
        var dv = CampProductionDemand.Build(village, village.Camps[0], EconomicIntent.From(village.Camps[0]));
        Check(ref checks, dv.SettlingCost == 0 && double.IsPositiveInfinity(dv.ExpansionEffortCap), "a camp that is already a village has nothing further to forfeit");
        // Settling that clearly pays goes ahead: the tail is scaled, not dropped, and every later step keeps a positive value.
        var rich = Camp(400, 20, false, 1, .24);
        var dr = CampProductionDemand.Build(rich, rich.Camps[0], EconomicIntent.From(rich.Camps[0]));
        Check(ref checks, dr.SettlingCost > 0 && dr.SettlesByExpansion && double.IsPositiveInfinity(dr.ExpansionEffortCap) && dr.Expansion != null && dr.Expansion.Value.All(v => v > 0) && dr.Expansion.Value.Zip(dr.Expansion.Value.Skip(1), (a, b) => a >= b).All(x => x), "when the fields clearly beat the herd settling proceeds with a scaled ladder");
        var relabeled = Camp(400, 20, true, 1, .24);
        var du = CampProductionDemand.Build(relabeled, relabeled.Camps[0], EconomicIntent.From(relabeled.Camps[0]));
        Check(ref checks, Near(dr.SettlingCost, du.SettlingCost) &&
            dr.Expansion.Value.SequenceEqual(du.Expansion.Value),
            "relabelling the same herd/field state leaves every transition-ladder value unchanged");
        // The threshold step is reported by the ladder.
        var steps = CultivationEconomy.ExpansionLadder(rich, rich.Camps[0], years, dr.Footprint);
        Check(ref checks, steps.Length > 0 && steps.Zip(steps.Skip(1), (a, b) => a.VillageAverage <= b.VillageAverage + 1e-12).All(x => x) && steps.Any(s => s.VillageAverage + 1e-10 >= CultivationEconomy.VillageIntensity), "ladder steps report a nondecreasing village-footprint average that reaches the threshold");
    }

    public static int Run()
    {
        int checks = 0;
        Practice(ref checks); Herds(ref checks); Settling(ref checks);
        Console.WriteLine("PASS: " + checks + " settling assertions");
        return checks;
    }
}
