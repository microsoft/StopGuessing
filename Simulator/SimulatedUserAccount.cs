using System.Collections.Concurrent;
using System.Net;
using StopGuessing.AccountStorage.Memory;
using StopGuessing.DataStructures;

namespace Simulator
{
    public class SimulatedUserAccount : MemoryUserAccount
    {
        public string Password;

        public ConcurrentBag<string> Cookies = new ConcurrentBag<string>();
        public ConcurrentBag<IPAddress> ClientAddresses = new ConcurrentBag<IPAddress>();

        public DecayingDouble ConsecutiveIncorrectAttempts = new DecayingDouble(0);
        public DecayingDouble MaxConsecutiveIncorrectAttempts = new DecayingDouble(0);

    }
}
