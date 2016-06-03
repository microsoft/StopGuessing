using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StopGuessing;
using StopGuessing.AccountStorage.Memory;
using StopGuessing.Clients;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace Simulator
{
    public partial class Simulator
    {
        //private readonly LoginAttemptController _loginAttemptController;
        //private readonly IUserAccountContextFactory _accountContextFactory;
        public BinomialLadderFilter _binomialLadderFilter;
        public AgingMembershipSketch _recentIncorrectPasswords;
        public SelfLoadingCache<IPAddress, SimIpHistory> _ipHistoryCache;
        public readonly ExperimentalConfiguration _experimentalConfiguration;
        public readonly MemoryUsageLimiter _memoryUsageLimiter;
        public readonly MemoryUserAccountController _userAccountController;

        private readonly TextWriter _AttackAttemptsWithValidPasswords;
        private readonly TextWriter _LegitiamteAttemptsWithValidPasswords;
        private readonly TextWriter _OtherAttempts;
        private readonly DebugLogger _logger;
        private readonly SimulatedPasswords _simPasswords;
        private IpPool _ipPool;
        private SimulatedAccounts _simAccounts;
        private SimulatedLoginAttemptGenerator _attemptGenerator;

        protected readonly DateTime StartTimeUtc = new DateTime(2016, 01, 01, 0, 0, 0, DateTimeKind.Utc);

        public delegate void ExperimentalConfigurationFunction(ExperimentalConfiguration config);

        private static string Fraction(ulong numerator, ulong denominmator)
        {
            if (denominmator == 0)
                return "NaN";
            else
                return (((double)numerator)/(double)denominmator).ToString(CultureInfo.InvariantCulture);
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
            string dirName = baseConfig.OutputPath + baseConfig.OutputDirectoryName + "_Run_" + now.Month + "_" + now.Day + "_" + now.Hour + "_" + now.Minute;
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
                //TextWriter dataWriter = System.IO.TextWriter.Synchronized(new StreamWriter(path + "data.txt"));
                TextWriter errorWriter = System.IO.TextWriter.Synchronized(new StreamWriter(path + "error.txt"));
                DebugLogger logger = new DebugLogger(errorWriter);
                try
                {
                    SimulatedPasswords simPasswords = new SimulatedPasswords(logger, config);
                    Simulator simulator = new Simulator(logger, path, config, simPasswords);
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

        public void ReduceMemoryUsage(object sender, MemoryUsageLimiter.ReduceMemoryUsageEventParameters parameters)
        {
            _ipHistoryCache.RecoverSpace(parameters.FractionOfMemoryToTryToRemove);
        }

        public Simulator(DebugLogger logger, string path, ExperimentalConfiguration myExperimentalConfiguration, SimulatedPasswords simPasswords)
        {
            
            _simPasswords = simPasswords;
            _logger = logger;
            _AttackAttemptsWithValidPasswords = System.IO.TextWriter.Synchronized(new StreamWriter(path + "AttackAttemptsWithValidPasswords.txt"));
            _LegitiamteAttemptsWithValidPasswords = System.IO.TextWriter.Synchronized(new StreamWriter(path + "LegitiamteAttemptsWithValidPasswords.txt"));
            _OtherAttempts = System.IO.TextWriter.Synchronized(new StreamWriter(path + "OtherAttempts.txt"));
            _logger.WriteStatus("Entered Simulator constructor");
            _experimentalConfiguration = myExperimentalConfiguration;
            BlockingAlgorithmOptions options = _experimentalConfiguration.BlockingOptions;
            
            _logger.WriteStatus("Creating binomial ladder");
            _binomialLadderFilter =
                new BinomialLadderFilter(options.NumberOfBitsInBinomialLadderFilter_N, options.HeightOfBinomialLadder_H);
            _ipHistoryCache = new SelfLoadingCache<IPAddress, SimIpHistory>(address => new SimIpHistory(options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos));
            _userAccountController = new MemoryUserAccountController();

            _memoryUsageLimiter = new MemoryUsageLimiter();
            _memoryUsageLimiter.OnReduceMemoryUsageEventHandler += ReduceMemoryUsage;

            _recentIncorrectPasswords = new AgingMembershipSketch(16, 128 * 1024);

            _logger.WriteStatus("Exiting Simulator constructor");
        }


        /// <summary>
        /// Evaluate the accuracy of our stopguessing service by sending user logins and malicious traffic
        /// </summary>
        /// <returns></returns>
        public async Task Run(CancellationToken cancellationToken = default(CancellationToken))
        {
            _logger.WriteStatus("In RunInBackground");

            _logger.WriteStatus("Priming password-tracking with known common passwords");
            _simPasswords.PrimeWithKnownPasswordsAsync(_binomialLadderFilter, 40);
            _logger.WriteStatus("Finished priming password-tracking with known common passwords");

            _logger.WriteStatus("Creating IP Pool");
            _ipPool = new IpPool(_experimentalConfiguration);
            _logger.WriteStatus("Generating simualted account records");
            _simAccounts = new SimulatedAccounts(_ipPool, _simPasswords, _logger);
            await _simAccounts.GenerateAsync(_experimentalConfiguration, cancellationToken);

            _logger.WriteStatus("Creating login-attempt generator");
            _attemptGenerator = new SimulatedLoginAttemptGenerator(_experimentalConfiguration, _simAccounts, _ipPool, _simPasswords);
            _logger.WriteStatus("Finiished creating login-attempt generator");


            foreach (TextWriter writer in new TextWriter[] { _AttackAttemptsWithValidPasswords, _LegitiamteAttemptsWithValidPasswords, _OtherAttempts })
                writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", //,{9}
                "Password",
                "UserID",
                "IP",
                "IsFrequentlyGuessedPw",
                "IsPasswordCorrect",
                "IsFromAttackAttacker",
                "IsAGuess",
                "IPInOposingPool",
                "IsClientAProxyIP",
                "TypeOfMistake"
                //string.Join(",")
                );

            TimeSpan testTimeSpan = _experimentalConfiguration.TestTimeSpan;
            double ticksBetweenLogins = ((double)testTimeSpan.Ticks)/(double)_experimentalConfiguration.TotalLoginAttemptsToIssue;
            
            await TaskParalllel.RepeatWithWorkers(_experimentalConfiguration.TotalLoginAttemptsToIssue, async (count, cancelToken) =>
            {
                if (count % 10000 == 0)
                    _logger.WriteStatus("Login Attempt {0:N0}", count);
                DateTime eventTimeUtc = StartTimeUtc.AddTicks((long) (ticksBetweenLogins * count));
                SimulatedLoginAttempt simAttempt;
                if (StrongRandomNumberGenerator.GetFraction() <
                    _experimentalConfiguration.FractionOfLoginAttemptsFromAttacker)
                {
                    switch (_experimentalConfiguration.AttackersStrategy)
                    {
                        case ExperimentalConfiguration.AttackStrategy.UseUntilLikelyPopular:                        
                            simAttempt = _attemptGenerator.MaliciousLoginAttemptBreadthFirstAvoidMakingPopular(eventTimeUtc);
                        break;
                        case ExperimentalConfiguration.AttackStrategy.Weighted:                         
                            simAttempt = _attemptGenerator.MaliciousLoginAttemptWeighted(eventTimeUtc);
                        break;
                        case ExperimentalConfiguration.AttackStrategy.BreadthFirst:
                        default:
                            simAttempt = _attemptGenerator.MaliciousLoginAttemptBreadthFirst(eventTimeUtc);
                            break;
                    }
                }
                else
                {
                    simAttempt = _attemptGenerator.BenignLoginAttempt(eventTimeUtc);                    
                }

                // Get information about the client's IP
                SimIpHistory ipHistory = await _ipHistoryCache.GetAsync(simAttempt.AddressOfClientInitiatingRequest, cancelToken);

                double[] scores = ipHistory.GetAllScores(_experimentalConfiguration.BlockingOptions.BlockScoreHalfLife,
                    simAttempt.TimeOfAttemptUtc);

                simAttempt.UpdateSimulatorState(this, ipHistory);

                var ipInfo = _ipPool.GetIpAddressDebugInfo(simAttempt.AddressOfClientInitiatingRequest);
                string outputString = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}", 
                    simAttempt.Password, 
                    simAttempt.SimAccount?.UsernameOrAccountId ?? "<null>",
                    simAttempt.AddressOfClientInitiatingRequest,
                    simAttempt.IsFrequentlyGuessedPassword ? "Frequent" : "Infrequent",
                    simAttempt.IsPasswordValid ? "Correct" : "Incorrect",
                    simAttempt.IsFromAttacker ? "FromAttacker" : "FromUser",
                    simAttempt.IsGuess ? "IsGuess" : "NotGuess",
                    simAttempt.IsFromAttacker ? (ipInfo.UsedByBenignUsers ? "IsInBenignPool" : "NotUsedByBenign") : 
                                                (ipInfo.UsedByAttackers ? "IsInAttackersIpPool" : "NotUsedByAttacker"),
                    ipInfo.IsPartOfProxy ? "ProxyIP" : "NotAProxy",
                    string.IsNullOrEmpty(simAttempt.MistakeType) ? "-" : simAttempt.MistakeType,
 
                    string.Join(",", scores.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray())
                    );

                if (simAttempt.IsFromAttacker && simAttempt.IsPasswordValid)
                {
                    await _AttackAttemptsWithValidPasswords.WriteLineAsync(outputString);
                    await _AttackAttemptsWithValidPasswords.FlushAsync();
                } else if (!simAttempt.IsFromAttacker && simAttempt.IsPasswordValid)
                {
                    await _LegitiamteAttemptsWithValidPasswords.WriteLineAsync(outputString);
                    await _LegitiamteAttemptsWithValidPasswords.FlushAsync();
                }
                else
                {
                    await _OtherAttempts.WriteLineAsync(outputString);
                    await _OtherAttempts.FlushAsync();
                }
            },
            //(e) => {
            //},
            cancellationToken: cancellationToken);
            _memoryUsageLimiter.Dispose();
        }
    }
}
