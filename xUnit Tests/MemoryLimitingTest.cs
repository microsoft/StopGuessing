using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace xUnit_Tests
{
    public class MemoryLimitingTest
    {
        [Fact]
        public void BigFreakinAllocationTest()
        {
            TestConfiguration config = FunctionalTests.InitTest();
            config.StableStore.Accounts = null;

            uint levelOfParallelism = 1;
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

        static readonly string bigString = new string('*', 100000);
        public void BigFreakinAllocationTestLoop(TestConfiguration config, uint threadIndex, uint levelOfParallelism)
        {
            for (uint i = threadIndex; i < 1 * 1024 * 1024; i += levelOfParallelism)
            {
                string username = "User" + i + bigString;
                System.Threading.Thread.Sleep(10);
                FunctionalTests.LoginTestCreateAccount(config, username, "passwordfor" + i);
            }
        }
    }
}
