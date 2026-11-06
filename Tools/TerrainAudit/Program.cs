using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using Anthropocene.Simulation;
class Audit {
static double P(IEnumerable<double> vals,double p) {var v=vals.OrderBy(x=>x).ToArray(); return v.Length==0?0:v[(int)((v.Length-1)*p)];}
static void Main() {
var cfg=JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText("Assets/_Anthropocene/Resources/OpeningConfig.json"),new JsonSerializerOptions{IncludeFields=true});PlaytestScenarios.Configure(cfg);
var w=new WorldGenerator().Generate(11,cfg); var metrics=w.TerrainMetrics;var land=w.Tiles.Where(t=>t.Land).ToArray();
Console.WriteLine($"Seed11 {cfg.Width}x{cfg.Height} lapse={cfg.LapseRate} sea={cfg.SeaLevel} land={land.Length}");
void Group(string name,IEnumerable<Tile> group) {var a=group.ToArray(); if(a.Length==0)return; Console.WriteLine($"{name}: n={a.Length} elev p50/p90/max={P(a.Select(t=>(double)t.Elevation),.5):F4}/{P(a.Select(t=>(double)t.Elevation),.9):F4}/{a.Max(t=>t.Elevation):F4} rugged p10/p50/p90/max={P(a.Select(t=>(double)metrics.At(t.Id).Ruggedness),.1):F2}/{P(a.Select(t=>(double)metrics.At(t.Id).Ruggedness),.5):F2}/{P(a.Select(t=>(double)metrics.At(t.Id).Ruggedness),.9):F2}/{a.Max(t=>metrics.At(t.Id).Ruggedness):F2} >=50={a.Count(t=>metrics.At(t.Id).Ruggedness>=50)*100.0/a.Length:F1}% meanT={a.Average(t=>t.Seasons.Average(s=>s.Temperature)):F2} cold/ice={a.Count(t=>t.Environment==EnvironmentKind.Cold||t.Environment==EnvironmentKind.Ice)*100.0/a.Length:F1}% anySnow={a.Count(t=>t.Seasons.Any(s=>s.Snow>0))*100.0/a.Length:F1}% lapseCooling={a.Average(t=>(t.Elevation-cfg.SeaLevel)*cfg.LapseRate):F2}C"); }
Group("land",land); Group("orogen core",land.Where(t=>w.Generation.AncientOrogenStrength[t.Id]>.5)); Group("orogen uplift>.05",land.Where(t=>w.Generation.AncientOrogenUplift[t.Id]>.05));
for(int i=1;i<=10;i++)Group("level"+i,land.Where(t=>t.ElevationLevel==i));
foreach(var t in land.OrderByDescending(t=>t.Elevation).Take(3))Console.WriteLine($"Peak tile{t.Id} q{t.Id%cfg.Width} r{t.Id/cfg.Width} h={t.Elevation} biome={t.Environment} temps={string.Join('/',t.Seasons.Select(s=>s.Temperature.ToString("F1")))} snow={string.Join('/',t.Seasons.Select(s=>s.Snow.ToString("F4")))} rugged={metrics.At(t.Id).Ruggedness}");
var g=new HexCorners(cfg.Width,cfg.Height); var sum=new double[g.Capacity];var n=new int[g.Capacity];foreach(var t in w.Tiles)for(int c=0;c<6;c++){int v=g.Corner(t.Id,c);sum[v]+=t.Elevation;n[v]++;}for(int v=0;v<n.Length;v++)if(n[v]>0)sum[v]/=n[v];
Console.WriteLine($"Rivers={w.Rivers.Count} rawCornerUphill={w.Rivers.Count(e=>sum[e.ToVertex]>sum[e.FromVertex]+1e-6)} routingUphill={w.Rivers.Count(e=>e.ToSurface>e.FromSurface+1e-6)} filledFromCorners={w.Rivers.Count(e=>e.FromSurface>sum[e.FromVertex]+.00001)}");
var differences=land.Where(t=>t.River).Select(t=>new{t,ns=w.Map.Radius(t.Id,2).Where(id=>w.Tiles[id].Land&&!w.Tiles[id].River).ToArray()}).Where(x=>x.ns.Length>0).Select(x=>x.t.Elevation-x.ns.Average(id=>w.Tiles[id].Elevation)).ToArray();Console.WriteLine($"riverBankRelativeToNearbyNonRiver median={P(differences.Select(x=>(double)x),.5):F5} belowShare={differences.Count(x=>x<0)*100.0/differences.Length:F1}%");
var cxt=new GenerationContext{World=w,Map=w.Map}; var before=w.Tiles.Select(t=>t.Seasons.Select(s=>s.Temperature).ToArray()).ToArray();new ClimateStage().Generate(cxt);
var delta=land.Select(t=>(double)(t.Seasons[0].Temperature-before[t.Id][0])).ToArray();Console.WriteLine($"FinalHeightClimateRefresh maxAbsDelta={delta.Max(Math.Abs):F4} changedAbove.1C={delta.Count(x=>Math.Abs(x)>.1)}");
}
}
