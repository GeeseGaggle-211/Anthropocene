using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    [Serializable]
    public sealed class ElderFeatureDefinition
    {
        public string Key, Block, Source, Units, MissingValue, Permission;
        public int Offset, Width, Rows;
        public bool PolicyVisible = true;
    }

    [Serializable]
    public sealed class ElderTensorObservation
    {
        public int SchemaVersion = ElderRLContract.ObservationSchemaVersion;
        public float[] Values = new float[ElderRLContract.TensorFeatureCount];
        public bool[] Present = new bool[ElderRLContract.TensorFeatureCount];
    }

    // Checked-in v1 tensor registry. Caps are encoding bounds, never simulation
    // limits. Raw IDs are explicitly excluded and stay in replay metadata.
    public static class ElderObservationSchema
    {
        public static readonly string[] CapabilityRows =
        {
            "field-retention", "herd-management", "resident-fishing",
            "fixed-construction", "local-trade"
        };

        public static readonly string[] CommodityRows =
            Enum.GetValues(typeof(CraftGood)).Cast<CraftGood>()
                .OrderBy(good => (int)good).Take(
                    ElderRLContract.MaxCommoditySlots)
                .Select(good => good.ToString()).ToArray();

        public static readonly ElderFeatureDefinition[] Registry =
        {
            Block("core", 0, 16, 1, "own committed settlement and bounded history", "mixed semantic scalars"),
            Block("fields", 16, 7, 1, "own retained cultivation capital", "tiles, ratios, food and worker-years"),
            Reserved("core-reserved-23", 23, 1),
            Block("herd-summary", 24, 13, 1, "owned domestic herd aggregate", "animals, ratios, food, water and worker-years"),
            Reserved("core-reserved-37", 37, 3),
            Block("previous-decision", 40, 14, 1, "previous requested, accepted and compiled outcome", "flags, action fractions and counts"),
            Reserved("core-reserved-54", 54, 2),
            Block("fission-history", 56, 7, 1, "own candidate and resolved fission history", "flags, people and welfare ratios"),
            Reserved("core-reserved-63", 63, 1),
            Block("capabilities", 64, 3, 5, "permission-filtered capability rows in checked-in semantic order", "available flag, experience and progress ratio"),
            Reserved("core-reserved-79", 79, 1),
            Block("continuous-action-mask", 80, 4, 1, "native action legality", "flags"),
            Reserved("core-reserved-84", 84, 44),
            Block("sites", 128, 12, 7, "permission-filtered SiteCoverage/AgriculturalSites", "normalized local ratios"),
            Block("fishing", 212, 12, 3, "permission-filtered FishingGrounds", "normalized local ratios"),
            Block("herd-groups", 248, 8, 8, "owned domestic herd observations", "animals and physical ratios"),
            Block("commodities", 312, 8, 11, "own inventory, needs and legal recipes", "stock, need, capacity, value, trade flow and capability flags"),
            Block("contacts", 400, 8, 8, "remembered/visible contact knowledge", "distance, age and normalized ratios"),
            Block("presence-overflow", 464, 3, 5, "encoder presence and overflow summaries", "present/omitted counts and overflow flag"),
            Reserved("presence-reserved-479", 479, 1),
            Block("movement-action-mask", 480, 2, 1, "native movement legality", "enabled flag and legal-candidate count"),
            Block("movement-candidate-mask", 482, 8, 1, "native categorical legality in stay/site-row order", "flags"),
            Reserved("presence-reserved-490", 490, 11),
            Reserved("schema-reserved-501", 501, 11)
        };

        private static ElderFeatureDefinition Block(string key, int offset,
            int width, int rows, string source, string units)
            => new ElderFeatureDefinition
            {
                Key = key, Block = key, Offset = offset, Width = width,
                Rows = rows, Source = source, Units = units,
                MissingValue = "zero with explicit presence bit",
                Permission = "local-game-knowledge"
            };

        private static ElderFeatureDefinition Reserved(string key, int offset,
            int width) => new ElderFeatureDefinition
            {
                Key = key, Block = "reserved", Offset = offset, Width = width,
                Rows = 1, Source = "reserved for schema migration",
                Units = "zero", MissingValue = "zero",
                Permission = "not policy data", PolicyVisible = false
            };

        public static void Validate()
        {
            var used = new bool[ElderRLContract.TensorFeatureCount];
            foreach (var item in Registry)
            {
                if (item.Offset < 0 || item.Width < 1 || item.Rows < 1 ||
                    item.Offset + item.Width * item.Rows > used.Length)
                    throw new InvalidOperationException("Invalid Elder feature range: " + item.Key);
                for (int i = item.Offset; i < item.Offset + item.Width * item.Rows; i++)
                {
                    if (used[i]) throw new InvalidOperationException("Overlapping Elder feature range at " + i);
                    used[i] = true;
                }
            }
            if (used.Any(value => !value))
                throw new InvalidOperationException("Elder feature registry must account for all 512 slots.");
        }

        public static ElderTensorObservation Encode(ElderObservation observation)
        {
            if (observation == null || observation.SchemaVersion !=
                ElderRLContract.ObservationSchemaVersion)
                throw new ArgumentException("Unsupported Elder observation schema.", nameof(observation));
            var result = new ElderTensorObservation();
            void Put(int offset, double value, bool present = true)
            {
                result.Values[offset] = (float)(ElderRLContract.Finite(value) ? value : 0);
                result.Present[offset] = present;
            }
            var c = observation.Core;
            Put(0, c.Population); Put(1, c.ConnectedConsumers);
            Put(2, c.DeficitTurns); Put(3, c.ElapsedDays); Put(4, c.HorizonYears);
            Put(5, c.QoL); Put(6, c.FoodNeedsMet); Put(7, c.WaterNeedsMet);
            Put(8, c.ClothingCoverage); Put(9, c.HousingCoverage);
            Put(10, c.FoodReserveYears); Put(11, c.PopulationTrend);
            Put(12, c.QoLTrend); Put(13, c.NetMigrationTrend);
            Put(14, c.IsVillage ? 1 : 0); Put(15, c.RecentlyMoved ? 1 : 0);
            var f = observation.Fields;
            Put(16, f.RetainedTiles); Put(17, f.CurrentOperation);
            Put(18, f.MaintenanceWorkerYears); Put(19, f.CurrentFoodSupport);
            Put(20, f.FutureFoodSupport); Put(21, f.RebuildBurden);
            Put(22, f.RetainedCapitalService);
            var h = observation.Herd;
            Put(24, h.Animals); Put(25, h.Groups); Put(26, h.Nutrition);
            Put(27, h.ManagementFulfilment); Put(28, h.ZeroDisplacementCapacity);
            Put(29, h.AbsoluteCapacity); Put(30, h.WaterCapacity);
            Put(31, h.CurrentManagementWorkerYears); Put(32, h.CurrentAnimalFood);
            Put(33, h.HumanFoodDisplaced); Put(34, h.BirthTrend);
            Put(35, h.MortalityTrend); Put(36, h.SlaughterTrend);
            var previous = observation.Previous;
            Put(40, previous.Present ? 1 : 0);
            Put(41, previous.RequestedFieldRetention, previous.Present);
            Put(42, previous.AcceptedFieldRetention, previous.Present);
            Put(43, previous.RealizedFieldOperation, previous.Present);
            Put(44, previous.RequestedHerdRetention, previous.Present);
            Put(45, previous.AcceptedHerdRetention, previous.Present);
            Put(46, previous.RealizedHerdRetention, previous.Present);
            Put(47, previous.RequestedDevelopmentBudget, previous.Present);
            Put(48, previous.AcceptedDevelopmentBudget, previous.Present);
            Put(49, previous.RealizedDevelopmentBudget, previous.Present);
            Put(50, previous.RequestedFieldShare, previous.Present);
            Put(51, previous.AcceptedFieldShare, previous.Present);
            Put(52, previous.RealizedFieldShare, previous.Present);
            Put(53, previous.InterventionCount, previous.Present);
            var fission = observation.Fission;
            Put(56, fission.CandidatePresent ? 1 : 0); Put(57, fission.RecentSplit ? 1 : 0);
            Put(58, fission.Pressure, fission.RecentSplit);
            Put(59, fission.ExpectedStayerGain, fission.RecentSplit);
            Put(60, fission.ExpectedDaughterQoL, fission.RecentSplit);
            Put(61, fission.ExitOpportunity, fission.RecentSplit);
            Put(62, fission.RecentDaughterPopulation, fission.RecentSplit);
            for (int row = 0; row < Math.Min(5,
                    observation.Capabilities.Length); row++)
            {
                var capability = observation.Capabilities[row];
                int o = 64 + row * 3;
                Put(o, capability.Available ? 1 : 0);
                Put(o + 1, capability.Experience);
                Put(o + 2, capability.Progress);
            }
            Put(80, observation.Mask.FieldRetention ? 1 : 0);
            Put(81, observation.Mask.HerdRetention ? 1 : 0);
            Put(82, observation.Mask.Development ? 1 : 0);
            Put(83, observation.Mask.DevelopmentSplit ? 1 : 0);

            for (int row = 0; row < Math.Min(7, observation.Sites.Length); row++)
            {
                var s = observation.Sites[row]; int o = 128 + row * 12;
                Put(o, s.Current ? 1 : 0); Put(o + 1, s.Known ? 1 : 0);
                Put(o + 2, s.HumanFood); Put(o + 3, s.HumanFoodAfterHerd);
                Put(o + 4, s.HerdZeroDisplacement); Put(o + 5, s.HerdAbsolute);
                Put(o + 6, s.HerdWater); Put(o + 7, s.FixedShelter);
                Put(o + 8, s.RecurringLabor); Put(o + 9, s.AbsoluteRecurringLabor);
                Put(o + 10, s.RetainedFieldService); Put(o + 11, s.LostRebuildBurden);
            }
            for (int row = 0; row < Math.Min(3, observation.Fishing.Length); row++)
            {
                var g = observation.Fishing[row]; int o = 212 + row * 12;
                Put(o, g.Distance); Put(o + 1, g.Incumbent ? 1 : 0);
                Put(o + 2, g.Known ? 1 : 0); Put(o + 3, g.YieldPerFisherYear);
                Put(o + 4, g.FoodPotential); Put(o + 5, g.Biomass);
                Put(o + 6, g.Condition); Put(o + 7, g.OpeningCapital);
                Put(o + 8, g.OperationFraction); Put(o + 9, g.MaintenanceWorkerYears);
                Put(o + 10, g.BuildWorkerYears);
            }
            for (int row = 0; row < Math.Min(8, observation.HerdGroups.Length); row++)
            {
                var g = observation.HerdGroups[row]; int o = 248 + row * 8;
                Put(o, g.Animals); Put(o + 1, g.Nutrition);
                Put(o + 2, g.ManagementFulfilment); Put(o + 3, g.FoodPerAnimal);
                Put(o + 4, g.WaterPerAnimal); Put(o + 5, g.AnimalFoodPerAnimal);
                Put(o + 6, g.BirthRate); Put(o + 7, g.MortalityRate);
            }
            for (int row = 0; row < Math.Min(11, observation.Commodities.Length); row++)
            {
                var g = observation.Commodities[row]; int o = 312 + row * 8;
                Put(o, g.Stock); Put(o + 1, g.LocalNeed);
                Put(o + 2, g.ProductionCapacity);
                Put(o + 3, g.MarginalValue);
                Put(o + 4, g.RecentImport); Put(o + 5, g.RecentExport);
                Put(o + 6, g.CanProduce ? 1 : 0);
                Put(o + 7, g.CanTrade ? 1 : 0);
            }
            for (int row = 0; row < Math.Min(8, observation.Contacts.Length); row++)
            {
                var g = observation.Contacts[row]; int o = 400 + row * 8;
                Put(o, g.Distance); Put(o + 1, g.AgeTurns); Put(o + 2, g.Affinity);
                Put(o + 3, g.RelativeAttractiveness); Put(o + 4, g.RecentTradeValue);
                Put(o + 5, g.RecentMigration); Put(o + 6, g.Kin ? 1 : 0);
                Put(o + 7, g.CurrentlyVisible ? 1 : 0);
            }
            var blocks = new[] { observation.Presence.Sites, observation.Presence.Fishing,
                observation.Presence.Herds, observation.Presence.Commodities,
                observation.Presence.Contacts };
            for (int i = 0; i < blocks.Length; i++)
            {
                int o = 464 + i * 3; Put(o, blocks[i].Present);
                Put(o + 1, blocks[i].Omitted); Put(o + 2, blocks[i].Overflow ? 1 : 0);
            }
            Put(480, observation.Mask.Movement ? 1 : 0);
            Put(481, observation.Mask.LegalMovementCandidates?.Length ?? 0);
            Put(482, observation.Mask.AllowsMovement(-1) ? 1 : 0);
            for (int candidate = 0;
                candidate < ElderRLContract.MaxLocalSites; candidate++)
                Put(483 + candidate,
                    observation.Mask.AllowsMovement(candidate) ? 1 : 0);
            return result;
        }
    }
}
