using Anthropocene.Simulation;

// Direct-call regression for the timestamp branch absent from full-turn captures.
static class FusionBoundaryChecks
{
    static (WorldState World, Camp Camp) Fixture()
    {
        var cfg=SimulationConfig.Small(1);
        var w=new WorldState{Seed=101,Config=cfg,Days=100,Tiles=new[]{new Tile{Id=0}}};
        var c=new Camp{Id=w.NextId++,Player=0,Tile=0,Population=100,PlantFood=10000};
        w.Camps.Add(c);Ownership.Bind(w);
        c.Technology=Technology.Cultivation;c.Technologies[(int)Technology.Cultivation]=true;
        c.People.Progression.Era=Era.Neolithic;c.CraftingPriorities.Housing=1;
        c.Inventory.HandTools=100;c.Inventory.HousingMaterial=10;
        w.FixedHousing=new(){new FixedHousingStock{CampId=c.Id,Tile=0,Units=2,LastUpdatedDays=w.Days}};
        return(w,c);
    }
    public static object Run()
    {
        const double intervals=2.5,labor=100,demand=2;
        int checkedCases=0;
        CandidateBridge.Mode="off";CandidateBridge.Capture=false;FusionBridge.Capture=false;
        foreach(double duration in new[]{0d,-1d,45d})
        foreach(string mode in new[]{"packed-cpu","fused-compact"})
        {
            var (cpu,c)=Fixture();var (gpu,g)=Fixture();
            var recipes=NeolithicProduction.Recipes(cpu.Config);
            var recipe=recipes.Single(r=>r.Good==CraftGood.Housing);
            var expected=new TurnRecord{StartDays=77,Duration=duration};
            var actual=new TurnRecord{StartDays=77,Duration=duration};
            var cpuCoverage=new ToolCoverageBudget(cpu,c,intervals);
            var gpuCoverage=new ToolCoverageBudget(gpu,g,intervals);
            var cpuHousing=new HousingMaterialPlan(.5,.5,null,null,0);
            var gpuHousing=new HousingMaterialPlan(.5,.5,null,null,0);
            double equipment=double.PositiveInfinity;
            double used=NeolithicProduction.Produce(cpu,c,recipe,WorkforceRoles.Craftsperson,labor,intervals,demand,ref equipment,expected,cpuCoverage,cpuHousing);
            var needs=new TurnNeeds{Production=Enum.GetValues<CraftGood>().ToDictionary(x=>x,x=>x==CraftGood.Housing?demand:0d)};
            var roleLabor=new Dictionary<string,double>{{WorkforceRoles.Craftsperson,labor}};
            FusionBridge.Mode=mode;
            if(!FusionBridge.Try(gpu,g,intervals,actual,needs,recipes,gpuCoverage,gpuHousing,roleLabor,new(),new[]{(WorkforceRoles.Craftsperson,recipe)}))
                throw new Exception("Boundary fixture unexpectedly fell back");
            var a=cpu.FixedHousing.Single();var b=gpu.FixedHousing.Single();
            if(expected.Production.Count!=1||actual.Production.Count!=1||!(expected.Production[0].Output>0))
                throw new Exception("Boundary fixture did not produce housing");
            if(a.LastUpdatedDays!=b.LastUpdatedDays||a.Units!=b.Units||c.Inventory.HousingMaterial!=g.Inventory.HousingMaterial||c.Inventory.HandTools!=g.Inventory.HandTools||labor-used!=roleLabor[WorkforceRoles.Craftsperson]||expected.Production[0].Output!=actual.Production[0].Output)
                throw new Exception($"Housing boundary differs: duration={duration}, mode={mode}");
            checkedCases++;
        }
        FusionBridge.Mode="off";
        Console.WriteLine($"PASS: {checkedCases} housing timestamp boundary cases against authoritative Produce");
        return new{checkedCases,durations=new[]{0,-1,45},backends=new[]{"packed-cpu","fused-compact"},exact=true};
    }
}
