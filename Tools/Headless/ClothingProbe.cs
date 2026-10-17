using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Anthropocene.Simulation;

// Diagnostic only (no behaviour change): per camp-turn clothing request -> grant -> production ->
// consumption -> coverage trace for the DemandControl planner under PhysicalMortalityV1.
// --clothing-probe=<turns>[,<outputDir>][,nofission]
internal static partial class DemographySocialAblation
{
    // Counterfactual oracle (diagnostic only): before each decision, top every camp's
    // fixed housing up to three times its linked population, so shelter never binds
    // over the turn even at the birth-only maximum (~2.46x in 20 years).
    public static void FreeHousingOracle(WorldState w)
    {
        var n = w.Config.Neolithic;
        foreach (var c in w.Camps.OrderBy(c => c.Id))
        {
            double people = 3.0 * Math.Max(1, w.LinkedPopulation(c));
            double gap = people - NeolithicProduction.HousingCapacity(w, c, w.Days).Total;
            if (gap > 0) NeolithicProduction.AddFixedHousing(w, c, gap / n.HousingPeoplePerUnit, w.Days);
        }
        NeolithicProduction.NormalizeFixedHousing(w);
    }

    internal static int LadderDumpTurn = -1, LadderDumps;

    // --kernel-scaling=<players>,<turns>[,<seed>]: matched-state planning cost of the kernel-scored Elder split
    // (lexicographic, 5%, surplus-funded births). After a social-free opening, each continuation turn plans the same
    // committed world three ways (kernel off, fast planning solver, exact authoritative solver; rotated order so cache
    // warming favours none), then resolves the fast plan with fission and immigration on. Scalar execution.
    public static int RunKernelScaling(int players, int turns, int seed, bool mixed = false)
    {
        DemandController.KernelMixedDiagnostic = mixed;
        var cfg = players <= 8 ? SimulationConfig.Small(players)
            : new SimulationConfig { Width = players <= 80 ? 1024 : 2048, Height = players <= 80 ? 512 : 1024, Players = players, BenchmarkScale = players > 80 };
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1; cfg.QoLMortalityEnabled = false;
        cfg.FissionEnabled = false; cfg.ImmigrationEnabled = false; cfg.TradeEnabled = true;
        cfg.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        cfg.Validate();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var world = new WorldGenerator().Generate(seed, cfg); var resolver = new TurnResolver();
        int opening = 0;
        while (!world.Finished && opening < 40) { var p = AiPlanning.Decide(world); resolver.Resolve(world, p.Orders, p.Traces); opening++; }
        Console.WriteLine("KERNEL_SCALING players=" + players + " seed=" + seed + " openingTurns=" + opening + " setupSeconds=" + clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture));
        world.Config.FissionEnabled = true; world.Config.ImmigrationEnabled = true;
        PhysicalDemography.SurplusFundedBirthsDiagnostic = true;
        DemandController.KernelSplitObjective = 2; DemandController.KernelLexTolerance = .05;
        double totalDecide = 0, totalOff = 0, totalFast = 0, totalExact = 0, totalFastKernel = 0, totalExactKernel = 0; long kernelCamps = 0;
        string I(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        for (int t = 0; t < turns; t++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            PlanningResult plan = AiPlanning.Decide(world);
            double decide = watch.Elapsed.TotalMilliseconds;
            // Each variant: the minimum of three timed repetitions after a full collection (rotated order).
            double off = double.MaxValue, fast = double.MaxValue, exact = double.MaxValue, fastKernel = double.MaxValue, exactKernel = double.MaxValue;
            PreparedTurn chosen = null; long camps = 0;
            for (int k = 0; k < 9; k++)
            {
                int variant = (k + t) % 3;
                DemandController.KernelSplitDiagnostic = variant != 0;
                DemandController.KernelExactScoringDiagnostic = variant == 2;
                long before = DemandController.KernelSplitCamps, ticks = DemandController.KernelSplitTicks;
                GC.Collect(); GC.WaitForPendingFinalizers();
                watch.Restart();
                var prepared = TurnPlanner.Prepare(world, plan.Orders, plan.Traces);
                double ms = watch.Elapsed.TotalMilliseconds;
                double kernelMs = (DemandController.KernelSplitTicks - ticks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (variant == 0) off = Math.Min(off, ms);
                else if (variant == 1) { fast = Math.Min(fast, ms); fastKernel = Math.Min(fastKernel, kernelMs); chosen = prepared; camps = DemandController.KernelSplitCamps - before; }
                else { exact = Math.Min(exact, ms); exactKernel = Math.Min(exactKernel, kernelMs); }
            }
            DemandController.KernelSplitDiagnostic = true; DemandController.KernelExactScoringDiagnostic = false;
            totalDecide += decide; totalOff += off; totalFast += fast; totalExact += exact; kernelCamps += camps;
            totalFastKernel += fastKernel; totalExactKernel += exactKernel;
            Console.WriteLine("KERNEL_SCALING_TURN players=" + players + " turn=" + world.Turn + " camps=" + world.Camps.Count + " kernelCamps=" + camps
                + " decideMs=" + I(decide) + " prepareOffMs=" + I(off) + " prepareFastMs=" + I(fast) + " prepareExactMs=" + I(exact)
                + " kernelFastMs=" + I(fastKernel) + " kernelExactMs=" + I(exactKernel));
            resolver.Resolve(world, chosen);
        }
        double baseline = totalDecide + totalOff;
        Console.WriteLine("KERNEL_SCALING_SUMMARY players=" + players + " seed=" + seed + " mixed=" + (mixed ? 1 : 0) + " turns=" + turns + " kernelCampTurns=" + kernelCamps
            + " planningOffMs=" + I(baseline) + " planningFastMs=" + I(totalDecide + totalFast) + " planningExactMs=" + I(totalDecide + totalExact)
            + " fastRatio=" + I((totalDecide + totalFast) / baseline) + " exactRatio=" + I((totalDecide + totalExact) / baseline)
            + " fastKernelMsPerCampTurn=" + I(kernelCamps > 0 ? totalFastKernel / kernelCamps : 0)
            + " exactKernelMsPerCampTurn=" + I(kernelCamps > 0 ? totalExactKernel / kernelCamps : 0)
            + " offPlanningMsPerCampTurn=" + I(kernelCamps > 0 ? baseline / kernelCamps : 0)
            + " kernelOverheadFast=" + I(totalFastKernel / baseline) + " kernelOverheadExact=" + I(totalExactKernel / baseline));
        DemandController.KernelSplitDiagnostic = false; PhysicalDemography.SurplusFundedBirthsDiagnostic = false;
        return 0;
    }

    // Equilibria of the real kernel for a site whose renewable food feeds K people per turn, under the v1.1 law
    // and the surplus-funded-births prototype. Clothing and housing are funded each turn for coverage x people.
    public static int RunSurplusEquilibria()
    {
        var cfg = SimulationConfig.Small(8);
        cfg.DemographicModel = DemographicModel.PhysicalMortalityV1; cfg.QoLMortalityEnabled = false; cfg.Validate();
        double years = cfg.TurnDays / cfg.DaysPerYear, foodRate = PhysicalDemography.FoodPerPersonYear(cfg), clothRate = PhysicalDemography.ClothingPerPersonYear(cfg);
        string I(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        Console.WriteLine("law,K,coverage,start,turnsToSettle,N,F,C,H,births/yr,deaths/yr,starvationDeaths/turn");
        foreach (bool surplus in new[] { false, true })
            foreach (double k in new[] { 300.0, 600, 900 })
                foreach (double coverage in new[] { 0.0, .5, 1 })
                    foreach (double start in new[] { 100.0, 1500 })
                    {
                        PhysicalDemography.SurplusFundedBirthsDiagnostic = surplus;
                        double n = start, deficit = 0; DemographicResult r = null; int settle = -1;
                        for (int turn = 0; turn < 80; turn++)
                        {
                            double food = k * foodRate * years;
                            double cloth = coverage * clothRate * PhysicalDemography.BirthOnlyPersonYears(n, cfg.BaseBirthRate, years);
                            r = PhysicalDemography.AdvanceBudget(cfg, n, years, food, cloth, coverage * n, deficit, true);
                            deficit = r.Nutrition < 1 - 1e-9 ? deficit + years : 0;
                            if (settle < 0 && Math.Abs(r.FinalPopulation - n) < 0.002 * n) settle = turn;
                            n = r.FinalPopulation;
                        }
                        Console.WriteLine(string.Join(",", surplus ? "surplus-births" : "v1.1", I(k), I(coverage), I(start), settle.ToString(), I(n), I(r.Nutrition), I(r.ClothingCoverage), I(r.AverageHousingCoverage),
                            I(r.Births / years), I(r.Deaths / years), I(r.LegacyStarvationDeaths)));
                    }
        PhysicalDemography.SurplusFundedBirthsDiagnostic = false;
        return 0;
    }

    public static int RunClothingProbe(int turns, string outputDir, bool fission, bool immigration, string tag = "", Action<SimulationConfig> tweak = null, Action<WorldState> beforeTurn = null, int[] seeds = null, int workers = 1)
    {
        // workers > 1: parallel planning and resolution with small reversed partitions (scalar/parallel replay check).
        var execution = workers > 1 ? new PhaseExecution(workers, 3, true, 1) : null;
        bool prior = ResolutionMetrics.CaptureDemandAllocations;
        ResolutionMetrics.CaptureDemandAllocations = true;
        try
        {
            Directory.CreateDirectory(outputDir);
            string path = Path.Combine(outputDir, "clothing-probe" + (fission ? "" : "-nofission") + (immigration ? "" : "-noimm") + tag + ".csv");
            using var csv = new StreamWriter(path);
            string I(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
            csv.WriteLine("seed,turn,camp,ageSinceFounded,route,N0,personYears,Nend,openFabric,needNow,grantNow,unmetNow,needRes,grantRes,unmetRes,"
                + "clothCeil,clothMade,fabricMade,consumed,C,F,H,QoL,budget,herders,farmers,foodGatherers,compiled,repair,trim,"
                + "laborPerUnit,plantPerUnit,contFracMin,contUnmet,fissionParent,fissionDaughterSize,trigger,"
                + "cIfGrantedFull,cIfN0Basis,plantEnvClothing,sparePlantClothing,feedstockGatherers,weavers,crafts,food0,plant0,foodEnd,plantEnd,supply,meals,reserve,consumedFood,W,expect,cohesion,fGain,fDaughterGain,fParentQoLFc,fDaughterQoLFc,fParentCFc,fParentHFc,fDaughterCFc,fDaughterHFc,"
                + "fieldEligible,footprint,fieldCapital,harvestProd,gapPerYear,acceptedGainPerYear,acceptedWorkerYears,expUnits,expGranted,expUnmet,"
                + "potGainPerYear,potWorkerYears,potSteps,foodRequired,foodProduced,foodDeficitEnd,cultivation,husbandry,ladderWholeGain,ladderTenGain,ladderFirstScore,ladderSteps,gatherPerWorkerYear,"
                + "fStage,fDistressLegal,fBaseUtility,fBaseF,fThreshold,fMoverChange,fSourceChange,fAggChange,fDQoL,fDF,fDC,fDH,fDRisk,fDFoodRatio,fSrcQoL,fSrcRisk,fDaughter,iStage,iCandidates,iEvals,iMoverRej,iAggRej,iSrcRej,iIncRej,iMaxGroup,iIntent,iAccepted");
            var planWatch = new System.Diagnostics.Stopwatch(); int planTurns = 0;
            foreach (int seed in seeds ?? new[] { 17, 53, 991 })
            {
                byte[] opening = OpeningBoundary(seed, out _, out _, out _, out _);
                WorldState world = SaveCodec.Load(opening);
                world.Config.FissionEnabled = fission;
                world.Config.ImmigrationEnabled = immigration;
                tweak?.Invoke(world.Config);
                world.Config.Validate();
                var resolver = execution != null ? new TurnResolver(execution) : new TurnResolver();
                var founded = new Dictionary<int, int>();
                double rate = PhysicalDemography.ClothingPerPersonYear(world.Config);
                for (int t = 0; t < turns; t++)
                {
                    beforeTurn?.Invoke(world);
                    planWatch.Start();
                    PlanningResult plan = AiPlanning.Decide(world, execution);
                    PreparedTurn prepared = TurnPlanner.Prepare(world, plan.Orders, plan.Traces, execution);
                    planWatch.Stop(); planTurns++;
                    // Ladder curve from the committed boundary (pure): annual gain reachable with the whole
                    // workforce for one turn, and with 10x that, plus the first step's score.
                    var ladder = new Dictionary<int, (double whole, double ten, double first, double steps)>();
                    double yearsL = world.Config.TurnDays / world.Config.DaysPerYear;
                    foreach (var c in world.Camps)
                    {
                        if (!CultivationEconomy.Enabled(c) || c.Population <= 0) continue;
                        var steps = CultivationEconomy.ExpansionLadder(world, c, yearsL, CultivationEconomy.Footprint(world, c).ToArray());
                        if (LadderDumpTurn == t && c.FoodNeedsMetRatio < 0.9 && LadderDumps++ < 4)
                        {
                            Console.WriteLine("LADDER_DUMP seed=" + seed + " turn=" + world.Turn + " camp=" + c.Id + " pop=" + c.Population + " F=" + I(c.FoodNeedsMetRatio));
                            foreach (int id in CultivationEconomy.Footprint(world, c))
                            {
                                var tile = world.Tiles[id]; var view = LazyEcology.ProjectEcology(world, id, world.Days);
                                Console.WriteLine("  tile " + id + " land=" + tile.Land + " capital=" + I(view.CultivationCapital) + " q=" + I(CultivationEconomy.Intensity(world.Config, view.CultivationCapital))
                                    + " operator=" + tile.CultivationOperator + " veg=" + view.CurrentVegetation + " food=" + I(view.Food) + " potential=" + I(view.PotentialFood) + " condition=" + I(view.LandCondition));
                            }
                            double cumY = 0, cumG = 0;
                            for (int i = 0; i < steps.Length; i++)
                            {
                                cumY += steps[i].WorkerYears; cumG += steps[i].AnnualGain;
                                if (i < 12 || i % 20 == 0 || i == steps.Length - 1)
                                    Console.WriteLine("  step " + i + " tile=" + steps[i].Tile + " score=" + I(steps[i].Score) + " workerYears=" + I(steps[i].WorkerYears)
                                        + " gain/yr=" + I(steps[i].AnnualGain) + " cumWorkerYears=" + I(cumY) + " cumGain/yr=" + I(cumG));
                            }
                        }
                        double GainAt(double budget)
                        {
                            double gain = 0, spent = 0;
                            foreach (var s in steps)
                            {
                                if (!(s.AnnualGain > 0) || !(s.WorkerYears > 0)) continue;
                                if (spent >= budget) break;
                                double take = Math.Min(s.WorkerYears, budget - spent);
                                gain += s.AnnualGain * take / s.WorkerYears; spent += take;
                            }
                            return gain;
                        }
                        double whole = c.Population * yearsL;
                        ladder[c.Id] = (GainAt(whole), GainAt(10 * whole), steps.Length > 0 ? steps[0].Score : 0, steps.Length);
                    }
                    var before = new Dictionary<int, (int N0, double fabric, double food, double plant)>();
                    foreach (var c in world.Camps)
                        before[c.Id] = (c.Population, Math.Max(0, c.Inventory.Cloth) + Math.Max(0, c.Inventory.AnimalFabric), c.Food, c.PlantFood);
                    double years = world.Config.TurnDays / world.Config.DaysPerYear;
                    Fission.Diagnostic = new Dictionary<int, Fission.Diag>(); Immigration.Diagnostic = new Dictionary<int, Immigration.Diag>();
                    TurnRecord record = resolver.Resolve(world, prepared);
                    var fissionDiag = Fission.Diagnostic; var immigrationDiag = Immigration.Diagnostic;
                    Fission.Diagnostic = null; Immigration.Diagnostic = null;
                    foreach (var f in record.Fissions) founded[f.DaughterCamp] = record.Turn;
                    foreach (var c in world.Camps.OrderBy(c => c.Id))
                    {
                        if (!before.TryGetValue(c.Id, out var b)) continue;
                        if (!prepared.DemandAllocations.TryGetValue(c.Id, out DemandController.Allocation a) || a == null) continue;
                        var dem = record.Demographics.FirstOrDefault(d => d.Camp == c.Id);
                        if (dem == null) continue;
                        var now = a.Requests.FirstOrDefault(q => q.Service == "clothing");
                        var res = a.Requests.FirstOrDefault(q => q.Service == "clothing reserve");
                        var cont = a.Requests.Where(q => (q.Class == DemandController.ServiceClass.Continuity || q.Service.StartsWith("shelter")) && q.Units > 1e-9).ToList();
                        double Produced(CraftGood g) => record.Production.Where(p => p.Camp == c.Id && p.Good == g).Sum(p => p.Output);
                        double ceil = (a.Ceilings.TryGetValue(CraftGood.Cloth, out double cc) ? cc : 0) + (a.Ceilings.TryGetValue(CraftGood.AnimalFabric, out double af) ? af : 0);
                        var fr = record.Fissions.FirstOrDefault(f => f.ParentCamp == c.Id);
                        var exp = a.Requests.FirstOrDefault(q => q.Service == "field expansion");
                        fissionDiag.TryGetValue(c.Id, out var fd); immigrationDiag.TryGetValue(c.Id, out var idg);
                        bool cultivated = CultivationEconomy.Enabled(c);
                        int[] footprint = cultivated ? CultivationEconomy.Footprint(world, c).ToArray() : Array.Empty<int>();
                        string[] fieldColumns =
                        {
                            a.FieldExpansionEligible ? "1" : "0", footprint.Length.ToString(),
                            I(footprint.Sum(id => Math.Max(0, world.Tiles[id].CultivationCapital))),
                            I(cultivated ? CultivationEconomy.HarvestProductivity(world, c, footprint) : 0),
                            I(a.FieldExpansionGapPerYear), I(a.FieldExpansionGainPerYear), I(a.FieldExpansionWorkerYears),
                            I(exp?.Units ?? 0), I(exp?.Granted ?? 0), (exp?.Unmet ?? "").Replace(' ', '_'),
                            I(a.FieldPotentialGainPerYear), I(a.FieldPotentialWorkerYears), a.FieldPotentialSteps.ToString(),
                            I(dem.FoodRequired), I(dem.FoodProduced), I(c.FoodDeficit),
                            cultivated ? "1" : "0", CultivationEconomy.HusbandryEnabled(c) ? "1" : "0",
                            I(ladder.TryGetValue(c.Id, out var lc) ? lc.whole : 0), I(lc.ten), I(lc.first), I(lc.steps),
                            I(world.Config.GatherPerPerson * world.Config.GenerationIntervals / years),
                            fd?.Stage ?? "none", fd != null && fd.DistressLegal ? "1" : "0", I(fd?.BaselineUtility ?? 0), I(fd?.BaselineNutrition ?? 0), I(fd?.Threshold ?? 0), I(fd?.MoverChange ?? 0), I(fd?.SourceChange ?? 0), I(fd?.AggregateChange ?? 0), I(fd?.DaughterQoL ?? 0), I(fd?.DaughterNutrition ?? 0), I(fd?.DaughterClothing ?? 0), I(fd?.DaughterHousing ?? 0), I(fd?.DaughterRisk ?? 0), I(fd?.DaughterRenewableFood ?? 0), I(fd?.SourceNoMoveQoL ?? 0), I(fd?.SourceNoMoveRisk ?? 0), (fd?.Daughter ?? 0).ToString(),
                            idg?.Stage ?? "none", (idg?.Candidates ?? 0).ToString(), (idg?.Evaluations ?? 0).ToString(), (idg?.MoverRejected ?? 0).ToString(), (idg?.AggregateRejected ?? 0).ToString(), (idg?.SourceRejected ?? 0).ToString(), (idg?.IncumbentRejected ?? 0).ToString(), (idg?.MaxGroup ?? 0).ToString(), (idg?.IntentSize ?? 0).ToString(), (idg?.Accepted ?? 0).ToString()
                        };
                        double laborPerUnit = now?.Bundle != null ? now.Bundle.RoleYears.Values.Sum() / years : double.NaN;
                        double plantPerUnit = now?.Bundle?.Plant ?? double.NaN;
                        double needNow = now?.Units ?? 0, grantNow = now?.Granted ?? 0;
                        double cFull = dem.PersonYears > 0 ? Math.Min(1, (b.fabric + needNow) / (rate * dem.PersonYears)) : 1;
                        double cN0 = b.N0 > 0 ? Math.Min(1, (b.fabric + grantNow) / (rate * b.N0 * years)) : 1;
                        int Staff(string r) => a.Staff.TryGetValue(r, out int s) ? s : 0;
                        csv.WriteLine(string.Join(",", new[]
                        {
                            seed.ToString(), record.Turn.ToString(), c.Id.ToString(),
                            founded.TryGetValue(c.Id, out int ft) ? (record.Turn - ft).ToString() : "-1",
                            now?.Good?.ToString() ?? "none",
                            b.N0.ToString(), I(dem.PersonYears), world.LinkedPopulation(c).ToString(), I(b.fabric),
                            I(needNow), I(grantNow), (now?.Unmet ?? "").Replace(',', ';'),
                            I(res?.Units ?? 0), I(res?.Granted ?? 0), (res?.Unmet ?? "").Replace(',', ';'),
                            I(ceil), I(Produced(CraftGood.Cloth)), I(Produced(CraftGood.AnimalFabric)), I(c.FabricConsumed),
                            I(dem.ClothingCoverage), I(dem.Nutrition), I(dem.HousingCoverage), I(c.QoL),
                            I(a.ResidentGoodsBudget), a.Herders.ToString(), a.Farmers.ToString(), a.FoodGatherers.ToString(),
                            I(a.FinalCompiledPersons), a.RepairPasses.ToString(), a.TrimPasses.ToString(),
                            I(laborPerUnit), I(plantPerUnit),
                            I(cont.Count > 0 ? cont.Min(q => q.Fraction) : 1),
                            string.Join("|", cont.Select(q => q.Service.Replace(' ', '_') + ":" + I(q.Units) + ":" + I(q.Fraction) + ":" + I(q.Bundle == null ? 0 : q.Bundle.RoleYears.Values.Sum() * q.Units / years) + ":" + q.Unmet.Replace(' ', '_'))),
                            fr != null ? "1" : "0", fr != null ? fr.Daughter.ToString() : "0", fr != null ? fr.Trigger.ToString() : "",
                            I(cFull), I(cN0), I(a.PlantEnvelopeClothing), "", I(a.FeedstockGatherers),
                            Staff(WorkforceRoles.Weaver).ToString(), Staff(WorkforceRoles.Craftsperson).ToString(), I(b.food), I(b.plant), I(c.Food), I(c.PlantFood), I(a.ExpectedSupply), I(a.Meals), I(a.OperatingReserve), I(c.LastConsumed), I(c.WaterNeedsMetRatio), I(c.RudimentaryExpectation), I(NeolithicProduction.SocialCohesion(world, c, world.LinkedPopulation(c))), I(fr?.Gain ?? 0), I(fr?.DaughterGain ?? 0), I(fr?.ForecastParentQoL ?? 0), I(fr?.ForecastDaughterQoL ?? 0), I(fr?.ForecastParentClothing ?? 0), I(fr?.ForecastParentHousing ?? 0), I(fr?.DaughterClothingCoverage ?? 0), I(fr?.DaughterHousingCoverage ?? 0)
                        }.Concat(fieldColumns)));
                    }
                    Console.WriteLine("CLOTHING_PROBE seed=" + seed + " turn=" + record.Turn + " camps=" + world.Camps.Count
                        + " pop=" + world.Camps.Sum(world.LinkedPopulation) + " fissions=" + record.Fissions.Count
                        + (t == turns - 1 ? " fingerprint=" + SaveCodec.Fingerprint(world) : "")
                        + " migrations=" + record.Migrations.Count + " pwQoL=" + I(world.Camps.Sum(c => c.QoL * world.LinkedPopulation(c)) / Math.Max(1, world.Camps.Sum(world.LinkedPopulation))) + " pwC=" + I(world.Camps.Sum(c => c.ClothPerCapita * world.LinkedPopulation(c)) / Math.Max(1, world.Camps.Sum(world.LinkedPopulation))));
                }
            }
            Console.WriteLine("CLOTHING_PROBE_PLANNING turns=" + planTurns + " totalMs=" + I(planWatch.Elapsed.TotalMilliseconds) + " kernelCamps=" + DemandController.KernelSplitCamps + " kernelCalls=" + DemandController.KernelSplitCalls + " kernelMs=" + I(DemandController.KernelSplitTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency) + " chosenClothing=" + string.Join("/", DemandController.KernelChosenClothing) + " chosenShelter=" + string.Join("/", DemandController.KernelChosenShelter)
                + " serviceIterations=" + DemandController.KernelServiceIterations
                + " mixedCamps=" + DemandController.KernelMixedCamps + " otherClothing=" + DemandController.KernelMixedOtherClothing + " otherShelter=" + DemandController.KernelMixedOtherShelter
                + " posture(herd/field/none)=" + string.Join("/", DemandController.KernelChosenPosture)
                + " verify=" + DemandController.KernelVerifyMismatches + "/" + DemandController.KernelVerifyDecisions + " maxPyErr=" + DemandController.KernelVerifyMaxPersonYearsError.ToString("G3", CultureInfo.InvariantCulture) + " maxQoLErr=" + DemandController.KernelVerifyMaxQoLError.ToString("G3", CultureInfo.InvariantCulture) + " worst=" + DemandController.KernelVerifyWorst
                + " surplusCalls=" + PhysicalDemography.SurplusCalls + " full/shortage/bound=" + PhysicalDemography.SurplusFullCalls + "/" + PhysicalDemography.SurplusShortageCalls + "/" + PhysicalDemography.SurplusBoundCalls);
            Console.WriteLine("CLOTHING_PROBE_CSV " + path);
            return 0;
        }
        finally { ResolutionMetrics.CaptureDemandAllocations = prior; }
    }
}


