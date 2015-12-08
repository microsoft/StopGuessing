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
        private readonly LoginAttemptController _loginAttemptController;
        private readonly IUserAccountContextFactory accountContextFactory;     
        private readonly ExperimentalConfiguration _experimentalConfiguration;

        private readonly TextWriter _outputWriter;
        private readonly DebugLogger _logger;
        private readonly SimulatedPasswords _simPasswords;
        private IpPool _ipPool;
        private SimulatedAccounts _simAccounts;
        private SimulatedLoginAttemptGenerator _attemptGenerator;


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
            _experimentalConfiguration = myExperimentalConfiguration;
            BlockingAlgorithmOptions options = _experimentalConfiguration.BlockingOptions;
            
            _logger.WriteStatus("Creating binomial ladder");
            BinomialLadderSketch localPasswordBinomialLadderSketch =
                new BinomialLadderSketch(1024 * 1024 * 1024, options.NumberOfRungsInBinomialLadder);
            MultiperiodFrequencyTracker<string> localPasswordFrequencyTracker =
                new MultiperiodFrequencyTracker<string>(
                    options.NumberOfPopularityMeasurementPeriods,
                    options.LengthOfShortestPopularityMeasurementPeriod,
                    options.FactorOfGrowthBetweenPopularityMeasurementPeriods);
            _logger.WriteStatus("Finished creating binomial ladder");


            accountContextFactory = new MemoryOnlyAccountContextFactory();

            MemoryUsageLimiter memoryUsageLimiter = new MemoryUsageLimiter();
            _loginAttemptController = new LoginAttemptController(
                accountContextFactory, localPasswordBinomialLadderSketch, localPasswordFrequencyTracker,
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

            _logger.WriteStatus("Priming password-tracking with known common passwords");
            await _simPasswords.PrimeWithKnownPasswordsAsync(_loginAttemptController);
            _logger.WriteStatus("Finished priming password-tracking with known common passwords");

            _logger.WriteStatus("Creating IP Pool");
            _ipPool = new IpPool(_experimentalConfiguration);
            _logger.WriteStatus("Generating simualted account records");
            _simAccounts = new SimulatedAccounts(_ipPool, _simPasswords, _logger);
            await _simAccounts.GenerateAsync(_experimentalConfiguration, accountContextFactory, cancellationToken);

            _logger.WriteStatus("Creating login-attempt generator");
            _attemptGenerator = new SimulatedLoginAttemptGenerator(_experimentalConfiguration, _simAccounts, _ipPool, _simPasswords);
            _logger.WriteStatus("Finiished creating login-attempt generator");

            _outputWriter.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                "IsPasswordCorrect",
                "IsFromAttackAttacker",
                "IsAGuess",
                "IPInOposingPool",
                "IsClientAProxyIP",
                "TypeOfMistake",
                "UserID",
                "Password",
                string.Join(",", _experimentalConfiguration.BlockingOptions.Conditions.Select( cond => cond.Name )));

            DateTime startTimeUtc = new DateTime(2016,01,01,0,0,0, DateTimeKind.Utc);
            TimeSpan testTimeSpan = _experimentalConfiguration.TestTimeSpan;
            double ticksBetweenLogins = ((double)testTimeSpan.Ticks)/(double)_experimentalConfiguration.TotalLoginAttemptsToIssue;
            
            await TaskParalllel.RepeatWithWorkers(_experimentalConfiguration.TotalLoginAttemptsToIssue, async (count, cancelToken) =>
            {
                if (count % 10000 == 0)
                    _logger.WriteStatus("Login Attempt {0:N0}", count);
                DateTime eventTimeUtc = startTimeUtc.AddTicks((long) (ticksBetweenLogins * count));
                SimulatedLoginAttempt simAttempt;
                if (StrongRandomNumberGenerator.GetFraction() <
                    _experimentalConfiguration.FractionOfLoginAttemptsFromAttacker)
                {
                    simAttempt = _attemptGenerator.MaliciousLoginAttemptBreadthFirst();
                }
                else
                {
                    simAttempt = _attemptGenerator.BenignLoginAttempt();
                }
                simAttempt.Attempt.TimeOfAttemptUtc = eventTimeUtc;

                double[] scores = await
                    _loginAttemptController.DetermineLoginAttemptOutcomeAsync(simAttempt.Attempt, simAttempt.Password,
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
