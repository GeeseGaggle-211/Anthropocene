using ILGPU;
using ILGPU.Algorithms;

public struct FusionCommand
{
    public int Good,Role,Enabled,Equipment,Housing,Settle;
    public double Rate,BaseRate,Stone,Clay,Wood,Plant,Animal,Fabric,HousingMaterial,Construction;
}
public struct FusionReceipt { public double Amount,Used,Wood,Clay,Hand,Tools; }
// State is private to one camp/lane. No atomics or inter-lane writes.
static class FusionKernel
{
    public const int Stride=192,MaxCommands=128,ResStart=96;
    static double Min(double a,double b)=>XMath.Min(a,b);
    static double Max(double a,double b)=>XMath.Max(a,b);
    static double Service(ArrayView<double> s,int b,FusionCommand c,double value,int kind)
    {
        double total=0;
        for(int j=0;j<3;j++)
        {
            int grade=c.Housing!=0?2-j:j;double mult=c.Housing!=0?s[b+22+grade]:1;
            if(mult<=0||(kind!=0&&value<=0))continue;
            double remaining=s[b+16+grade],amount,time;
            if(kind==0) // Capacity for a labor budget.
            {
                if(grade==2){time=Min(remaining,value);amount=time*c.Rate*mult;}
                else{amount=Min(remaining,value*c.Rate*mult);time=amount<=0?0:amount/(c.Rate*mult);}
                total+=amount;value-=time;
            }
            else // Time or Spend for a requested output.
            {
                if(kind==1){amount=grade==2?Min(remaining*c.Rate*mult,value):Min(remaining,value);time=amount/(c.Rate*mult);}
                else if(grade==2){time=Min(remaining,Max(0,value)/(c.Rate*mult));amount=time*c.Rate*mult;}
                else{amount=Min(remaining,Max(0,value));time=amount/(c.Rate*mult);}
                total+=time;value-=amount;
                if(kind==2){s[b+16+grade]=Max(0,remaining-(grade==2?time:amount));if(grade<2)s[b+19+grade]+=amount/(grade==0?1:4);}
            }
        }
        return total;
    }
    static double HousingLimit(ArrayView<double> s,int b,FusionCommand c,double labor,double demand)
    {
        if(!(demand>0)||!(labor>0)||!(c.Rate>0))return 0;
        double ceiling=Service(s,b,c,double.PositiveInfinity,0),stock=Max(0,s[b+9]);
        double hi=Min(demand,ceiling-stock);if(!(hi>0))return 0;
        if(stock<=0)return Min(hi,Service(s,b,c,labor/2,0));
        if(Service(s,b,c,hi,1)+Service(s,b,c,Min(hi+stock,ceiling),1)<=labor)return hi;
        double lo=0;for(int i=0;i<48;i++){double mid=(lo+hi)/2;if(Service(s,b,c,mid,1)+Service(s,b,c,Min(mid+stock,ceiling),1)<=labor)lo=mid;else hi=mid;}return lo;
    }
    static double Reserved(ArrayView<double> s,int b,int hard,int material)
    {
        double total=0;for(int i=0;i<(int)s[b+83];i++){int r=b+ResStart+5*i;if((int)s[r+4]==hard)total+=s[r+3]*s[r+material];}return total;
    }
    static double MaterialLimit(ArrayView<double> s,int b,FusionCommand c,double desired,out double wood,out double clay)
    {
        wood=clay=0;if(!(desired>0)||!(c.Construction>0))return 0;
        double sw=Reserved(s,b,0,1),sc=Reserved(s,b,0,2),uw=Max(0,s[b+13]-Reserved(s,b,1,1)),uc=Max(0,s[b+12]-Reserved(s,b,1,2));
        double amount=Min(desired,(uw+uc)/c.Construction);if(amount<=1e-12)return 0;
        double total=amount*c.Construction,fw=Max(0,uw-sw),fc=Max(0,uc-sc);
        wood=Min(fw,total*s[b+81]);clay=Min(fc,total*s[b+82]);double left=total-wood-clay;
        double add=Min(left,fw-wood);wood+=add;left-=add;add=Min(left,fc-clay);clay+=add;left-=add;
        if(left>1e-12){double rw=Max(0,Min(sw,uw-wood)),rc=Max(0,Min(sc,uc-clay)),rt=rw+rc;if(rt>0){double dw=Min(rw,left*rw/rt),dc=Min(rc,left-dw);dw+=Min(rw-dw,left-dw-dc);wood+=dw;clay+=dc;}}
        return amount;
    }
    static void Complete(ArrayView<double> s,int b,FusionCommand c,double amount,bool release)
    {
        if(c.Construction>0)s[b+80]=release?0:Max(0,s[b+80]-amount*c.Construction);
        else for(int i=0;i<(int)s[b+83];i++){int r=b+ResStart+5*i;if((int)s[r]==c.Good)s[r+3]=release?0:Max(0,s[r+3]-amount);}
    }
    public static FusionReceipt Execute(ArrayView<double> s,int b,FusionCommand c)
    {
        var result=new FusionReceipt();double labor=s[b+32+c.Role],target=Max(0,s[b+64+c.Good]-s[b+48+c.Good]);
        if(c.Enabled!=0&&labor>0&&target>0&&c.Rate>0&&c.BaseRate>0)
        {
            double a=c.Good==9?HousingLimit(s,b,c,labor,target):Min(target,c.Equipment!=0?Service(s,b,c,labor,0):labor*c.Rate);
            if(c.Stone>0)a=Min(a,s[b+11]/c.Stone);if(c.Clay>0)a=Min(a,s[b+12]/c.Clay);if(c.Wood>0)a=Min(a,s[b+13]/c.Wood);
            if(c.Plant>0)a=Min(a,s[b+14]/c.Plant);if(c.Animal>0)a=Min(a,s[b+15]/c.Animal);if(c.Fabric>0)a=Min(a,Max(0,s[b+6])/c.Fabric);if(c.HousingMaterial>0)a=Min(a,Max(0,s[b+9])/c.HousingMaterial);
            if(c.Construction<=0&&(c.Wood>0||c.Clay>0)&&s[b+80]>1e-12&&(((int)s[b+84]&(1<<c.Good))!=0))
            {
                double uw=Max(0,s[b+13]-Reserved(s,b,1,1)),uc=Max(0,s[b+12]-Reserved(s,b,1,2));
                if(c.Wood>0)a=Min(a,Max(0,uw-Max(0,s[b+80]-uc))/c.Wood);if(c.Clay>0)a=Min(a,Max(0,uc-Max(0,s[b+80]-uw))/c.Clay);a=Max(0,a);
            }
            if(c.Construction>0)a=MaterialLimit(s,b,c,a,out _,out _);
            if(a>1e-12)
            {
                double wood=a*c.Wood,clay=a*c.Clay;if(c.Construction>0)MaterialLimit(s,b,c,a,out wood,out clay);
                double hb=c.Equipment!=0?s[b+19]:0,tb=c.Equipment!=0?s[b+20]:0;
                double used=c.Equipment!=0?Service(s,b,c,a,2):c.Good==9?Service(s,b,c,a,1):a/c.Rate;
                double hand=c.Equipment!=0?s[b+19]-hb:0,tools=c.Equipment!=0?s[b+20]-tb:0;
                s[b]-=hand;s[b+5]-=tools;s[b+11]-=a*c.Stone;s[b+13]-=wood;s[b+12]-=clay;s[b+14]-=a*c.Plant;s[b+15]-=a*c.Animal;s[b+6]-=a*c.Fabric;s[b+9]-=a*c.HousingMaterial;
                Complete(s,b,c,a,false);if(c.Good!=4)s[b+c.Good]+=a;if(c.Good==0)s[b+16]+=a;else if(c.Good==5)s[b+17]+=a*4;
                s[b+32+c.Role]-=used;s[b+48+c.Good]+=a;result=new(){Amount=a,Used=used,Wood=wood,Clay=clay,Hand=hand,Tools=tools};
            }
        }
        if(c.Settle!=0)Complete(s,b,c,0,true);return result;
    }
    public static void Fused(Index1D lane,ArrayView<double> s,ArrayView<FusionCommand> commands,ArrayView<int> counts,ArrayView<FusionReceipt> output,int commandStride)
    {for(int j=0;j<counts[lane];j++)output[lane*commandStride+j]=Execute(s,lane*Stride,commands[lane*commandStride+j]);}
    public static void Step(Index1D lane,ArrayView<double> s,ArrayView<FusionCommand> commands,ArrayView<int> counts,ArrayView<FusionReceipt> output,int commandStride,int j)
    {if(j<counts[lane])output[lane*commandStride+j]=Execute(s,lane*Stride,commands[lane*commandStride+j]);}
}
