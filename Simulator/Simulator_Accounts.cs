using System.Collections.Generic;
using System.Net;
using StopGuessing.EncryptionPrimitives;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.Serialization;

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
            return MaliciousAccounts[(int) StrongRandomNumberGenerator.Get32Bits(MaliciousAccounts.Count)];
        }

        public string GetPasswordFromWeightedDistribution()
        {
            return PasswordSelector.GetItemByWeightedRandom();
        }

        [DataContract]
        public class IPAddressDebugInfo
        {
            [DataMember]
            public HashSet<string> UserIdsOfBenignUsers = new HashSet<string>();
            [DataMember]
            public bool IsPartOfProxy;
            [DataMember]
            public bool IsInAttackersIpPool;
        }

        private readonly Dictionary<IPAddress,IPAddressDebugInfo> _debugInformationAboutIpAddresses = new Dictionary<IPAddress, IPAddressDebugInfo>();

        public IPAddressDebugInfo GetIpAddressDebugInfo(IPAddress address)
        {
            lock (_debugInformationAboutIpAddresses)
            {
                if (!_debugInformationAboutIpAddresses.ContainsKey(address))
                {
                    _debugInformationAboutIpAddresses[address] = new IPAddressDebugInfo();
                }
                return _debugInformationAboutIpAddresses[address];
            }
        }



        private readonly Object _proxyAddressLock = new object();
        private IPAddress _currentProxyAddress = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
        private int _numberOfClientsBehindTheCurrentProxy = 0;
        /// <summary>
        ///Generate a random benign IP address.
        /// </summary>
        public IPAddress GetNewRandomBenignIp(string forUserId)
        {
            IPAddress address;
            IPAddressDebugInfo debugInfo;
            if (StrongRandomNumberGenerator.GetFraction() < MyExperimentalConfiguration.FractionOfBenignIPsBehindProxies)
            {
                // Use the most recent proxy IP
                lock (_proxyAddressLock)
                {
                    address = _currentProxyAddress;
                    if (++_numberOfClientsBehindTheCurrentProxy >=
                        MyExperimentalConfiguration.ProxySizeInUniqueClientIPs)
                        _currentProxyAddress = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                    debugInfo = GetIpAddressDebugInfo(_currentProxyAddress);
                    lock (debugInfo)
                    {
                        debugInfo.IsPartOfProxy = true;
                    }
                }
            }
            else
            {
                // Just pick a random address
                address = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                _ipAddresssesInUseByBenignUsers.Add(address);
                debugInfo = GetIpAddressDebugInfo(address);
            }
            lock (debugInfo)
            {
                debugInfo.UserIdsOfBenignUsers.Add(forUserId);
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
            for (i = 0; i < numberOfOverlappingIps && listOfIpAddressesInUseByBenignUsers.Count > 0; i++)
            {
                int randIndex = (int) StrongRandomNumberGenerator.Get32Bits(listOfIpAddressesInUseByBenignUsers.Count);
                IPAddress address = listOfIpAddressesInUseByBenignUsers[randIndex];
                IPAddressDebugInfo debugInfo = GetIpAddressDebugInfo(address);
                lock (debugInfo)
                {
                    debugInfo.IsInAttackersIpPool = true;
                }
                _maliciousIpAddresses.Add(address);
                listOfIpAddressesInUseByBenignUsers.RemoveAt(randIndex);
            }
            for (i = 0; i < MyExperimentalConfiguration.NumberOfIpAddressesControlledByAttacker; i++)
            {
                IPAddress address = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                IPAddressDebugInfo debugInfo = GetIpAddressDebugInfo(address);
                lock (debugInfo)
                {
                    debugInfo.IsInAttackersIpPool = true;
                }
                _maliciousIpAddresses.Add(address);
            }
        }


        /// <summary>
        /// Generate a random malicious IP address
        /// </summary>
        public IPAddress GetRandomMaliciousIp()
        {
            int randIndex = (int)StrongRandomNumberGenerator.Get32Bits(_maliciousIpAddresses.Count);
            IPAddress address = _maliciousIpAddresses[randIndex];
            var debugInfo = GetIpAddressDebugInfo(address);
            lock (debugInfo)
            {
                if (debugInfo.IsInAttackersIpPool != true)
                {
                    debugInfo.IsInAttackersIpPool = true;
                }
            }
            return _maliciousIpAddresses[randIndex];
        }


        public static List<string> GetKnownPopularPasswords(string pathToPreviouslyKnownPopularPasswordFile)
        {
            List<string> knownPopularPasswords = new List<string>();
            using (System.IO.StreamReader file =
                new System.IO.StreamReader(pathToPreviouslyKnownPopularPasswordFile))
            {

                string line;
                while ((line = file.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (!string.IsNullOrEmpty(line))
                        knownPopularPasswords.Add(line);
                }
            }
            return knownPopularPasswords;
        }

        public void PrimeWithKnownPasswords(IEnumerable<string> knownPopularPasswords)
        {
            foreach (string password in knownPopularPasswords)
            {
                for (int i = 0; i < 100; i++)
                {
                    MyLoginAttemptController._passwordPopularityTracker.GetPopularityOfPasswordAmongFailures(
                        password, false);
                }
            }
        }

        public static WeightedSelector<string> GetPasswordSelector(string PathToWeightedFrequencyFile)
        {
            WeightedSelector<string> passwordSelector = new WeightedSelector<string>();
            // Created a weighted-random selector for paasswords based on the RockYou database.
            using (System.IO.StreamReader file =
                new System.IO.StreamReader(PathToWeightedFrequencyFile))
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
                    passwordSelector.AddItem(password, count);
                }
            }
            return passwordSelector;
        }



        /// <summary>
        /// Create accounts, generating passwords, primary IP
        /// </summary>
        public void GenerateSimulatedAccounts()
        {
            CommonPasswordSelector =
                PasswordSelector.TrimToInitialItems(
                    (int) MyExperimentalConfiguration.NumberOfPopularPasswordsForAttackerToExploit);
            OrderedListOfMostCommonPasswords =
                PasswordSelector.GetItems((int) MyExperimentalConfiguration.NumberOfPopularPasswordsForAttackerToExploit);

            int totalAccounts = 0;

            // Generate benign accounts
            for (uint i = 0; i < MyExperimentalConfiguration.NumberOfBenignAccounts; i++)
            {
                SimulatedAccount account = new SimulatedAccount()
                {
                    UniqueId = (totalAccounts++).ToString(),
                    Password = PasswordSelector.GetItemByWeightedRandom()
                };
                account.ClientAddresses.Add(GetNewRandomBenignIp(account.UniqueId));
                account.Cookies.Add(StrongRandomNumberGenerator.Get64Bits().ToString());
                BenignAccounts.Add(account);
                double inverseFrequency = Distributions.GetLogNormal(0, 1);
                if (inverseFrequency < 0.01d)
                    inverseFrequency = 0.01d;
                if (inverseFrequency > 50d)
                    inverseFrequency = 50d;
                double frequency = 1/inverseFrequency;
                BenignAccountSelector.AddItem(account, frequency);
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