using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Simulator
{
    /// <summary>
    /// Information that the simulator maintains about each IP address for reporting purposes
    /// </summary>
    public class IpAddressDebugInfo
    {
        /// <summary>
        ///  Set if one or more benign users use this IP address to login
        /// </summary>
        public bool UsedByBenignUsers;

        /// <summary>
        /// Set if one or more attackers use this IP address either to guess legitimate user's passwords
        /// or to login to accounts the attackers control (e.g., to provide traffic that makes the IP look
        /// like it's in use by legitimate users.)
        /// </summary>
        public bool UsedByAttackers;

        /// <summary>
        /// Set if the IP is a proxy shared by many users
        /// </summary>
        public bool IsPartOfProxy;
    }

}
