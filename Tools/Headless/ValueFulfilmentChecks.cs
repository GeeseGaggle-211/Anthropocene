using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Anthropocene.Simulation;

// WP3: local value rule (ValueFulfilment) and its planner wiring. Owns its worlds and counters.
internal static class ValueFulfilmentChecks
{
    private static void Check(ref int checks, bool ok, string message) { checks++; if (!ok) throw new Exception("Value fulfilment: " + message); }
    private static bool Info => Environment.GetEnvironmentVariable("WP3_INFO") == "1";

    private static WorldState Agricultural(bool value, int population = 100)
    {
        var w = NeolithicChecks.Fixture();
        int count = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash, ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash, Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[count], History = new ushort[count], Host = new byte[count], Marine = new byte[count], Stone = new ushort[count], Clay = new ushort[count], Wood = new ushort[count], WoodRecoveryPerYear = .05 };
        var c = w.Camps[0]; int tile = c.Tile;
        w.Resources.Stone[tile] = 1000; w.Resources.Clay[tile] = 1000; w.Resources.Wood[tile] = 1000;
        var t = w.Tiles[tile]; t.CultivationCapital = .1; t.Cultivation = (float)CultivationEconomy.Intensity(w.Config, .1); t.CultivationOperator = c.Id;
        c.Inventory.HandTools = 1000; c.Population = population;
        foreach (string tech in NeolithicTech.FirstTier) c.People.NeolithicTechnologies.Add(tech);
        // Both rules are named explicitly so these fixtures do not depend on the shipped default (Value).
        w.Config.Neolithic.PlannerFulfilment = value ? "Value" : "Uniform";
        return w;
    }

    private static ProductionClaim Claim(CraftGood good, double quantity, double stock, double labour, double v, bool valued = true)
        => new ProductionClaim { Good = good, Quantity = quantity, Stock = stock, Need = stock + quantity, LabourPerUnit = labour, FirstUnitValue = v, Value = v, Priority = 1, Valued = valued };

    private static List<ProductionClaim> RandomClaims(Random rng, int count)
    {
        var claims = new List<ProductionClaim>();
        for (int i = 0; i < count; i++)
        {
            double need = 1 + rng.NextDouble() * 200, cover = rng.NextDouble() * .9;
            bool valued = rng.NextDouble() < .8;
            var claim = Claim((CraftGood)(i % 9), need * (1 - cover), need * cover, .05 + rng.NextDouble() * 10, .5 + rng.NextDouble() * 300, valued);
            // Half the claims carry a reserve: useful demand beyond the need level, as clothing does.
            if (rng.NextDouble() < .5) claim.Need = (claim.Stock + claim.Quantity) / (1 + rng.NextDouble());
            claims.Add(claim);
        }
        return claims;
    }

    public static int Run()
    {
        int checks = 0;
        var defaults = new SimulationConfig();
        var n = defaults.Neolithic;

        // ---- Config: defaults, validation, save/config round trips, hash.
        Check(ref checks, n.PlannerFulfilment == "Value" && n.ValueCurveSteepness == 3 && n.DiscountRatePerYear == .01 && n.ValueFulfilmentSelected, "config defaults select Value");
        Check(ref checks, EconomicPlanner.ConfiguredRule(defaults) is ValueFulfilment, "default config selects the value rule");
        var uniformConfig = new SimulationConfig(); uniformConfig.Neolithic.PlannerFulfilment = "Uniform";
        Check(ref checks, ReferenceEquals(EconomicPlanner.ConfiguredRule(uniformConfig), EconomicPlanner.DefaultRule), "explicit Uniform selects the uniform rule instance");
        foreach (int which in new[] { 0, 1, 2, 3, 4 })
        {
            var bad = new SimulationConfig().Neolithic;
            if (which == 0) bad.PlannerFulfilment = "Best"; else if (which == 1) bad.PlannerFulfilment = null; else if (which == 2) bad.ValueCurveSteepness = 0; else if (which == 3) bad.DiscountRatePerYear = -.1; else bad.ValueCurveSteepness = double.NaN;
            bool rejected = false; try { bad.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(ref checks, rejected, "invalid value config " + which + " rejected");
        }
        var zeroRate = new SimulationConfig().Neolithic; zeroRate.DiscountRatePerYear = 0; zeroRate.PlannerFulfilment = "Value"; zeroRate.Validate();
        Check(ref checks, true, "zero discount rate is allowed");
        var saved = Agricultural(true); saved.Config.Neolithic.ValueCurveSteepness = 2.5; saved.Config.Neolithic.DiscountRatePerYear = .02;
        var loaded = SaveCodec.Load(SaveCodec.Save(saved));
        Check(ref checks, loaded.Config.Neolithic.PlannerFulfilment == "Value" && loaded.Config.Neolithic.ValueCurveSteepness == 2.5 && loaded.Config.Neolithic.DiscountRatePerYear == .02, "save round trip keeps the value config");
        Check(ref checks, SaveCodec.Fingerprint(saved) == SaveCodec.Fingerprint(loaded), "fingerprint stable across the value-config round trip");
        Check(ref checks, SaveCodec.ConfigurationHash(uniformConfig) != SaveCodec.ConfigurationHash(new SimulationConfig()), "the selected rule enters the configuration hash");
        var serializer = new DataContractSerializer(typeof(NeolithicConfig));
        string xml;
        using (var ms = new MemoryStream()) { serializer.WriteObject(ms, new SimulationConfig().Neolithic); xml = Encoding.UTF8.GetString(ms.ToArray()); }
        string stripped = Regex.Replace(xml, "<(PlannerFulfilment|ValueCurveSteepness|DiscountRatePerYear)>[^<]*</\\1>", "");
        Check(ref checks, stripped.Length < xml.Length && !stripped.Contains("PlannerFulfilment"), "fixture removed the value fields");
        NeolithicConfig old;
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(stripped))) old = (NeolithicConfig)serializer.ReadObject(ms);
        Check(ref checks, old.PlannerFulfilment == "Uniform" && old.ValueCurveSteepness == 3 && old.DiscountRatePerYear == .01, "legacy payload loads Uniform with default curve values");
        old.Validate();

        // ---- Rule properties on seeded random claim sets.
        var rng = new Random(20260929);
        int interiorChecked = 0;
        for (int trial = 0; trial < 60; trial++)
        {
            var claims = RandomClaims(rng, 3 + trial % 7);
            double lambda = .5 + rng.NextDouble() * 5;
            var rule = new ValueFulfilment(lambda);
            var previous = new double[claims.Count]; var shares = new double[claims.Count];
            bool anyNeed = claims.Any(ValueFulfilment.IsNeed);
            for (int step = 0; step <= 200; step++)
            {
                double s = step / 200.0;
                rule.Shares(claims, s, shares);
                for (int i = 0; i < shares.Length; i++)
                {
                    Check(ref checks, shares[i] >= 0 && shares[i] <= 1 && shares[i] >= previous[i], "shares nondecreasing in s (trial " + trial + ", claim " + i + ", s " + s + ")");
                    if (!ValueFulfilment.IsNeed(claims[i]) && anyNeed) Check(ref checks, shares[i] == s, "unpriced claims take the uniform share");
                }
                if (step == 0) Check(ref checks, shares.Select((x, i) => ValueFulfilment.IsNeed(claims[i]) ? x : 0).All(x => x == 0), "s = 0 pursues no need claim");
                if (step == 200) Check(ref checks, shares.Select((x, i) => ValueFulfilment.IsNeed(claims[i]) ? x : 1).All(x => x == 1), "s = 1 fully fulfils every need claim");
                Array.Copy(shares, previous, shares.Length);
            }
            if (!anyNeed) continue;
            foreach (double s in new[] { .13, .37, .5, .71, .88 })
            {
                rule.Shares(claims, s, shares);
                double price = rule.LaborPrice(claims, s);
                Check(ref checks, price > 0 && !double.IsNaN(price), "labour price is positive");
                for (int i = 0; i < claims.Count; i++)
                {
                    if (!ValueFulfilment.IsNeed(claims[i]) || shares[i] <= 1e-9 || shares[i] >= 1 - 1e-9) continue;
                    interiorChecked++;
                    double marginalPerLabour = rule.MarginalValue(claims[i], shares[i]) / claims[i].LabourPerUnit;
                    Check(ref checks, Math.Abs(marginalPerLabour - price) <= 1e-9 * price, "equimarginal: V exp(-lambda c) / l equals w (trial " + trial + ", claim " + i + ")");
                }
            }
            // Doubling one claim's V (a priority multiplier of 2) never lowers its own share at fixed s.
            for (int i = 0; i < claims.Count; i++)
            {
                if (!ValueFulfilment.IsNeed(claims[i])) continue;
                var doubled = claims.Select(x => { var y = Claim(x.Good, x.Quantity, x.Stock, x.LabourPerUnit, x.FirstUnitValue, x.Valued); y.Need = x.Need; return y; }).ToList();
                doubled[i].FirstUnitValue *= 2;
                var after = new double[claims.Count];
                for (int step = 0; step <= 40; step++)
                {
                    rule.Shares(claims, step / 40.0, shares); rule.Shares(doubled, step / 40.0, after);
                    Check(ref checks, after[i] >= shares[i] - 1e-12, "a higher V never lowers its own share (trial " + trial + ", claim " + i + ")");
                }
            }
            // Purity: the same inputs give the same shares, and the claims are untouched.
            var again = new double[claims.Count]; rule.Shares(claims, .37, shares); rule.Shares(claims, .37, again);
            Check(ref checks, shares.SequenceEqual(again), "rule is deterministic");
        }
        Check(ref checks, interiorChecked > 100, "equimarginal check covered interior claims (" + interiorChecked + ")");
        var none = new List<ProductionClaim> { Claim(CraftGood.Pots, 5, 0, 0, 0, false) };
        var noneShares = new double[1]; new ValueFulfilment(3).Shares(none, .4, noneShares);
        Check(ref checks, noneShares[0] == .4 && double.IsNaN(new ValueFulfilment(3).LaborPrice(none, .4)), "without a need claim the rule is uniform and reports no price");
        var free = new List<ProductionClaim> { Claim(CraftGood.Cloth, 10, 0, 1, 0), Claim(CraftGood.Housing, 10, 0, 1, 5) };
        var freeShares = new double[2]; new ValueFulfilment(3).Shares(free, .5, freeShares);
        Check(ref checks, freeShares[0] == 0 && freeShares[1] > 0, "a priced good with zero value (priority 0) gets share 0 at any s");

        // ---- Value inputs on a real Agricultural fixture camp.
        var vw = Agricultural(true); var vc = vw.Camps[0];
        string before = SaveCodec.Fingerprint(vw);
        var demand = CampProductionDemand.Build(vw, vc, EconomicIntent.From(vc));
        Check(ref checks, before == SaveCodec.Fingerprint(vw), "value demand pass does not mutate the world");
        var cloth = demand.Claims.Single(x => x.Good == CraftGood.Cloth); var housing = demand.Claims.Single(x => x.Good == CraftGood.Housing);
        var nn = vw.Config.Neolithic;
        double e = nn.RudimentaryExpectationFloor, sCoh = NeolithicProduction.SocialCohesion(nn, vc, vc.Population);
        double k = -Math.Log(1 - nn.HousingDecayPerTurn) / nn.ReferenceTurnYears, life = 1 / (k + nn.DiscountRatePerYear);
        Check(ref checks, Math.Abs(cloth.FirstUnitValue - .5 * e * sCoh * 20) < 1e-12 && cloth.Value == cloth.FirstUnitValue, "Cloth V = 1/2 E B S 20 and Value carries it");
        Check(ref checks, Math.Abs(housing.FirstUnitValue - 4 * .5 * e * sCoh * life) < 1e-12, "Housing V = 4 * 1/2 * E B S * discounted life");
        double perYear = vw.Config.DaysPerYear / vw.Config.BaseDays, gather = vw.Config.GatherPerPerson * perYear;
        Check(ref checks, Math.Abs(cloth.LabourPerUnit - (1 / (cloth.Rate * perYear) + nn.PlantFoodPerFabricUnit / gather)) < 1e-12, "Cloth l = weaving time + plant feedstock gathering");
        // Housing runs as two Builder steps (Housing Material, then assembly) at twice the old rate each,
        // so building time per unit is unchanged: 1 / (BuilderHousingOutputPerBaseDays * perYear * multiplier).
        double housingLabour = 2 / (housing.Rate * perYear * demand.HousingMultiplier) + nn.HousingMaterialUnitsPerHousing / (nn.SpecialistMaterialExtractionRate * perYear);
        Check(ref checks, Math.Abs(housing.Rate - 2 * nn.BuilderHousingOutputPerBaseDays) < 1e-15 && Math.Abs(2 / (housing.Rate * perYear * demand.HousingMultiplier) - 1 / (nn.BuilderHousingOutputPerBaseDays * perYear * demand.HousingMultiplier)) < 1e-12, "Housing claim rate is the split-step rate and building time per unit is unchanged");
        Check(ref checks, Math.Abs(housing.LabourPerUnit - housingLabour) < 1e-12, "Housing l = two-step building time + construction material extraction");
        Check(ref checks, Math.Abs(cloth.Need - demand.Preview.Population * vw.Config.Neolithic.FabricConsumptionPerPersonTurn * (vw.Config.TurnDays / vw.Config.DaysPerYear) / vw.Config.Neolithic.ReferenceTurnYears) < 1e-9 && cloth.Stock == Math.Max(0, vw.Camps.First(x => x.Id == demand.Preview.Id).Inventory.Cloth) + Math.Max(0, vw.Camps.First(x => x.Id == demand.Preview.Id).Inventory.AnimalFabric) && housing.Need == housing.Stock + housing.Quantity && housing.Stock == NeolithicProduction.HousingSupply(vw, demand.Preview, vw.Days + 20 * vw.Config.DaysPerYear), "clothing need is this turn's requirement over the shared clothing stock; shelter need is stock plus useful quantity");
        if (Info)
            foreach (var claim in new[] { cloth, housing })
                Console.WriteLine("WP3_INFO " + claim.Good + " V=" + claim.FirstUnitValue.ToString("F4") + " l=" + claim.LabourPerUnit.ToString("F4") + " V/l=" + (claim.FirstUnitValue / claim.LabourPerUnit).ToString("F3") + " D=" + claim.Need.ToString("F2") + " stock=" + claim.Stock.ToString("F2") + " Q=" + claim.Quantity.ToString("F2") + " E=" + e + " S=" + sCoh.ToString("F4") + " L=" + life.ToString("F2"));
        // A doubled priority doubles V; zero priority disables the good.
        vc.CraftingPriorities.Cloth *= 2;
        var doubledDemand = CampProductionDemand.Build(vw, vc, EconomicIntent.From(vc));
        Check(ref checks, Math.Abs(doubledDemand.Claims.Single(x => x.Good == CraftGood.Cloth).FirstUnitValue - 2 * cloth.FirstUnitValue) < 1e-12, "priority multiplier doubles V");
        vc.CraftingPriorities.Cloth = 0;
        var zeroDemand = CampProductionDemand.Build(vw, vc, EconomicIntent.From(vc));
        var zeroCloth = zeroDemand.Claims.Single(x => x.Good == CraftGood.Cloth);
        var zeroShares = new double[zeroDemand.Claims.Count]; int clothIndex = zeroDemand.Claims.ToList().IndexOf(zeroCloth);
        bool zeroAlways = true;
        foreach (double s in new[] { 0, .2, .5, .9, 1 }) { new ValueFulfilment(3).Shares(zeroDemand.Claims, s, zeroShares); zeroAlways &= zeroShares[clothIndex] == 0; }
        Check(ref checks, zeroCloth.FirstUnitValue == 0 && zeroAlways, "priority 0 gives Cloth zero value and share 0 at every s");
        var zeroPlan = EconomicPlanner.Plan(vw, vc, EconomicIntent.From(vc));
        Check(ref checks, !zeroPlan.Assignments.Any(a => a.Role == WorkforceRoles.Weaver), "priority 0 stages no Weavers under the Value rule");

        // ---- Explicit Uniform config: legacy behaviour and claim contents unchanged (the shipped default is Value).
        var uw = Agricultural(false); var uc = uw.Camps[0];
        var ud = CampProductionDemand.Build(uw, uc, EconomicIntent.From(uc));
        Check(ref checks, ud.Claims.All(x => x.Value == x.Priority), "Uniform config keeps Value = Priority");
        var defaultPlan = EconomicPlanner.Plan(uw, uc, EconomicIntent.From(uc), UniformFulfilment.Instance, out double uniformPrice);
        Check(ref checks, double.IsNaN(uniformPrice), "uniform rule reports no labour price");
        Check(ref checks, Sig(defaultPlan) == Sig(EconomicPlanner.Plan(uw, uc, EconomicIntent.From(uc))), "Uniform config Plan uses the uniform rule");

        // ---- Ample labour: Value pursues everything Uniform does at s = 1.
        var aw = Agricultural(true, 100); var ac = aw.Camps[0]; var au = Agricultural(false, 100);
        var valuePlan = EconomicPlanner.Plan(aw, ac, EconomicIntent.From(ac), new ValueFulfilment(3), out double ample);
        var uniformPlan = EconomicPlanner.Plan(au, au.Camps[0], EconomicIntent.From(au.Camps[0]));
        Check(ref checks, Sig(valuePlan) == Sig(uniformPlan), "with ample labour the Value plan equals full fulfilment (" + Sig(valuePlan) + ")");
        Check(ref checks, valuePlan.Assignments.Any(a => a.Role == WorkforceRoles.Weaver), "ample plan staffs Weavers (" + Sig(valuePlan) + ")");
        var ampleDemand = CampProductionDemand.Build(aw, ac, EconomicIntent.From(ac));
        Check(ref checks, Math.Abs(ample - new ValueFulfilment(3).LaborPrice(ampleDemand.Claims, 1)) < 1e-9 * ample, "slack labour reports the lowest price w_lo");

        // ---- Scarce labour: the price rises, staffing stays resident bounded, planning is pure.
        var sw = Agricultural(true, 14); var sc = sw.Camps[0];
        string beforeScarce = SaveCodec.Fingerprint(sw);
        var scarcePlan = EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc), new ValueFulfilment(3), out double scarce);
        Check(ref checks, beforeScarce == SaveCodec.Fingerprint(sw), "value planning does not mutate the world");
        Check(ref checks, scarcePlan.Assignments.Sum(a => a.Population) <= sc.Population, "scarce Value plan stays within residents");
        Check(ref checks, scarce >= ample * (1 - 1e-12), "scarce labour cannot lower the labour price (" + scarce + " vs " + ample + ")");
        Check(ref checks, Sig(EconomicPlanner.Plan(sw, sc, EconomicIntent.From(sc))) == Sig(scarcePlan), "config-selected rule matches the explicit Value rule");
        var dw = NeolithicChecks.Fixture(); Check(ref checks, dw.Config.Neolithic.ValueFulfilmentSelected && EconomicPlanner.ConfiguredRule(dw.Config) is ValueFulfilment, "untouched fixture config plans with the Value rule by default");

        // ---- Husbandry fixture exercises Animal Fabric / Light Housing and the ordering guard.
        var hw = PastoralCraftChecks.PlannerWorld(); var hc = hw.Camps.Single();
        foreach (var tech in new[] { NeolithicTech.Toolmaking, NeolithicTech.Carpentry, NeolithicTech.Weaving, NeolithicTech.Construction }) hc.People.NeolithicTechnologies.Add(tech);
        hc.Inventory.Stone = hc.Inventory.Wood = 1000; hc.Inventory.HandTools = 1000;
        hw.Config.Neolithic.PlannerFulfilment = "Value";
        string beforeHusbandry = SaveCodec.Fingerprint(hw);
        var hd = CampProductionDemand.Build(hw, hc, EconomicIntent.From(hc));
        var fabric = hd.Claims.Single(x => x.Good == CraftGood.AnimalFabric); var shelter = hd.Claims.Single(x => x.Good == CraftGood.LightHousing);
        Check(ref checks, fabric.Valued && shelter.Valued && shelter.LabourPerUnit > fabric.LabourPerUnit, "Light Housing l includes its Animal Fabric input");
        var hp = EconomicPlanner.Plan(hw, hc, EconomicIntent.From(hc), new ValueFulfilment(3), out double herdPrice);
        Check(ref checks, beforeHusbandry == SaveCodec.Fingerprint(hw) && hp.Assignments.Sum(a => a.Population) <= hc.Population && !double.IsNaN(herdPrice), "husbandry Value planning is pure, bounded and priced");
        if (Info) Console.WriteLine("WP3_INFO husbandry plan " + Sig(hp) + " w=" + herdPrice.ToString("F3") + " fabricV/l=" + (fabric.FirstUnitValue / fabric.LabourPerUnit).ToString("F2") + " shelterV/l=" + (shelter.FirstUnitValue / shelter.LabourPerUnit).ToString("F2"));

        // ---- Determinism: scalar versus reversed 4-worker planning under the Value config.
        var reference = new WorldGenerator().Generate(53, SimulationConfig.Small(4));
        reference.Config.Neolithic.PlannerFulfilment = "Value";
        var workers = new[] { new PhaseExecution(2, 7, false, 1), new PhaseExecution(4, 3, true, 1) };
        var worlds = workers.Select(x => SaveCodec.Load(SaveCodec.Save(reference))).ToArray();
        Check(ref checks, worlds.All(x => x.Config.Neolithic.ValueFulfilmentSelected), "replay worlds carry the Value config");
        int turns = 0, post = 0, agricultural = 0;
        while ((!reference.Finished && turns < 12) || post < 6)
        {
            if (turns >= 12 && !reference.Finished) throw new Exception("Value replay opening did not finish within 12 turns.");
            if (reference.Finished) post++;
            if (reference.Camps.Any(CultivationEconomy.Enabled)) agricultural++;
            var plan = AiPlanning.Decide(reference); new TurnResolver().Resolve(reference, plan.Orders, plan.Traces);
            for (int i = 0; i < worlds.Length; i++)
            {
                var w = worlds[i]; w.Camps.Reverse(); w.Hunters.Reverse(); w.Herds.Reverse();
                var parallel = AiPlanning.Decide(w, workers[i]);
                new TurnResolver(workers[i]).Resolve(w, parallel.Orders, parallel.Traces);
                Check(ref checks, SaveCodec.Fingerprint(reference) == SaveCodec.Fingerprint(w), "Value replay matches scalar for worker layout " + i + " at turn " + turns);
            }
            turns++;
        }
        Check(ref checks, agricultural > 0, "Value replay reached active agriculture");

        Console.WriteLine("PASS: " + checks + " value fulfilment assertions");
        return checks;
    }

    // Order-stable summary of a plan's assignments.
    private static string Sig(EconomicIntent e) => string.Join(",", e.Assignments.OrderBy(a => a.Role, StringComparer.Ordinal).Select(a => a.Role + a.Population)) + "|" + Math.Round(e.Labor?.MaterialGatherers ?? 0, 9) + "|" + Math.Round(e.Labor?.ExpansionFarmers ?? 0, 9);
}
