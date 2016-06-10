using System.Collections.Concurrent;
using System.Net;
using StopGuessing.AccountStorage.Memory;

namespace Simulator
{
    public class SimulatedUserAccount : MemoryUserAccount
    {
        public string Password;

        public ConcurrentBag<string> Cookies = new ConcurrentBag<string>();
        public ConcurrentBag<IPAddress> ClientAddresses = new ConcurrentBag<IPAddress>();

    }
}
