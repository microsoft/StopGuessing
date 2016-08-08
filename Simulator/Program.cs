using System;
using System.Threading.Tasks;
using StopGuessing.Models;
using System.IO;

namespace Simulator
{
    
    public class Program
    {
        public static string BasePath = @"e:\";
        public static void Main(string[] args)
        {
            ulong sizeInMillions = 1;
            Simulator.RunExperimentalSweep(new[]
            {
                GetConfig(ExperimentalConfiguration.AttackStrategy.BreadthFirst, 100, scaleInMillions: sizeInMillions),
                GetConfig(ExperimentalConfiguration.AttackStrategy.UseUntilLikelyPopular, 100, scaleInMillions: sizeInMillions),
                GetConfig(ExperimentalConfiguration.AttackStrategy.Weighted, 100, scaleInMillions: sizeInMillions),
            } );

        }


        private const ulong Thousand = 1000;
        private const ulong Million = Thousand * Thousand;
        private const ulong Billion = Thousand * Million;

        public static ExperimentalConfiguration GetConfig(
            ExperimentalConfiguration.AttackStrategy attackersStrategy = ExperimentalConfiguration.AttackStrategy.BreadthFirst,
            int numberOfMostPopularPasswordsToRemoveFromDistribution = 100,
            double fractionOfBenignIPsBehindProxies = 0.1,
            double fractionOfMaliciousIPsToOverlapWithBenign = .01d,
            double fractionOfLoginAttemptsFromAttacker = 0.5d,
            ulong scaleInMillions = 1)
        {
            ExperimentalConfiguration config = new ExperimentalConfiguration();
            // Scale of test
            config.AttackersStrategy = attackersStrategy;
            config.PopularPasswordsToRemoveFromDistribution = numberOfMostPopularPasswordsToRemoveFromDistribution;
            config.FractionOfBenignIPsBehindProxies = fractionOfBenignIPsBehindProxies;
            config.FractionOfMaliciousIPsToOverlapWithBenign = fractionOfMaliciousIPsToOverlapWithBenign;

            ulong totalLoginAttempts = scaleInMillions * Million;
            config.TestTimeSpan = new TimeSpan(7, 0, 0, 0); // 7 days
            double meanNumberOfLoginsPerBenignAccountDuringExperiment = 100d;
            double meanNumberOfLoginsPerAttackerControlledIP = 1000d;


            config.OutputPath = BasePath;
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
            config.BlockingOptions.BinomialLadderFrequencyThreshdold_T = 44;
            config.BlockingOptions.ExpensiveHashingFunctionIterations = 1;
            return config;
        }
    }
}
