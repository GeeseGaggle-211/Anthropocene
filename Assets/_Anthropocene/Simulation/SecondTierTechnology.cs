using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Shared activity-driven craft discovery. Each defined track supplies its
    // prerequisite, useful-output predicate and actual-activity context. Undefined
    // Kiln/Plough/Irrigation effects remain inert. Uses the existing common craft
    // mastery units (output / recipe base rate) and configured threshold.
    public static class SecondTierTechnology
    {
        private sealed class Track
        {
            public string Technology, Prerequisite, Role;
            public Func<CraftGood, bool> Output;
        }
        private static readonly Track[] Tracks =
        {
            new Track { Technology = NeolithicTech.Sailing, Prerequisite = NeolithicTech.Weaving,
                Role = WorkforceRoles.Weaver, Output = good => good == CraftGood.Cloth || good == CraftGood.AnimalFabric }
        };

        internal static void Resolve(WorldState world, TurnRecord record)
        {
            if (!world.Config.NeolithicSailingEnabled) return;
            var camps = world.Camps.ToDictionary(c => c.Id);
            var hunters = world.Hunters.ToDictionary(h => h.Id);
            var activeWatercraft = new HashSet<int>();
            foreach (var receipt in record.Transfers)
            {
                if (receipt.Food <= 0) continue;
                if (receipt.Kind == "FisheryHarvest") activeWatercraft.Add(receipt.Entity);
                else if (receipt.Kind == "Fishing" && hunters.TryGetValue(receipt.Entity, out var hunter) &&
                    world.Tiles[hunter.Tile].Environment == EnvironmentKind.Shallows)
                    activeWatercraft.Add(hunter.SourceCamp);
            }
            // Positive authoritative receipts already prove legal navigable-water activity;
            // do not rebuild visibility or search the world for each productive camp.
            var recipes = NeolithicProduction.Recipes(world.Config).ToDictionary(r => r.Good);
            foreach (var track in Tracks)
            {
                // Canonical camp/receipt order is independent of entity collection order.
                foreach (var group in record.Production.Where(p => p.Role == track.Role && track.Output(p.Good) && p.Output > 0)
                    .GroupBy(p => p.Camp).OrderBy(g => g.Key))
                {
                    if (!camps.TryGetValue(group.Key, out var camp) || !FishingEconomy.FeatureEnabled(world, camp) ||
                        !camp.People.HasTech(track.Prerequisite) || camp.People.HasTech(track.Technology) ||
                        !activeWatercraft.Contains(camp.Id)) continue;
                    double work = group.OrderBy(p => p.Good).ThenBy(p => p.Output)
                        .Sum(p => p.Output / recipes[p.Good].Rate);
                    camp.People.CraftExperience.TryGetValue(track.Technology, out double prior);
                    double progress = prior + work;
                    camp.People.CraftExperience[track.Technology] = progress;
                    if (progress < world.Config.Neolithic.CraftMasteryThreshold) continue;
                    camp.People.NeolithicTechnologies.Add(track.Technology);
                    record.Log("Society " + camp.SocietyId + " discovered " + track.Technology + " through useful " + track.Role + " work.");
                }
            }
        }
    }
}
