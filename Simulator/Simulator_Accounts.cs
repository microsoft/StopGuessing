using System.Collections.Generic;
using System.Net;
using StopGuessing.EncryptionPrimitives;
using System;
using System.Collections.Concurrent;
using System.Linq;

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
        private readonly ConcurrentBag<IPAddress> _ipAddresssesInUseByBenignUsers = new ConcurrentBag<IPAddress>();





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

        private readonly Object _proxyAddressLock = new object();
        private IPAddress _currentProxyAddress = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
        private int _numberOfClientsBehindTheCurrentProxy = 0;
        /// <summary>
        ///Generate a random benign IP address.
        /// </summary>
        public IPAddress GetNewRandomBenignIp()
        {
            IPAddress address;
            if (StrongRandomNumberGenerator.GetFraction() < MyExperimentalConfiguration.FractionOfBenignIPsBehindProxies)
            {
                // Use the most recent proxy IP
                lock (_proxyAddressLock)
                {
                    address = _currentProxyAddress;
                    if (++_numberOfClientsBehindTheCurrentProxy >=
                        MyExperimentalConfiguration.ProxySizeInUniqueClientIPs)
                        _currentProxyAddress = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                }
            }
            else
            {
                // Just pick a random address
                address = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                _ipAddresssesInUseByBenignUsers.Add(address);
            }
            return address;
        }


        private readonly List<IPAddress> _maliciousIpAddresses = new List<IPAddress>();
        public void GenerateMaliciousIps()
        {
            List<IPAddress> listOfIpAddressesInUseByBenignUsers = _ipAddresssesInUseByBenignUsers.ToList();
            uint numberOfOverlappingIps = (uint) 
                (MyExperimentalConfiguration.NumberOfIpAddressesControlledByAttacker*
                 MyExperimentalConfiguration.FractionOfMaliciousIPsToOverlapWithBenign);
            uint i;
            for (i = 0; i < numberOfOverlappingIps; i++)
            {
                int randIndex = (int) StrongRandomNumberGenerator.Get32Bits(listOfIpAddressesInUseByBenignUsers.Count);
                _maliciousIpAddresses.Add(listOfIpAddressesInUseByBenignUsers[randIndex]);
                listOfIpAddressesInUseByBenignUsers.RemoveAt(randIndex);
            }
            for (i = 0; i < MyExperimentalConfiguration.NumberOfIpAddressesControlledByAttacker; i++)
            {
                _maliciousIpAddresses.Add(new IPAddress(StrongRandomNumberGenerator.Get32Bits()));
            }
        }


        /// <summary>
        /// Generate a random malicious IP address
        /// </summary>
        public IPAddress GetRandomMaliciousIp()
        {
            int randIndex = (int)StrongRandomNumberGenerator.Get32Bits(_maliciousIpAddresses.Count);
            return _maliciousIpAddresses[randIndex];
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

            // Generate benign accounts
            foreach (
                ExperimentalConfiguration.BenignUserAccountGroup group in MyExperimentalConfiguration.BenignUserGroups)
            {
                for (ulong i = 0; i < group.GroupSize; i++)
                {
                    SimulatedAccount account = new SimulatedAccount()
                    {
                        UniqueId = (totalAccounts++).ToString(),
                        Password = PasswordSelector.GetItemByWeightedRandom()
                    };
                    account.ClientAddresses.Add(GetNewRandomBenignIp());
                    account.Cookies.Add(StrongRandomNumberGenerator.Get64Bits().ToString());
                    BenignAccounts.Add(account);
                    BenignAccountSelector.AddItem(account, group.LoginsPerYear);
                }
            }

            // Right after creating benign accounts we can create malicious ones. 
            // (we'll needed to wait for the the benign IPs to be generated create some overlap)
            GenerateMaliciousIps();

            // Generate attacker accounts
            for (ulong i = 0; i < MyExperimentalConfiguration.NumberOfAttackerControlledAccounts; i++)
            {
                SimulatedAccount account = new SimulatedAccount()
                {
                    UniqueId = (totalAccounts++).ToString(),
                    Password = PasswordSelector.GetItemByWeightedRandom(),
                };
                account.ClientAddresses.Add(GetRandomMaliciousIp());
                MaliciousAccounts.Add(account);
            }
        }
    }

}