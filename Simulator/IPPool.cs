using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StopGuessing.EncryptionPrimitives;

namespace Simulator
{
    public class IpPool
    {
        private readonly Object _proxyAddressLock = new object();
        private IPAddress _currentProxyAddress = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
        private int _numberOfClientsBehindTheCurrentProxy = 0;
        private readonly ConcurrentBag<IPAddress> _ipAddresssesInUseByBenignUsers = new ConcurrentBag<IPAddress>();
        private readonly ConcurrentDictionary<IPAddress, IPAddressDebugInfo> _debugInformationAboutIpAddresses = new ConcurrentDictionary<IPAddress, IPAddressDebugInfo>();
        private readonly ExperimentalConfiguration _experimentalConfiguration;


        public IpPool(ExperimentalConfiguration experimentalConfiguration)
        {
            _experimentalConfiguration = experimentalConfiguration;
        }


        public IPAddressDebugInfo GetIpAddressDebugInfo(IPAddress address)
        {
            return _debugInformationAboutIpAddresses.GetOrAdd(address, a => new IPAddressDebugInfo());
        }

        public IPAddress GetNewRandomBenignIp(string forUserId)
        {
            IPAddress address;
            IPAddressDebugInfo debugInfo;
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.FractionOfBenignIPsBehindProxies)
            {
                // Use the most recent proxy IP
                lock (_proxyAddressLock)
                {
                    address = _currentProxyAddress;
                    if (++_numberOfClientsBehindTheCurrentProxy >=
                        _experimentalConfiguration.ProxySizeInUniqueClientIPs)
                        _currentProxyAddress = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                    debugInfo = GetIpAddressDebugInfo(_currentProxyAddress);
                    lock (debugInfo)
                    {
                        debugInfo.IsPartOfProxy = true;
                    }
                }
            }
            else
            {
                // Just pick a random address
                address = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                _ipAddresssesInUseByBenignUsers.Add(address);
                debugInfo = GetIpAddressDebugInfo(address);
            }
            lock (debugInfo)
            {
                if (debugInfo.UserIdsOfBenignUsers == null)
                    debugInfo.UserIdsOfBenignUsers = new List<string>();
                debugInfo.UserIdsOfBenignUsers.Add(forUserId);
            }
            return address;
        }


        private readonly List<IPAddress> _maliciousIpAddresses = new List<IPAddress>();
        public void GenerateAttackersIps()
        {
            List<IPAddress> listOfIpAddressesInUseByBenignUsers = _ipAddresssesInUseByBenignUsers.ToList();
            uint numberOfOverlappingIps = (uint)
                (_experimentalConfiguration.NumberOfIpAddressesControlledByAttacker *
                 _experimentalConfiguration.FractionOfMaliciousIPsToOverlapWithBenign);
            uint i;
            for (i = 0; i < numberOfOverlappingIps && listOfIpAddressesInUseByBenignUsers.Count > 0; i++)
            {
                int randIndex = (int)StrongRandomNumberGenerator.Get32Bits(listOfIpAddressesInUseByBenignUsers.Count);
                IPAddress address = listOfIpAddressesInUseByBenignUsers[randIndex];
                IPAddressDebugInfo debugInfo = GetIpAddressDebugInfo(address);
                lock (debugInfo)
                {
                    debugInfo.IsInAttackersIpPool = true;
                }
                _maliciousIpAddresses.Add(address);
                listOfIpAddressesInUseByBenignUsers.RemoveAt(randIndex);
            }
            for (; i < _experimentalConfiguration.NumberOfIpAddressesControlledByAttacker; i++)
            {
                IPAddress address = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                IPAddressDebugInfo debugInfo = GetIpAddressDebugInfo(address);
                lock (debugInfo)
                {
                    debugInfo.IsInAttackersIpPool = true;
                }
                _maliciousIpAddresses.Add(address);
            }
        }


        /// <summary>
        /// Generate a random malicious IP address
        /// </summary>
        public IPAddress GetRandomMaliciousIp()
        {
            int randIndex = (int)StrongRandomNumberGenerator.Get32Bits(_maliciousIpAddresses.Count);
            IPAddress address = _maliciousIpAddresses[randIndex];
            return _maliciousIpAddresses[randIndex];
        }

    }
}
