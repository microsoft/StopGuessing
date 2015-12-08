using System.Collections.Generic;
using System.Net;
using StopGuessing.EncryptionPrimitives;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using StopGuessing.DataStructures;

namespace Simulator
{

    public class SimulatedAccounts
    {
        public List<SimulatedAccount> BenignAccounts = new List<SimulatedAccount>();
        public List<SimulatedAccount> MaliciousAccounts = new List<SimulatedAccount>();
        public WeightedSelector<SimulatedAccount> BenignAccountSelector = new WeightedSelector<SimulatedAccount>();
        private IpPool _ipPool;
        private DebugLogger _logger;
        private SimulatedPasswords _simPasswords;

        public SimulatedAccounts(IpPool ipPool, SimulatedPasswords simPasswords, DebugLogger logger)
        {
            _ipPool = ipPool;
            _logger = logger;
            _simPasswords = simPasswords;
        }


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



        /// <summary>
        /// Create accounts, generating passwords, primary IP
        /// </summary>
        public void Generate(ExperimentalConfiguration experimentalConfiguration)
        {
            _logger.WriteStatus("Creating {0:N0} benign accounts", experimentalConfiguration.NumberOfBenignAccounts);
            int totalAccounts = 0;


            // Generate benign accounts
            for (uint i = 0; i < experimentalConfiguration.NumberOfBenignAccounts; i++)
            {
                if (i > 0 && i%10000 == 0)
                    _logger.WriteStatus("Created {0:N0} benign accounts", i);
                SimulatedAccount account = new SimulatedAccount()
                {
                    UniqueId = (totalAccounts++).ToString(),
                    Password = _simPasswords.GetPasswordFromWeightedDistribution()
                };
                account.ClientAddresses.Add(_ipPool.GetNewRandomBenignIp(account.UniqueId));
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
            _logger.WriteStatus("Finished creating {0:N0} benign accounts",
                experimentalConfiguration.NumberOfBenignAccounts);


            // Right after creating benign accounts we can create malicious ones. 
            // (we'll needed to wait for the the benign IPs to be generated create some overlap)
            _ipPool.GenerateMaliciousIps();

            _logger.WriteStatus("Creating {0:N0} attacker accounts",
                experimentalConfiguration.NumberOfAttackerControlledAccounts);

            // Generate attacker accounts
            for (ulong i = 0; i < experimentalConfiguration.NumberOfAttackerControlledAccounts; i++)
            {
                SimulatedAccount account = new SimulatedAccount()
                {
                    UniqueId = (totalAccounts++).ToString(),
                    Password = _simPasswords.GetPasswordFromWeightedDistribution(),
                };
                account.ClientAddresses.Add(_ipPool.GetRandomMaliciousIp());
                MaliciousAccounts.Add(account);
            }
            _logger.WriteStatus("Finished creating {0:N0} attacker accounts",
                experimentalConfiguration.NumberOfAttackerControlledAccounts);
        }
    }
}