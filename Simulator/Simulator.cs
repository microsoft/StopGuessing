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
        
        public LoginAttemptController MyLoginAttemptController;
        public IUserAccountContextFactory MyAccountContextFactory;     
        public ExperimentalConfiguration MyExperimentalConfiguration;

        private TextWriter _outputWriter;
        private DebugLogger _logger;
        private SimulatedPasswords _simPasswords;
        IpPool _ipPool;
        SimulatedAccounts _simAccounts;


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


        public async Task PrimeWithKnownPasswordsAsync(IEnumerable<string> knownPopularPasswords)
        {
            //WriteStatus("I'm not going to prime with common passwords so that we can test more quickly.  FIXME for real testing");
            await TaskParalllel.ForEachWithWorkers(knownPopularPasswords, async (password, itemNumer, cancelToken) =>
                await MyLoginAttemptController.PrimeCommonPasswordAsync(password, 100, cancelToken));
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
                TextWriter dataWriter = System.IO.TextWriter.Synchronized(new StreamWriter(path + "data.txt"));
                TextWriter errorWriter = System.IO.TextWriter.Synchronized(new StreamWriter(path + "error.txt"));
                DebugLogger logger = new DebugLogger(errorWriter);
                try
                {
                    SimulatedPasswords simPasswords = new SimulatedPasswords(logger, config);
                    Simulator simulator = new Simulator(logger, dataWriter, config, simPasswords);
                    await simulator.PrimeWithKnownPasswordsAsync(simPasswords.passwordsAlreadyKnownToBePopular);
                    await simulator.Run();
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

        public Simulator(DebugLogger logger, TextWriter outputWriter, ExperimentalConfiguration myExperimentalConfiguration, SimulatedPasswords simPasswords)
        {
            _outputWriter = outputWriter;
            _simPasswords = simPasswords;
            _logger = logger;

            _logger.WriteStatus("Entered Simulator constructor");
            MyExperimentalConfiguration = myExperimentalConfiguration;
            BlockingAlgorithmOptions options = MyExperimentalConfiguration.BlockingOptions;
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
            _logger.WriteStatus("Creating responsible hosts");
            //MyResponsibleHosts = new MaxWeightHashing<RemoteHost>("FIXME-uniquekeyfromconfig");
            //configuration.MyResponsibleHosts.Add("localhost", new RemoteHost { Uri = new Uri("http://localhost:80"), IsLocalHost = true });
            //RemoteHost localHost = new RemoteHost { Uri = new Uri("http://localhost:80") };
            //MyResponsibleHosts.Add("localhost", localHost);

            _logger.WriteStatus("Creating binomial ladder");
            BinomialLadderSketch localPasswordBinomialLadderSketch =
                new BinomialLadderSketch(1024 * 1024 * 1024, options.NumberOfRungsInBinomialLadder);
            MultiperiodFrequencyTracker<string> localPasswordFrequencyTracker =
                new MultiperiodFrequencyTracker<string>(
                    options.NumberOfPopularityMeasurementPeriods,
                    options.LengthOfShortestPopularityMeasurementPeriod,
                    options.FactorOfGrowthBetweenPopularityMeasurementPeriods);
            _logger.WriteStatus("Finished creating binomial ladder");

            //MyLoginAttemptClient = new LoginAttemptClient(MyResponsibleHosts, localHost);

            MyAccountContextFactory = new MemoryOnlyAccountContextFactory();

            MemoryUsageLimiter memoryUsageLimiter = new MemoryUsageLimiter();
            MyLoginAttemptController = new LoginAttemptController(
                MyAccountContextFactory, localPasswordBinomialLadderSketch, localPasswordFrequencyTracker,
                memoryUsageLimiter, myExperimentalConfiguration.BlockingOptions);

            _logger.WriteStatus("Exiting Simulator constructor");
        }


        /// <summary>
        /// Evaluate the accuracy of our stopguessing service by sending user logins and malicious traffic
        /// </summary>
        /// <returns></returns>
        public async Task Run(CancellationToken cancellationToken = default(CancellationToken))
        {
            _logger.WriteStatus("In Run");
            _ipPool = new IpPool(MyExperimentalConfiguration);
            _simAccounts = new SimulatedAccounts(_ipPool, _simPasswords, _logger);
            _simAccounts.Generate(MyExperimentalConfiguration);

            _logger.WriteStatus("Creating user accounts for each simluated account record");
            List<SimulatedAccount> allSimAccounts = new List<SimulatedAccount>(_simAccounts.BenignAccounts);
            allSimAccounts.AddRange(_simAccounts.MaliciousAccounts);
            ConcurrentBag<UserAccount> userAccounts = new ConcurrentBag<UserAccount>();
            await TaskParalllel.ForEachWithWorkers(allSimAccounts,
#pragma warning disable 1998
                async (simAccount, index, cancelToken) =>
#pragma warning restore 1998
                {
                    if (index % 10000 == 0)
                        _logger.WriteStatus("Created account {0:N0}", index);
                    UserAccount account = UserAccount.Create(simAccount.UniqueId,
                        MyExperimentalConfiguration.BlockingOptions.Conditions.Length,
                        MyExperimentalConfiguration.BlockingOptions.AccountCreditLimit,
                        MyExperimentalConfiguration.BlockingOptions.AccountCreditLimitHalfLife,
                        simAccount.Password,                 
                        "PBKDF2_SHA256",
                        MyExperimentalConfiguration.BlockingOptions.ExpensiveHashingFunctionIterations);
                    foreach (string cookie in simAccount.Cookies)
                        account.HashesOfDeviceCookiesThatHaveSuccessfullyLoggedIntoThisAccount.Add(
                            LoginAttempt.HashCookie(cookie));
                    userAccounts.Add(account);
                },
                cancellationToken: cancellationToken);
            _logger.WriteStatus("Finished creating user accounts for each simluated account record");


            _logger.WriteStatus("Performing a PUT on each account");
            await TaskParalllel.ForEachWithWorkers(userAccounts,
                async (account,index,cancelToken) =>
                {
                    if (index % 10000 == 0)
                        _logger.WriteStatus("PUT account {0:N0}", index);
                    await MyAccountContextFactory.Get().WriteNewAsync(account, cancellationToken: cancellationToken);
                }, cancellationToken: cancellationToken);
            _logger.WriteStatus("Done performing a PUT on each account");


            _outputWriter.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                "IsPasswordCorrect",
                "IsFromAttackAttacker",
                "IsAGuess",
                "IPInOposingPool",
                "IsClientAProxyIP",
                "TypeOfMistake",
                "UserID",
                "Password",
                string.Join(",", MyExperimentalConfiguration.BlockingOptions.Conditions.Select( cond => cond.Name )));


            Stopwatch sw = new Stopwatch();
            sw.Start();
            DateTime startTimeUtc = new DateTime(2016,01,01,0,0,0, DateTimeKind.Utc);
            TimeSpan testTimeSpan = MyExperimentalConfiguration.TestTimeSpan;
            double ticksBetweenLogins = ((double)testTimeSpan.Ticks)/(double)MyExperimentalConfiguration.TotalLoginAttemptsToIssue;
            
            await TaskParalllel.RepeatWithWorkers(MyExperimentalConfiguration.TotalLoginAttemptsToIssue, async (count, cancelToken) =>
            {
                if (count % 10000 == 0)
                    _logger.WriteStatus("Login Attempt {0:N0}", count);
                DateTime eventTimeUtc = startTimeUtc.AddTicks((long) (ticksBetweenLogins * count));
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
                simAttempt.Attempt.TimeOfAttemptUtc = eventTimeUtc;

                double[] scores = await
                    MyLoginAttemptController.DetermineLoginAttemptOutcomeAsync(simAttempt.Attempt, simAttempt.Password,
                        cancellationToken: cancellationToken);

                var ipInfo = _ipPool.GetIpAddressDebugInfo(simAttempt.Attempt.AddressOfClientInitiatingRequest);
                string outputString = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                    simAttempt.IsPasswordValid ? "Correct" : "Incorrect",
                    simAttempt.IsFromAttacker ? "FromAttacker" : "FromUser",
                    simAttempt.IsGuess ? "IsGuess" : "NotGuess",
                    simAttempt.IsFromAttacker ? (ipInfo.UserIdsOfBenignUsers != null && ipInfo.UserIdsOfBenignUsers.Count > 0 ? "IsInBenignPool" : "NotUsedByBenign") : 
                                                (ipInfo.IsInAttackersIpPool ? "IsInAttackersIpPool" : "NotUsedByAttacker"),
                    ipInfo.IsPartOfProxy ? "ProxyIP" : "NotAProxy",
                    string.IsNullOrEmpty(simAttempt.MistakeType) ? "-" : simAttempt.MistakeType,
                    simAttempt.Attempt.UsernameOrAccountId ?? "<null>",
                    simAttempt.Password,
                    string.Join(",", scores.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray()));

                await _outputWriter.WriteLineAsync(outputString);
                await _outputWriter.FlushAsync();
            },
            //(e) => {
            //},
            cancellationToken: cancellationToken);
            
        }
    }
}
