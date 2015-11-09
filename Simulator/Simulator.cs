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
        public class Stats
        {
            public ulong FalseNegatives = 0;
            public ulong FalsePositives = 0;
            public ulong TrueNegatives = 0;
            public ulong TruePositives = 0;
            public ulong GuessWasWrong = 0;
            public ulong BenignErrors = 0;
            public ulong TotalLoopIterations = 0;
            public ulong TotalExceptions = 0;
            public ulong TotalLoopIterationsThatShouldHaveRecordedStats = 0;
        }

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
        public async Task Run(BlockingAlgorithmOptions options,CancellationToken cancellationToken = default(CancellationToken))
        {            
            //1.Create account from Rockyou 
            //Create 2*accountnumber accounts, first half is benign accounts, and second half is correct accounts owned by attackers

            //Record the accounts into stable store 
            try
            {
                GenerateSimulatedAccounts();

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
//                    file.WriteLine("How many requests? {0}", i);
                }
            }

            //using (StreamWriter file = new StreamWriter(@"account.txt"))
            //{
            //    file.WriteLine("time is {0}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
            //    file.WriteLine("How many requests? {0}", i);
            //}


            Stopwatch sw = new Stopwatch();
            sw.Start();

            Stats stats = new Stats();

            ulong count = 0;
            double falsePositiveRate = 0;
            double falseNegativeRate = 0;
            //The percentage of malicious attempts get caught (over all malicious attempts)
            double detectionRate = 0;
            //The percentage of benign attempts get labeled as malicious (over all benign attempts)
            double falseDetectionRate = 0;
            ulong bootstrapall = 0;
            ulong bootstrapsuccess = 0;
            //            List<int> Runtime = new List<int>(new int[MyExperimentalConfiguration.TotalLoginAttemptsToIssue]);

            for (int bigi = 0; bigi <(int) (MyExperimentalConfiguration.TotalLoginAttemptsToIssue/MyExperimentalConfiguration.RecordUnitAttempts); bigi++)
            {



                await TaskParalllel.ParallelRepeat(MyExperimentalConfiguration.TotalLoginAttemptsToIssue, async () =>
            {
                SimulatedLoginAttempt simAttempt;
                if (StrongRandomNumberGenerator.GetFraction() <
                    MyExperimentalConfiguration.FractionOfLoginAttemptsFromAttacker)
                {
                    simAttempt = MaliciousLoginAttemptBreadthFirst();
                }
                else
                {
                    simAttempt = BenignLoginAttempt();
                }

                LoginAttempt attemptWithOutcome = await
                    MyLoginAttemptController.LocalPutAsync(simAttempt.Attempt, simAttempt.Password);//,
                                                                                                    // cancellationToken: cancellationToken);
                AuthenticationOutcome outcome = attemptWithOutcome.Outcome;

                lock (stats)
                {
                    stats.TotalLoopIterationsThatShouldHaveRecordedStats++;
                    if (simAttempt.IsGuess)
                    {
                        if (outcome == AuthenticationOutcome.CredentialsValidButBlocked)
                            stats.TruePositives++;
                        else if (outcome == AuthenticationOutcome.CredentialsValid)
                            stats.FalseNegatives++;
                        else
                            stats.GuessWasWrong++;
                    }
                    else
                    {
                        if (outcome == AuthenticationOutcome.CredentialsValid)
                            stats.TrueNegatives++;
                        else if (outcome == AuthenticationOutcome.CredentialsValidButBlocked)
                            stats.FalsePositives++;
                        else
                            stats.BenignErrors++;
                    }
                }
            },
            (e) =>
            {
                lock (stats)
                {
                    stats.TotalExceptions++;
                }
                Console.Error.WriteLine(e.ToString());
                count++;
            });

                falsePositiveRate = ((double)stats.FalsePositives) / ((double)stats.FalsePositives + stats.TruePositives);
                falseNegativeRate = ((double)stats.FalseNegatives) / ((double)stats.FalseNegatives + stats.TrueNegatives);
                detectionRate = ((double)stats.TruePositives) / ((double)stats.TruePositives + stats.FalseNegatives);
                falseDetectionRate = ((double)stats.FalsePositives) / ((double)stats.FalsePositives + stats.TrueNegatives);
                using (StringWriter filename = new StringWriter())
                {
                    filename.Write("Detailed_Log_Unpopular{0}.txt", options.BlockThresholdUnpopularPassword);

                    //string filename = "Detailed_Log_Unpopular{0}.txt", options.BlockThresholdUnpopularPassword;
                    using (StreamWriter detailed = File.AppendText(@filename.ToString()))
                    {
                        detailed.WriteLine("The false postive rate is {0}/({0}+{1}) ({2:F20}%)", stats.FalsePositives, stats.TruePositives, falsePositiveRate * 100d);
                        detailed.WriteLine("The false negative rate is {0}/({0}+{1}) ({2:F20}%)", stats.FalseNegatives, stats.TrueNegatives, falseNegativeRate * 100d);
                        detailed.WriteLine("The detection rate is {0}/({0}+{1}) ({2:F20}%)", stats.TruePositives, stats.FalseNegatives, detectionRate * 100d);
                        detailed.WriteLine("The false detection rate is {0}/({0}+{1}) ({2:F20}%)", stats.FalsePositives, stats.TrueNegatives, falseDetectionRate * 100d);
                    }
                }

            }



            sw.Stop();

            Console.WriteLine("Time Elapsed={0}", sw.Elapsed);
            Console.WriteLine("the new count is {0}", count);

            falsePositiveRate = ((double) stats.FalsePositives)/((double)stats.FalsePositives + stats.TruePositives);
            falseNegativeRate = ((double)stats.FalseNegatives)/((double)stats.FalseNegatives + stats.TrueNegatives);

            using (System.IO.StreamWriter file =
            new System.IO.StreamWriter(@"result_log.txt"))
            {
                file.WriteLine("The false postive rate is {0}/({0}+{1}) ({2:F20}%)", stats.FalsePositives, stats.TruePositives, falsePositiveRate * 100d);
                file.WriteLine("The false negative rate is {0}/({0}+{1}) ({2:F20}%)", stats.FalseNegatives, stats.TrueNegatives, falseNegativeRate * 100d);
                file.WriteLine("Time Elapsed={0}", sw.Elapsed);
            }
        }
    }
}
