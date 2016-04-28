using System.Collections.Generic;
using System.Net;
using StopGuessing.EncryptionPrimitives;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing;
using StopGuessing.DataStructures;
using StopGuessing.Models;
using StopGuessing.Memory;

namespace Simulator
{
    /// <summary>
    /// A class that tracks all of the user accounts being simulated, both benign and belonging to attackers.
    /// </summary>
    public class SimulatedAccounts
    {
        public List<SimulatedUserAccount> BenignAccounts = new List<SimulatedUserAccount>();
        public List<SimulatedUserAccount> AttackerAccounts = new List<SimulatedUserAccount>();
        public WeightedSelector<SimulatedUserAccount> BenignAccountSelector = new WeightedSelector<SimulatedUserAccount>();
        private readonly IpPool _ipPool;
        private readonly DebugLogger _logger;
        private readonly SimulatedPasswords _simPasswords;

        public SimulatedAccounts(IpPool ipPool, SimulatedPasswords simPasswords, DebugLogger logger)
        {
            _ipPool = ipPool;
            _logger = logger;
            _simPasswords = simPasswords;
        }


        public SimulatedUserAccount GetBenignAccountWeightedByLoginFrequency()
        {
            lock (BenignAccountSelector)
            {
                return BenignAccountSelector.GetItemByWeightedRandom();
            }
        }

        public SimulatedUserAccount GetBenignAccountAtRandomUniform()
        {
            return BenignAccounts[(int) StrongRandomNumberGenerator.Get32Bits(BenignAccounts.Count)];
        }

        public SimulatedUserAccount GetMaliciousAccountAtRandomUniform()
        {
            return AttackerAccounts[(int) StrongRandomNumberGenerator.Get32Bits(AttackerAccounts.Count)];
        }



        /// <summary>
        /// Create accounts, generating passwords, primary IP
        /// </summary>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task GenerateAsync(ExperimentalConfiguration experimentalConfiguration,
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
                              //IUserAccountContextFactory accountContextFactory,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _logger.WriteStatus("Creating {0:N0} benign accounts", experimentalConfiguration.NumberOfBenignAccounts);        
            MemoryUserAccountController userAccountController = new MemoryUserAccountController();;
            ConcurrentBag<SimulatedUserAccount> benignSimulatedAccountBag = new ConcurrentBag<SimulatedUserAccount>();
            //
            // Create benign accounts in parallel
            Parallel.For(0, (int) experimentalConfiguration.NumberOfBenignAccounts, (index) =>
            {
                if (index > 0 && index % 10000 == 0)
                    _logger.WriteStatus("Created {0:N0} benign accounts", index);
                SimulatedUserAccount userAccount = new SimulatedUserAccount()
                {
                    UsernameOrAccountId = "user_" + index.ToString(),
                    Password = _simPasswords.GetPasswordFromWeightedDistribution()
                };
                userAccount.ClientAddresses.Add(_ipPool.GetNewRandomBenignIp());
                userAccount.Cookies.Add(StrongRandomNumberGenerator.Get64Bits().ToString());

                benignSimulatedAccountBag.Add(userAccount);

                double inverseFrequency = Distributions.GetLogNormal(0, 1);
                if (inverseFrequency < 0.01d)
                    inverseFrequency = 0.01d;
                if (inverseFrequency > 50d)
                    inverseFrequency = 50d;
                double frequency = 1 / inverseFrequency;
                lock (BenignAccountSelector)
                {
                    BenignAccountSelector.AddItem(userAccount, frequency);
                }
            });
            BenignAccounts = benignSimulatedAccountBag.ToList();
            _logger.WriteStatus("Finished creating {0:N0} benign accounts",
                experimentalConfiguration.NumberOfBenignAccounts);

            //
            // Right after creating benign accounts we create IPs and accounts controlled by the attacker. 
            // (We create the attacker IPs here, and not earlier, because we need to have the benign IPs generated in order to create overlap)
            _logger.WriteStatus("Creating attacker IPs");            
            _ipPool.GenerateAttackersIps();

            _logger.WriteStatus("Creating {0:N0} attacker accounts",
                experimentalConfiguration.NumberOfAttackerControlledAccounts);
            ConcurrentBag<SimulatedUserAccount> maliciousSimulatedAccountBag = new ConcurrentBag<SimulatedUserAccount>();
            
            //
            // Create accounts in parallel
            Parallel.For(0, (int) experimentalConfiguration.NumberOfAttackerControlledAccounts, (index) =>
            {
                SimulatedUserAccount userAccount = new SimulatedUserAccount()
                {
                    UsernameOrAccountId = "attacker_" + index.ToString(),
                    Password = _simPasswords.GetPasswordFromWeightedDistribution(),
                };
                userAccount.ClientAddresses.Add(_ipPool.GetRandomMaliciousIp());
                maliciousSimulatedAccountBag.Add(userAccount);
            });
            AttackerAccounts = maliciousSimulatedAccountBag.ToList();
            _logger.WriteStatus("Finished creating {0:N0} attacker accounts",
                experimentalConfiguration.NumberOfAttackerControlledAccounts);
            
            //
            // Now create full UserAccount records for each simulated account and store them into the account context
            Parallel.ForEach(BenignAccounts.Union(AttackerAccounts),
                (simAccount, loopState) =>
                {
                    //if (loopState. % 10000 == 0)
                    //    _logger.WriteStatus("Created account {0:N0}", index);
                    simAccount.CreditHalfLife = experimentalConfiguration.BlockingOptions.AccountCreditLimitHalfLife;
                    simAccount.CreditLimit = experimentalConfiguration.BlockingOptions.AccountCreditLimit;

                    foreach (string cookie in simAccount.Cookies)
                        userAccountController.HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
                            simAccount,
                            LoginAttempt.HashCookie(cookie),
                            cancellationToken);
                });
            _logger.WriteStatus("Finished creating user accounts for each simluated account record");
        }
    }
}