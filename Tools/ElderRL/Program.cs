using System;
using System.Text.Json;
using Anthropocene.Simulation;

namespace Anthropocene.Tools.ElderRL
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string command = args.Length == 0 ? "schema" : args[0].ToLowerInvariant();
            if (command == "schema")
            {
                var payload = new
                {
                    schemaVersion = ElderRLContract.SchemaVersion,
                    maxLocalSites = ElderRLContract.MaxLocalSites,
                    maxFishingGrounds = ElderRLContract.MaxFishingGrounds,
                    maxKnownContacts = ElderRLContract.MaxKnownContacts,
                    maxCommoditySlots = ElderRLContract.MaxCommoditySlots,
                    maxObligations = ElderRLContract.MaxObligations,
                    defaultEnvironment = new ElderEnvironmentOptions(),
                    defaultDirective = new ElderDirective()
                };
                Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            Console.Error.WriteLine("ElderRL scaffold commands:");
            Console.Error.WriteLine("  schema   Print the versioned model-neutral RL contract.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Reset/Step is intentionally not wired yet. The next work item is to implement");
            Console.Error.WriteLine("IElderEnvironment against the real TurnPlanning/DemandControl/resolver path.");
            return 2;
        }
    }
}
