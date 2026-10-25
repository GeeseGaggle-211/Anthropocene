using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Critic-only privileged view (intervention A, Docs/ElderModel9Contract7.md "Privileged critic view"). A versioned,
    // typed set of families describing hidden economic state at the decision boundary, emitted on the same village row
    // as the observation it belongs to. It is never part of ElderObservation, ElderModel9Tensor, the actor-labor catalog,
    // legality masks or any player/AI observation (ElderModel9PrivilegedChecks). Pure reads of committed state.
    //
    // Family 0 ("core") is the ten-float ElderModel9Contract.PrivilegedFeatures vector, unchanged, one row, no overflow.
    // Typed families have a preregistered row cap plus one overflow-aggregate row (always the last row): the column-wise
    // sum of every row beyond the cap, whose "row-count" column is the number of rows aggregated. Every row has a
    // presence bit; the overflow row is present only when something overflowed. Missing rows are zero and not present.
    public static class ElderModel9Privileged
    {
        public sealed class Family
        {
            public readonly string Name; public readonly int Cap; public readonly bool Overflow; public readonly string[] Features;
            internal Family(string name, int cap, bool overflow, string[] features) { Name = name; Cap = cap; Overflow = overflow; Features = features; }
            public int Rows => Cap + (Overflow ? 1 : 0);
            public int Width => Features.Length;
        }

        static readonly string[] Domains = { "hunting", "gathering", "fishing" };
        static readonly string[] Technologies = { "husbandry", "cultivation", "watercraft" };
        static readonly CraftGood[] Goods = (CraftGood[])Enum.GetValues(typeof(CraftGood));

        // Units: animals, people, food (food units), field-capital (CultivationCapital E), worker counts (residents
        // staffed for the turn), worker-years, native good units, material units; fractions in [0, 1]; flags 0/1.
        public static readonly string[] FieldFeatures =
        {
            "row-count", "distance-hex", "operated-by-camp", "foreign-operated", "access", "capital-E", "intensity-q",
            "operating-fraction", "food-stock", "biomass-stock", "land-condition", "vegetation-tier",
            "mean-soil-moisture", "mean-max-food", "mean-food-recovery-per-day", "crop-suitability", "expansion-available",
            "maintenance-farmers", "farm-tool-demand"
        };
        public static readonly string[] HerdFeatures =
        {
            "row-count", "animals", "species-index", "growth-rate-per-year", "food-per-animal-interval",
            "water-per-animal-interval", "starvation-mortality", "growth-remainder", "death-remainder", "food-nutrition",
            "water-nutrition", "nutrition", "unmet-forage", "unmet-water", "deficit-turns", "deficit-severity", "managed",
            "management-fulfilment", "breeding-intensity", "shared-zero-displacement-forage-capacity",
            "shared-absolute-forage-capacity", "shared-water-capacity", "animal-food-potential", "animal-food-produced"
        };
        public static readonly string[] UnlockFeatures = new[] { "row-count", "era-neolithic" }
            .Concat(Technologies.Select(t => "first-technology-" + t))
            .Concat(Technologies.Select(t => "acquired-" + t))
            .Concat(Domains.Select(d => "lifetime-practice-" + d))
            .Concat(Domains.Select(d => "neolithic-practice-" + d))
            .Concat(Domains.Select(d => "exposure-" + d))
            .Concat(Domains.Select(d => "progress-over-threshold-" + d))
            .Concat(Technologies.Select(t => "withheld-" + t))
            .Concat(new[] { "forced-transition", "secondary-domain-plus-one", "era-elapsed-years", "neolithic-technologies", "unlocked-specialists" })
            .ToArray();
        public static readonly string[] EconomyFeatures = new[] { "row-count", "residents", "linked-population", "food", "plant-food", "animal-food", "aquatic-food" }
            .Concat(Goods.Select(g => "stock-" + g))
            .Concat(new[] { "stone", "clay", "wood" })
            .Concat(Goods.Select(g => "recipe-available-" + g))
            .Concat(new[] { "farm-tool-farmer-years", "meals-per-turn", "clothing-need-units", "housing-capacity-people", "cloth-per-capita", "housing-per-capita" })
            .Concat(WorkforceRoles.All.Select(r => "workers-" + r))
            // Training-only counterpart stocks: the known trade partners Trade.Plan would select this turn.
            .Concat(new[] { "trade-partners", "partner-food" })
            .Concat(Goods.Select(g => "partner-stock-" + g))
            .ToArray();

        // Preregistered row caps: fields 16, herds 8, unlocks 1, economy 1, each with one overflow row.
        public static readonly Family[] Families =
        {
            new Family("core", 1, false, ElderModel9Contract.PrivilegedFeatures),
            new Family("fields", 16, true, FieldFeatures),
            new Family("herds", 8, true, HerdFeatures),
            new Family("unlocks", 1, true, UnlockFeatures),
            new Family("economy", 1, true, EconomyFeatures)
        };
        public const int Core = 0, Fields = 1, Herds = 2, Unlocks = 3, Economy = 4;

        // One pass per decision step over herds, societies, camps and hunters, shared by every village; each village
        // then costs O(its footprint + owned herds + contacts). No per-village world scan.
        internal sealed class Index
        {
            internal readonly Dictionary<int, List<Herd>> HerdsByCamp = new Dictionary<int, List<Herd>>();
            internal readonly Dictionary<int, int> DaughtersBySociety = new Dictionary<int, int>();
            internal readonly Dictionary<int, int> HunterPopulation = new Dictionary<int, int>();
            internal readonly Dictionary<int, Camp> Camps = new Dictionary<int, Camp>();
            internal Index(WorldState w)
            {
                foreach (var herd in w.Herds.Where(h => h.DomesticCamp >= 0).OrderBy(h => h.Id))
                {
                    if (!HerdsByCamp.TryGetValue(herd.DomesticCamp, out var list)) HerdsByCamp[herd.DomesticCamp] = list = new List<Herd>();
                    list.Add(herd);
                }
                foreach (var s in w.Societies) DaughtersBySociety[s.ParentSociety] = (DaughtersBySociety.TryGetValue(s.ParentSociety, out int n) ? n : 0) + 1;
                foreach (var h in w.Hunters) HunterPopulation[h.SourceCamp] = (HunterPopulation.TryGetValue(h.SourceCamp, out int n) ? n : 0) + h.Population;
                foreach (var c in w.Camps) Camps[c.Id] = c;
            }
            internal List<Herd> Owned(int camp) => HerdsByCamp.TryGetValue(camp, out var list) ? list : new List<Herd>();
        }

        // Family 0: exactly the ten ElderModel9Contract.PrivilegedFeatures floats (values identical to the legacy
        // computation; the herd profile and daughter count now come from the shared index).
        internal static float[] CoreVector(WorldState w, Camp camp, int priorPopulation, int age, Index index, out SettlementSiteEvaluation site)
        {
            var cfg = w.Config;
            double meals = Math.Max(1e-9, Math.Max(1, camp.Population) * cfg.Consumption * cfg.GenerationIntervals);
            var profile = HerdConsumptionProfile.FromHerds(w, index.Owned(camp.Id));
            site = SettlementSiteEvaluation.Evaluate(w, camp, camp.Tile, camp.Population, profile);
            double potential = 0, food = 0;
            foreach (int tile in w.Map.Radius(camp.Tile, 1))
            {
                var ecology = LazyEcology.ProjectEcology(w, tile, w.Days);
                potential += Math.Max(0, ecology.PotentialFood); food += Math.Max(0, ecology.Food);
            }
            double animals = Math.Max(1, profile.Animals);
            int daughters = index.DaughtersBySociety.TryGetValue(camp.SocietyId, out int d) ? d : 0;
            double housing = NeolithicProduction.FixedHousingAt(w, camp, w.Days) * cfg.Neolithic.HousingPeoplePerUnit / Math.Max(1, camp.Population);
            double growth = priorPopulation > 0 ? camp.Population / (double)priorPopulation - 1 : 0;
            return new[] { Math.Log(1 + Math.Max(0, camp.Population)), age, site.HumanFood / meals, potential / meals, food / meals,
                site.SustainableHerdCapacity / animals, site.AbsoluteHerdCapacity / animals, growth, daughters, housing }
                .Select(v => (float)(ElderRLContract.Finite(v) ? v : 0)).ToArray();
        }

        internal static ElderModel9PrivilegedView Build(WorldState w, Camp camp, int priorPopulation, int age, Index index)
        {
            var view = ElderModel9PrivilegedView.Empty();
            var core = CoreVector(w, camp, priorPopulation, age, index, out var site);
            Array.Copy(core, view.Rows[Core], core.Length); view.Present[Core][0] = true;
            FillRows(view, Fields, FieldRows(w, camp));
            FillRows(view, Herds, HerdRows(w, camp, index, site));
            FillRows(view, Unlocks, new[] { UnlockRow(w, camp) });
            FillRows(view, Economy, new[] { EconomyRow(w, camp, index) });
            return view;
        }

        static void FillRows(ElderModel9PrivilegedView view, int family, IEnumerable<double[]> rows)
        {
            var f = Families[family]; int r = 0;
            var overflow = new double[f.Width];
            bool overflowed = false;
            foreach (var row in rows)
            {
                if (row.Length != f.Width) throw new InvalidOperationException("Privileged " + f.Name + " row width " + row.Length + " != " + f.Width);
                if (r < f.Cap)
                {
                    for (int c = 0; c < f.Width; c++) view.Rows[family][r * f.Width + c] = (float)(ElderRLContract.Finite(row[c]) ? row[c] : 0);
                    view.Present[family][r] = true;
                }
                else
                {
                    overflowed = true;
                    for (int c = 0; c < f.Width; c++) overflow[c] += ElderRLContract.Finite(row[c]) ? row[c] : 0;
                }
                r++;
            }
            if (!f.Overflow || !overflowed) return;
            for (int c = 0; c < f.Width; c++) view.Rows[family][f.Cap * f.Width + c] = (float)overflow[c];
            view.Present[family][f.Cap] = true;
        }

        // Owned or operated tiles: the camp's cultivable footprint (radius 1, ownership or expansion candidates) plus any
        // radius-2 tile this camp still operates, by distance then tile ID.
        static IEnumerable<double[]> FieldRows(WorldState w, Camp camp)
        {
            var cfg = w.Config; var n = cfg.Neolithic;
            double years = cfg.TurnDays / cfg.DaysPerYear;
            double efficiency = CultivationEconomy.FarmToolFieldWorkEfficiency(n);
            double required = CultivationEconomy.Enabled(camp) ? CultivationEconomy.MaintenanceFarmersForPlanning(w, camp, years) : 0;
            int farmers = camp.Workforce.Assignments.Where(a => a.Role == WorkforceRoles.Farmer).Sum(a => a.Population);
            double operation = required > 1e-9 ? Numbers.Clamp(farmers / required, 0, 1) : 0;
            double ceiling = 1 / (cfg.ReferenceIntensity * cfg.ReferenceIntensity);
            var tiles = w.Map.Radius(camp.Tile, 2)
                .Where(t => w.Map.Distance(camp.Tile, t) <= 1 ? CultivationEconomy.Cultivable(w.Tiles[t]) : w.Tiles[t].CultivationOperator == camp.Id && w.Tiles[t].TraversableLand)
                .OrderBy(t => w.Map.Distance(camp.Tile, t)).ThenBy(t => t);
            foreach (int id in tiles)
            {
                var t = w.Tiles[id];
                var ecology = LazyEcology.ProjectEcology(w, id, w.Days);
                double capital = Math.Max(0, ecology.CultivationCapital);
                bool operated = t.CultivationOperator == camp.Id, foreign = t.CultivationOperator >= 0 && !operated;
                int distance = w.Map.Distance(camp.Tile, id);
                bool access = !foreign && distance <= 1;
                double moisture = t.Seasons.Average(s => (double)s.Moisture);
                double maintenance = operated || access
                    ? n.MaintenanceWorkersPerTileE(cfg.MaintenanceCoefficient) * CultivationEconomy.MaintenanceMultiplier(cfg, t, ecology.CurrentVegetation)
                        * CultivationEconomy.EffortForCapital(w, camp, capital) : 0;
                yield return new double[]
                {
                    1, distance, operated ? 1 : 0, foreign ? 1 : 0, access ? 1 : 0, capital, CultivationEconomy.Intensity(cfg, capital),
                    operated ? operation : 0, Math.Max(0, ecology.Food), Math.Max(0, ecology.PotentialFood), ecology.LandCondition, (int)ecology.CurrentVegetation,
                    moisture, t.Seasons.Average(s => (double)s.MaxFood), t.Seasons.Average(s => (double)s.FoodRecoveryPerDay),
                    EcologyMath.CropSuitability(cfg, moisture), access && capital < ceiling - 1e-12 ? 1 : 0,
                    maintenance, maintenance / Math.Max(1e-12, efficiency) * years / Math.Max(1e-12, n.FarmToolFarmerYearsPerUnit)
                };
            }
        }

        static IEnumerable<double[]> HerdRows(WorldState w, Camp camp, Index index, SettlementSiteEvaluation site)
        {
            var cfg = w.Config;
            foreach (var herd in index.Owned(camp.Id))
            {
                var species = cfg.GetSpecies(herd.Species);
                yield return new double[]
                {
                    1, herd.Animals, cfg.Species.IndexOf(species), species.GrowthRate, species.FoodConsumption, species.FreshwaterConsumption,
                    cfg.Mortality, herd.GrowthRemainder, herd.DeathRemainder, herd.LastFoodNutrition, herd.LastWaterNutrition, herd.LastNutrition,
                    Math.Max(0, herd.UnmetFood), Math.Max(0, herd.UnmetFreshwater), herd.DeficitTurns, herd.DeficitSeverity, 1,
                    herd.ManagementFulfillment, camp.ManagedBreedingIntensity,
                    site.Herd.ZeroDisplacementForageCapacity, site.Herd.AbsoluteForageCapacity, site.Herd.WaterCapacity,
                    herd.AnimalFoodPotential, herd.AnimalFoodProduced
                };
            }
        }

        static double[] UnlockRow(WorldState w, Camp camp)
        {
            var cfg = w.Config; var s = camp.People; var row = new List<double> { 1 };
            bool neolithic = s.Progression.Era == Era.Neolithic;
            row.Add(neolithic ? 1 : 0);
            for (int i = 0; i < 3; i++) row.Add(s.Technology == (Technology)(i + 1) ? 1 : 0);
            for (int i = 0; i < 3; i++) row.Add(s.Technologies[i + 1] || neolithic && s.Technology == (Technology)(i + 1) ? 1 : 0);
            for (int i = 0; i < 3; i++) row.Add(s.Experience[i]);
            for (int i = 0; i < 3; i++) row.Add(s.NeolithicPractice((Domain)i));
            for (int i = 0; i < 3; i++) row.Add(s.KnowledgeExposure((Domain)i));
            double threshold = neolithic ? cfg.SecondaryTechnologyThreshold : cfg.ExperienceThreshold;
            for (int i = 0; i < 3; i++) row.Add(TechnologyDiscovery.EffectiveProgress(s, (Domain)i, cfg) / Math.Max(1e-12, threshold));
            for (int i = 0; i < 3; i++) row.Add(cfg.TrainingWithheld((Technology)(i + 1)) ? 1 : 0);
            row.Add(s.ForcedTransition ? 1 : 0); row.Add(s.SecondaryDomain + 1);
            row.Add(Math.Max(0, w.Days - s.Progression.EraStartDays) / cfg.DaysPerYear);
            row.Add(s.NeolithicTechnologies?.Count ?? 0); row.Add(s.UnlockedSpecialists?.Count ?? 0);
            return row.ToArray();
        }

        static double[] EconomyRow(WorldState w, Camp camp, Index index)
        {
            var cfg = w.Config; var n = cfg.Neolithic; double years = cfg.TurnDays / cfg.DaysPerYear;
            int hunters = index.HunterPopulation.TryGetValue(camp.Id, out int away) ? away : 0;
            var row = new List<double> { 1, camp.Population, camp.Population + hunters, camp.Food, camp.PlantFood, camp.AnimalFood, camp.AquaticFood };
            foreach (var g in Goods) row.Add(GoodStock(w, camp, g));
            row.Add(camp.Inventory.Stone); row.Add(camp.Inventory.Clay); row.Add(camp.Inventory.Wood);
            var recipes = NeolithicProduction.Recipes(w, camp);
            foreach (var g in Goods) row.Add(recipes.Any(r => r.Good == g && NeolithicProduction.RecipeAvailable(w, camp, r)) ? 1 : 0);
            row.Add(Math.Max(0, camp.Inventory.FarmTools) * n.FarmToolFarmerYearsPerUnit);
            row.Add(Math.Max(1, camp.Population) * cfg.Consumption * cfg.GenerationIntervals);
            row.Add(NeolithicProduction.ClothingNeedUnits(n, camp.Population, years, camp.Inventory.Cloth, camp.Inventory.AnimalFabric));
            row.Add(NeolithicProduction.HousingCapacity(w, camp, w.Days).Total);
            row.Add(camp.ClothPerCapita); row.Add(camp.HousingPerCapita);
            foreach (string role in WorkforceRoles.All) row.Add(camp.Workforce.Assignments.Where(a => a.Role == role).Sum(a => a.Population));
            var partners = TradePartners(w, camp, index);
            row.Add(partners.Count); row.Add(partners.Sum(p => p.Food));
            foreach (var g in Goods) row.Add(partners.Sum(p => GoodStock(w, p, g)));
            return row.ToArray();
        }

        static double GoodStock(WorldState w, Camp c, CraftGood g)
            => g == CraftGood.Housing ? NeolithicProduction.FixedHousingAt(w, c, w.Days) : NeolithicProduction.Stock(c, g);

        // The remembered partners Trade.Plan would pair with this camp (other Societies' eligible camps at an unmoved
        // remembered tile within TradeRange, nearest first, at most TradeRequisitionsPerCamp). O(contacts).
        static List<Camp> TradePartners(WorldState w, Camp camp, Index index)
        {
            var cfg = w.Config;
            if (!Trade.Eligible(w, camp)) return new List<Camp>();
            return camp.People.Memory.Contacts
                .Where(k => !k.Moved && k.Tile >= 0 && index.Camps.TryGetValue(k.Camp, out var other) && Trade.Eligible(w, other) && other.SocietyId != camp.SocietyId)
                .Select(k => (k.Camp, Distance: w.Map.Distance(camp.Tile, k.Tile)))
                .Where(p => p.Distance <= cfg.TradeRange)
                .OrderBy(p => p.Distance).ThenBy(p => p.Camp).Take(cfg.TradeRequisitionsPerCamp)
                .Select(p => index.Camps[p.Camp]).ToList();
        }

        public static Dictionary<string, object> Layout() => new Dictionary<string, object>
        {
            ["schemaVersion"] = ElderModel9Contract.PrivilegedSchemaVersion,
            ["families"] = Families.Select(f => new Dictionary<string, object>
            { ["name"] = f.Name, ["cap"] = f.Cap, ["overflowRow"] = f.Overflow, ["rows"] = f.Rows, ["width"] = f.Width, ["features"] = f.Features }).ToArray()
        };
    }

    // The emitted critic-only view: per family, Rows x Width floats (row-major) and a per-row presence mask.
    [Serializable]
    public sealed class ElderModel9PrivilegedView
    {
        public int SchemaVersion = ElderModel9Contract.PrivilegedSchemaVersion;
        public float[][] Rows;
        public bool[][] Present;
        public static ElderModel9PrivilegedView Empty() => new ElderModel9PrivilegedView
        {
            Rows = ElderModel9Privileged.Families.Select(f => new float[f.Rows * f.Width]).ToArray(),
            Present = ElderModel9Privileged.Families.Select(f => new bool[f.Rows]).ToArray()
        };
        public ElderModel9PrivilegedView Copy() => new ElderModel9PrivilegedView
        { SchemaVersion = SchemaVersion, Rows = Rows.Select(r => (float[])r.Clone()).ToArray(), Present = Present.Select(p => (bool[])p.Clone()).ToArray() };
    }
}
