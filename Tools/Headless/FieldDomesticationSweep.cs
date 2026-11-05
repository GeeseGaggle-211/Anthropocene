using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class FieldDomesticationSweep
{
    private sealed class Result
    {
        public string Cohort; public int Seed, Threshold, Society, VillageTurn = -1, FirstManaged = -1, MaxContact, SelectedTurns, DeficitTurns, SelectedLost;
        public int InitialPeople, FinalPeople, HerdsConverted, AnimalsConverted; public double HumanStarvation;
    }
    private static readonly List<Result> results = new List<Result>();
    public static void Run(string[] args)
    {
        string output = args.FirstOrDefault(a => a.StartsWith("--issue12-output="))?.Substring("--issue12-output=".Length) ?? "TestResults/Issue12";
        Directory.CreateDirectory(output); results.Clear();
        bool quick = args.Contains("--issue12-quick");
        int naturalSeeds = quick ? 1 : 6, controlledSeeds = quick ? 4 : 40;
        foreach (int seed in Enumerable.Range(1, naturalSeeds))
        {
            var cfg = JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"), new JsonSerializerOptions { IncludeFields = true });
            cfg.Width = 96; cfg.Height = 48; cfg.Players = 8; cfg.StartSeparation = 9;
            cfg.Shape = (WorldShape)((seed - 1) % 3);
            var initial = new WorldGenerator().Generate(seed, cfg);
            byte[] saved = SaveCodec.Save(initial);
            foreach (int threshold in new[] { 0, 5, 6 })
            {
                var w = SaveCodec.Load(saved); w.Config.FieldDomesticationEnabled = threshold != 0;
                if (threshold != 0) w.Config.FieldContactTurns = threshold;
                RunWorld(w, "natural-" + cfg.Shape, seed, threshold, 30, true);
            }
            Console.WriteLine("Natural seed " + seed + " complete.");
        }
        foreach (string supply in new[] { "supplied", "limited" })
        foreach (int distance in new[] { 2, 4, 6 })
        foreach (int seed in Enumerable.Range(1, controlledSeeds))
        {
            var initial = FieldDomesticationChecks.Fixture(distance); initial.Seed = seed;
            initial.Config.WanderChance = .05; initial.Config.GetSpecies("goat").GrowthRate = .0201;
            if (supply == "limited")
                foreach (var t in initial.Tiles)
                {
                    t.Freshwater = 100; t.PotentialFood = 800; t.Food = 80;
                    for (int i = 0; i < 4; i++) { t.Seasons[i].MaxFreshwater = 100; t.Seasons[i].MaxPotentialFood = 800; t.Seasons[i].MaxFood = 80; }
                }
            byte[] saved = SaveCodec.Save(initial);
            foreach (int threshold in new[] { 5, 6 })
            {
                var w = SaveCodec.Load(saved); w.Config.FieldContactTurns = threshold;
                RunWorld(w, "controlled-" + supply + "-d" + distance, seed, threshold, 24, false);
            }
        }
        using (var csv = new StreamWriter(Path.Combine(output, "cohorts.csv")))
        {
            csv.WriteLine("cohort,seed,threshold,society,village_turn,first_managed_turn,turns_from_village,max_contact,selected_herd_turns,selected_deficit_turns,selected_herds_lost,herds_converted,animals_converted,initial_people,final_people,human_starvation_deaths");
            foreach (var r in results)
                csv.WriteLine(string.Join(",", r.Cohort, r.Seed, r.Threshold, r.Society, r.VillageTurn, r.FirstManaged, r.FirstManaged < 0 || r.VillageTurn < 0 ? -1 : r.FirstManaged - r.VillageTurn, r.MaxContact, r.SelectedTurns, r.DeficitTurns, r.SelectedLost, r.HerdsConverted, r.AnimalsConverted, r.InitialPeople, r.FinalPeople, r.HumanStarvation.ToString("R", CultureInfo.InvariantCulture)));
        }
        using (var summary = new StreamWriter(Path.Combine(output, "summary.txt")))
        {
            foreach (var group in results.GroupBy(r => (r.Cohort, r.Threshold)).OrderBy(g => g.Key.Cohort).ThenBy(g => g.Key.Threshold))
            {
                var villages = group.Where(r => r.VillageTurn >= 0).ToArray(); var discovered = villages.Where(r => r.FirstManaged >= 0).ToArray();
                var turns = discovered.Select(r => r.FirstManaged - r.VillageTurn).OrderBy(n => n).ToArray();
                string line = group.Key + " agriculture_societies=" + group.Count() + " villages=" + villages.Length + " discovered=" + discovered.Length + " probability=" + (discovered.Length / (double)Math.Max(1, villages.Length)).ToString("F3", CultureInfo.InvariantCulture)
                    + " first_managed_delay_median=" + (turns.Length == 0 ? "NA" : turns[turns.Length / 2].ToString()) + " selected_herd_turns=" + group.Sum(r => r.SelectedTurns) + " deficit_turns=" + group.Sum(r => r.DeficitTurns) + " selected_lost=" + group.Sum(r => r.SelectedLost) + " max_contact=" + group.Max(r => r.MaxContact) + " human_starvation=" + group.Sum(r => r.HumanStarvation).ToString("F2", CultureInfo.InvariantCulture);
                summary.WriteLine(line); Console.WriteLine(line);
            }
            summary.WriteLine("Natural denominator: all Agriculture-primary societies (including daughters) that formed a village by turn 30. Controls match generation and all AI behavior; threshold=0 disables only field domestication. Controlled denominator: one pre-established village per seed, 24 actual resolver turns, no scripted herd moves or contact credits. Limited supply uses 800 biomass / 80 edible / 100 water per tile. Wild shortfalls in the existing model reduce reproduction and drive movement, but do not kill wild animals: deficit exposure is reported separately, not interpreted as zero starvation risk. No rapid transfers: every transfer checked against the prior same-herd/Society streak >= threshold-1.");
        }
        Scale(output);
    }
    private static void RunWorld(WorldState w, string cohort, int seed, int threshold, int horizon, bool ai)
    {
        var stats = new Dictionary<int, Result>(); var resolver = new TurnResolver();
        void Enroll()
        {
            foreach (var society in w.Societies.Where(s => s.Technology == Technology.Cultivation))
            {
                if (!stats.TryGetValue(society.Id, out var r))
                {
                    r = new Result { Cohort = cohort, Seed = seed, Threshold = threshold, Society = society.Id, InitialPeople = w.Camps.Where(c => c.SocietyId == society.Id).Sum(w.LinkedPopulation) }; stats.Add(society.Id, r);
                }
                if (r.VillageTurn < 0 && w.Camps.Any(c => c.SocietyId == society.Id && c.IsVillage)) r.VillageTurn = w.Turn;
            }
        }
        Enroll();
        while (w.Turn < horizon && w.Camps.Any(c => w.LinkedPopulation(c) > 0))
        {
            var wild = w.Herds.Where(h => h.DomesticCamp < 0).ToDictionary(h => h.Id);
            var contacts = wild.Values.ToDictionary(h => h.Id, h => h.FieldContacts?.ToDictionary(c => c.Society, c => c.Turns) ?? new Dictionary<int, int>());
            var selected = new FieldDomestication(w, new PlanningIndex(w)).SelectedContacts.ToArray();
            var alreadyManaged = stats.Values.Where(r => r.FirstManaged >= 0).Select(r => r.Society).ToHashSet();
            var decisions = ai ? AiPlanning.Decide(w) : null;
            var record = resolver.Resolve(w, decisions?.Orders ?? new Dictionary<int, List<Order>>(), decisions?.Traces);
            Enroll();
            foreach (var h in w.Herds.Where(h => h.DomesticCamp >= 0 && wild.ContainsKey(h.Id)))
                if (stats.TryGetValue(h.OwnerSociety, out var r))
                {
                    if (!contacts[h.Id].TryGetValue(h.OwnerSociety, out int prior) || prior < threshold - 1) throw new Exception("Rapid false-positive domestication");
                    if (r.FirstManaged < 0) r.FirstManaged = w.Turn;
                    r.HerdsConverted++; r.AnimalsConverted += h.Animals;
                }
            foreach (var r in stats.Values)
            {
                r.FinalPeople = w.Camps.Where(c => c.SocietyId == r.Society).Sum(w.LinkedPopulation);
                var campIds = w.Camps.Where(c => c.SocietyId == r.Society).Select(c => c.Id).ToHashSet();
                r.HumanStarvation += record.Demographics.Where(d => campIds.Contains(d.Camp)).Sum(d => d.StarvationDeaths);
                r.MaxContact = Math.Max(r.MaxContact, w.Herds.SelectMany(h => h.FieldContacts ?? new List<HerdContact>()).Where(c => c.Society == r.Society).Select(c => c.Turns).DefaultIfEmpty(0).Max());
                // Exact pre-resolution selection, including the successful transfer
                // turn and herds that move out of detection or lose their village.
                if (alreadyManaged.Contains(r.Society)) continue;
                foreach (var pair in selected.Where(p => p.Society == r.Society))
                {
                    var h = wild[pair.Herd];
                    r.SelectedTurns++;
                    if (h.UnmetFood > 1e-8 || h.UnmetFreshwater > 1e-8) r.DeficitTurns++;
                    if (!w.Herds.Contains(h) || h.Animals <= 0) r.SelectedLost++;
                }
            }
        }
        results.AddRange(stats.Values.OrderBy(r => r.Society));
    }
    private static void Scale(string output)
    {
        using var csv = new StreamWriter(Path.Combine(output, "scale.csv"));
        csv.WriteLine("villages,herds,width,height,repetitions,median_ms,allocated_bytes_per_pass");
        foreach (int count in new[] { 8, 80, 320, 1280 })
        {
            var w = FieldDomesticationChecks.Fixture(); var template = w.Tiles[0];
            w.Config.Width = 320; w.Config.Height = 320; w.Config.FieldContactTurns = 100;
            w.Tiles = new Tile[320 * 320];
            for (int i = 0; i < w.Tiles.Length; i++) w.Tiles[i] = new Tile { Id = i, ElevationLevel = 1, Terrain = Terrain.Grassland, Seasons = template.Seasons.ToArray() };
            var owner = w.Camps[0].People; w.Camps.Clear(); w.Herds.Clear();
            for (int i = 0; i < count; i++)
            {
                int tile = (i / 40 * 8 + 4) * 320 + i % 40 * 8 + 4;
                var c = new Camp { Id = w.NextId++, Player = 0, SocietyId = owner.Id, Tile = tile, Population = 24, IsVillage = true }; w.Camps.Add(c);
                w.Herds.Add(new Herd { Id = w.NextId++, Tile = tile, Animals = 8, Species = "goat" });
                w.Tiles[tile].CultivationCapital = 1; w.Tiles[tile].CultivationOperator = c.Id;
            }
            Ownership.Bind(w); var index = new PlanningIndex(w); var samples = new List<double>(); long allocated = 0;
            for (int i = 0; i < 12; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
                var fields = new FieldDomestication(w, index); fields.Resolve(w, new TurnRecord());
                clock.Stop(); if (i >= 2) { samples.Add(clock.Elapsed.TotalMilliseconds); allocated += GC.GetAllocatedBytesForCurrentThread() - before; }
            }
            samples.Sort(); string row = string.Join(",", count, count, 320, 320, 10, samples[5].ToString("F3", CultureInfo.InvariantCulture), allocated / 10);
            csv.WriteLine(row); Console.WriteLine("Scale " + row);
        }
    }
}
