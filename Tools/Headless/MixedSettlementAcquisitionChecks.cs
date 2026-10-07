using System;
using System.Collections.Generic;
using System.Linq;
using Anthropocene.Simulation;

internal static class MixedSettlementAcquisitionChecks
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        checks++;
        if (!ok) throw new Exception("Mixed settlement acquisition: " + message);
    }
    private static void Near(double a, double b, string message)
        => Check(Math.Abs(a - b) < 1e-9, message + " (" + a + " vs " + b + ")");

    private static WorldState Fixture(Technology primary)
    {
        var w = NeolithicChecks.Fixture(false);
        w.Config.NeolithicFishingEnabled = true;
        w.Config.CorrectedAquaticSeasonalPressureEnabled = true;
        w.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        w.Config.HusbandryTamingEnabled = true;
        w.Config.HusbandryProximityPracticePerYear = 2.5;
        w.Config.TurnDays = w.Config.DaysPerYear;
        w.Config.WanderChance = 0;
        w.Config.WildHerdFissionEnabled = false;
        var c = w.Camps.Single();
        c.Technology = primary; c.Technologies[(int)primary] = true;
        c.Technologies[(int)Technology.AnimalHusbandry] = true;
        c.People.Progression.Era = Era.Neolithic;
        c.SpeciesExperience.Add(new SpeciesPractice { Species = "goat", Experience = 100 });
        w.Herds.Add(new Herd { Id = w.NextId++, Tile = w.Map.Neighbors(c.Tile).OrderBy(t => t).First(),
            Species = "goat", Animals = 20, Capacity = 100 });
        return w;
    }

    private static void ResolvedResidentAcquisition()
    {
        foreach (var primary in new[] { Technology.Cultivation, Technology.Watercraft })
        {
            var w = Fixture(primary); var c = w.Camps.Single();
            int tile = c.Tile;
            Check(w.Hunters.Count == 0, primary + " fixture contains no hunters");
            var action = new Order(OrderKind.NoOp, c.Id) { RequestAutomaticEconomy = true };
            var prepared = TurnPlanner.Prepare(w,
                new Dictionary<int, List<Order>> { [c.Player] = new List<Order> { action } });
            Check(prepared.Rejections.Count == 0, "resident automatic plan is accepted");
            var record = new TurnResolver().Resolve(w, prepared);
            var domestic = w.Herds.SingleOrDefault(h => h.DomesticCamp == c.Id && h.Animals > 0);
            Check(domestic != null && domestic.Animals == w.Config.InitialDomesticAnimals,
                primary + " residents acquire a real herd without hunter detachments");
            Check(c.Tile == tile && c.Technology == primary,
                "animal acquisition changes neither location nor first-technology identity");
            Check(record.ManagedHerds.All(r => domestic == null || r.Herd != domestic.Id),
                "new capture cannot earn current-horizon managed production");
            Check(record.Transfers.All(t => t.Entity != c.Id || t.Kind != "Animal Product"),
                "no retroactive animal product is created by capture");
            TurnResolver.Invariants(w);
            var saved = SaveCodec.Load(SaveCodec.Save(w));
            Check(SaveCodec.Fingerprint(w) == SaveCodec.Fingerprint(saved),
                "newly acquired animals and knowledge round-trip");
        }
    }

    private static void LiquidationAndConservation()
    {
        var w = Fixture(Technology.Cultivation); var c = w.Camps.Single();
        var wild = w.Herds.Single(); wild.GrowthRemainder = .4; wild.DeathRemainder = .2;
        int before = w.Herds.Sum(h => h.Animals);
        var record = new TurnRecord();
        Check(HusbandryAcquisition.ResolveTaming(w, Array.Empty<HusbandryAcquisition.Opportunity>(), record) == 5,
            "empty hunter opportunity list still allows qualified local habituation");
        Check(w.Herds.Sum(h => h.Animals) == before, "wild plus managed animals are conserved");
        Near(w.Herds.Sum(h => h.GrowthRemainder), .4, "fractional birth progress is conserved");
        Near(w.Herds.Sum(h => h.DeathRemainder), .2, "fractional death progress is conserved");
        var domestic = w.Herds.Single(h => h.DomesticCamp == c.Id);
        c.HerdTargetSet = true; c.HerdTarget = 0;
        domestic.Animals = 0; // Authoritative slaughter deliberately preserves this zero record.
        int remainingWild = wild.Animals;
        Check(HusbandryAcquisition.ResolveTaming(w, Array.Empty<HusbandryAcquisition.Opportunity>(), record) == 0 &&
            wild.Animals == remainingWild, "taming does not refill a deliberately liquidated herd");

        var multi = Fixture(Technology.Cultivation); var mc = multi.Camps.Single();
        mc.SpeciesExperience.Add(new SpeciesPractice { Species = "horse", Experience = 100 });
        multi.Herds.Add(new Herd { Id = multi.NextId++, Tile = multi.Herds.Single().Tile,
            Species = "horse", Animals = 20, Capacity = 100 });
        HusbandryAcquisition.ResolveTaming(multi, null, new TurnRecord());
        Check(multi.Herds.Where(h => h.DomesticCamp == mc.Id).Sum(h => h.Animals) == 5,
            "bootstrap ceiling is per camp, not five animals for every species");

        var noKnowledge = Fixture(Technology.Cultivation); var nc = noKnowledge.Camps.Single();
        nc.Technologies[(int)Technology.AnimalHusbandry] = false;
        Check(HusbandryAcquisition.ResolveTaming(noKnowledge, null, new TurnRecord()) == 0,
            "proximity without Husbandry capability cannot create livestock");
        var noAnimals = Fixture(Technology.Cultivation); noAnimals.Herds.Clear();
        Check(HusbandryAcquisition.ResolveTaming(noAnimals, null, new TurnRecord()) == 0,
            "knowledge without wild animals cannot create livestock");
        var noSurplus = Fixture(Technology.Cultivation);
        noSurplus.Herds.Single().Animals = noSurplus.Config.GetSpecies("goat").MinimumAnimals;
        Check(HusbandryAcquisition.ResolveTaming(noSurplus, null, new TurnRecord()) == 0,
            "wild breeding reserve cannot be captured");
    }

    private static void KnowledgeLocality()
    {
        var paleo = Fixture(Technology.Cultivation); var pc = paleo.Camps.Single();
        pc.People.Progression.Era = Era.Paleolithic; pc.SpeciesExperience.Clear();
        pc.Technology = Technology.None; Array.Clear(pc.Technologies, 0, pc.Technologies.Length);
        TechnologyDiscovery.RecordHusbandryProximity(paleo);
        Check(pc.People.TechnologyExposure.All(x => x == 0) && pc.SpeciesExperience.Count == 0,
            "social exposure does not alter Paleolithic first-breakthrough species practice");

        var local = Fixture(Technology.Cultivation); var lc = local.Camps.Single();
        lc.Technologies[(int)Technology.AnimalHusbandry] = false; lc.SpeciesExperience.Clear();
        TechnologyDiscovery.RecordHusbandryProximity(local);
        Near(lc.People.KnowledgeExposure(Domain.Hunting), 2.5,
            "one year with twenty nearby animals grants the configured exposure");
        Near(lc.Experience[(int)Domain.Hunting], 0, "observational knowledge does not improve hunting proficiency");
        var far = Fixture(Technology.Cultivation); var fc = far.Camps.Single();
        fc.Technologies[(int)Technology.AnimalHusbandry] = false; fc.SpeciesExperience.Clear();
        far.Herds.Single().Tile = far.Map.Radius(fc.Tile, 2)
            .Where(t => far.Map.Distance(fc.Tile, t) == 2).First();
        TechnologyDiscovery.RecordHusbandryProximity(far);
        Near(fc.People.KnowledgeExposure(Domain.Hunting), 0, "nonlocal animals grant no proximity knowledge");

        var a = Fixture(Technology.Cultivation); var b = SaveCodec.Load(SaveCodec.Save(a));
        b.Camps.Reverse(); b.Herds.Reverse(); b.Societies.Reverse();
        HusbandryAcquisition.ResolveTaming(a, null, new TurnRecord());
        HusbandryAcquisition.ResolveTaming(b, null, new TurnRecord());
        TechnologyDiscovery.RecordHusbandryProximity(a); TechnologyDiscovery.RecordHusbandryProximity(b);
        Check(SaveCodec.Fingerprint(a) == SaveCodec.Fingerprint(b),
            "canonical capture and knowledge are independent of collection order and save/load");
    }

    public static int Run()
    {
        checks = 0; ResolvedResidentAcquisition(); LiquidationAndConservation(); KnowledgeLocality();
        Console.WriteLine("PASS: " + checks + " mixed settlement acquisition assertions");
        return checks;
    }
}
