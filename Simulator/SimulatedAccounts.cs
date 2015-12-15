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

namespace Simulator
{
    /// <summary>
    /// A class that tracks all of the user accounts being simulated, both benign and belonging to attackers.
    /// </summary>
    public class SimulatedAccounts
    {
        public List<SimulatedAccount> BenignAccounts = new List<SimulatedAccount>();
        public List<SimulatedAccount> AttackerAccounts = new List<SimulatedAccount>();
        public WeightedSelector<SimulatedAccount> BenignAccountSelector = new WeightedSelector<SimulatedAccount>();
        private readonly IpPool _ipPool;
        private readonly DebugLogger _logger;
        private readonly SimulatedPasswords _simPasswords;

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
            return AttackerAccounts[(int) StrongRandomNumberGenerator.Get32Bits(AttackerAccounts.Count)];
        }



        /// <summary>
        /// Create accounts, generating passwords, primary IP
        /// </summary>
        public async Task GenerateAsync(ExperimentalConfiguration experimentalConfiguration,
            IUserAccountContextFactory accountContextFactory,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _logger.WriteStatus("Creating {0:N0} benign accounts", experimentalConfiguration.NumberOfBenignAccounts);        
            ConcurrentBag<SimulatedAccount> benignSimulatedAccountBag = new ConcurrentBag<SimulatedAccount>();
            //
            // Create benign accounts in parallel
            Parallel.For(0, (int) experimentalConfiguration.NumberOfBenignAccounts, (index) =>
            {
                if (index > 0 && index % 10000 == 0)
                    _logger.WriteStatus("Created {0:N0} benign accounts", index);
                SimulatedAccount account = new SimulatedAccount()
                {
                    UniqueId = "user_" + index.ToString(),
                    Password = _simPasswords.GetPasswordFromWeightedDistribution()
                };
                account.ClientAddresses.Add(_ipPool.GetNewRandomBenignIp());
                account.Cookies.Add(StrongRandomNumberGenerator.Get64Bits().ToString());

                benignSimulatedAccountBag.Add(account);

                double inverseFrequency = Distributions.GetLogNormal(0, 1);
                if (inverseFrequency < 0.01d)
                    inverseFrequency = 0.01d;
                if (inverseFrequency > 50d)
                    inverseFrequency = 50d;
                double frequency = 1 / inverseFrequency;
                lock (BenignAccountSelector)
                {
                    BenignAccountSelector.AddItem(account, frequency);
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
            ConcurrentBag<SimulatedAccount> maliciousSimulatedAccountBag = new ConcurrentBag<SimulatedAccount>();
            
            //
            // Create accounts in parallel
            Parallel.For(0, (int) experimentalConfiguration.NumberOfAttackerControlledAccounts, (index) =>
            {
                SimulatedAccount account = new SimulatedAccount()
                {
                    UniqueId = "attacker_" + index.ToString(),
                    Password = _simPasswords.GetPasswordFromWeightedDistribution(),
                };
                account.ClientAddresses.Add(_ipPool.GetRandomMaliciousIp());
                maliciousSimulatedAccountBag.Add(account);
            });
            AttackerAccounts = maliciousSimulatedAccountBag.ToList();
            _logger.WriteStatus("Finished creating {0:N0} attacker accounts",
                experimentalConfiguration.NumberOfAttackerControlledAccounts);
            
            //
            // Now create full UserAccount records for each simulated account and store them into the account context
            await TaskParalllel.ForEachWithWorkers(BenignAccounts.Union(AttackerAccounts),
                async (simAccount, index, cancelToken) =>
                {
                    if (index % 10000 == 0)
                        _logger.WriteStatus("Created account {0:N0}", index);
                    UserAccount account = UserAccount.Create(simAccount.UniqueId,
                        experimentalConfiguration.BlockingOptions.Conditions.Length,
                        experimentalConfiguration.BlockingOptions.AccountCreditLimit,
                        experimentalConfiguration.BlockingOptions.AccountCreditLimitHalfLife,
                        simAccount.Password,
                        "PBKDF2_SHA256",
                        experimentalConfiguration.BlockingOptions.ExpensiveHashingFunctionIterations);
                    foreach (string cookie in simAccount.Cookies)
                        account.HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount.Add(
                            LoginAttempt.HashCookie(cookie));
                    await accountContextFactory.Get().WriteNewAsync(account, cancelToken);
                },
                cancellationToken: cancellationToken);
            _logger.WriteStatus("Finished creating user accounts for each simluated account record");
        }
    }
}