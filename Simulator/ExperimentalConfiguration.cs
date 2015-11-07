using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

        public string PasswordFrequencyFile = @"rockyou-withcount.txt";

        public ulong TotalLoginAttemptsToIssue = 2*Million;

        public double ChanceOfCoookieReUse = 0.20d;
        public int MaxCookiesPerUserAccount = 10;
        public double ChanceOfBenignPasswordTypo = 0.02d;
        public double ChanceOfBenignAccountNameTypoResultingInAValidUserName = 0.02d;
        public double ChanceOfAccidentallyUsingAnotherAccountPassword = 0.02d;

        public double ChanceOfUsingThePrimaryIp = 2d / 3d;

        public double FractionOfLoginAttemptsFromAttacker = 0.5d;

        public ulong NumberOfAttackerControlledAccounts = 100*Thousand;

        public uint SizeOfBenignIpSpace = 300 * (uint)Thousand;
        public uint SizeOfNonOverlappingAttackerIpSpace = 300 * (uint)Thousand;
        public double FractionOfMaliciousIPsToOverlapWithBenign = 0.10d;

        public uint NumberOfPopularPasswordsForAttackerToExploit = 1*(uint)Thousand;

        public readonly BenignUserAccountGroup[] BenignUserGroups = new BenignUserAccountGroup[]
        {
            // Group 0 logs in 5 times per day
            new BenignUserAccountGroup() {GroupSize = 200*Thousand, LoginsPerYear = DaysPerYear*5},
            // Group 1 logs in once per day
            new BenignUserAccountGroup() {GroupSize = 200*Thousand, LoginsPerYear = DaysPerYear},
            // Group 2 logs in once per week
            new BenignUserAccountGroup() {GroupSize = 200*Thousand, LoginsPerYear = WeeksPerYear},
            // Group 3 logs in once per month
            new BenignUserAccountGroup() {GroupSize = 200*Thousand, LoginsPerYear = MonthsPerYear},
            // Group 4 logs in once per year
            new BenignUserAccountGroup() {GroupSize = 200*Thousand, LoginsPerYear = 1}
        };
    }
}
