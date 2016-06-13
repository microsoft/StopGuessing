using System;
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

        public static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        public static async Task MainAsync(string[] args)
        {
            await Simulator.RunExperimentalSweep((config) =>
            {
                // Scale of test
                config.AttackersStrategy = ExperimentalConfiguration.AttackStrategy.BreadthFirst;
                config.PopularPasswordsToRemoveFromDistribution = 10000;
                config.FractionOfBenignIPsBehindProxies = 0; //0.1d;
                config.FractionOfMaliciousIPsToOverlapWithBenign = 0; //0.01d; // 0.1;

                //config.BlockingOptions.PopularityBasedPenaltyMultiplier_phi = (keyHeight, ladderHeight, frequency) =>
                //    frequency.Proportion.Numerator > 0 ? 5 : 1;

                //config.BlockingOptions.PopularityBasedThresholdMultiplier_T_multiplier = (keyHeight, ladderHeight, frequency) =>
                //    frequency.Proportion.Numerator > 0 ? 1 : 100;

                //ulong totalLoginAttempts = Billion;
                //config.TestTimeSpan = new TimeSpan(7, 0, 0, 0); // 7 days
                //double meanNumberOfLoginsPerBenignAccountDuringExperiment = 100d;
                //double meanNumberOfLoginsPerAttackerControlledIP = 1000d;

                //ulong totalLoginAttempts = 100 * Million; // 2.5m // 500 * Thousand; // * Million;
                //config.TestTimeSpan = new TimeSpan(7, 0, 0, 0); // 7 days
                //double meanNumberOfLoginsPerBenignAccountDuringExperiment = 10d;
                //double meanNumberOfLoginsPerAttackerControlledIP = 100d;

                ulong totalLoginAttempts = 5 * Million; // 2.5m // 500 * Thousand; // * Million;
                config.TestTimeSpan = new TimeSpan(7, 0, 0, 0); // 7 days
                double meanNumberOfLoginsPerBenignAccountDuringExperiment = 100d;
                double meanNumberOfLoginsPerAttackerControlledIP = 1000d;


                config.OutputPath = @"e:\";
                config.OutputDirectoryName = string.Format("Size_{0}_Strategy_{1}_Remove_{2}_Proxies_{3}_Overlap_{4}",
                    (int)Math.Log10(totalLoginAttempts),
                    config.AttackersStrategy == ExperimentalConfiguration.AttackStrategy.BreadthFirst
                        ? "BreadthFirst"
                        : config.AttackersStrategy == ExperimentalConfiguration.AttackStrategy.Weighted
                            ? "Weighted"
                            : "Avoid",
                    config.PopularPasswordsToRemoveFromDistribution,
                    (int)1000 * config.FractionOfBenignIPsBehindProxies,
                    (int)1000 * config.FractionOfMaliciousIPsToOverlapWithBenign
                    );

                // Figure out parameters from scale
                double fractionOfLoginAttemptsFromAttacker = 0.5d;
                double fractionOfLoginAttemptsFromBenign = 1d - fractionOfLoginAttemptsFromAttacker;

                double expectedNumberOfBenignAttempts = totalLoginAttempts * fractionOfLoginAttemptsFromBenign;
                double numberOfBenignAccounts = expectedNumberOfBenignAttempts /
                                                meanNumberOfLoginsPerBenignAccountDuringExperiment;

                double expectedNumberOfAttackAttempts = totalLoginAttempts * fractionOfLoginAttemptsFromAttacker;
                double numberOfAttackerIps = expectedNumberOfAttackAttempts /
                                             meanNumberOfLoginsPerAttackerControlledIP;

                // Make any changes to the config or the config.BlockingOptions within config here
                config.TotalLoginAttemptsToIssue = totalLoginAttempts;

                config.FractionOfLoginAttemptsFromAttacker = fractionOfLoginAttemptsFromAttacker;
                config.NumberOfBenignAccounts = (uint)numberOfBenignAccounts;

                // Scale of attackers resources
                config.NumberOfIpAddressesControlledByAttacker = (uint)numberOfAttackerIps;
                config.NumberOfAttackerControlledAccounts = (uint)numberOfAttackerIps;

                // Additional sources of false positives/negatives
                config.ProxySizeInUniqueClientIPs = 1000;

                // Blocking parameters
                // Make typos almost entirely ignored
                config.BlockingOptions.HeightOfBinomialLadder_H = 48;
                config.BlockingOptions.NumberOfBitsInBinomialLadderFilter_N = 1 << 29;
                config.BlockingOptions.PenaltyMulitiplierForTypo = 0.1d;
                //config.BlockingOptions.BlockThresholdMultiplierForUnpopularPasswords = 10d;
                config.BlockingOptions.ExpensiveHashingFunctionIterations = 1;


            });



        }

    }
}
