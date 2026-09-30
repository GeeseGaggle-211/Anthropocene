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
    }

    // Immigration between known settlements (QoLFissionTradeDesign 4 and 9). Runs once per turn in the Close phase after
    // demography and before fission. It is a resolver population process like births, not an AI decision.
    //
    // Authoritative state: Camp.Population, the typed food stores and the workforce ledger (people and portable food move
    // between camps), TurnRecord.Migrations, DemographicReport.Emigrated/Immigrated and the remembered contacts.
    // Temporal abstraction: one evaluation per turn boundary from the frozen post-demography snapshot; QoL is evaluated by the
    // same formula as fission at present residents and is not recomputed after any transfer.
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
            public int Residents, DestinationResidents, Distance, Size; public double QualitySource, QualityDestination, Utility;
            public double Plant, Animal, Aquatic;   // frozen source stores: migrants carry their per-capita share of these
            public int Accepted; public double PostInflowQuality;
        }

        private sealed class Evaluation { public Camp Camp; public Intent Intent; public int Candidates; }

        private sealed class Context
        {
            public readonly WorldState World;
            public readonly Dictionary<int, List<Camp>> CampsByTile = new Dictionary<int, List<Camp>>();
            public readonly Dictionary<int, double> Quality = new Dictionary<int, double>();
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
            var quality = execution.Map(camps, c => Fission.QoLModelOf(w, c).At(c.Population));
            for (int i = 0; i < camps.Length; i++) context.Quality[camps[i].Id] = quality[i];
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
            var models = new Dictionary<int, Fission.QoLModel>(); var inflow = new Dictionary<int, int>();
            var ordered = intents.OrderBy(i => i.Destination.Id).ThenBy(i => i.Source.Id).ToArray();
            foreach (var intent in ordered)
            {
                var destination = intent.Destination;
                if (!models.TryGetValue(destination.Id, out var model)) models[destination.Id] = model = Fission.QoLModelOf(w, destination);
                inflow.TryGetValue(destination.Id, out int already);
                double floor = intent.QualitySource + cfg.ImmigrationDeadband;
                int lo = 0, hi = intent.Size;   // QoL is non-increasing in headcount, so the largest acceptable group is found by bisection
                while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (model.At(destination.Population + already + mid) >= floor) lo = mid; else hi = mid - 1; }
                intent.Accepted = lo; intent.PostInflowQuality = model.At(destination.Population + already + lo);
                inflow[destination.Id] = already + lo;
                if (lo == 0) metrics.ImmigrationRejected++; else { metrics.ImmigrationAccepted++; if (lo < intent.Size) metrics.ImmigrationPartial++; }
            }
            var participants = new HashSet<int>(taken);
            var accepted = ordered.Where(i => i.Accepted > 0).ToArray();
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
                    if (demographics.TryGetValue(source.Id, out var report)) report.Emigrated += m;
                }
                foreach (var intent in accepted)
                {
                    var source = intent.Source; var destination = intent.Destination; int m = intent.Accepted; double f = m / (double)intent.Residents;
                    destination.Population += m;
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
                        Plant = intent.Plant * f, Animal = intent.Animal * f, Aquatic = intent.Aquatic * f
                    });
                    record.Log("Camp " + source.Id + " (tribe " + source.Player + ") sent " + m + " of " + intent.Residents + " people to camp " + destination.Id + " (tribe " + destination.Player + ").");
                }
            }
            metrics.ImmigrationMilliseconds = timer.Elapsed.TotalMilliseconds;
            return participants;
        }

        private static Evaluation Evaluate(Context context, Camp camp)
        {
            var w = context.World; var cfg = w.Config; var e = new Evaluation { Camp = camp };
            // Sources too small to give a minimum group and keep the floor emit nothing.
            if (camp.Population < cfg.ImmigrationSourceFloor + cfg.ImmigrationMinimumGroup) return e;
            var visible = context.Index.VisibleCached(camp.Player);
            var memory = ContactKnowledge.Remembers(camp.People) ? camp.People.Memory : null;
            double qi = context.Quality[camp.Id]; Camp best = null; double bestUtility = 0, bestQuality = 0; int bestDistance = 0;
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
                    int distance = w.Map.Distance(camp.Tile, tile); double qj = context.Quality[c.Id];
                    double u = Utility(cfg, qi, qj, distance, camp, c);
                    if (best == null || u > bestUtility || u == bestUtility && (distance < bestDistance || distance == bestDistance && c.Id < best.Id))
                    { best = c; bestUtility = u; bestQuality = qj; bestDistance = distance; }
                }
            }
            if (best == null) return e;
            int size = Size(cfg, camp.Population, bestUtility);
            if (size <= 0) return e;
            e.Intent = new Intent
            {
                Source = camp, Destination = best, Residents = camp.Population, DestinationResidents = best.Population, Distance = bestDistance, Size = size, QualitySource = qi, QualityDestination = bestQuality, Utility = bestUtility,
                Plant = camp.PlantFood, Animal = camp.AnimalFood, Aquatic = camp.AquaticFood
            };
            return e;
        }
    }
}
