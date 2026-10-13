using System.Reflection;
using System.Diagnostics;
using Anthropocene.Simulation;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Algorithms;

sealed class FusionCase
{
    public int Camp,Turn;public double[] Initial,Expected;public FusionCommand[] Commands;public FusionReceipt[] ExpectedReceipts;
}
sealed class FusionDevice:IDisposable
{
    readonly Context context;public readonly Accelerator Gpu;
    readonly Action<Index1D,ArrayView<double>,ArrayView<FusionCommand>,ArrayView<int>,ArrayView<FusionReceipt>,int> fused;
    readonly Action<Index1D,ArrayView<double>,ArrayView<FusionCommand>,ArrayView<int>,ArrayView<FusionReceipt>,int,int> step;
    int commandStride=FusionKernel.MaxCommands;
    public readonly MemoryBuffer1D<double,Stride1D.Dense> State;
    public readonly MemoryBuffer1D<FusionCommand,Stride1D.Dense> Commands;
    public readonly MemoryBuffer1D<int,Stride1D.Dense> Counts;
    public readonly MemoryBuffer1D<FusionReceipt,Stride1D.Dense> Output;
    public FusionDevice(int capacity)
    {
        context=Context.Create(b=>b.Cuda().EnableAlgorithms().Profiling());Gpu=context.CreateCudaAccelerator(0);
        fused=Gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<double>,ArrayView<FusionCommand>,ArrayView<int>,ArrayView<FusionReceipt>,int>(FusionKernel.Fused);
        step=Gpu.LoadAutoGroupedStreamKernel<Index1D,ArrayView<double>,ArrayView<FusionCommand>,ArrayView<int>,ArrayView<FusionReceipt>,int,int>(FusionKernel.Step);
        State=Gpu.Allocate1D<double>(capacity*FusionKernel.Stride);Commands=Gpu.Allocate1D<FusionCommand>(capacity*FusionKernel.MaxCommands);Counts=Gpu.Allocate1D<int>(capacity);Output=Gpu.Allocate1D<FusionReceipt>(capacity*FusionKernel.MaxCommands);
    }
    public void Upload(double[] state,FusionCommand[] commands,int[] counts){commandStride=commands.Length/counts.Length;if(counts.Any(n=>n>commandStride)||commands.Length%counts.Length!=0)throw new ArgumentException("Invalid command layout");State.View.SubView(0,state.Length).CopyFromCPU(state);Commands.View.SubView(0,commands.Length).CopyFromCPU(commands);Counts.View.SubView(0,counts.Length).CopyFromCPU(counts);}
    public void Launch(int count,string mode,int maxSteps)
    {if(mode=="resident")for(int j=0;j<maxSteps;j++)step(count,State.View,Commands.View,Counts.View,Output.View,commandStride,j);else fused(count,State.View,Commands.View,Counts.View,Output.View,commandStride);}
    public void Download(double[] state,FusionReceipt[] receipts){State.View.SubView(0,state.Length).CopyToCPU(state);Output.View.SubView(0,receipts.Length).CopyToCPU(receipts);Gpu.Synchronize();}
    public void Run(double[] state,FusionCommand[] commands,int[] counts,FusionReceipt[] receipts,string mode){Upload(state,commands,counts);Launch(counts.Length,mode,counts.Max());Download(state,receipts);}
    public void Dispose(){Output.Dispose();Counts.Dispose();Commands.Dispose();State.Dispose();Gpu.Dispose();context.Dispose();}
}
static class FusionBridge
{
    public static string Mode="off";public static FusionDevice Device;public static bool Capture;
    public static readonly List<FusionCase> Cases=new();public static readonly Dictionary<string,long> Counters=new();
    static void Count(string key,long n=1){Counters.TryGetValue(key,out long value);Counters[key]=value+n;}
    static readonly FieldInfo Reservations=typeof(HousingMaterialPlan).GetField("reservations",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo Lower=typeof(HousingMaterialPlan).GetField("lowerRanked",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo Remaining=typeof(ToolCoverageBudget).GetField("remaining",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo Consumed=typeof(ToolCoverageBudget).GetField("consumed",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo Multipliers=typeof(ToolCoverageBudget).GetField("housingMultipliers",BindingFlags.Instance|BindingFlags.NonPublic);
    sealed class Pending
    {
        public Camp Camp;public ToolCoverageBudget Coverage;public HousingMaterialPlan Housing;public Dictionary<string,double> Labor;public Dictionary<CraftGood,double> Produced;public TurnNeeds Needs;public string[] Roles;public FusionCase Case;public int ReceiptStart;public double Intervals;
    }
    static Pending pending;
    static double[] Pack(Pending p)
    {
        var s=new double[FusionKernel.Stride];var c=p.Camp;
        for(int i=0;i<11;i++)s[i]=NeolithicProduction.Stock(c,(CraftGood)i);
        s[11]=c.Inventory.Stone;s[12]=c.Inventory.Clay;s[13]=c.Inventory.Wood;s[14]=c.PlantFood;s[15]=c.AnimalFood;
        Array.Copy((double[])Remaining.GetValue(p.Coverage),0,s,16,3);Array.Copy((double[])Consumed.GetValue(p.Coverage),0,s,19,3);Array.Copy((double[])Multipliers.GetValue(p.Coverage),0,s,22,3);
        for(int i=0;i<p.Roles.Length;i++)s[32+i]=p.Labor[p.Roles[i]];
        for(int i=0;i<11;i++){p.Produced.TryGetValue((CraftGood)i,out double value);s[48+i]=value;s[64+i]=p.Needs.Production[(CraftGood)i];}
        if(p.Housing!=null)
        {
            s[80]=p.Housing.Claim;s[81]=p.Housing.WoodShare;s[82]=p.Housing.ClayShare;
            var reservations=(List<HousingMaterialPlan.Reservation>)Reservations.GetValue(p.Housing);s[83]=reservations.Count;
            foreach(var good in (HashSet<CraftGood>)Lower.GetValue(p.Housing))s[84]=(int)s[84]|(1<<(int)good);
            for(int i=0;i<reservations.Count;i++){int j=96+i*5;var r=reservations[i];s[j]=(int)r.Good;s[j+1]=r.Wood;s[j+2]=r.Clay;s[j+3]=r.Output;s[j+4]=r.Hard?1:0;}
        }
        return s;
    }
    public static bool Try(WorldState w,Camp c,double intervals,TurnRecord record,TurnNeeds needs,CraftRecipe[] recipes,ToolCoverageBudget coverage,HousingMaterialPlan housing,Dictionary<string,double> labor,Dictionary<CraftGood,double> produced,(string Role,CraftRecipe Recipe)[] steps)
    {
        if(Mode=="off")return false;Count("attempts");pending=null;
        bool Reject(string reason){Count("fallback-"+reason);return false;}
        if(needs?.Production==null)return Reject("unprepared-targets");
        if(c.GoodsStorage!=null)return Reject("goods-storage");
        if(c.Inventory.HousingUnits!=0)return Reject("legacy-housing");
        if(c.TradeOwed!=null&&c.TradeOwed.Any(x=>x!=0))return Reject("exports");
        if(intervals<=0)return Reject("intervals");
        if(housing==null)return Reject("missing-housing-plan");
        if(((List<HousingMaterialPlan.Reservation>)Reservations.GetValue(housing)).Count>16)return Reject("reservation-capacity");
        var roles=labor.Keys.ToArray();if(roles.Length>16)return Reject("role-capacity");
        // Craft capabilities now commit at Close, so eligibility stays fixed here.
        var cmds=new List<FusionCommand>();
        for(int layer=0;layer<2;layer++)foreach(var (role,r) in steps)
        {
            bool tool=r.Good==CraftGood.HandTools||r.Good==CraftGood.Tools;if(tool!=(layer==0))continue;
            if(!NeolithicProduction.RecipeAvailable(w,c,r)||NeolithicProduction.Priority(c,r.Good)<=0)continue;
            bool specialist=role==r.Specialist&&c.People.HasTech(r.Track);bool allowed=specialist||(role==WorkforceRoles.Craftsperson&&!r.SpecialistOnly);
            cmds.Add(new(){Good=(int)r.Good,Role=Array.IndexOf(roles,role),Enabled=allowed?1:0,Equipment=r.Equipment?1:0,Housing=ToolCoverageBudget.IsHousingWork(r.Good)?1:0,Settle=role==WorkforceRoles.Craftsperson||(r.SpecialistOnly&&role==r.Specialist)?1:0,Rate=SpecialistProductivity.CraftRate(w,c,r,role),BaseRate=r.Rate,Stone=r.Stone,Clay=r.Clay,Wood=r.Wood,Plant=r.PlantFood,Animal=r.AnimalFood,Fabric=r.AnimalFabric,HousingMaterial=r.HousingMaterial,Construction=r.ConstructionMaterial});
        }
        if(cmds.Count>FusionKernel.MaxCommands)return Reject("command-capacity");
        if(cmds.Any(x=>x.Role<0))throw new Exception("Missing role mapping");
        var p=new Pending{Camp=c,Coverage=coverage,Housing=housing,Labor=labor,Produced=produced,Needs=needs,Roles=roles,ReceiptStart=record.Production.Count,Intervals=intervals};
        var initial=Pack(p);p.Case=new(){Camp=c.Id,Turn=w.Turn,Initial=initial,Commands=cmds.ToArray()};Count("eligible");Count("commands",cmds.Count);
        if(Mode=="capture"){pending=p;return false;}
        // One-lane compact mode transfers only live commands/receipts; the kernel
        // addresses lane zero identically and retains the same arithmetic/order.
        int transferred=Mode=="fused-compact"?cmds.Count:FusionKernel.MaxCommands;
        var state=(double[])initial.Clone();var commands=new FusionCommand[transferred];cmds.CopyTo(commands);var output=new FusionReceipt[transferred];
        if(Mode=="packed-cpu")for(int j=0;j<cmds.Count;j++)output[j]=FusionCpu.Execute(state,0,commands[j]);
        else if(cmds.Count>0)Device.Run(state,commands,new[]{cmds.Count},output,Mode);
        if(Capture&&Cases.Count<2048)Cases.Add(p.Case);
        Commit(w,p,state,output,record,recipes);return true;
    }
    public static void End(TurnRecord record)
    {
        if(pending==null)return;var p=pending;pending=null;p.Case.Expected=Pack(p);var actual=record.Production.Skip(p.ReceiptStart).ToArray();
        p.Case.ExpectedReceipts=p.Case.Commands.Select(cmd=>{var r=actual.SingleOrDefault(x=>(int)x.Good==cmd.Good&&x.Role==p.Roles[cmd.Role]);return r==null?new FusionReceipt():new FusionReceipt{Amount=r.Output,Used=r.WorkerIntervals,Wood=r.Wood,Clay=r.Clay,Hand=r.HandToolsConsumed,Tools=r.ToolsConsumed};}).ToArray();
        if(Cases.Count<2048)Cases.Add(p.Case);Count("captured");
    }
    static void Commit(WorldState w,Pending p,double[] s,FusionReceipt[] output,TurnRecord record,CraftRecipe[] recipes)
    {
        var c=p.Camp;var inv=c.Inventory;inv.HandTools=s[0];inv.Pots=s[1];inv.Cloth=s[2];inv.FarmTools=s[3];inv.Tools=s[5];inv.AnimalFabric=s[6];inv.LightHousingUnits=s[7];inv.HerdingTools=s[8];inv.HousingMaterial=s[9];inv.FishingTools=s[10];inv.Stone=s[11];inv.Clay=s[12];inv.Wood=s[13];c.PlantFood=s[14];c.AnimalFood=s[15];
        var demographic=record.Demographics.FirstOrDefault(d=>d.Camp==c.Id);
        for(int j=0;j<p.Case.Commands.Length;j++)
        {
            var o=output[j];if(o.Amount<=1e-12)continue;var cmd=p.Case.Commands[j];var r=recipes.Single(x=>(int)x.Good==cmd.Good);string role=p.Roles[cmd.Role];double a=o.Amount;
            if(r.Good==CraftGood.Housing)NeolithicProduction.AddFixedHousing(w,c,a,record.Duration>0?record.StartDays+record.Duration:w.Days+p.Intervals*w.Config.BaseDays);
            record.Production.Add(new(){Camp=c.Id,Good=r.Good,Role=role,Output=a,WorkerIntervals=o.Used,Stone=a*r.Stone,Clay=o.Clay,Wood=o.Wood,PlantFood=a*r.PlantFood,AnimalFood=a*r.AnimalFood,AnimalFabric=a*r.AnimalFabric,HousingMaterial=a*r.HousingMaterial,EquipmentWorkerIntervals=r.Equipment?o.Used:0,HandToolsConsumed=o.Hand,ToolsConsumed=o.Tools});
            if(demographic!=null)demographic.ManufacturingFood+=a*(r.PlantFood+r.AnimalFood);
            TechnologyDiscovery.RecordCraftPractice(w,c.People,r.Track,a/r.Rate);
        }
        for(int i=0;i<p.Roles.Length;i++)p.Labor[p.Roles[i]]=s[32+i];
    }
}
