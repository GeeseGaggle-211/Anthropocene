using System.Diagnostics;
using System.Text.Json;
using Anthropocene.Simulation;

static class WholeTurnProbe
{
    internal static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    internal sealed class Measurement
    {
        public double PrefetchMs,DraftMs,PrepareMs,ResolveMs,RefreshMs;
        public double TotalMs=>PrefetchMs+DraftMs+PrepareMs+ResolveMs+RefreshMs;
        public ResolutionMetrics Resolution;
        public string Fingerprint;
        public int Camps,Herds,ProductionReceipts,ManagedHerdReceipts;
    }
    internal static Measurement Step(WorldState world,int workers,bool fingerprint=true)
    {
        var execution=new PhaseExecution(workers);var resolver=new TurnResolver(execution);
        var result=new Measurement();var clock=Stopwatch.StartNew();
        #if RESEARCH_HOOKS
        ResidentBridge.Begin(world);result.PrefetchMs=clock.Elapsed.TotalMilliseconds;clock.Restart();ResidentBridge.Stage="Draft";
        #endif
        var draft=TurnPlanner.Draft(world,0,execution);result.DraftMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
        #if RESEARCH_HOOKS
        ResidentBridge.Stage="Prepare";
        #endif
        var ai=new PlanningResult{Traces=new List<DecisionTrace>(draft.Ai.Traces)};
        foreach(var pair in draft.Ai.Orders)ai.Orders[pair.Key]=pair.Value;
        ai.Orders[0]=new List<Order>();
        var prepared=TurnPlanner.Prepare(world,ai.Orders,ai.Traces,execution,draft);
        result.PrepareMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
        #if RESEARCH_HOOKS
        CandidateBridge.InResolution=true;
        ResidentBridge.Stage="Resolve";
        #endif
        var record=resolver.Resolve(world,prepared);
        #if RESEARCH_HOOKS
        CandidateBridge.InResolution=false;
        ResidentBridge.Stage="Refresh";
        #endif
        result.ResolveMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
        using(PhaseProfile.Scope("Refresh"))new AgentObservationBuilder().Build(world,0);
        result.RefreshMs=clock.Elapsed.TotalMilliseconds;result.Resolution=resolver.LastMetrics;
        result.Camps=world.Camps.Count(c=>c.Population>0);result.Herds=world.Herds.Count(h=>h.Animals>0);
        result.ProductionReceipts=record.Production.Count;result.ManagedHerdReceipts=record.ManagedHerds.Count;
        if(fingerprint)result.Fingerprint=SaveCodec.Fingerprint(world);
        return result;
    }
    internal static WorldState Generate(int width,int height,int players)
    {
        var config=JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"),Json);
        var resources=JsonSerializer.Deserialize<ResourceGenerationOptions>(File.ReadAllText("Assets/_Anthropocene/Resources/ResourceGenerationConfig.json"),Json);
        config.Width=width;config.Height=height;config.Players=players;config.Shape=WorldShape.Continents;config.BenchmarkScale=players>80;
        return new WorldGenerator().Generate(11,config,resources);
    }
}
