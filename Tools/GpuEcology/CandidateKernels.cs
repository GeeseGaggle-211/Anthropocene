using ILGPU;
using ILGPU.Algorithms;

// Research-only packed contracts. Field meanings are defined at each packer in CandidateBridge.
enum CandidateKind { Population, Production, Herd, FisheryCapital, FisheryStock }
public struct CandidateInput { public double A,B,C,D,E,F,G,H,I,J,K,L,M,N,O; public int Count; }
public struct CandidateOutput { public double A,B,C,D,E,F,G,H; public int Discrete; }
static class CandidateKernels
{
    static double Clamp(double x,double a,double b)=>XMath.Min(b,XMath.Max(a,x));
    static double Exp(double x)=>x==0?1:XMath.Exp(x);
    static double GrowthFood(double populationBase,double rate,double years)
    {if(rate<=1e-12||years<=0)return 0;double x=rate*years;return populationBase*((Exp(x)-1)/x-1);}
    static CandidateOutput ForRate(CandidateInput p,double rate,double clothingPlant,double clothingAnimal)
    {
        double food=GrowthFood(p.A,rate,p.K),final=p.Count*Exp(rate*p.K);
        double plant=XMath.Min(p.L,XMath.Max(0,p.F)),animal=XMath.Min(p.M,XMath.Max(0,p.H));
        plant=XMath.Max(plant,clothingPlant);animal=XMath.Max(animal,clothingAnimal);
        return new(){A=p.A+food,B=p.A,C=food,D=p.Count,E=final,F=plant+animal,G=plant,H=animal};
    }
    public static CandidateOutput Population(CandidateInput p)
    {
        double net=p.C-p.D;
        double cp=XMath.Min(XMath.Max(0,p.E),XMath.Max(0,p.F));
        double ca=XMath.Min(XMath.Max(0,p.G),XMath.Max(0,p.H));
        double after=XMath.Max(0,p.C-p.A);cp=XMath.Min(cp,after);ca=XMath.Min(ca,XMath.Max(0,after-cp));double clothing=cp+ca;
        double available=net+1e-8>=p.B+clothing&&p.C+1e-8>=p.A+clothing+p.I
            ?XMath.Min(XMath.Max(0,net-p.B-clothing),XMath.Max(0,p.C-p.A-clothing-p.I)):0;
        double low=0,high=p.J;
        if(p.J>1e-12&&available>1e-12)for(int k=0;k<64;k++)
        {double rate=(low+high)*.5;var plan=ForRate(p,rate,cp,ca);if(plan.C+plan.F-clothing<=available+1e-8)low=rate;else high=rate;}
        var result=ForRate(p,low,cp,ca);double budget=XMath.Max(0,available-result.C);
        double plant=XMath.Min(result.G-cp,budget);budget-=plant;double animal=XMath.Min(result.H-ca,budget);
        result.G=cp+XMath.Max(0,plant);result.H=ca+XMath.Max(0,animal);result.F=result.G+result.H;
        result.Discrete=(int)XMath.Floor(p.N+XMath.Max(0,result.E-result.D));return result;
    }
    public static CandidateOutput Production(CandidateInput p)
    {
        double amount=p.A;
        if(p.C>0)amount=XMath.Min(amount,p.B/p.C);
        if(p.E>0)amount=XMath.Min(amount,p.D/p.E);
        if(p.G>0)amount=XMath.Min(amount,p.F/p.G);
        if(p.I>0)amount=XMath.Min(amount,p.H/p.I);
        if(p.K>0)amount=XMath.Min(amount,p.J/p.K);
        if(p.M>0)amount=XMath.Min(amount,XMath.Max(0,p.L)/p.M);
        if(p.O>0)amount=XMath.Min(amount,XMath.Max(0,p.N)/p.O);
        return new(){A=amount,Discrete=amount>1e-12?1:0};
    }
    public static CandidateOutput Herd(CandidateInput p)
    {
        double target=(p.Count+p.A)*Exp(p.B*p.C*p.D*p.E);
        return new(){A=target,B=target-XMath.Floor(target),Discrete=XMath.Max(0,(int)XMath.Floor(target)-p.Count)};
    }
    public static CandidateOutput Capital(CandidateInput p)=>new(){A=p.A*Exp(p.D*(1-Clamp(p.B,0,1))*p.C)};
    public static CandidateOutput Stock(CandidateInput p)
    {
        double initial=p.A,capacity=p.B,rate=p.C,request=p.D,days=p.E,inflow=p.F;
        bool managed=inflow>0;
        if(!managed){capacity=XMath.Max(0,capacity);rate=XMath.Max(0,rate);request=XMath.Max(0,request);}
        double overflow=XMath.Max(0,initial-capacity),start=Clamp(initial,0,capacity),depletion=double.PositiveInfinity,end;
        if(!managed&&days<=0)return new(){A=start,E=overflow,F=depletion};
        double supply=rate*capacity+(managed?inflow:0);
        if(rate<=1e-18)
        {
            if(managed){if(request>inflow)depletion=start/(request-inflow);end=start+(inflow-request)*days;}
            else{if(request>0)depletion=start/request;end=XMath.Max(0,start-request*days);}
        }
        else
        {
            double equilibrium=managed?(supply-request)/rate:capacity-request/rate;
            end=equilibrium+(start-equilibrium)*Exp(-rate*days);
            if(equilibrium<0)depletion=XMath.Log((start-equilibrium)/-equilibrium)/rate;
        }
        double realized=request*XMath.Min(days,depletion)+XMath.Min(request,supply)*XMath.Max(0,days-depletion);
        end=Clamp(end,0,capacity);
        return new(){A=end,B=realized,C=XMath.Max(0,request*days-realized),D=XMath.Max(0,end-start+realized),E=overflow,F=depletion};
    }
    public static void PopulationKernel(Index1D i,ArrayView<CandidateInput> input,ArrayView<CandidateOutput> output)=>output[i]=Population(input[i]);
    public static void ProductionKernel(Index1D i,ArrayView<CandidateInput> input,ArrayView<CandidateOutput> output)=>output[i]=Production(input[i]);
    public static void HerdKernel(Index1D i,ArrayView<CandidateInput> input,ArrayView<CandidateOutput> output)=>output[i]=Herd(input[i]);
    public static void CapitalKernel(Index1D i,ArrayView<CandidateInput> input,ArrayView<CandidateOutput> output)=>output[i]=Capital(input[i]);
    public static void StockKernel(Index1D i,ArrayView<CandidateInput> input,ArrayView<CandidateOutput> output)=>output[i]=Stock(input[i]);
}
