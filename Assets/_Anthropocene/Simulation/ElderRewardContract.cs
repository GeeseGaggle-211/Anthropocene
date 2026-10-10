using System;

namespace Anthropocene.Simulation
{
    public static class ElderRewardContract
    {
        public static ElderRewardVector Physical(int priorPopulation,
            double realizedQoL, double elapsedYears)
        {
            int population = Math.Max(0, priorPopulation);
            double quality = ElderRLContract.Unit(realizedQoL);
            double years = Math.Max(0, elapsedYears);
            double rate = population * quality;
            return new ElderRewardVector
            {
                PriorPopulation = population,
                RealizedQoL = quality,
                ElapsedYears = years,
                WelfareRate = rate,
                UndiscountedIntervalWelfare = rate * years
            };
        }
    }
}
