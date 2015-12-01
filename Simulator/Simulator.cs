using System;
using System.Collections.Concurrent;
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
        public LoginAttemptController MyLoginAttemptController;
        //public LoginAttemptClient MyLoginAttemptClient;
        public IUserAccountContextFactory MyAccountContextFactory;
        //public LimitPerTimePeriod[] CreditLimits;
        //public MemoryOnlyStableStore StableStore = new MemoryOnlyStableStore();        
        public ExperimentalConfiguration MyExperimentalConfiguration;

        public delegate void ExperimentalConfigurationFunction(ExperimentalConfiguration config);
        public delegate void StatisticsWritingFunction(ResultStatistics resultStatistics);
        public delegate void ParameterSettingFunction<in T>(ExperimentalConfiguration config, T iterationParameter);

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

        private StreamWriter _errorWriter;
        private DateTime whenStarted = DateTime.UtcNow;
        private long lastMemory = 0;
        private DateTime lastEventTime;
        public void WriteStatus(string status, params Object[] args)
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan eventTime = now - whenStarted;
            TimeSpan sinceLastEvent = now - lastEventTime;
            long eventMemory = GC.GetTotalMemory(false);
            long memDiff = eventMemory - lastMemory;
            long eventMemoryMB = eventMemory/(1024L*1024L);
            long memDiffMB = memDiff/(1024L*1024L);
            lastMemory = eventMemory;
            lastEventTime = now;
            Console.Out.WriteLine("Time: {0:00}:{1:00}:{2:00}.{3:000} seconds ({4:0.000}),  Memory: {5}MB (increased by {6}MB)",
                eventTime.Hours,
                eventTime.Minutes,
                eventTime.Seconds,
                eventTime.Milliseconds,
                sinceLastEvent.TotalSeconds,
                eventMemoryMB, memDiffMB);
            Console.Out.WriteLine(status, args);
            _errorWriter.WriteLine("Time: {0:00}:{1:00}:{2:00}.{3:000} seconds ({4:0.000}),  Memory: {5}MB (increased by {6}MB)",
                eventTime.Hours,
                eventTime.Minutes,
                eventTime.Seconds,
                eventTime.Milliseconds,
                sinceLastEvent.Milliseconds,
                eventMemoryMB, memDiffMB);
            _errorWriter.WriteLine(status, args);
            _errorWriter.Flush();
        }

        public static async Task RunExperimentalSweep(
            ExperimentalConfigurationFunction configurationDelegate,
            IParameterSweeper[] parameterSweeps = null,
            int startingTest = 0)
        {
            int totalTests = parameterSweeps == null ? 1 :
                // Get the legnths of each dimension of the multi-dimensional parameter sweep
                parameterSweeps.Select(ps => ps.GetParameterCount())
                    // Calculates the product of the number of parameters in each dimension
                    .Aggregate((runningProduct, nextFactor) => runningProduct*nextFactor);
             
            ExperimentalConfiguration baseConfig = new ExperimentalConfiguration();
            configurationDelegate(baseConfig);
            WeightedSelector <string> passwordSelector = Simulator.GetPasswordSelector(baseConfig.PasswordFrequencyFile);
            List<string> passwordsAlreadyKnownToBePopular = Simulator.GetKnownPopularPasswords(baseConfig.PreviouslyKnownPopularPasswordFile);

            DateTime now = DateTime.Now;
            string dirName = @"..\..\Experiment_" + now.Month + "_" + now.Day + "_" + now.Hour + "_" + now.Minute;
            if (parameterSweeps != null)
                Directory.CreateDirectory(dirName);
            for (int testIndex = startingTest; testIndex < totalTests; testIndex++)
            {
                // Start with the default configuration from the provided configuration factory
                ExperimentalConfiguration config = new ExperimentalConfiguration();
                configurationDelegate(config);

                // Next set the parameters for this test in the swwep
                string path = dirName +  (parameterSweeps == null ? "" : ("\\Expermient" + testIndex.ToString()) );
                Directory.CreateDirectory(path);
                path += @"\";
                int parameterIndexer = testIndex;
                if (parameterSweeps != null)
                {
                    for (int dimension = parameterSweeps.Length - 1; dimension >= 0; dimension--)
                    {
                        IParameterSweeper sweep = parameterSweeps[dimension];
                        int parameterIndex = parameterIndexer%sweep.GetParameterCount();
                        parameterIndexer /= sweep.GetParameterCount();
                        sweep.SetParameter(config, parameterIndex);
                        path += "_" + sweep.GetParameterString(parameterIndex).Replace(".", "_");
                    }
                }

                // Now that all of the parameters of the sweep have been set, run the simulation
                StreamWriter dataWriter = new StreamWriter(path + "data.txt");
                StreamWriter errorWriter = new StreamWriter(path + "error.txt");
                try
                {
                    Simulator simulator = new Simulator(errorWriter,config, passwordSelector);
                    await simulator.PrimeWithKnownPasswordsAsync(passwordsAlreadyKnownToBePopular);
                    await simulator.Run(dataWriter);
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

        public Simulator(StreamWriter errorWriter, ExperimentalConfiguration myExperimentalConfiguration, WeightedSelector<string> passwordSelector)
        {
            _errorWriter = errorWriter;
            WriteStatus("Entered Simulator constructor");
            MyExperimentalConfiguration = myExperimentalConfiguration;
            BlockingAlgorithmOptions options = MyExperimentalConfiguration.BlockingOptions;
            PasswordSelector = passwordSelector;
            //CreditLimits = new[]
            //{
            //    // 3 per hour
            //    new LimitPerTimePeriod(new TimeSpan(1, 0, 0), 3f),
            //    // 6 per day (24 hours, not calendar day)
            //    new LimitPerTimePeriod(new TimeSpan(1, 0, 0, 0), 6f),
            //    // 10 per week
            //    new LimitPerTimePeriod(new TimeSpan(6, 0, 0, 0), 10f),
            //    // 15 per month
            //    new LimitPerTimePeriod(new TimeSpan(30, 0, 0, 0), 15f)
            //};
            //We are testing with local server now
            WriteStatus("Creating responsible hosts");
            MyResponsibleHosts = new MaxWeightHashing<RemoteHost>("FIXME-uniquekeyfromconfig");
            //configuration.MyResponsibleHosts.Add("localhost", new RemoteHost { Uri = new Uri("http://localhost:80"), IsLocalHost = true });
            RemoteHost localHost = new RemoteHost { Uri = new Uri("http://localhost:80") };
            MyResponsibleHosts.Add("localhost", localHost);

            WriteStatus("Creating binomial ladder");
            BinomialLadderSketch localPasswordBinomialLadderSketch =
                new BinomialLadderSketch(1024 * 1024 * 1024, options.NumberOfRungsInBinomialLadder);
            MultiperiodFrequencyTracker<string> localPasswordFrequencyTracker =
                new MultiperiodFrequencyTracker<string>(
                    options.NumberOfPopularityMeasurementPeriods,
                    options.LengthOfShortestPopularityMeasurementPeriod,
                    options.FactorOfGrowthBetweenPopularityMeasurementPeriods);
            WriteStatus("Finished creating binomial ladder");

            //MyLoginAttemptClient = new LoginAttemptClient(MyResponsibleHosts, localHost);

            MyAccountContextFactory = new MemoryOnlyAccountContextFactory();

            MemoryUsageLimiter memoryUsageLimiter = new MemoryUsageLimiter();
            MyLoginAttemptController = new LoginAttemptController(//MyLoginAttemptClient,
                MyAccountContextFactory, localPasswordBinomialLadderSketch, localPasswordFrequencyTracker,
                memoryUsageLimiter, myExperimentalConfiguration.BlockingOptions);

            WriteStatus("Creating login attempt controller");
            //MyLoginAttemptClient.SetLocalLoginAttemptController(MyLoginAttemptController);
            WriteStatus("Finished creating login attempt controller");
            WriteStatus("Exiting Simulator constructor");
        }


        /// <summary>
        /// Evaluate the accuracy of our stopguessing service by sending user logins and malicious traffic
        /// </summary>
        /// <returns></returns>
        public async Task Run(StreamWriter outcomeWriter,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            WriteStatus("In Run");
            //1.Create account from Rockyou 
            //Create 2*accountnumber accounts, first half is benign accounts, and second half is correct accounts owned by attackers

            //Record the accounts into stable store 

            GenerateSimulatedAccounts();

            WriteStatus("Creating user accounts for each simluated account record");
            List<SimulatedAccount> allSimAccounts = new List<SimulatedAccount>(BenignAccounts);
            allSimAccounts.AddRange(MaliciousAccounts);
            ConcurrentBag<UserAccount> userAccounts = new ConcurrentBag<UserAccount>();
            await TaskParalllel.ForEach(allSimAccounts,
                simAccount =>
                {
                    UserAccount account = UserAccount.Create(simAccount.UniqueId,
                        MyExperimentalConfiguration.BlockingOptions.Conditions.Length,
                        MyExperimentalConfiguration.BlockingOptions.AccountCreditLimit,
                        MyExperimentalConfiguration.BlockingOptions.AccountCreditLimitHalfLife,
                        simAccount.Password, "PBKDF2_SHA256", 1);
                    foreach (string cookie in simAccount.Cookies)
                        account.HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount.Add(
                            LoginAttempt.HashCookie(cookie));
                    userAccounts.Add(account);
                }, cancellationToken: cancellationToken);
            WriteStatus("Finished creating user accounts for each simluated account record");


            WriteStatus("Performing a PUT on each account");
            await TaskParalllel.ForEach(userAccounts,
                async account =>
                {
                    await MyAccountContextFactory.Get().WriteNewAsync(account, cancellationToken: cancellationToken);
                }, cancellationToken: cancellationToken);
            WriteStatus("Done performing a PUT on each account");


            outcomeWriter.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                "IsPasswordCorrect",
                "IsFromAttackAttacker",
                "IsAGuess",
                "IsIpInAttackersPool",
                "IsClientAProxyIP",
                "TypeOfMistake",
                "UserID",
                "Password",
                "Scores");


            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            await TaskParalllel.ParallelRepeat(MyExperimentalConfiguration.TotalLoginAttemptsToIssue, async (count) =>
            {
                if (count % 10000 == 0)
                    WriteStatus("Login Attempt {0:N0}", count);
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

                double[] scores = await
                    MyLoginAttemptController.DetermineLoginAttemptOutcomeAsync(simAttempt.Attempt, simAttempt.Password,
                        cancellationToken: cancellationToken);
                //LoginAttempt attemptWithOutcome = dlaoResult.Item1;
                //BlockingScoresForEachAlgorithm blockingScoresForEachAlgorithm = dlaoResult.Item2;
                //AuthenticationOutcome outcome = attemptWithOutcome.Outcome;

                lock (outcomeWriter)
                {
                    var ipInfo = GetIpAddressDebugInfo(simAttempt.Attempt.AddressOfClientInitiatingRequest);
                    outcomeWriter.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                        simAttempt.IsPasswordValid ? "Correct" : "Incorrect",
                        simAttempt.IsFromAttacker ? "FromAttacker" : "FromUser",
                        simAttempt.IsGuess ? "IsGuess" : "NotGuess",
                        ipInfo.IsInAttackersIpPool ? "InAttackersIpPool" : "NotUsedByAttacker",
                        ipInfo.IsPartOfProxy ? "ProxyIP" : "NotAProxy",
                        string.IsNullOrEmpty(simAttempt.MistakeType) ? "-" : simAttempt.MistakeType,
                        simAttempt.Attempt.UsernameOrAccountId ?? "<null>",
                        simAttempt.Password,
                        string.Join(",", scores.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray())
                        );
                    outcomeWriter.Flush();
                }                
            },
            (e) => {
            });
            
        }
    }
}
