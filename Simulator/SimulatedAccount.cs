using System.Collections.Concurrent;
using System.Net;

namespace Simulator
{
    public class SimulatedAccount
    {
        public string UniqueId;
        public string Password;
        public IPAddress PrimaryIp;
        public ConcurrentBag<string> Cookies = new ConcurrentBag<string>();
    }
}
