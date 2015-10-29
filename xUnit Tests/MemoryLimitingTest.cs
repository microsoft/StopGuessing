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

            // Create so many accounts that we have to flush them from cache.
            for (uint i = 0; i < 1*1024*1024*1024; i++)
            {
                string username = "User" + i;
                FunctionalTests.LoginTestCreateAccount(config, username, "passwordfor" + username);
            }
        }
    }
}
