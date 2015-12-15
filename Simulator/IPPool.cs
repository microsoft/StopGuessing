using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using StopGuessing.EncryptionPrimitives;

namespace Simulator
{
    /// <summary>
    /// Tracks the pool of IP addresses used by the simulator.
    /// </summary>
    public class IpPool
    {
        private IPAddress _currentProxyAddress = null;
        private int _numberOfClientsBehindTheCurrentProxy = 0;
        private readonly ConcurrentBag<IPAddress> _ipAddresssesInUseByBenignUsers = new ConcurrentBag<IPAddress>();
        private readonly ConcurrentDictionary<IPAddress, IpAddressDebugInfo> _debugInformationAboutIpAddresses = new ConcurrentDictionary<IPAddress, IpAddressDebugInfo>();
        private readonly ExperimentalConfiguration _experimentalConfiguration;


        public IpPool(ExperimentalConfiguration experimentalConfiguration)
        {
            _experimentalConfiguration = experimentalConfiguration;
        }


        public IpAddressDebugInfo GetIpAddressDebugInfo(IPAddress address)
        {
            return _debugInformationAboutIpAddresses.GetOrAdd(address, a => new IpAddressDebugInfo());
        }

        private readonly Object _proxyAddressLock = new object();
        /// <summary>
        /// Get a new IP address for use in a benign request
        /// </summary>
        /// <returns>An IP address</returns>
        public IPAddress GetNewRandomBenignIp()
        {
            IpAddressDebugInfo debugInfo;
            if (StrongRandomNumberGenerator.GetFraction() < _experimentalConfiguration.FractionOfBenignIPsBehindProxies)
            {
                // Use a proxy IP address
                lock (_proxyAddressLock)
                {
                    if (_currentProxyAddress == null || ++_numberOfClientsBehindTheCurrentProxy >=
                        _experimentalConfiguration.ProxySizeInUniqueClientIPs)
                    {
                        // Create a new proxy IP address
                        _currentProxyAddress = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                        debugInfo = GetIpAddressDebugInfo(_currentProxyAddress);
                        debugInfo.IsPartOfProxy = true;
                        debugInfo.UsedByBenignUsers = true;
                        _numberOfClientsBehindTheCurrentProxy = 0;
                        return _currentProxyAddress;
                    }
                    else
                    {
                        // Use the most recent proxy IP
                        return _currentProxyAddress;
                    }
                }
            }
            else
            {
                // Just pick a random address
                IPAddress address = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                _ipAddresssesInUseByBenignUsers.Add(address);
                debugInfo = GetIpAddressDebugInfo(address);
                debugInfo.UsedByBenignUsers = true;
                return address;
            }
        }


        private readonly List<IPAddress> _maliciousIpAddresses = new List<IPAddress>();
        /// <summary>
        /// Generate a set of IP addresses to be owned by attackers.
        /// Must be called after all benign users accounts have been created so as to create
        /// the desired level of overlap between those benign addresses and the malicious ones.
        /// </summary>
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
                IpAddressDebugInfo debugInfo = GetIpAddressDebugInfo(address);
                lock (debugInfo)
                {
                    debugInfo.UsedByAttackers = true;
                }
                _maliciousIpAddresses.Add(address);
                listOfIpAddressesInUseByBenignUsers.RemoveAt(randIndex);
            }
            for (; i < _experimentalConfiguration.NumberOfIpAddressesControlledByAttacker; i++)
            {
                IPAddress address = new IPAddress(StrongRandomNumberGenerator.Get32Bits());
                IpAddressDebugInfo debugInfo = GetIpAddressDebugInfo(address);
                lock (debugInfo)
                {
                    debugInfo.UsedByAttackers = true;
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
