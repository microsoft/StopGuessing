using System.Collections.Concurrent;
using System.Net;
using StopGuessing.Memory;

namespace Simulator
{
    public class SimulatedAccount
    {
        public string UniqueId;
        public string Password;
        public ConcurrentBag<string> Cookies = new ConcurrentBag<string>();
        public ConcurrentBag<IPAddress> ClientAddresses = new ConcurrentBag<IPAddress>();
        public MemoryUserAccount Account;
    }
}
