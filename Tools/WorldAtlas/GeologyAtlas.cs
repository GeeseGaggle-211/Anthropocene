using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Anthropocene.Simulation;

internal static class GeologyAtlas
{
    private sealed class Layer { public string File, Label, Legend; }
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
    private static readonly (int r, int g, int b)[] Palette = { (143, 135, 123), (222, 159, 135), (99, 132, 151), (105, 133, 77), (226, 212, 159), (177, 137, 76), (171, 136, 192), (228, 129, 184) };
    private static readonly (int r, int g, int b)[] MarinePalette = { (98, 117, 88), (70, 163, 180), (50, 158, 185), (50, 99, 166), (25, 42, 85), (208, 157, 100), (241, 102, 86) };
    public static string[] Export(WorldState w, string directory)
    {
        if (w.Resources == null) { File.WriteAllText(Path.Combine(directory, "geology.html"), "<!doctype html><title>Resources absent</title><p>This world has no resource layer.</p>"); return Array.Empty<string>(); }
        var r = w.Resources; var layers = new List<Layer>(); int count = w.Tiles.Length;
        void Map(string file, string label, string legend, Func<int, (int r, int g, int b)> color)
        {
            var pixels = new byte[count * 3];
            for (int i = 0; i < count; i++) { var c = color(i); pixels[i * 3] = (byte)c.r; pixels[i * 3 + 1] = (byte)c.g; pixels[i * 3 + 2] = (byte)c.b; }
            Png.Write(Path.Combine(directory, file + ".png"), w.Config.Width, w.Config.Height, pixels);
            layers.Add(new Layer { File = file + ".png", Label = label, Legend = legend });
        }
        (int r, int g, int b) Back(int id) => w.Tiles[id].Land ? (49, 61, 53) : (13, 29, 48);
        void Heat(string file, string label, string legend, double[] values, double ceiling)
        {
            Map(file, label, legend, i => {
                if (values[i] <= 0) return Back(i);
                double t = Math.Min(1, Math.Log(1 + values[i]) / Math.Log(1 + ceiling));
                return ((int)(80 + 175 * t), (int)(64 + 162 * t), (int)(82 - 30 * t));
            });
        }
        Map("geology-host", "Host rock", string.Join(" · ", Enum.GetValues(typeof(HostRock)).Cast<HostRock>().Select(h => h + " " + Hex(Palette[(int)h]))) + ". Generated historical context; does not replace terrain.", i => Palette[r.Host[i]]);
        Map("geology-province", "Geological provinces", "Each color identifies the dominant province. Background grey is inherited basement/oceanic crust. Histories may overlap beyond these dominant boundaries.", i => r.Province[i] == 0 ? (55, 65, 67) : ((int)(80 + Numbers.Random(w.Seed, 12900, r.Province[i]) * 175), (int)(80 + Numbers.Random(w.Seed, 12901, r.Province[i]) * 175), (int)(80 + Numbers.Random(w.Seed, 12902, r.Province[i]) * 175)));
        Map("geology-marine", "Marine setting", string.Join(" · ", Enum.GetValues(typeof(MarineSetting)).Cast<MarineSetting>().Select(s => s + " " + Hex(MarinePalette[(int)s]))) + ". Relative classes, not metres of bathymetry.", i => MarinePalette[r.Marine[i]]);
        var family = new int[count]; var amount = new double[count]; var depth = Enumerable.Repeat(-1, count).ToArray(); var exposed = new double[count]; var paths = new int[count];
        foreach (var body in r.Bodies)
        {
            foreach (var site in body.Sites)
            {
                double share = body.Components.Sum(component => r.TotalAt(body, site, component.Material));
                if (share > amount[site.Tile]) { amount[site.Tile] = share; family[site.Tile] = body.Profile; depth[site.Tile] = (int)site.Burial; exposed[site.Tile] = site.Exposure / 100.0; }
            }
            foreach (int id in body.TransportPath) paths[id] = 1;
            if (body.SourceBody != 0) paths[body.OriginTile] = 2;
        }
        Map("geology-deposits", "Deposit profiles", "Colored footprints show finite bodies; overlaps show the body with the largest local material quantity. Separate material maps show all intersecting bodies. Profile colors and counts are in the table below.", i => family[i] == 0 ? Back(i) : ProfileColor(family[i]));
        Map("geology-burial", "Burial depth", "For the largest local body: cream = surface, amber = shallow burial, violet = deep burial. Water depth is independent; see Marine setting.", i => depth[i] < 0 ? Back(i) : depth[i] == 0 ? (240, 220, 169) : depth[i] == 1 ? (206, 144, 67) : (139, 94, 189));
        Heat("geology-exposure", "Local exposed fraction", "Exposed fraction of the largest local body, 0–100%. A reference allocation modifier; separate from the actual accessible fraction of combined reserves and future equipment requirements.", exposed, 100);
        Map("geology-placers", "Source-to-placer paths", "Cyan = conserved source transport path; gold = final placer. Modern drainage reaches the sea; a bounded shelf sorting proxy can continue the same material budget seaward.", i => paths[i] == 0 ? Back(i) : paths[i] == 1 ? (83, 202, 220) : (255, 211, 85));
        foreach (var material in ResourceCatalog.Materials.Where(m => !m.Reserved))
        {
            var total = new double[count]; var accessible = new double[count]; var deep = new double[count];
            if ((int)material.Id <= 3)
                for (int i = 0; i < count; i++) { total[i] = r.SurfaceTotal(i, material.Id); accessible[i] = r.SurfaceAccessible(i, material.Id); deep[i] = r.SurfaceDeepTotal(i, material.Id); }
            foreach (var body in r.Bodies)
            {
                if (r.TotalComponentQuantity(body, material.Id) <= 0) continue;
                foreach (var site in body.Sites)
                { total[site.Tile] += r.TotalAt(body, site, material.Id); deep[site.Tile] += r.DeepAt(body, site, material.Id); accessible[site.Tile] += r.AccessibleAt(body, site, material.Id); }
            }
            string name = material.Name.ToLowerInvariant();
            double deepUnits = deep.Sum(), totalCeiling = r.MaximumQuantity * (1 + (deepUnits > 0 ? r.DeepReserveMultiplier : 0));
            double expectedTotal = r.Bodies.Sum(b => r.TotalComponentQuantity(b, material.Id)) + ((int)material.Id <= 3 ? Enumerable.Range(0, count).Sum(i => r.SurfaceTotal(i, material.Id)) : 0);
            double expectedDeep = r.Bodies.Sum(b => r.DeepComponentQuantity(b, material.Id)) + ((int)material.Id <= 3 ? Enumerable.Range(0, count).Sum(i => r.SurfaceDeepTotal(i, material.Id)) : 0);
            if (Math.Abs(total.Sum() - expectedTotal) > Math.Max(.01, expectedTotal * 1e-10) || Math.Abs(deepUnits - expectedDeep) > Math.Max(.01, expectedDeep * 1e-10))
                throw new InvalidOperationException("Atlas allocation does not conserve " + material.Name + " reserves.");
            Heat("resource-" + name + "-total", material.Name + " — total", "Local allocated " + material.Name + " units, including separate deep reserves where present; each finite material pool is allocated once across its source footprint. Reference host/surface cap " + r.MaximumQuantity.ToString("N0") + ". Initial deep mineral reserves equal " + r.DeepReserveMultiplier.ToString("0.###") + "× the initial accessible material pool; Wood retains 25% accessibility and has no deep reserve. Overlapping sources can sum higher. Logarithmic scale 0–" + totalCeiling.ToString("N0") + ".", total, totalCeiling);
            string discovery = r.DiscoveryTechnologies[(int)material.Id];
            double totalUnits = total.Sum(), accessibleUnits = accessible.Sum();
            Heat("resource-" + name + "-accessible", material.Name + " — accessible", "Saved reference allocation factor " + (r.AccessibleFraction(material.Id) * 100).ToString("0.###") + "%, combined with local exposure for the initial material pool; it is not the fraction of combined reserves. Current world accessible share " + (totalUnits > 0 ? (100 * accessibleUnits / totalUnits).ToString("0.###") + "%" : "not applicable (absent)") + ". Deep reserves are excluded. This is physical potential, not known or deliverable supply. Discovery requirement: " + (discovery.Length == 0 ? "none" : discovery) + ". Logarithmic scale 0–" + r.MaximumQuantity.ToString("N0") + ".", accessible, r.MaximumQuantity);
            if (deepUnits > 0)
                Heat("resource-" + name + "-deep", material.Name + " — deep reserve", "Separate allocated deep " + material.Name + " units. Excluded from early-accessible stock; metadata requires " + r.DeepMiningTechnology + ". Extraction and technology progression are not implemented in this generation pass. Logarithmic scale 0–" + (r.MaximumQuantity * r.DeepReserveMultiplier).ToString("N0") + ".", deep, r.MaximumQuantity * r.DeepReserveMultiplier);
        }
        var renewal = Enumerable.Range(0, count).Select(i => r.SurfaceTotal(i, MaterialId.Wood) * r.WoodRecoveryPerYear).ToArray();
        Heat("resource-wood-renewal", "Wood — renewal potential", "Maximum annual renewal from an empty stock: capacity × " + r.WoodRecoveryPerYear.ToString("0.###") + ". Future stock recovery uses dW/dt = rate × (capacity − W); this generation pass does not harvest or advance Wood. Scale 0–" + r.MaximumQuantity.ToString("N0") + " units/year.", renewal, r.MaximumQuantity);
        var profiles = GeologicalProfiles.All.Select(p => new {
            p.Id, p.Name, Color = Hex(ProfileColor(p.Id)), Count = r.Bodies.Count(b => b.Profile == p.Id),
            InitialReferenceHostUnits = r.Bodies.Where(b => b.Profile == p.Id).Sum(b => b.InitialQuantity), RemainingReferenceHostUnits = r.Bodies.Where(b => b.Profile == p.Id).Sum(b => b.Quantity),
            AccessibleMaterialUnits = r.Bodies.Where(b => b.Profile == p.Id).Sum(b => b.Components.Sum(c => b.Sites.Sum(s => r.AccessibleAt(b, s, c.Material)))),
            InitialDeepMaterialUnits = r.Bodies.Where(b => b.Profile == p.Id).Sum(b => b.Components.Sum(c => c.InitialDeepQuantity)), RemainingDeepMaterialUnits = r.Bodies.Where(b => b.Profile == p.Id).Sum(b => b.Components.Sum(c => c.DeepQuantity)),
            Absence = r.Bodies.Any(b => b.Profile == p.Id) ? "" : "No eligible candidate passed this profile's occurrence sampling; no guaranteed deposits."
        }).ToArray();
        var camps = w.Camps.OrderBy(c => c.Id).Select(c => new {
            c.Id, c.Player, c.Tile,
            Opportunities = new[] { MaterialId.Stone, MaterialId.Clay, MaterialId.Wood }.Select(m => new {
                Material = m.ToString(), LocalAccessibleUnits = w.Map.Radius(c.Tile, 1).Where(t => w.Map.Reachable(c.Tile, t, 1, n => w.Tiles[n].TraversableLand)).Sum(t => r.SurfaceAccessible(t, m)),
                WithinTwoHexes = w.Map.Radius(c.Tile, 2).Any(t => r.SurfaceTotal(t, m) > 0 && w.Map.Reachable(c.Tile, t, 2, n => w.Tiles[n].TraversableLand))
            }).ToArray()
        }).ToArray();
        var report = new {
            r.Version, r.GenerationVersion, r.CoreGeneratorVersion, r.CatalogHash, r.ProfileHash, r.ConfigurationHash, r.MaximumQuantity, r.SurfaceUnit, r.WoodRecoveryPerYear, r.DeepReserveMultiplier, r.DeepMiningTechnology,
            RemainingReferenceHostUnits = r.Bodies.Sum(b => b.Quantity), RemainingDeepDepositUnits = r.Bodies.Sum(b => b.Components.Sum(c => c.DeepQuantity)),
            SurfaceDeepUnits = Enumerable.Range(0, count).Sum(i => r.SurfaceDeepTotal(i, MaterialId.Stone) + r.SurfaceDeepTotal(i, MaterialId.Clay)),
            QuantityDefinitions = "Reference host quantities and allocation factors generate initial accessible pools; they are not additional extractable stock. For schema 4, mineral totals sum accessible and separate deep reserves; Wood remains capacity with 25% accessibility. Deep reserves require AdvancedMining metadata; extraction is deferred.",
            ResourceFingerprint = ResourceCodec.Fingerprint(r), PackedStateBytes = r.PackedBytes,
            OverlayMilliseconds = w.Generation.Stages.Select((s, i) => new { Stage = s, Milliseconds = w.Generation.Milliseconds[i] }).Where(s => s.Stage == "Latent geological provinces" || s.Stage == "Finite geological deposits" || s.Stage == "Surface materials and woody capacity" || s.Stage == "Resource opportunity audit").ToArray(),
            Profiles = profiles, Camps = camps, Materials = ResourceCatalog.Materials.Select(m => new { m.Id, m.Name, ReferenceAccessibleFraction = r.AccessibleFraction(m.Id), DiscoveryTechnology = r.DiscoveryTechnologies[(int)m.Id], m.Reserved,
                ReferenceMineralUnits = r.Bodies.Sum(b => r.ComponentQuantity(b, m.Id)), AccessibleMineralUnits = r.Bodies.Sum(b => b.Sites.Sum(s => r.AccessibleAt(b, s, m.Id))), DeepMineralUnits = r.Bodies.Sum(b => r.DeepComponentQuantity(b, m.Id)),
                TotalMineralUnits = r.Bodies.Sum(b => r.TotalComponentQuantity(b, m.Id)),
                SurfaceAccessibleUnits = (int)m.Id <= 3 ? Enumerable.Range(0, count).Sum(i => r.SurfaceAccessible(i, m.Id)) : 0,
                SurfaceDeepUnits = (int)m.Id <= 3 ? Enumerable.Range(0, count).Sum(i => r.SurfaceDeepTotal(i, m.Id)) : 0,
                SurfaceTotalUnits = (int)m.Id <= 3 ? Enumerable.Range(0, count).Sum(i => r.SurfaceTotal(i, m.Id)) : 0 }).ToArray(),
            OnshoreBodies = r.Bodies.Count(b => b.Sites.All(s => s.Setting == MarineSetting.Land)), OffshoreBodies = r.Bodies.Count(b => b.Sites.All(s => s.Setting != MarineSetting.Land)), CoastCrossingBodies = r.Bodies.Count(b => b.Sites.Any(s => s.Setting == MarineSetting.Land) && b.Sites.Any(s => s.Setting != MarineSetting.Land)),
            Bodies = r.Bodies.Select(b => new { b.Id, b.Profile, b.SourceBody, b.OriginTile, InitialReferenceHostQuantity = b.InitialQuantity, ReferenceHostQuantity = b.Quantity,
                InitialDeepQuantity = b.Components.Sum(c => c.InitialDeepQuantity), DeepQuantity = b.Components.Sum(c => c.DeepQuantity), b.Shape, b.Concentration, b.Hardness, b.Quality, b.Components, Sites = b.Sites.Length, b.TransportPath }).ToArray(),
            Limitations = "Developer truth only. No discovery, harvesting, delivery, trade, or new economy. Normalized procedural historical contexts, not reconstructed geological history. Surface capacity is per playable hex; normalized body candidates and total body reserves do not multiply with raster resolution. Finite minerals; renewable woody capacity independent of edible biomass. Quality/hardness are generated descriptors, not recipe rules."
        };
        File.WriteAllText(Path.Combine(directory, "geology.json"), JsonSerializer.Serialize(report, Json));
        string options = string.Join("", layers.Select((l, i) => "<option value='" + i + "'>" + System.Net.WebUtility.HtmlEncode(l.Label) + "</option>"));
        string rows = string.Join("", profiles.Select(p => "<tr><td><span style='color:" + p.Color + "'>■</span> " + p.Name + "</td><td>" + p.Count + "</td><td>" + p.AccessibleMaterialUnits.ToString("N0") + "</td><td>" + p.RemainingDeepMaterialUnits.ToString("N0") + "</td></tr>"));
        string html = "<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><title>Geological resources</title><style>body{max-width:1500px;margin:32px auto;padding:0 24px;background:#102027;color:#e5eee4;font:16px/1.5 system-ui}a{color:#abdacc}select,button{font:inherit;padding:8px;margin:6px 10px 6px 0;background:#233c44;color:#fff;border:1px solid #71918c}#viewport{overflow:auto;border:1px solid #5f7975}img{display:block;width:100%;image-rendering:pixelated}table{border-collapse:collapse}td,th{padding:6px 20px 6px 0;text-align:left}p{max-width:1050px}</style><h1>Geological resources — " + w.Config.Shape + " / " + w.Seed + "</h1><p>Developer truth maps · resource generator " + r.GenerationVersion + " · resource schema " + r.Version + " · terrain generator " + w.Generation.GeneratorVersion + " · " + w.Config.Width + " × " + w.Config.Height + ". <a href='index.html'>World gallery</a> · <a href='geology.json'>Resource data and provenance</a></p><p>Reference host or surface stock cap " + r.MaximumQuantity.ToString("N0") + " units. Minerals carry a separate deep reserve equal to " + r.DeepReserveMultiplier.ToString("0.###") + "× the initial accessible material pool, requiring " + (string.IsNullOrEmpty(r.DeepMiningTechnology) ? "no deep reserve in this world" : r.DeepMiningTechnology) + " metadata. Deep reserves are excluded from accessible maps. Technology progression and extraction are not implemented; these maps grant no player knowledge.</p><label for='layer'>Map </label><select id='layer'>" + options + "</select><button id='zoom'>Native pixels</button><a id='download'>Open PNG</a><h2 id='title'></h2><p id='legend'></p><div id='viewport'><img id='map' alt='Generated resource map'></div><h2>Deposit profiles</h2><table><tr><th>Profile</th><th>Bodies</th><th>Accessible material units</th><th>Deep material units</th></tr>" + rows + "</table><p>Blank material maps are valid: regional geology and scarcity are not repaired around camps. Per-start opportunities are in the resource data; they are not economic viability claims.</p><script>const layers=" + JsonSerializer.Serialize(layers, Json) + ";const select=document.getElementById('layer'),map=document.getElementById('map');function show(){const l=layers[+select.value];map.src=l.File;map.alt=l.Label;document.getElementById('title').textContent=l.Label;document.getElementById('legend').textContent=l.Legend;document.getElementById('download').href=l.File;}select.addEventListener('change',show);document.getElementById('zoom').onclick=()=>{map.style.width=map.style.width==='auto'?'100%':'auto';};show();</script></html>";
        File.WriteAllText(Path.Combine(directory, "geology.html"), html, Encoding.UTF8);
        return layers.Select(l => l.File).ToArray();
    }
    private static (int r, int g, int b) ProfileColor(int id) => ((int)(85 + Numbers.Random(42, 12903, id) * 170), (int)(85 + Numbers.Random(42, 12904, id) * 170), (int)(85 + Numbers.Random(42, 12905, id) * 170));
    private static string Hex((int r, int g, int b) c) => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
}
