using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing;
using StopGuessing.Clients;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace Simulator
{
    public partial class Simulator
    {
        public IDistributedResponsibilitySet<RemoteHost> MyResponsibleHosts;
        public UserAccountController MyUserAccountController;
        public LoginAttemptController MyLoginAttemptController;
        public UserAccountClient MyUserAccountClient;
        public LoginAttemptClient MyLoginAttemptClient;
        public LimitPerTimePeriod[] CreditLimits;
        public MemoryOnlyStableStore StableStore = new MemoryOnlyStableStore();
        public ExperimentalConfiguration MyExperimentalConfiguration;

        public Simulator(ExperimentalConfiguration myExperimentalConfiguration, BlockingAlgorithmOptions options = default(BlockingAlgorithmOptions))
        {
            MyExperimentalConfiguration = myExperimentalConfiguration;
            if (options == null)
                options = new BlockingAlgorithmOptions();
            CreditLimits = new[]
            {
                // 3 per hour
                new LimitPerTimePeriod(new TimeSpan(1, 0, 0), 3f),
                // 6 per day (24 hours, not calendar day)
                new LimitPerTimePeriod(new TimeSpan(1, 0, 0, 0), 6f),
                // 10 per week
                new LimitPerTimePeriod(new TimeSpan(6, 0, 0, 0), 10f),
                // 15 per month
                new LimitPerTimePeriod(new TimeSpan(30, 0, 0, 0), 15f)
            };
            //We are testing with local server now
            MyResponsibleHosts = new MaxWeightHashing<RemoteHost>("FIXME-uniquekeyfromconfig");
            //configuration.MyResponsibleHosts.Add("localhost", new RemoteHost { Uri = new Uri("http://localhost:80"), IsLocalHost = true });
            RemoteHost localHost = new RemoteHost { Uri = new Uri("http://localhost:80") };
            MyResponsibleHosts.Add("localhost", localHost);

            MyUserAccountClient = new UserAccountClient(MyResponsibleHosts, localHost);
            MyLoginAttemptClient = new LoginAttemptClient(MyResponsibleHosts, localHost);

            MemoryUsageLimiter memoryUsageLimiter = new MemoryUsageLimiter();

            MyUserAccountController = new UserAccountController(MyUserAccountClient,
                MyLoginAttemptClient, memoryUsageLimiter, options, StableStore,
                CreditLimits);
            MyLoginAttemptController = new LoginAttemptController(MyLoginAttemptClient, MyUserAccountClient,
                memoryUsageLimiter, options, StableStore);

            MyUserAccountController.SetLoginAttemptClient(MyLoginAttemptClient);
            MyUserAccountClient.SetLocalUserAccountController(MyUserAccountController);

            MyLoginAttemptController.SetUserAccountClient(MyUserAccountClient);
            MyLoginAttemptClient.SetLocalLoginAttemptController(MyLoginAttemptController);
            //fix outofmemory bug by setting the loginattempt field to null
            StableStore.LoginAttempts = null;
        }

        /// <summary>
        /// Evaluate the accuracy of our stopguessing service by sending user logins and malicious traffic
        /// </summary>
        /// <returns></returns>
        public async Task Run(CancellationToken cancellationToken = default(CancellationToken))
        {            
            //1.Create account from Rockyou 
            //Create 2*accountnumber accounts, first half is benign accounts, and second half is correct accounts owned by attackers
            GenerateSimulatedAccounts();

            int i = 0;
            //Record the accounts into stable store 
            try
            {
                List<SimulatedAccount> allAccounts = new List<SimulatedAccount>(BenignAccounts);
                allAccounts.AddRange(MaliciousAccounts);
                Parallel.ForEach(allAccounts, async simAccount =>
                {
                    await MyUserAccountController.PutAsync(
                        UserAccount.Create(simAccount.UniqueId, (int)CreditLimits.Last().Limit,
                            simAccount.Password, "PBKDF2_SHA256", 1), cancellationToken: cancellationToken);
                });
            }
            catch (Exception e)
            {
                using (StreamWriter file = new StreamWriter(@"account_error.txt"))
                {
                    file.WriteLine("{0} Exception caught in account creation.", e);
                    file.WriteLine("time is {0}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
                    file.WriteLine("How many requests? {0}", i);
                }
            }

            using (StreamWriter file = new StreamWriter(@"account.txt"))
            {
                file.WriteLine("time is {0}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
                file.WriteLine("How many requests? {0}", i);
            }


            Stopwatch sw = new Stopwatch();
            sw.Start();

            ulong falseNegatives = 0;
            ulong falsePositives = 0;
            ulong trueNegatives = 0;
            ulong truePositives = 0;

            for (ulong attemptCount = 0; attemptCount < MyExperimentalConfiguration.TotalLoginAttemptsToIssue; attemptCount++)
            {
                SimulatedLoginAttempt simAttempt;
                if (StrongRandomNumberGenerator.GetFraction() < MyExperimentalConfiguration.FractionOfLoginAttemptsFromAttacker)
                {
                    simAttempt = MaliciousLoginAttemptWeighted();
                }
                else
                {
                    simAttempt = BenignLoginAttempt();
                }

                LoginAttempt attemptWithOutcome = await
                    MyLoginAttemptController.LocalPutAsync(simAttempt.Attempt, simAttempt.Password, cancellationToken: cancellationToken);
                AuthenticationOutcome outcome = attemptWithOutcome.Outcome;
                
                if (simAttempt.IsGuess)
                {
                    if (outcome == AuthenticationOutcome.CredentialsValidButBlocked)
                        truePositives++;
                    else if (outcome == AuthenticationOutcome.CredentialsValid)
                        falseNegatives++;
                }
                if (!simAttempt.IsFromAttacker)
                {
                    if (outcome == AuthenticationOutcome.CredentialsValid)
                        trueNegatives++;
                    if (outcome == AuthenticationOutcome.CredentialsValidButBlocked)
                        falsePositives++;
                }
            }

            sw.Stop();

            double falsePositiveRate = ((double) falsePositives)/((double) falsePositives + truePositives);
            double falseNegativeRate = ((double) falseNegatives)/((double) falseNegatives + trueNegatives);

            using (System.IO.StreamWriter file =
            new System.IO.StreamWriter(@"result_log.txt"))
            {
                file.WriteLine("The false postive rate is {0:F20}", falsePositiveRate);
                file.WriteLine("The false negative rate is {0:F20}", falseNegativeRate);
                file.WriteLine("Time Elapsed={0}", sw.Elapsed);
            }
        }
    }
}
