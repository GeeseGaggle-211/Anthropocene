#!/usr/bin/env python3
"""One-time development patch. Exact anchors; never touches simulation calibration."""
from pathlib import Path

p = Path('Assets/_Anthropocene/Simulation/Societies.cs')
s = p.read_text()
start = s.index('        public static double RecordHusbandryProximity(WorldState world)\n')
end = s.index('        // Progress toward a technology.', start)
s = s[:start] + '''        public static double RecordHusbandryProximity(WorldState world)
        {
            if (world == null || !(world.Config.HusbandryProximityPracticePerYear > 0)) return 0;
            double years = Math.Max(0, world.Config.TurnDays / world.Config.DaysPerYear);
            if (!(years > 0)) return 0;
            // O(H + C + local contacts), apart from canonical sorting. The old
            // society-by-all-herds scan was not suitable for mature RL worlds.
            var byTile = world.Herds.Where(h => h.Animals > 0 &&
                    world.Config.GetSpecies(h.Species).Domesticable)
                .OrderBy(h => h.Id).ToLookup(h => h.Tile);
            var campsBySociety = world.Camps.Where(c => c.Population > 0)
                .OrderBy(c => c.Id).ToLookup(c => c.SocietyId);
            double totalCredited = 0;
            foreach (var society in world.Societies.Where(s => s.Progression.Era == Era.Neolithic)
                .OrderBy(s => s.Id))
            {
                var seen = new HashSet<int>();
                var bySpecies = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var camp in campsBySociety[society.Id])
                    foreach (int tile in world.Map.Radius(camp.Tile, 1))
                        foreach (var herd in byTile[tile])
                        {
                            if (herd.DomesticCamp >= 0 && herd.OwnerSociety == society.Id ||
                                !seen.Add(herd.Id)) continue;
                            double weight = herd.Animals * (herd.DomesticCamp >= 0
                                ? world.Config.HusbandryDomesticExposureMultiplier : 1);
                            bySpecies[herd.Species] = (bySpecies.TryGetValue(herd.Species,
                                out double prior) ? prior : 0) + weight;
                        }
                var species = bySpecies.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
                double weightedAnimals = species.Sum(p => p.Value);
                if (!(weightedAnimals > 0)) continue;
                double amount = years * world.Config.HusbandryProximityPracticePerYear *
                    Math.Min(1, weightedAnimals / world.Config.HusbandryProximityReferenceAnimals);
                if (!Holds(society, Technology.AnimalHusbandry))
                    totalCredited += AddKnowledgeExposure(world, society, Domain.Hunting,
                        amount, PracticeSource.Proximity);
                foreach (var pair in species)
                    AddSpeciesExposure(society, pair.Key, amount * pair.Value / weightedAnimals);
            }
            return totalCredited;
        }

''' + s[end:]
p.write_text(s)

p = Path('Tools/Headless/Program.cs'); s = p.read_text()
anchor = '                new ParallelChecks.Entry { Name = "HusbandryDiffusionChecks", Run = () => HusbandryDiffusionChecks.Run() },'
assert s.count(anchor) == 1
if 'Name = "MixedSettlementAcquisitionChecks"' not in s:
    s = s.replace(anchor, anchor + '\n                new ParallelChecks.Entry { Name = "MixedSettlementAcquisitionChecks", Run = () => MixedSettlementAcquisitionChecks.Run() },')
p.write_text(s)
p = Path('Tools/Headless/Anthropocene.Headless.csproj'); s = p.read_text()
anchor = '  <Compile Include="HusbandryDiffusionChecks.cs" />'
assert s.count(anchor) == 1
if 'MixedSettlementAcquisitionChecks.cs' not in s:
    s = s.replace(anchor, anchor + '\n  <Compile Include="MixedSettlementAcquisitionChecks.cs" />')
p.write_text(s)
print('Applied local knowledge indexing and registered resident-acquisition regressions.')
