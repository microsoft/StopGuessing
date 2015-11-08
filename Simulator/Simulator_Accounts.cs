using System.Collections.Generic;
using System.Net;
using StopGuessing.EncryptionPrimitives;
using System;

namespace Simulator
{

    public partial class Simulator
    {
        public List<SimulatedAccount> BenignAccounts = new List<SimulatedAccount>();
        public List<SimulatedAccount> MaliciousAccounts = new List<SimulatedAccount>();
        public WeightedSelector<string> PasswordSelector;
        public WeightedSelector<string> CommonPasswordSelector; 
        public List<string> OrderedListOfMostCommonPasswords = new List<string>(); 
        public WeightedSelector<SimulatedAccount> BenignAccountSelector = new WeightedSelector<SimulatedAccount>();


        public SimulatedAccount GetBenignAccountWeightedByLoginFrequency()
        {
            return BenignAccountSelector.GetItemByWeightedRandom();
        }

        public SimulatedAccount GetBenignAccountAtRandomUniform()
        {
            return BenignAccounts[(int) StrongRandomNumberGenerator.Get32Bits(BenignAccounts.Count)];
        }

        public SimulatedAccount GetMaliciousAccountAtRandomUniform()
        {
            return BenignAccounts[(int) StrongRandomNumberGenerator.Get32Bits(BenignAccounts.Count)];
        }

        public string GetPasswordFromWeightedDistribution()
        {
            return PasswordSelector.GetItemByWeightedRandom();
        }

        /// <summary>
        ///Generate a random benign IP address.
        /// </summary>
        public IPAddress GetRandomBenignIp()
        {
            long v4Address = StrongRandomNumberGenerator.Get32Bits(MyExperimentalConfiguration.SizeOfBenignIpSpace);
            return new IPAddress(v4Address);
        }

        /// <summary>
        /// Generate a random malicious IP address
        /// </summary>
        public IPAddress GetRandomMaliciousIp()
        {
            if (StrongRandomNumberGenerator.GetFraction() <
                MyExperimentalConfiguration.FractionOfMaliciousIPsToOverlapWithBenign)
                return GetRandomBenignIp();
            // Start the non-ovlerlapping malicious address from the top of the IP space so that they don't overlap with benign
            long v4Address = UInt32.MaxValue - 
                StrongRandomNumberGenerator.Get32Bits(MyExperimentalConfiguration.SizeOfNonOverlappingAttackerIpSpace);
            return new IPAddress(v4Address);
        }

        /// <summary>
        /// Create accounts, generating passwords, primary IP
        /// </summary>
        public void GenerateSimulatedAccounts()
        {
            PasswordSelector = new WeightedSelector<string>();
            CommonPasswordSelector = new WeightedSelector<string>();
            uint lineNumber = 0;
            // Created a weighted-random selector for paasswords based on the RockYou database.
            using (System.IO.StreamReader file = 
                new System.IO.StreamReader(MyExperimentalConfiguration.PasswordFrequencyFile))
            {
                string lineWithCountFollowedBySpaceFollowedByPassword;
                while ((lineWithCountFollowedBySpaceFollowedByPassword = file.ReadLine()) != null)
                {
                    lineWithCountFollowedBySpaceFollowedByPassword =
                        lineWithCountFollowedBySpaceFollowedByPassword.Trim();
                    int indexOfFirstSpace = lineWithCountFollowedBySpaceFollowedByPassword.IndexOf(' ');
                    if (indexOfFirstSpace < 0 ||
                        indexOfFirstSpace + 1 >= lineWithCountFollowedBySpaceFollowedByPassword.Length)
                        continue; // The line is invalid as it doesn't have a space with a password after it
                    string countAsString = lineWithCountFollowedBySpaceFollowedByPassword.Substring(0, indexOfFirstSpace);
                    ulong count;
                    if (!ulong.TryParse(countAsString, out count))
                        continue; // The count field is invalid as it doesn't parse to an unsigned number
                    string password = lineWithCountFollowedBySpaceFollowedByPassword.Substring(indexOfFirstSpace + 1);
                    PasswordSelector.AddItem(password, count);
                    if (lineNumber++ < MyExperimentalConfiguration.NumberOfPopularPasswordsForAttackerToExploit)
                    {
                        CommonPasswordSelector.AddItem(password, count);
                        OrderedListOfMostCommonPasswords.Add(password);
                    }
                }
            }

            int totalAccounts = 0;
            foreach (
                ExperimentalConfiguration.BenignUserAccountGroup group in MyExperimentalConfiguration.BenignUserGroups)
            {
                for (ulong i = 0; i < group.GroupSize; i++)
                {
                    SimulatedAccount account = new SimulatedAccount()
                    {
                        UniqueId = (totalAccounts++).ToString(),
                        Password = PasswordSelector.GetItemByWeightedRandom(),
                        PrimaryIp = GetRandomBenignIp()
                    };
                    BenignAccounts.Add(account);
                    BenignAccountSelector.AddItem(account, group.LoginsPerYear);
                }
            }

            for (ulong i = 0; i < MyExperimentalConfiguration.NumberOfAttackerControlledAccounts; i++)
            {
                MaliciousAccounts.Add(new SimulatedAccount()
                {
                    UniqueId = (totalAccounts++).ToString(),
                    Password = PasswordSelector.GetItemByWeightedRandom(),
                    PrimaryIp = GetRandomBenignIp()
                });
            }
        }
    }

}