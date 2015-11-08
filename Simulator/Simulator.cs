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

        public static async Task ParallelRepeat(
            ulong numberOfTimesToRepeat,
            Action actionToRun,
            Action<Exception> callOnException = null,
            int maxConcurrentTasks = 1000)
        {
            Task[] activeTasks = new Task[maxConcurrentTasks];
            Dictionary<Task, int> taskToIndex = new Dictionary<Task, int>();
            HashSet<Task> exceptionHandlingTasks = new HashSet<Task>();

            ulong tasksStarted = 0;
            // Phase 1 -- start maxConcurrentTasks executing
            while (tasksStarted < (ulong) activeTasks.Length && tasksStarted < numberOfTimesToRepeat)
            {
                Task startedTask = Task.Run(actionToRun);
                activeTasks[tasksStarted] = startedTask;
                taskToIndex[startedTask] = (int) tasksStarted;
                tasksStarted++;
            }

            // Phase 2 -- A stable stat in which there are always the maximum number of tasks
            //            in our array of active tasks
            while (tasksStarted < numberOfTimesToRepeat)
            {
                // Wait for a task to complete
                Task completedTask = await Task.WhenAny(activeTasks.ToArray());
                int indexOfTaskToReplace = taskToIndex[completedTask];
                // Replace the task that completed with a new task...
                // If there was an exception, the replacement should be a task to handle
                // that exception.  Otherwise, it should be the next work item.
                bool callExceptionHandler = false;
                if (callOnException != null)
                {
                    bool completedTaskWasExceptionHandler = exceptionHandlingTasks.Contains(completedTask);
                    // We'll want to run a task with the caller's exception handler...
                    callExceptionHandler = completedTask.IsFaulted;
                    if (completedTaskWasExceptionHandler)
                    {
                        // unless it was the caller's exception handler that faulted
                        exceptionHandlingTasks.Remove(completedTask);
                        callExceptionHandler = false;
                    }
                }
                Task replacementTask = callExceptionHandler
                    ? Task.Run(() => callOnException(completedTask.Exception))
                    : Task.Run(actionToRun);
                // Put the replacement task at the same index in the array as the prior task
                activeTasks[indexOfTaskToReplace] = replacementTask;
                taskToIndex.Remove(completedTask);
                taskToIndex[replacementTask] = indexOfTaskToReplace;
                tasksStarted++;
            }

            // Phase 3 -- A final phase in which we empty out exceptions
            Task.WaitAll(activeTasks);
            if (callOnException != null)
            {
                foreach (Task exceptionTask in activeTasks.Where(t => t.IsFaulted))
                    #pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Run(() => callOnException(exceptionTask.Exception));
            }
        }



        public static async Task RunWithMaxDegreeOfConcurrency<T>(
            int maxDegreeOfConcurrency,
            IEnumerable<T> collection, 
            Func<T, Task> taskFactory)
        {
            var activeTasks = new List<Task>(maxDegreeOfConcurrency);
            foreach (var task in collection.Select(taskFactory))
            {
                activeTasks.Add(task);
                if (activeTasks.Count == maxDegreeOfConcurrency)
                {
                    await Task.WhenAny(activeTasks.ToArray());
                    //observe exceptions here
                    activeTasks.RemoveAll(t => t.IsCompleted);
                }
            }
            await Task.WhenAll(activeTasks.ToArray()).ContinueWith(t =>
            {
                //observe exceptions in a manner consistent with the above   
            });
        }

        /// <summary>
        /// Evaluate the accuracy of our stopguessing service by sending user logins and malicious traffic
        /// </summary>
        /// <returns></returns>
        public async Task Run(CancellationToken cancellationToken = default(CancellationToken))
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
            int count = 0;
            List<int> Runtime = new List<int>(new int[MyExperimentalConfiguration.TotalLoginAttemptsToIssue]);

            await RunWithMaxDegreeOfConcurrency(1000, Runtime, async i =>
            {
                try { 
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
                    MyLoginAttemptController.LocalPutAsync(simAttempt.Attempt, simAttempt.Password,
                        cancellationToken: cancellationToken);
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
            }
                catch (Exception e)
            {
                lock (stats)
                {
                    stats.TotalExceptions++;
                }
                Console.Error.WriteLine(e.ToString());
            }
        
                count++; 
            });



            //Parallel.For(0, (int) MyExperimentalConfiguration.TotalLoginAttemptsToIssue, async (index, state) =>
            //{
            //    try
            //    {
            //        lock (stats)
            //        {
            //            stats.TotalLoopIterations++;
            //        }
            //        SimulatedLoginAttempt simAttempt;
            //        if (StrongRandomNumberGenerator.GetFraction() <
            //            MyExperimentalConfiguration.FractionOfLoginAttemptsFromAttacker)
            //        {
            //            simAttempt = MaliciousLoginAttemptBreadthFirst();
            //        }
            //        else
            //        {
            //            simAttempt = BenignLoginAttempt();
            //        }

            //        LoginAttempt attemptWithOutcome = await
            //            MyLoginAttemptController.LocalPutAsync(simAttempt.Attempt, simAttempt.Password,
            //                cancellationToken: cancellationToken);
            //        AuthenticationOutcome outcome = attemptWithOutcome.Outcome;

            //        lock (stats)
            //        {
            //            stats. TotalLoopIterationsThatShouldHaveRecordedStats++;
            //            if (simAttempt.IsGuess)
            //            {
            //                if (outcome == AuthenticationOutcome.CredentialsValidButBlocked)
            //                    stats.TruePositives++;
            //                else if (outcome == AuthenticationOutcome.CredentialsValid)
            //                    stats.FalseNegatives++;
            //                else
            //                    stats.GuessWasWrong++;
            //            }
            //            else
            //            {
            //                if (outcome == AuthenticationOutcome.CredentialsValid)
            //                    stats.TrueNegatives++;
            //                else if (outcome == AuthenticationOutcome.CredentialsValidButBlocked)
            //                    stats.FalsePositives++;
            //                else
            //                    stats.BenignErrors++;
            //            }
            //        }
            //    }
            //    catch (Exception e)
            //    {
            //        lock (stats)
            //        {
            //            stats.TotalExceptions++;
            //        }
            //        Console.Error.WriteLine(e.ToString());
            //    }
            //});

            sw.Stop();

            Console.WriteLine("Time Elapsed={0}", sw.Elapsed);
            Console.WriteLine("the new count is {0}", count);

            double falsePositiveRate = ((double) stats.FalsePositives)/((double)stats.FalsePositives + stats.TruePositives);
            double falseNegativeRate = ((double)stats.FalseNegatives)/((double)stats.FalseNegatives + stats.TrueNegatives);

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
