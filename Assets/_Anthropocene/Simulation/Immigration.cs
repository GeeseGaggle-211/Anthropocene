using System;
using System.Collections.Generic;
using System.Linq;

namespace Anthropocene.Simulation
{
    // One committed flow of people between two settlements, recorded canonically (destination camp, then source camp).
    [Serializable]
    public sealed class MigrationReport
    {
        public int Turn, SourceCamp, DestinationCamp, SourceSociety, DestinationSociety, SourceBefore, DestinationBefore, Intended, Migrants, Distance;
        public bool Kin;
        public double SourceQoL, DestinationQoL, Utility, PostInflowQoL, Plant, Animal, Aquatic;
        // WP10b-2: the return penalty inside Utility and the person-equivalents of food need the migrants carried (absent in older saves).
        [System.Runtime.Serialization.OptionalField] public double Penalty, Carried;
        // Diagnostic only; utility remains ForwardUtility(QoL). These annual physical death rates reveal attractive
        // destinations whose welfare is higher while survival risk is worse.
        [System.Runtime.Serialization.OptionalField] public double SourceMortalityBefore, DestinationMortalityBefore, SourceMortalityAfter, DestinationMortalityAfter, MortalityGap;
        [System.Runtime.Serialization.OptionalField] public bool HigherQoLWorseMortality;
    }

    // One remembered flow at a camp (hysteresis): the other camp, the turn and the direction (+1 this camp sent people, -1 it received them).
    [Serializable]
    public sealed class MigrationMemory
    {
        public int Other, Turn, Direction;
    }

    // Immigration between known settlements (QoLFissionTradeDesign 4 and 9). Runs once per turn in the Close phase after
    // demography and before fission. It is a resolver population process like births, not an AI decision.
    //
    // Authoritative state: Camp.Population, the typed food stores and the workforce ledger (people and portable food move
    // between camps), TurnRecord.Migrations, DemographicReport.Emigrated/Immigrated and the remembered contacts.
    // Temporal abstraction: one evaluation per turn boundary from the frozen post-demography snapshot. The utility is forward-looking
    // (WP10b-2): dU(M) = w_Q (Qhat_j(N_j + M) - Qhat_i(N_i - M)) - K_distance d - K_attachment - K_lifestyle - ReturnPenalty, with Qhat the
    // same pure estimator as fission (Fission.QoLModel.ForwardAt) including food and water dilution and the food the migrants carry.
    // QoL is evaluated once per camp from the snapshot and never recomputed after a transfer; a bounded per-camp memory of recent flows
    // (Camp.RecentMigration) damps reversals.
    // Cost: O(N) QoL terms, O(N * r_imm^2) candidate scans (one map-radius walk per eligible source, each tile a dictionary lookup
    // and each camp one binary-searched contact test); reconcile is O(intents * log M) and commit O(intents). Nothing scales with
    // elapsed years, map size or the number of Societies. Fixed-housing lookup inside the QoL terms is shared with fission.
    //
    // Rejected architectures (design 9): all-pairs comparison, rerouting of rejected or excess migrants to second-best targets,
    // stable matching, flows, and recomputing QoL after each transfer.
    public static class Immigration
    {
        // Lifestyle barrier: nomadic camp -> village costs ImmigrationToSedentaryCost, village -> nomadic camp ImmigrationToNomadicCost.
        public static double LifestyleCost(SimulationConfig config, Camp source, Camp destination)
            => !source.IsVillage && destination.IsVillage ? config.ImmigrationToSedentaryCost : source.IsVillage && !destination.IsVillage ? config.ImmigrationToNomadicCost : 0;

        // dU = w_Q (Q_j - Q_i) - K_distance * d - K_attachment - K_lifestyle (the opportunity term is zero).
        public static double Utility(SimulationConfig config, double qualitySource, double qualityDestination, int distance, Camp source, Camp destination)
            => config.ImmigrationQualityWeight * (qualityDestination - qualitySource) - config.ImmigrationDistanceCost * distance - config.ImmigrationAttachmentCost - LifestyleCost(config, source, destination);

        // Forward-looking utility of moving m people from source to destination (WP10b-2). The source is evaluated at N_i - m and carries
        // no food term; the destination at N_j + inflow + m with `carried` person-equivalents of food need arriving with all of the
        // inflow so far (earlier sources reconciled at the same destination included).
        public static double ForwardUtility(SimulationConfig config, Fission.QoLModel sourceModel, Fission.QoLModel destinationModel, int m, int inflow, double carried, int distance, Camp source, Camp destination, double penalty)
            => Utility(config, sourceModel.ForwardAt(sourceModel.Residents - m), destinationModel.ForwardAt(destinationModel.Residents + inflow + m, carried), distance, source, destination) - penalty;

        // Person-equivalents of the turn's food need that one migrant brings: the source's per-head stored food over the per-head requirement.
        public static double CarriedPerPerson(SimulationConfig config, Camp source)
        {
            double need = config.Consumption * config.TurnDays / config.BaseDays;
            return source.Population > 0 && need > 0 ? Math.Max(0, source.PlantFood + source.AnimalFood + source.AquaticFood) / source.Population / need : 0;
        }

        // The largest group a source may send this turn: the maximum fraction of residents, keeping the source floor.
        public static int MaxGroup(SimulationConfig config, int residents)
            => Math.Min((int)Math.Floor(residents * config.ImmigrationMaxFraction + 1e-9), residents - config.ImmigrationSourceFloor);

        // Return penalty: a flow from `source` to `destination` within ImmigrationReturnCooldownTurns of the destination having sent people
        // to the source pays ImmigrationReturnPenalty * (cooldown - age + 1) / cooldown for age = turns since that flow (1..cooldown), else 0.
        public static double ReturnPenalty(WorldState w, Camp source, Camp destination)
        {
            int cooldown = w.Config.ImmigrationReturnCooldownTurns; var memory = source.RecentMigration;
            if (cooldown <= 0 || memory == null || w.Config.ImmigrationReturnPenalty <= 0) return 0;
            double best = 0;
            foreach (var m in memory)
            {
                if (m.Other != destination.Id || m.Direction >= 0) continue;
                int age = Math.Max(1, w.Turn - m.Turn);
                if (age <= cooldown) best = Math.Max(best, w.Config.ImmigrationReturnPenalty * (cooldown - age + 1) / cooldown);
            }
            return best;
        }

        // M = floor(N * maxFraction * clamp((dU - theta) / theta, 0, 1)), kept above the source floor; zero below the minimum group.
        public static int Size(SimulationConfig config, int residents, double utility)
        {
            double theta = config.ImmigrationDeadband;
            if (!(utility > theta)) return 0;
            int size = (int)Math.Floor(residents * config.ImmigrationMaxFraction * Numbers.Clamp((utility - theta) / theta, 0, 1) + 1e-9);   // epsilon: exact ramp points must not lose a person to rounding
            size = Math.Min(size, residents - config.ImmigrationSourceFloor);
            return size >= config.ImmigrationMinimumGroup ? size : 0;
        }

        public sealed class Intent
        {
            public Camp Source, Destination;
            public int Residents, DestinationResidents, Distance, Size; public double QualitySource, QualityDestination, Utility, Penalty, CarriedPerPerson;
            public double Plant, Animal, Aquatic;   // frozen source stores: migrants carry their per-capita share of these
            public double GrowthProgress, DeathProgress, FoodDeficitYears, DeficitSeverity;
            public double SourceMortalityBefore, DestinationMortalityBefore, SourceMortalityAfter, DestinationMortalityAfter;
            public int Accepted; public double PostInflowQuality;
        }

        private sealed class Evaluation { public Camp Camp; public Intent Intent; public int Candidates; }

        private sealed class Context
        {
            public readonly WorldState World;
            public readonly Dictionary<int, List<Camp>> CampsByTile = new Dictionary<int, List<Camp>>();
            public readonly Dictionary<int, double> Quality = new Dictionary<int, double>();
            public readonly Dictionary<int, Fission.QoLModel> Models = new Dictionary<int, Fission.QoLModel>();
            private readonly Lazy<PlanningIndex> index;
            public PlanningIndex Index => index.Value;
            public Context(WorldState w) { World = w; index = new Lazy<PlanningIndex>(() => new PlanningIndex(w), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication); }
        }

        // Returns the camps that took part (sources with an intent and destinations that received arrivals), which sit out fission this
        // turn; null when immigration is disabled.
        public static HashSet<int> Resolve(WorldState w, TurnRecord record, PhaseExecution execution, ResolutionMetrics metrics)
        {
            if (!w.Config.ImmigrationEnabled) return null;
            var timer = System.Diagnostics.Stopwatch.StartNew(); var cfg = w.Config;
            execution = execution ?? new PhaseExecution();
            // Frozen post-demography snapshot: nothing below the parallel maps writes to the world.
            var camps = w.Camps.Where(c => c.Population > 0).OrderBy(c => c.Id).ToArray();
            var context = new Context(w);
            foreach (var c in camps) { if (!context.CampsByTile.TryGetValue(c.Tile, out var list)) context.CampsByTile[c.Tile] = list = new List<Camp>(); list.Add(c); }
            var modelsByCamp = execution.Map(camps, c => Fission.QoLModelOf(w, c));
            for (int i = 0; i < camps.Length; i++) { context.Models[camps[i].Id] = modelsByCamp[i]; context.Quality[camps[i].Id] = modelsByCamp[i].At(camps[i].Population); }
            var evaluations = execution.Map(camps, c => Evaluate(context, c));
            var intents = new List<Intent>();
            foreach (var e in evaluations)
            {
                if (e.Camp.Population >= cfg.ImmigrationSourceFloor + cfg.ImmigrationMinimumGroup) metrics.ImmigrationCampsEvaluated++;
                metrics.ImmigrationCandidatesScored += e.Candidates;
                if (e.Intent != null) { intents.Add(e.Intent); metrics.ImmigrationIntents++; }
            }
            var taken = new HashSet<int>(intents.Select(i => i.Source.Id));
            // Canonical reconcile: destination camp ID, then source camp ID. A destination accepts while its estimated post-inflow QoL stays
            // at least Q_i + theta for each source in order; the excess stays home and is never rerouted.
            var inflow = new Dictionary<int, int>(); var carriedIn = new Dictionary<int, double>();
            var ordered = intents.OrderBy(i => i.Destination.Id).ThenBy(i => i.Source.Id).ToArray();
            foreach (var intent in ordered)
            {
                var destination = intent.Destination; var destinationModel = context.Models[destination.Id]; var sourceModel = context.Models[intent.Source.Id];
                inflow.TryGetValue(destination.Id, out int already); carriedIn.TryGetValue(destination.Id, out double carriedAlready);
                double theta = cfg.ImmigrationDeadband;
                // Forward utility at the group size m (with the inflow already accepted): the largest acceptable m is found by bisection.
                bool Accept(int m) => ForwardUtility(cfg, sourceModel, destinationModel, m, already, carriedAlready + m * intent.CarriedPerPerson, intent.Distance, intent.Source, destination, intent.Penalty) > theta;
                int lo = 0, hi = intent.Size;
                while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (Accept(mid)) lo = mid; else hi = mid - 1; }
                intent.Accepted = lo; intent.PostInflowQuality = destinationModel.ForwardAt(destination.Population + already + lo, carriedAlready + lo * intent.CarriedPerPerson);
                if (lo > 0) intent.Utility = ForwardUtility(cfg, sourceModel, destinationModel, lo, already, carriedAlready + lo * intent.CarriedPerPerson, intent.Distance, intent.Source, destination, intent.Penalty);
                if (PhysicalDemography.Enabled(cfg))
                {
                    int sourceAfter = Math.Max(1, intent.Residents - lo), destinationAfter = Math.Max(1, intent.DestinationResidents + already + lo);
                    double destinationFoodAfter = Fission.QoLModel.FoodAt(intent.Destination.FoodNeedsMetRatio, intent.DestinationResidents, carriedAlready + lo * intent.CarriedPerPerson, destinationAfter);
                    double sourceClothingAfter = Math.Min(1, intent.Source.ClothPerCapita * intent.Residents / (double)sourceAfter);
                    double destinationClothingAfter = Math.Min(1, intent.Destination.ClothPerCapita * intent.DestinationResidents / (double)destinationAfter);
                    intent.SourceMortalityBefore = PhysicalDemography.ProjectedSettlementRates(w, intent.Source, intent.Residents, intent.Source.FoodNeedsMetRatio, intent.Source.ClothPerCapita).Death;
                    intent.DestinationMortalityBefore = PhysicalDemography.ProjectedSettlementRates(w, destination, intent.DestinationResidents, destination.FoodNeedsMetRatio, destination.ClothPerCapita).Death;
                    // Food leaves proportionally, while the existing transfer baseline moves no clothing stock: source
                    // coverage improves with fewer people and destination coverage is diluted, matching QoLModel.Shape.
                    intent.SourceMortalityAfter = PhysicalDemography.ProjectedSettlementRates(w, intent.Source, sourceAfter, intent.Source.FoodNeedsMetRatio, sourceClothingAfter).Death;
                    intent.DestinationMortalityAfter = PhysicalDemography.ProjectedSettlementRates(w, destination, destinationAfter, destinationFoodAfter, destinationClothingAfter).Death;
                }
                inflow[destination.Id] = already + lo; carriedIn[destination.Id] = carriedAlready + lo * intent.CarriedPerPerson;
                if (lo == 0) metrics.ImmigrationRejected++; else { metrics.ImmigrationAccepted++; if (lo < intent.Size) metrics.ImmigrationPartial++; }
            }
            var participants = new HashSet<int>(taken);
            var accepted = ordered.Where(i => i.Accepted > 0).ToArray();
            Prune(w);
            if (accepted.Length > 0)
            {
                var demographics = new Dictionary<int, DemographicReport>();
                foreach (var d in record.Demographics) demographics[d.Camp] = d;
                // Commit simultaneously: every departure from the frozen residents and stores first, then every arrival.
                foreach (var intent in accepted.OrderBy(i => i.Source.Id))
                {
                    var source = intent.Source; int m = intent.Accepted; double f = m / (double)intent.Residents;
                    source.Workforce.ApplyCasualties(intent.Residents, m);
                    source.Population -= m;
                    source.PlantFood -= intent.Plant * f; source.AnimalFood -= intent.Animal * f; source.AquaticFood -= intent.Aquatic * f;
                    if (PhysicalDemography.Enabled(w.Config))
                    {
                        intent.GrowthProgress = source.GrowthRemainder * f; intent.DeathProgress = source.DeathRemainder * f;
                        source.GrowthRemainder -= intent.GrowthProgress; source.DeathRemainder -= intent.DeathProgress;
                        intent.FoodDeficitYears = source.FoodDeficitYears; intent.DeficitSeverity = source.DeficitSeverity;
                    }
                    if (demographics.TryGetValue(source.Id, out var report)) report.Emigrated += m;
                }
                foreach (var intent in accepted)
                {
                    var source = intent.Source; var destination = intent.Destination; int m = intent.Accepted; double f = m / (double)intent.Residents;
                    int destinationBefore = destination.Population;
                    if (PhysicalDemography.Enabled(w.Config) && destinationBefore + m > 0)
                    {
                        destination.FoodDeficitYears = (destination.FoodDeficitYears * destinationBefore + intent.FoodDeficitYears * m) / (destinationBefore + m);
                        destination.DeficitSeverity = (destination.DeficitSeverity * destinationBefore + intent.DeficitSeverity * m) / (destinationBefore + m);
                    }
                    destination.Population += m;
                    if (PhysicalDemography.Enabled(w.Config))
                    {
                        destination.GrowthRemainder += intent.GrowthProgress; destination.DeathRemainder += intent.DeathProgress;
                    }
                    destination.PlantFood += intent.Plant * f; destination.AnimalFood += intent.Animal * f; destination.AquaticFood += intent.Aquatic * f;
                    if (demographics.TryGetValue(destination.Id, out var report)) report.Immigrated += m;
                    ContactKnowledge.RecordMigration(w, source.People, destination);
                    ContactKnowledge.RecordMigration(w, destination.People, source);
                    participants.Add(destination.Id); metrics.ImmigrationMigrants += m;
                    record.Migrations.Add(new MigrationReport
                    {
                        Turn = w.Turn, SourceCamp = source.Id, DestinationCamp = destination.Id, SourceSociety = source.SocietyId, DestinationSociety = destination.SocietyId,
                        SourceBefore = intent.Residents, DestinationBefore = intent.DestinationResidents, Intended = intent.Size, Migrants = m, Distance = intent.Distance, Kin = Kinship.Affinity(source.People, destination.People) > 0,
                        SourceQoL = intent.QualitySource, DestinationQoL = intent.QualityDestination, Utility = intent.Utility, PostInflowQoL = intent.PostInflowQuality,
                        Plant = intent.Plant * f, Animal = intent.Animal * f, Aquatic = intent.Aquatic * f, Penalty = intent.Penalty, Carried = m * intent.CarriedPerPerson,
                        SourceMortalityBefore = intent.SourceMortalityBefore, DestinationMortalityBefore = intent.DestinationMortalityBefore,
                        SourceMortalityAfter = intent.SourceMortalityAfter, DestinationMortalityAfter = intent.DestinationMortalityAfter,
                        MortalityGap = intent.DestinationMortalityAfter - intent.SourceMortalityAfter,
                        HigherQoLWorseMortality = intent.PostInflowQuality > intent.QualitySource && intent.DestinationMortalityAfter > intent.SourceMortalityAfter + 1e-12
                    });
                    if (intent.PostInflowQuality > intent.QualitySource && intent.DestinationMortalityAfter > intent.SourceMortalityAfter + 1e-12)
                        metrics.ImmigrationHigherQoLWorseMortality++;
                    Remember(w, source, destination.Id, +1); Remember(w, destination, source.Id, -1);
                    record.Log("Camp " + source.Id + " (tribe " + source.Player + ") sent " + m + " of " + intent.Residents + " people to camp " + destination.Id + " (tribe " + destination.Player + ").");
                }
            }
            metrics.ImmigrationMilliseconds = timer.Elapsed.TotalMilliseconds;
            return participants;
        }

        // Hysteresis memory: drop records older than the cooldown from every camp that holds any (a serial O(camps) scan of a sparse field).
        private static void Prune(WorldState w)
        {
            int cooldown = w.Config.ImmigrationReturnCooldownTurns;
            foreach (var camp in w.Camps)
            {
                if (camp.RecentMigration == null) continue;
                camp.RecentMigration.RemoveAll(m => w.Turn - m.Turn > cooldown);
                if (camp.RecentMigration.Count == 0) camp.RecentMigration = null;
            }
        }
        private static void Remember(WorldState w, Camp camp, int other, int direction)
        {
            if (w.Config.ImmigrationReturnCooldownTurns <= 0) return;
            if (camp.RecentMigration == null) camp.RecentMigration = new List<MigrationMemory>();
            camp.RecentMigration.RemoveAll(m => m.Other == other && m.Direction == direction);   // one record per (camp, other, direction): the latest
            camp.RecentMigration.Add(new MigrationMemory { Other = other, Turn = w.Turn, Direction = direction });
        }

        private static Evaluation Evaluate(Context context, Camp camp)
        {
            var w = context.World; var cfg = w.Config; var e = new Evaluation { Camp = camp };
            // Sources too small to give a minimum group and keep the floor emit nothing.
            if (camp.Population < cfg.ImmigrationSourceFloor + cfg.ImmigrationMinimumGroup) return e;
            var visible = context.Index.VisibleCached(camp.Player);
            var memory = ContactKnowledge.Remembers(camp.People) ? camp.People.Memory : null;
            double qi = context.Quality[camp.Id]; Camp best = null; double bestUtility = 0, bestQuality = 0, bestPenalty = 0; int bestDistance = 0;
            // Candidates are compared at the largest group the source could send (the ramp is set by the utility at M_max).
            int maxGroup = MaxGroup(cfg, camp.Population); if (maxGroup < cfg.ImmigrationMinimumGroup) return e;
            var sourceModel = context.Models[camp.Id]; double perPerson = CarriedPerPerson(cfg, camp);
            foreach (int tile in w.Map.Radius(camp.Tile, cfg.ImmigrationRadius).OrderBy(n => n))
            {
                if (!context.CampsByTile.TryGetValue(tile, out var here)) continue;
                foreach (var c in here)
                {
                    if (c.Id == camp.Id) continue;
                    // Known settlements only: the owner's own camps, camps in current vision, and (after the first breakthrough) remembered contacts
                    // that have not moved or expired. A contact whose camp is no longer at its recorded tile is not found.
                    bool known = c.SocietyId == camp.SocietyId || visible.Contains(tile);
                    if (!known && memory != null) { var contact = memory.Find(c.Id); known = contact != null && !contact.Moved && contact.Tile == tile; }
                    if (!known) continue;
                    e.Candidates++;
                    int distance = w.Map.Distance(camp.Tile, tile); double qj = context.Quality[c.Id]; double penalty = ReturnPenalty(w, camp, c);
                    double u = ForwardUtility(cfg, sourceModel, context.Models[c.Id], maxGroup, 0, maxGroup * perPerson, distance, camp, c, penalty);
                    if (best == null || u > bestUtility || u == bestUtility && (distance < bestDistance || distance == bestDistance && c.Id < best.Id))
                    { best = c; bestUtility = u; bestQuality = qj; bestDistance = distance; bestPenalty = penalty; }
                }
            }
            if (best == null) return e;
            int size = Size(cfg, camp.Population, bestUtility);
            if (size <= 0) return e;
            // Re-evaluate at the final group: emit only if the utility there still clears the deadband.
            double final = ForwardUtility(cfg, sourceModel, context.Models[best.Id], size, 0, size * perPerson, bestDistance, camp, best, bestPenalty);
            if (!(final > cfg.ImmigrationDeadband)) return e;
            e.Intent = new Intent
            {
                Source = camp, Destination = best, Residents = camp.Population, DestinationResidents = best.Population, Distance = bestDistance, Size = size, QualitySource = qi, QualityDestination = bestQuality, Utility = final,
                Penalty = bestPenalty, CarriedPerPerson = perPerson, Plant = camp.PlantFood, Animal = camp.AnimalFood, Aquatic = camp.AquaticFood
            };
            return e;
        }
    }
}
