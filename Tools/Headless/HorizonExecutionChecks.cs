using System;
using System.Collections.Generic;
using Anthropocene.Simulation;

internal static class HorizonExecutionChecks
{
    public static void Run()
    {
        var world = new WorldGenerator().Generate(219, SimulationConfig.Small(2));
        var original = SaveCodec.Save(world);
        (int ActiveTiles, int TileIntegrations, long ClaimsAllocated) RunAt(double days)
        {
            var state = SaveCodec.Load(original);
            state.Config.TurnDays = days;
            var camp = state.Camps[0];
            camp.Technology = Technology.Cultivation;
            camp.Technologies[(int)Technology.Cultivation] = true;
            Ownership.Bind(state);
            var resolver = new TurnResolver(new PhaseExecution(1));
            var prepared = TurnPlanner.Prepare(state, new Dictionary<int, List<Order>>());
            resolver.Resolve(state, prepared);
            var metrics = resolver.LastMetrics;
            if (metrics.SeasonalTiles != 0 || metrics.TileIntegrations != metrics.ActiveTiles ||
                metrics.FullTurnTiles != metrics.ActiveTiles)
                throw new Exception("Horizon execution must evaluate each active tile exactly once.");
            return (metrics.ActiveTiles, metrics.TileIntegrations, metrics.ClaimsAllocated);
        }
        var oneYear = RunAt(365);
        var twentyYears = RunAt(7300);
        if (oneYear.TileIntegrations != twentyYears.TileIntegrations ||
            oneYear.ActiveTiles != twentyYears.ActiveTiles)
            throw new Exception("Horizon duration changed the number of tile kernels on a fixed world.");
        Console.WriteLine("PASS: horizon execution uses " + twentyYears.TileIntegrations +
            " tile kernels for both one-year and twenty-year turns");
    }
}
