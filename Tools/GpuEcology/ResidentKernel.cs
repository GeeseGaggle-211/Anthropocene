using ILGPU;
using ILGPU.Algorithms;

public struct ResidentProfile { public double Food,Biomass,Water,FoodRate,BiomassRate,WaterRate,Moisture,Salt; }
public struct ResidentConfig { public double BaseDays,DaysPerYear,HalfLife,ReferenceIntensity,Displacement,MoistureReference,MoistureExponent,Flux; }
public struct ResidentLane
{
    public int Slot;public double Food,Biomass,Water,Days,Capital,Operation,Query,Working,Ending,Salt;
}
public struct ResidentCycle { public double FA,FB,FC,NA,NB,NC,WA,WB,WC; }
static class ResidentKernel
{
    static double Min(double x,double y)=>XMath.Min(x,y);
    static double Max(double x,double y)=>XMath.Max(x,y);
    static double Clamp(double x,double l,double h)=>Min(h,Max(l,x));
    struct Affine
    {
        public double A,B,C;
        public Affine(double a,double b,double c){A=a;B=b;C=c;}
        public Affine Then(Affine next)=>new(next.A*A,next.A*B+next.B,Min(next.C,next.A*C+next.B));
        public double Apply(double value)=>Min(C,A*value+B);
        public double Repeat(double value,long count,bool integerPower=false)
        {
            if(count==0)return value;
            double power=integerPower?Power(A,count):XMath.Pow(A,count),sum=XMath.Abs(1-A)<1e-14?count:(1-power)/(1-A);
            double previous=integerPower?Power(A,count-1):XMath.Pow(A,count-1),previousSum=XMath.Abs(1-A)<1e-14?count-1:(1-previous)/(1-A);
            double ceiling=C==double.PositiveInfinity?C:Min(C,previous*C+B*previousSum);
            return Min(ceiling,power*value+B*sum);
        }
    }
    static double Power(double x,long n){double value=1;while(n>0){if((n&1)!=0)value*=x;n>>=1;if(n>0)x*=x;}return value;}
    static Affine Recovery(double capacity,double rate,double days){double a=XMath.Exp(-rate*days);return new(a,capacity*(1-a),capacity);}
    static Affine Managed(double capacity,double rate,double flow,double days)
    {
        capacity=Max(0,capacity);rate=Max(0,rate);flow=Max(0,flow);double a=XMath.Exp(-rate*days);
        double added=rate<=1e-18?flow*days:flow*(1-a)/rate;return new(a,capacity*(1-a)+added,capacity);
    }
    static double Q(ResidentConfig c,double capital)=>Min(1,c.ReferenceIntensity*XMath.Sqrt(Max(0,capital)));
    static double Suit(ResidentConfig c,double moisture)
    {if(c.MoistureReference<=0)return 1;double ratio=Clamp(moisture/c.MoistureReference,0,1);return c.MoistureExponent==2?ratio*ratio:XMath.Pow(ratio,c.MoistureExponent);}
    static double Food(ResidentConfig c,ResidentProfile p,double q)
    {
        double retention=q<=c.Displacement?1:Clamp((1-q)/(1-c.Displacement),0,1);
        double capacity=Max(0,p.Biomass),natural=Clamp(Max(0,p.Food)*retention,0,capacity);
        return natural+(capacity-natural)*q*q*Suit(c,p.Moisture);
    }
    static double Flow(ResidentConfig c,ResidentProfile p,double q,double operation)=>Max(0,c.Flux)*Clamp(q,0,1)*Max(0,p.Biomass)*Max(0,p.BiomassRate)*Clamp(operation,0,1)*Suit(c,p.Moisture);
    static void Reclassify(ref ResidentLane s,ArrayView<ResidentProfile> profiles,ResidentConfig c,double oldCapital,double nextCapital)
    {
        if(XMath.Abs(oldCapital-nextCapital)<=1e-12||s.Biomass<=0)return;
        var p=profiles[s.Slot*4+(int)XMath.Floor(s.Days/c.BaseDays)%4];
        double oldFood=Food(c,p,Q(c,oldCapital)),newFood=Food(c,p,Q(c,nextCapital));
        double oldShare=p.Biomass<=0?0:Clamp(oldFood/p.Biomass,0,1),newShare=p.Biomass<=0?0:Clamp(newFood/p.Biomass,0,1);
        double nonfood=Max(0,s.Biomass-s.Food);
        if(newShare>oldShare&&oldShare<1)s.Food+=nonfood*(newShare-oldShare)/(1-oldShare);
        else if(newShare<oldShare&&oldShare>0)s.Food*=newShare/oldShare;
        s.Food=Clamp(s.Food,0,s.Biomass);
    }
    static void Begin(ref ResidentLane s,ArrayView<ResidentProfile> profiles,ResidentConfig c)
    {
        double days=Max(0,s.Query-s.Days);s.Working=s.Capital;s.Ending=s.Capital;
        if(days<=0)return;
        if(s.Capital>0&&s.Operation<1)
        {
            double decay=XMath.Log(2.0)/c.HalfLife*(1-Clamp(s.Operation,0,1))/c.DaysPerYear;
            double x=Max(0,decay)*Max(0,days);
            double mean=x<1e-5?1-x/2+x*x/6-x*x*x/24:(1-XMath.Exp(-x))/x;
            s.Working*=mean;
            s.Ending=s.Capital*XMath.Exp(-XMath.Log(2.0)/c.HalfLife*(1-Clamp(s.Operation,0,1))*(days/c.DaysPerYear));
        }
        Reclassify(ref s,profiles,c,s.Capital,s.Working);
    }
    static void Edge(ref ResidentLane s,ArrayView<ResidentProfile> profiles,ResidentConfig c,double days)
    {
        double remaining=days,q=Q(c,s.Working);
        for(int i=0;i<5&&remaining>1e-9;i++)
        {
            long interval=(long)XMath.Floor(s.Days/c.BaseDays+1e-10);
            double part=Min(remaining,(interval+1)*c.BaseDays-s.Days);if(part<=1e-9)break;
            var p=profiles[s.Slot*4+(int)(interval%4)];double food=Food(c,p,q);
            double n=Recovery(Max(0,p.Biomass-food),p.BiomassRate,part).Apply(Max(0,s.Biomass-s.Food));
            s.Food=Managed(food,p.FoodRate,Flow(c,p,q,s.Operation),part).Apply(s.Food);
            s.Biomass=s.Food+n;s.Water=Recovery(p.Water,p.WaterRate,part).Apply(s.Water);s.Days+=part;remaining-=part;
        }
    }
    static void Recover(ref ResidentLane s,ArrayView<ResidentProfile> profiles,ResidentConfig c)
    {
        double days=Max(0,s.Query-s.Days);if(days<=0)return;
        double cycle=4*c.BaseDays,phase=s.Days-XMath.Floor(s.Days/cycle)*cycle;
        double first=phase<=1e-9?0:Min(days,cycle-phase);if(first>0)Edge(ref s,profiles,c,first);
        double remaining=days-first;long cycles=(long)XMath.Floor(remaining/cycle+1e-10);
        if(cycles>0)
        {
            var food=new Affine(1,0,double.PositiveInfinity);var nonfood=food;var water=food;double q=Q(c,s.Working);
            for(int season=0;season<4;season++)
            {
                var p=profiles[s.Slot*4+season];double f=Food(c,p,q);
                food=food.Then(Managed(f,p.FoodRate,Flow(c,p,q,s.Operation),c.BaseDays));
                nonfood=nonfood.Then(Recovery(Max(0,p.Biomass-f),p.BiomassRate,c.BaseDays));
                water=water.Then(Recovery(p.Water,p.WaterRate,c.BaseDays));
            }
            double n=nonfood.Repeat(Max(0,s.Biomass-s.Food),cycles);s.Food=food.Repeat(s.Food,cycles);s.Biomass=s.Food+n;s.Water=water.Repeat(s.Water,cycles);
            double covered=cycles*cycle;s.Days+=covered;remaining-=covered;
        }
        if(remaining>1e-9)Edge(ref s,profiles,c,remaining);
    }
    static void Finish(ref ResidentLane s,ArrayView<ResidentProfile> profiles,ResidentConfig c)
    {
        Reclassify(ref s,profiles,c,s.Working,s.Ending);s.Capital=s.Ending;s.Days=s.Query;
        var p=profiles[s.Slot*4+(int)XMath.Floor(s.Days/c.BaseDays)%4];
        s.Biomass=Clamp(s.Biomass,0,p.Biomass);s.Food=Clamp(s.Food,0,Min(s.Biomass,Food(c,p,Q(c,s.Capital))));s.Water=Clamp(s.Water,0,p.Water);s.Salt=p.Salt;
    }
    public static void Fused(Index1D i,ArrayView<ResidentLane> states,ArrayView<ResidentProfile> profiles,ResidentConfig c)
    {var s=states[i];Begin(ref s,profiles,c);Recover(ref s,profiles,c);Finish(ref s,profiles,c);states[i]=s;}
    public static void Split(Index1D i,ArrayView<ResidentLane> states,ArrayView<ResidentProfile> profiles,ResidentConfig c,int phase)
    {var s=states[i];if(phase==0)Begin(ref s,profiles,c);else if(phase==1)Recover(ref s,profiles,c);else Finish(ref s,profiles,c);states[i]=s;}
    public static void BuildCycles(Index1D i,ArrayView<ResidentProfile> profiles,ArrayView<ResidentCycle> cycles,ResidentConfig c,int first)
    {
        int slot=first+i;var f=new Affine(1,0,double.PositiveInfinity);var n=f;var w=f;
        for(int season=0;season<4;season++)
        {
            var p=profiles[slot*4+season];double food=Food(c,p,0);
            f=f.Then(Managed(food,p.FoodRate,0,c.BaseDays));n=n.Then(Recovery(Max(0,p.Biomass-food),p.BiomassRate,c.BaseDays));w=w.Then(Recovery(p.Water,p.WaterRate,c.BaseDays));
        }
        cycles[slot]=new(){FA=f.A,FB=f.B,FC=f.C,NA=n.A,NB=n.B,NC=n.C,WA=w.A,WB=w.B,WC=w.C};
    }
    public static void CoefficientFused(Index1D i,ArrayView<ResidentLane> states,ArrayView<ResidentProfile> profiles,ArrayView<ResidentCycle> cycles,ResidentConfig c)
    {
        var s=states[i];double days=Max(0,s.Query-s.Days),cycle=4*c.BaseDays,phase=s.Days-XMath.Floor(s.Days/cycle)*cycle;
        long count=(long)XMath.Floor(days/cycle+1e-10);
        if(s.Capital==0&&days>0&&phase<=1e-9&&count>0&&XMath.Abs(days-count*cycle)<=1e-9)
        {
            var k=cycles[s.Slot];double n=new Affine(k.NA,k.NB,k.NC).Repeat(Max(0,s.Biomass-s.Food),count,true);
            s.Food=new Affine(k.FA,k.FB,k.FC).Repeat(s.Food,count,true);s.Biomass=s.Food+n;
            s.Water=new Affine(k.WA,k.WB,k.WC).Repeat(s.Water,count,true);s.Working=s.Ending=0;s.Days=s.Query;Finish(ref s,profiles,c);
        }
        else{Begin(ref s,profiles,c);Recover(ref s,profiles,c);Finish(ref s,profiles,c);}
        states[i]=s;
    }
}
