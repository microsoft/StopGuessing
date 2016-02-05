using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace Simulator
{
    public class ExperimentalConfiguration
    {
        private const int DaysPerYear = 365;
        private const int WeeksPerYear = 52;
        private const int MonthsPerYear = 12;
        private const ulong Thousand = 1000;
        private const ulong Million = Thousand*Thousand;
        private const ulong Billion = Thousand*Million;

        public class BenignUserAccountGroup
        {
            public ulong GroupSize;
            public ulong LoginsPerYear;
        }

        public BlockingAlgorithmOptions BlockingOptions = new BlockingAlgorithmOptions();

        public TimeSpan TestTimeSpan = new TimeSpan(1, 0, 0, 0); // 1 day

        public enum AttackStrategy
        {
            BreadthFirst,
            Weighted,
            UseUntilLikelyPopular
        };
        public AttackStrategy AttackersStrategy = AttackStrategy.BreadthFirst;

        public string OutputPath = @"e:\";
        public string OutputDirectoryName = @"Experiment";
        public string PasswordFrequencyFile = @"..\..\rockyou-withcount.txt";
        public string PreviouslyKnownPopularPasswordFile = @"..\..\phpbb.txt";

        public ulong TotalLoginAttemptsToIssue = 10*Thousand;

        public double ChanceOfCoookieReUse = 0.90d;
        public int MaxCookiesPerUserAccount = 10;

        public double ChanceOfIpReUse = 0.85d;
        public int MaxIpPerUserAccount = 5;

        public int PopularPasswordsToRemoveFromDistribution = 0;

        public double ChanceOfLongRepeatOfStalePassword = 0.0004; // 1 in 2,500
        public double MinutesBetweenLongRepeatOfOldPassword = 5; // an attempt every 5 minutes
        public uint LengthOfLongRepeatOfOldPassword = (uint) ( (60 * 24) / 5 ); // 24 hours / an attempt every 5 minutes
        public double ChanceOfBenignPasswordTypo = 0.02d;
        public double ChanceOfRepeatTypo = 2d/3d; // two thirds
        public double ChanceOfRepeatUseOfPasswordFromAnotherAccount = 1d / 3d; // one thirds
        public double ChanceOfRepeatWrongAccountName = .2d; // 20%
        public double DelayBetweenRepeatBenignErrorsInSeconds = 7d;
        public double ChanceOfBenignAccountNameTypoResultingInAValidUserName = 0.02d;
        public double ChanceOfAccidentallyUsingAnotherAccountPassword = 0.02d;

        public double FractionOfLoginAttemptsFromAttacker = 0.5d;

        public ulong NumberOfAttackerControlledAccounts = 1*Thousand;
        
        public uint NumberOfIpAddressesControlledByAttacker = 100;// * (uint)Thousand;
        public double FractionOfMaliciousIPsToOverlapWithBenign = 0.1;

        public ulong MaxAttackerGuessesPerPassword = 25;

        public uint ProxySizeInUniqueClientIPs = 1000;
        public double FractionOfBenignIPsBehindProxies = 0.20d; 

        public double ProbabilityThatAttackerChoosesAnInvalidAccount = 0.10d;

        public uint NumberOfPopularPasswordsForAttackerToExploit = 1*(uint)Thousand;

        public uint NumberOfBenignAccounts = 10*(uint)Thousand;
    }
}
