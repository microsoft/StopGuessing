using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;

namespace StopGuessing.DataStructures
{


    public class DistributedBinomialLadderClient
    {
        /// <summary>
        /// The number of hosts to distribute a ladder over
        /// </summary>
        public int HostsPerLadder;

        /// <summary>
        /// The number of rungs that each key's ladder should have
        /// </summary>
        public int HeightOfLadderInRungs;

        public class RemoteRungNotYetClimbed
        {
            public RemoteHost Host;
            public int Index;

            public RemoteRungNotYetClimbed(RemoteHost host, int index)
            {
                Host = host;
                Index = index;
            }
        }

        private readonly IDistributedResponsibilitySet<RemoteHost> _responsibleHosts;

        public async Task<DistributedBinomialLadder> GetLadder(string key, TimeSpan timeout, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
            List<RemoteHost> hosts = _responsibleHosts.FindMembersResponsible(key, HostsPerLadder);
            int rungsPerHost = HeightOfLadderInRungs / hosts.Count;
            ConcurrentBag<RemoteRungNotYetClimbed> rungsNotYetClimbed = new ConcurrentBag<RemoteRungNotYetClimbed>();
            int rungsCalculated = 0;
            object rungLock = new object();

            await TaskParalllel.ForEach(hosts,
                async (host) =>
                {
                    int[] indexesOfRungsNotYetClimbed = await RestClientHelper.GetAsync<int[]>(
                        host.Uri,
                        "BinomialLadder/" + Microsoft.Framework.WebEncoders.UrlEncoder.Default.UrlEncode(key),
                        new KeyValuePair<string, string>[]
                        {
                            new KeyValuePair<string, string>("numberOfRungs", rungsPerHost.ToString())
                        },
                        timeout: timeout, cancellationToken: cancellationToken);
                    foreach (int indexOfRungNotYetClimbed in indexesOfRungsNotYetClimbed)
                        rungsNotYetClimbed.Add(new RemoteRungNotYetClimbed(host, indexOfRungNotYetClimbed));
                    lock (rungLock)
                    {
                        rungsCalculated += rungsPerHost;
                    }
                },
                (e) =>
                {
                    // FIXME -- what to do with exceptions?
                }, cancellationToken: cancellationToken);

            return new DistributedBinomialLadder(rungsNotYetClimbed, rungsCalculated);
        }

        protected static async Task StepAsync(RemoteRungNotYetClimbed rungToClimb, CancellationToken cancellationToken = default(CancellationToken))
        {
            await RestClientHelper.PutAsync(
                   rungToClimb.Host.Uri,
                   "BinomialLadderRung/" + rungToClimb.Index,
                   "1",
                   cancellationToken: cancellationToken);
        }


        public DistributedBinomialLadderClient(IDistributedResponsibilitySet<RemoteHost> responsibleHosts,
            int hostsPerLadder,
            int heightOfLadderInRungs)
        {
            _responsibleHosts = responsibleHosts;
            HostsPerLadder = hostsPerLadder;
            HeightOfLadderInRungs = heightOfLadderInRungs;
        }


        public class DistributedBinomialLadder : LadderForKey<RemoteRungNotYetClimbed>
        {
            public DistributedBinomialLadder(IEnumerable<RemoteRungNotYetClimbed> rungsNotYetClimbed, int heightOfLadderInRungs) : base(rungsNotYetClimbed, heightOfLadderInRungs)
            {
            }

            protected override async Task StepAsync(RemoteRungNotYetClimbed rungToClimb, CancellationToken cancellationToken = default(CancellationToken))
            {
                await DistributedBinomialLadderClient.StepAsync(rungToClimb, cancellationToken);
            }

        }
    }
}
