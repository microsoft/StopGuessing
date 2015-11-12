using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        public class ResultStatistics
        {
            public ulong FalseNegatives = 0;
            public ulong FalsePositives = 0;
            public ulong TrueNegatives = 0;
            public ulong TruePositives = 0;
            public ulong GuessWasWrong = 0;
            public ulong BenignErrors = 0;
            public ulong TotalLoopIterations = 0;
            public ulong TotalExceptions = 0;
        }

        public IDistributedResponsibilitySet<RemoteHost> MyResponsibleHosts;
        public UserAccountController MyUserAccountController;
        public LoginAttemptController MyLoginAttemptController;
        public UserAccountClient MyUserAccountClient;
        public LoginAttemptClient MyLoginAttemptClient;
        public LimitPerTimePeriod[] CreditLimits;
        public MemoryOnlyStableStore StableStore = new MemoryOnlyStableStore();
        public ExperimentalConfiguration MyExperimentalConfiguration;

        public delegate void ExperimentalConfigurationFunction(ExperimentalConfiguration config);
        public delegate void StatisticsWritingFunction(ResultStatistics resultStatistics);
        public delegate void ParameterSettingFunction<in T>(ExperimentalConfiguration config, T iterationParameter);



        public enum SystemMode
        {
            // ReSharper disable once InconsistentNaming
            SSH,
            Basic,
            StopGuessing
        };

        public static void SetSystemMode(ExperimentalConfiguration config, SystemMode mode)
        {
            if (mode == SystemMode.Basic || mode == SystemMode.SSH)
            {
                //
                // Industrial-best-practice baseline
                //
                // Use the same threshold regardless of the popularity of the account password
                config.BlockingOptions.BlockThresholdMultiplierForUnpopularPasswords = 1d;
                // Make all failures increase the count towards the threshold by one
                config.BlockingOptions.PenaltyMulitiplierForTypo = 1d;
                config.BlockingOptions.PenaltyForInvalidAccount = config.BlockingOptions.BasePenaltyForInvalidPassword;
                // If the below is empty, the multiplier for any popularity level will be 1.
                config.BlockingOptions.PenaltyForReachingEachPopularityThreshold = new List<PenaltyForReachingAPopularityThreshold>();
                // Correct passwords shouldn't help
                config.BlockingOptions.RewardForCorrectPasswordPerAccount = 0;
            }
            if (mode == SystemMode.SSH)
            {
                // SSH mode doesn't discard the repeat <password/account> pairs
                config.BlockingOptions.FOR_SIMULATION_ONLY_TURN_ON_SSH_STUPID_MODE = true;
            }
        }


        public interface IParameterSweeper
        {
            int GetParameterCount();
            void SetParameter(ExperimentalConfiguration config, int parameterIndex);
            string GetParameterString(int parameterIndex);
        }

        public class ParameterSweeper<T> : IParameterSweeper
        {
            public string Name;
            public T[] Parameters;
            public ParameterSettingFunction<T> ParameterSetter;

            public int GetParameterCount()
            {
                return Parameters.Length;
            }

            public void SetParameter(ExperimentalConfiguration config, int parameterIndex)
            {
                ParameterSetter(config, Parameters[parameterIndex]);
            }

            public string GetParameterString(int parameterIndex)
            {
                return Parameters[parameterIndex].ToString();
            }
        }

        private static string Fraction(ulong numerator, ulong denominmator)
        {
            if (denominmator == 0)
                return "NaN";
            else
                return (((double)numerator)/(double)denominmator).ToString(CultureInfo.InvariantCulture);
        }

        public static async Task RunExperimentalSweep(
            ExperimentalConfigurationFunction configurationDelegate,
            IParameterSweeper[] parameterSweeps,
            int startingTest = 0)
        {
            int totalTests =
                // Get the legnths of each dimension of the multi-dimensional parameter sweep
                parameterSweeps.Select(ps => ps.GetParameterCount())
                    // Calculates the product of the number of parameters in each dimension
                    .Aggregate((runningProduct, nextFactor) => runningProduct*nextFactor);
             
            ExperimentalConfiguration baseConfig = new ExperimentalConfiguration();
            configurationDelegate(baseConfig);
            WeightedSelector <string> passwordSelector = Simulator.GetPasswordSelector(baseConfig.PasswordFrequencyFile);
            List<string> passwordsAlreadyKnownToBePopular = Simulator.GetKnownPopularPasswords(baseConfig.PreviouslyKnownPopularPasswordFile);

            DateTime now = DateTime.Now;
            string dirName = @"..\Experiment_" + now.Month + "_" + now.Day + "_" + now.Hour + "_" + now.Minute;
            Directory.CreateDirectory(dirName);
            StreamWriter statsWriter = new StreamWriter(dirName + "\\" + "ResultStatistics.csv");
                statsWriter.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}", new string(',', parameterSweeps.Length),
                "FalsePositives", "TruePositives", "FalsePositiveRate", "TruePositiveRate",
                "FalseNegatives", "TrueNegatives", "FalseNegativeRate", "TrueNegativeRate",
                "Precision",
                "Recall",
                "BenignErrors",
                "GuessWasWrong",
                "TotalExceptions",
                "TotalLoopIterations");
            for (int testIndex = startingTest; testIndex < totalTests; testIndex++)
            {
                string statisticsCsvLine = testIndex.ToString();

                // Start with the default configuration from the provided configuration factory
                ExperimentalConfiguration config = new ExperimentalConfiguration();
                configurationDelegate(config);

                // Next set the parameters for this test in the swwep
                string path = dirName + "\\Exp" + testIndex.ToString();
                int parameterIndexer = testIndex;
                for (int dimension = parameterSweeps.Length - 1; dimension >= 0; dimension--)
                {
                    IParameterSweeper sweep = parameterSweeps[dimension];
                    int parameterIndex = parameterIndexer%sweep.GetParameterCount();
                    parameterIndexer /= sweep.GetParameterCount();
                    sweep.SetParameter(config, parameterIndex);
                    path += "_" + sweep.GetParameterString(parameterIndex).Replace(".", "_");
                    statisticsCsvLine += "," +
                                         sweep.GetParameterString(parameterIndex).Replace(",", "_");
                }

                // Now that all of the parameters of the sweep have been set, run the simulation
                StreamWriter errorWriter = new StreamWriter(path + ".txt");
                try
                {
                    Simulator simulator = new Simulator(config, passwordSelector);
                    simulator.PrimeWithKnownPasswords(passwordsAlreadyKnownToBePopular);
                    ResultStatistics stats = await simulator.Run(errorWriter);
                    statsWriter.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12}", statisticsCsvLine,
                        stats.FalsePositives, stats.TruePositives,
                        Fraction(stats.FalsePositives, stats.FalsePositives + stats.TruePositives),
                        Fraction(stats.TruePositives, stats.FalsePositives + stats.TruePositives),
                        stats.FalseNegatives, stats.TrueNegatives,
                        Fraction(stats.FalseNegatives, stats.FalseNegatives + stats.TrueNegatives),
                        Fraction(stats.TrueNegatives, stats.FalseNegatives + stats.TrueNegatives),
                        // Precision
                        Fraction(stats.TruePositives, stats.TruePositives + stats.FalsePositives),
                        // Recall
                        Fraction(stats.TruePositives, stats.TruePositives + stats.FalseNegatives),
                        stats.BenignErrors,
                        stats.GuessWasWrong,
                        stats.TotalExceptions,
                        stats.TotalLoopIterations
                        );
                    statsWriter.Flush();
                }
                catch (Exception e)
                {
                    while (e != null)
                    {
                        errorWriter.WriteLine(e.Message);
                        errorWriter.WriteLine(e.StackTrace);
                        errorWriter.WriteLine(e);
                        e = e.InnerException;
                    }
                    errorWriter.Flush();
                }
            }
        }


        public Simulator(ExperimentalConfiguration myExperimentalConfiguration, WeightedSelector<string> passwordSelector)
        {
            MyExperimentalConfiguration = myExperimentalConfiguration;
            PasswordSelector = passwordSelector;
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
                MyLoginAttemptClient, memoryUsageLimiter, myExperimentalConfiguration.BlockingOptions, StableStore,
                CreditLimits);
            MyLoginAttemptController = new LoginAttemptController(MyLoginAttemptClient, MyUserAccountClient,
                memoryUsageLimiter, myExperimentalConfiguration.BlockingOptions, StableStore);

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
        public async Task<ResultStatistics> Run(StreamWriter errorWriter, CancellationToken cancellationToken = default(CancellationToken))
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
                    UserAccount account = UserAccount.Create(simAccount.UniqueId, (int)CreditLimits.Last().Limit,
                            simAccount.Password, "PBKDF2_SHA256", 1);
                    foreach (string cookie in simAccount.Cookies)
                        account.HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount.Add(LoginAttempt.HashCookie(cookie));
                    await MyUserAccountController.PutAsync(account, cancellationToken: cancellationToken);
                });
            }
            catch (Exception e)
            {
                using (StreamWriter file = new StreamWriter(@"account_error.txt"))
                {
                    file.WriteLine("{0} Exception caught in account creation.", e);
                    file.WriteLine("time is {0}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
                }
            }


            Stopwatch sw = new Stopwatch();
            sw.Start();

            ResultStatistics resultStatistics = new ResultStatistics();

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

                    lock (resultStatistics)
                    {
                    resultStatistics.TotalLoopIterations++;
                        if (simAttempt.IsGuess)
                        {
                            if (outcome == AuthenticationOutcome.CredentialsValidButBlocked)
                                resultStatistics.TruePositives++;
                            else if (outcome == AuthenticationOutcome.CredentialsValid)
                            {
                                resultStatistics.FalseNegatives++;
                                var addressDebugInfo =
                                    GetIpAddressDebugInfo(simAttempt.Attempt.AddressOfClientInitiatingRequest);
                                string jsonOfIp;
                                lock (addressDebugInfo)
                                {
                                    jsonOfIp =
                                        JsonConvert.SerializeObject(addressDebugInfo);
                                }
                                errorWriter.WriteLine("False Negative\r\n{0}\r\n{1}\r\n{2}\r\n\r\n",
                                    simAttempt.Password,
                                    jsonOfIp,
                                    JsonConvert.SerializeObject(attemptWithOutcome));
                                errorWriter.Flush();
                            }
                            else
                                resultStatistics.GuessWasWrong++;
                        }
                        else
                        {
                            if (outcome == AuthenticationOutcome.CredentialsValid)
                                resultStatistics.TrueNegatives++;
                            else if (outcome == AuthenticationOutcome.CredentialsValidButBlocked)
                            {
                                var addressDebugInfo =
                                    GetIpAddressDebugInfo(simAttempt.Attempt.AddressOfClientInitiatingRequest);
                                string jsonOfIp;
                                lock (addressDebugInfo)
                                {
                                    jsonOfIp =
                                        JsonConvert.SerializeObject(addressDebugInfo);
                                }
                                string jsonOfattempt =
                                    JsonConvert.SerializeObject(attemptWithOutcome);
                                resultStatistics.FalsePositives++;
                                errorWriter.WriteLine("False Positive\r\n{0}\r\n{1}\r\n{2}\r\n\r\n",
                                    simAttempt.Password,
                                    jsonOfIp, jsonOfattempt);
                                errorWriter.Flush();
                        }
                            else
                                resultStatistics.BenignErrors++;
                        }
                    }
            },
            (e) => { 
                    lock (resultStatistics)
                    {
                        resultStatistics.TotalExceptions++;
                    }
                    Console.Error.WriteLine(e.ToString());
            });

            return resultStatistics;
        }
    }
}
