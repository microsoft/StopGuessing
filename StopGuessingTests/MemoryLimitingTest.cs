using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace StopGuessingTests
{
    public class MemoryLimitingTest
    {
        [Fact]
        public void BigFreakinAllocationTest()
        {
            Console.Error.WriteLine("Starting test");
            Console.Out.WriteLine("Out starting test.");
            TestConfiguration config = FunctionalTests.InitTest();
            //config.StableStore.Accounts = null;
            //config.StableStore.LoginAttempts = null;

            uint levelOfParallelism = 8;
            List<Task> tasks = new List<Task>();
            // Create so many accounts that we have to flush them from cache.
            for (uint thread = 1; thread <= levelOfParallelism; thread++)//
            {
                uint myThread = thread;
                tasks.Add(
                    Task.Run(() => BigFreakinAllocationTestLoop(config, myThread, levelOfParallelism)));
            }
            Task.WaitAll(tasks.ToArray());
        }

        static readonly string BigString = new string('*', 10*1024);
        public void BigFreakinAllocationTestLoop(TestConfiguration config, uint threadIndex, uint levelOfParallelism)
        {
            for (uint i = threadIndex; i < 512 * 1024; i += levelOfParallelism)
            {
                string username = "User" + i + BigString;
                System.Threading.Thread.Sleep(10);
                FunctionalTests.CreateTestAccount(config, username, "passwordfor" + i);
            }
        }
    }
}
