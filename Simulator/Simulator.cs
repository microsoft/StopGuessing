using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Formatters;
using StopGuessing.AccountStorage.Memory;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;
using StopGuessing.Utilities;
using System.Collections.Concurrent;

namespace Simulator
{
    public partial class Simulator
    {
        //private readonly LoginAttemptController _loginAttemptController;
        //private readonly IUserAccountContextFactory _accountContextFactory;
        public BinomialLadderFilter _binomialLadderFilter;
        public AgingMembershipSketch _recentIncorrectPasswords;
        //public SelfLoadingCache<IPAddress, SimIpHistory> _ipHistoryCache;
        public ConcurrentDictionary<IPAddress, SimIpHistory> _ipHistoryCache;
        public readonly ExperimentalConfiguration _experimentalConfiguration;
        //public readonly MemoryUsageLimiter _memoryUsageLimiter;
        public readonly MemoryUserAccountController _userAccountController;

        private readonly ConcurrentStreamWriter _AttackAttemptsWithValidPasswords;
        private readonly ConcurrentStreamWriter _LegitimateAttemptsWithValidPasswords;
        private readonly ConcurrentStreamWriter _OtherAttempts;
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


        public static void RunExperimentalSweep(ExperimentalConfiguration[] configurations)
        {
            foreach (ExperimentalConfiguration config in configurations)
            {
                DateTime now = DateTime.Now;
                string dirName = config.OutputPath + config.OutputDirectoryName + "_Run_" + now.Month + "_" + now.Day +
                                 "_" + now.Hour + "_" + now.Minute;
                Directory.CreateDirectory(dirName);
                Directory.CreateDirectory(dirName);
                string path = dirName + @"\";

                // Now that all of the parameters of the sweep have been set, run the simulation
                //TextWriter dataWriter = System.IO.TextWriter.Synchronized(new StreamWriter(path + "data.txt"));
                TextWriter errorWriter = //TextWriter.Synchronized
                    (new StreamWriter(new FileStream(path + "error.txt", FileMode.CreateNew, FileAccess.Write)));
                DebugLogger logger = new DebugLogger(errorWriter);
                try
                {
                    SimulatedPasswords simPasswords = new SimulatedPasswords(logger, config);
                    Simulator simulator = new Simulator(logger, path, config, simPasswords);
                    simulator.Run();
                }
                catch (Exception e)
                {
                    lock (errorWriter)
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
        }

        //public void ReduceMemoryUsage(object sender, MemoryUsageLimiter.ReduceMemoryUsageEventParameters parameters)
        //{
            //_ipHistoryCache.RecoverSpace(parameters.FractionOfMemoryToTryToRemove);
        //}

        public Simulator(DebugLogger logger, string path, ExperimentalConfiguration myExperimentalConfiguration, SimulatedPasswords simPasswords)
        {
            
            _simPasswords = simPasswords;
            _logger = logger;
            _AttackAttemptsWithValidPasswords = //System.IO.TextWriter.Synchronized 
                new ConcurrentStreamWriter(path + "AttackAttemptsWithValidPasswords.txt");
                //(new StreamWriter(new FileStream(path + "AttackAttemptsWithValidPasswords.txt", FileMode.CreateNew, FileAccess.Write)));
            _LegitimateAttemptsWithValidPasswords = //System.IO.TextWriter.Synchronized
                new ConcurrentStreamWriter(path + "LegitimateAttemptsWithValidPasswords.txt");
            //(new StreamWriter(new FileStream(path + "LegitiamteAttemptsWithValidPasswords.txt", FileMode.CreateNew, FileAccess.Write)));
            _OtherAttempts = //System.IO.TextWriter.Synchronized
                new ConcurrentStreamWriter(path + "OtherAttempts.txt");
                //(new StreamWriter(new FileStream(path + "OtherAttempts.txt", FileMode.CreateNew, FileAccess.Write)));
            _logger.WriteStatus("Entered Simulator constructor");
            _experimentalConfiguration = myExperimentalConfiguration;
            BlockingAlgorithmOptions options = _experimentalConfiguration.BlockingOptions;
            
            _logger.WriteStatus("Creating binomial ladder");
            _binomialLadderFilter =
                new BinomialLadderFilter(options.NumberOfBitsInBinomialLadderFilter_N, options.HeightOfBinomialLadder_H);
            _ipHistoryCache = new ConcurrentDictionary<IPAddress, SimIpHistory>(); // new SelfLoadingCache<IPAddress, SimIpHistory>(address => new SimIpHistory(options.NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos));
            _userAccountController = new MemoryUserAccountController();

            //_memoryUsageLimiter = new MemoryUsageLimiter();
            //_memoryUsageLimiter.OnReduceMemoryUsageEventHandler += ReduceMemoryUsage;

            _recentIncorrectPasswords = new AgingMembershipSketch(16, 128 * 1024);

            _logger.WriteStatus("Exiting Simulator constructor");
        }


        /// <summary>
        /// Evaluate the accuracy of our stopguessing service by sending user logins and malicious traffic
        /// </summary>
        /// <returns></returns>
        public void Run(CancellationToken cancellationToken = default(CancellationToken))
        {
            _logger.WriteStatus("In RunInBackground");

            _logger.WriteStatus("Priming password-tracking with known common passwords");
            _simPasswords.PrimeWithKnownPasswordsAsync(_binomialLadderFilter, 40);
            _logger.WriteStatus("Finished priming password-tracking with known common passwords");

            _logger.WriteStatus("Creating IP Pool");
            _ipPool = new IpPool(_experimentalConfiguration);
            _logger.WriteStatus("Generating simualted account records");
            _simAccounts = new SimulatedAccounts(_ipPool, _simPasswords, _logger);
            _simAccounts.Generate(_experimentalConfiguration, cancellationToken);

            _logger.WriteStatus("Creating login-attempt generator");
            _attemptGenerator = new SimulatedLoginAttemptGenerator(_experimentalConfiguration, _simAccounts, _ipPool,
                _simPasswords);
            _logger.WriteStatus("Finiished creating login-attempt generator");


            foreach (
                ConcurrentStreamWriter writer in
                    new[]
                    {_AttackAttemptsWithValidPasswords, _LegitimateAttemptsWithValidPasswords, _OtherAttempts})
            {
                lock (writer)
                {

                    writer.WriteLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}" +
                                                   "\t{11}\t{12}\t{13}\t{14}\t{15}\t{16}\t{17}\t{18}\t{19}\t{20}\t{21}\t{22}\t{23}",
                        "Password",
                        "UserID",
                        "IP",
                        "DeviceCookie",
                        "IsFrequentlyGuessedPw",
                        "IsPasswordCorrect",
                        "IsFromAttackAttacker",
                        "IsAGuess",
                        "IPInOposingPool",
                        "IsClientAProxyIP",
                        "TypeOfMistake",
                        "DecayedSuccessfulLogins",
                        "DecayedAccountFailuresInfrequentPassword",
                        "DecayedAccountFailuresFrequentPassword",
                        "DecayedRepeatAccountFailuresInfrequentPassword",
                        "DecayedRepeatAccountFailuresFrequentPassword",
                        "DecayedPasswordFailuresNoTypoInfrequentPassword",
                        "DecayedPasswordFailuresNoTypoFrequentPassword",
                        "DecayedPasswordFailuresTypoInfrequentPassword",
                        "DecayedPasswordFailuresTypoFrequentPassword",
                        "DecayedRepeatPasswordFailuresNoTypoInfrequentPassword",
                        "DecayedRepeatPasswordFailuresNoTypoFrequentPassword",
                        "DecayedRepeatPasswordFailuresTypoInfrequentPassword",
                        "DecayedRepeatPasswordFailuresTypoFrequentPassword"
                        ));
                }
            }

            TimeSpan testTimeSpan = _experimentalConfiguration.TestTimeSpan;
            double ticksBetweenLogins = ((double) testTimeSpan.Ticks)/
                                        (double) _experimentalConfiguration.TotalLoginAttemptsToIssue;
            int interlockedCount = 0;

            Parallel.For(0L, (long) _experimentalConfiguration.TotalLoginAttemptsToIssue, (count, pls) =>
                //) TaskParalllel.RepeatWithWorkers(_experimentalConfiguration.TotalLoginAttemptsToIssue, 
                //async (count, cancelToken) =>
                // (count) => 
            {
                interlockedCount = Interlocked.Add(ref interlockedCount, 1);
                if (interlockedCount % 10000 == 0)
                    _logger.WriteStatus("Login Attempt {0:N0}", interlockedCount);
                DateTime eventTimeUtc = StartTimeUtc.AddTicks((long) (ticksBetweenLogins* interlockedCount));
                SimulatedLoginAttempt simAttempt;
                if (StrongRandomNumberGenerator.GetFraction() <
                    _experimentalConfiguration.FractionOfLoginAttemptsFromAttacker)
                {
                    switch (_experimentalConfiguration.AttackersStrategy)
                    {
                        case ExperimentalConfiguration.AttackStrategy.UseUntilLikelyPopular:
                            simAttempt =
                                _attemptGenerator.MaliciousLoginAttemptBreadthFirstAvoidMakingPopular(eventTimeUtc);
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
                SimIpHistory ipHistory = _ipHistoryCache.GetOrAdd(simAttempt.AddressOfClientInitiatingRequest,
                    (ip) => new SimIpHistory(
                            _experimentalConfiguration.BlockingOptions
                                .NumberOfFailuresToTrackForGoingBackInTimeToIdentifyTypos) );
                //SimIpHistory ipHistory = await _ipHistoryCache.GetAsync(simAttempt.AddressOfClientInitiatingRequest, cancelToken);

                double[] scores = ipHistory.GetAllScores(_experimentalConfiguration.BlockingOptions.BlockScoreHalfLife,
                    simAttempt.TimeOfAttemptUtc);

                simAttempt.UpdateSimulatorState(this, ipHistory);

                var ipInfo = _ipPool.GetIpAddressDebugInfo(simAttempt.AddressOfClientInitiatingRequest);
                string outputString = string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\t{11}",
                    simAttempt.Password,
                    simAttempt.SimAccount?.UsernameOrAccountId ?? "<null>",
                    simAttempt.AddressOfClientInitiatingRequest,
                    simAttempt.DeviceCookieHadPriorSuccessfulLoginForThisAccount ? "HadCookie" : "NoCookie",
                    simAttempt.IsFrequentlyGuessedPassword ? "Frequent" : "Infrequent",
                    simAttempt.IsPasswordValid ? "Correct" : "Incorrect",
                    simAttempt.IsFromAttacker ? "FromAttacker" : "FromUser",
                    simAttempt.IsGuess ? "IsGuess" : "NotGuess",
                    simAttempt.IsFromAttacker
                        ? (ipInfo.UsedByBenignUsers ? "IsInBenignPool" : "NotUsedByBenign")
                        : (ipInfo.UsedByAttackers ? "IsInAttackersIpPool" : "NotUsedByAttacker"),
                    ipInfo.IsPartOfProxy ? "ProxyIP" : "NotAProxy",
                    string.IsNullOrEmpty(simAttempt.MistakeType) ? "-" : simAttempt.MistakeType,

                    string.Join("\t", scores.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray())
                    );

                if (simAttempt.IsFromAttacker && simAttempt.IsPasswordValid)
                {
                    lock (_AttackAttemptsWithValidPasswords)
                    {
                        _AttackAttemptsWithValidPasswords.WriteLine(outputString);
                        //_AttackAttemptsWithValidPasswords.Flush();
                    }
                }
                else if (!simAttempt.IsFromAttacker && simAttempt.IsPasswordValid)
                {
                    lock (_LegitimateAttemptsWithValidPasswords)
                    {
                        _LegitimateAttemptsWithValidPasswords.WriteLine(outputString);
                        //_LegitiamteAttemptsWithValidPasswords.Flush();
                    }
                }
                else
                {
                    lock (_OtherAttempts)
                    {
                        _OtherAttempts.WriteLine(outputString);
                        //_OtherAttempts.Flush();
                    }
                }
            });
            //(e) => {
            //},
            //cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (
                ConcurrentStreamWriter writer in
                    new []
                    {_AttackAttemptsWithValidPasswords, _LegitimateAttemptsWithValidPasswords, _OtherAttempts})
            {
                writer.Close();
            }
            //_memoryUsageLimiter.Dispose();
        }

    }
}
