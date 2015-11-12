using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StopGuessing.Models;
using System.IO;

namespace Simulator
{


    public class Program
    {
        private const ulong Thousand = 1000;
        private const ulong Million = Thousand * Thousand;
        private const ulong Billion = Thousand * Million;

        public async Task Main(string[] args)
        {
            await Simulator.RunExperimentalSweep((config) =>
            {
                // Scale of test
                ulong totalLoginAttempts = 1 * Million;

                // Figure out parameters from scale
                double meanNumberOfLoginsPerBenignAccountDuringExperiment = 10d;
                double meanNumberOfLoginsPerAttackerControlledIP = 100d;

                double fractionOfLoginAttemptsFromAttacker = 0.5d;
                double fractionOfLoginAttemptsFromBenign = 1d - fractionOfLoginAttemptsFromAttacker;

                double expectedNumberOfBenignAttempts = totalLoginAttempts*fractionOfLoginAttemptsFromBenign;
                double numberOfBenignAccounts = expectedNumberOfBenignAttempts/
                                                meanNumberOfLoginsPerBenignAccountDuringExperiment;

                double expectedNumberOfAttackAttempts = totalLoginAttempts*fractionOfLoginAttemptsFromAttacker;
                double numberOfAttackerIps = expectedNumberOfAttackAttempts/
                                             meanNumberOfLoginsPerAttackerControlledIP;

                // Make any changes to the config or the config.BlockingOptions within config here
                config.TotalLoginAttemptsToIssue = totalLoginAttempts;

                config.FractionOfLoginAttemptsFromAttacker = fractionOfLoginAttemptsFromAttacker;
                config.NumberOfBenignAccounts = (uint) numberOfBenignAccounts;

                // Scale of attackers resources
                config.NumberOfIpAddressesControlledByAttacker = (uint)numberOfAttackerIps;
                config.NumberOfAttackerControlledAccounts = (uint)numberOfAttackerIps;

                // Additional sources of false positives/negatives
                config.FractionOfBenignIPsBehindProxies = 0.1d;
                config.ProxySizeInUniqueClientIPs = 1000;

                // Blocking parameters
                // Make typos almost entirely ignored
                config.BlockingOptions.PenaltyMulitiplierForTypo = 0.1d;
            }, new Simulator.IParameterSweeper[]
            {
                new Simulator.ParameterSweeper<Simulator.SystemMode>
                {
                    Name = "Algorithm",
                    Parameters = new Simulator.SystemMode[]
                    {
                        Simulator.SystemMode.StopGuessing,
                        Simulator.SystemMode.Basic,
                        Simulator.SystemMode.SSH
                    },
                    ParameterSetter =
                        (config, modeForThisExp) => Simulator.SetSystemMode(config, modeForThisExp)
                },
                new Simulator.ParameterSweeper<double>
                {
                    Name = "BlockThresholdPopularPassword",
                    Parameters = new double[] {
                        3,
                        5,
                        8,
                        12,
                        18,
                        28,
                        40,
                        60,
                        80,
                        100 },
                    ParameterSetter =
                        (config, thresholdForThisExp) => config.BlockingOptions.BlockThresholdPopularPassword = thresholdForThisExp
                }
            });



        }
    }
}
