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


    public class DistributedBinomialLadder
    {
        // FIXME
        public int HostsPerKey = 6;
        public int TotalRungs = 96;

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

        public class DistributedKeysLadder : Ladder<RemoteRungNotYetClimbed>
        {
            public DistributedKeysLadder(IEnumerable<RemoteRungNotYetClimbed> rungsNotYetClimbed, int heightOfLadderInRungs) : base(rungsNotYetClimbed, heightOfLadderInRungs)
            {
            }

            protected override async Task StepAsync(RemoteRungNotYetClimbed rungToClimb, CancellationToken cancellationToken = default(CancellationToken))
            {
                await RestClientHelper.PutAsync(
                       rungToClimb.Host.Uri,
                       "DistributedBinomialSketchElement/" + rungToClimb.Index,
                       "Climb!",
                       cancellationToken: cancellationToken);
            }

        }


        private readonly IDistributedResponsibilitySet<RemoteHost> _responsibleHosts;

        public async Task<DistributedKeysLadder> GetZeros(string key, TimeSpan timeout, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
            List<RemoteHost> hosts = _responsibleHosts.FindMembersResponsible(key, HostsPerKey);
            int rungsPerHost = TotalRungs / hosts.Count;
            ConcurrentBag<RemoteRungNotYetClimbed> rungsNotYetClimbed = new ConcurrentBag<RemoteRungNotYetClimbed>();
            int rungsCalculated = 0;
            object rungLock = new object();

            await TaskParalllel.ForEach(hosts,
                async (host) =>
                {
                    int[] indexesOfRungsNotYetClimbed = await RestClientHelper.GetAsync<int[]>(
                        host.Uri,
                        "DistributedBinomialSketch/" + Microsoft.Framework.WebEncoders.UrlEncoder.Default.UrlEncode(key),
                        new KeyValuePair<string, string>[]
                        {
                            new KeyValuePair<string, string>("Rungs", rungsPerHost.ToString())
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

            return new DistributedKeysLadder(rungsNotYetClimbed, rungsCalculated);
        }


        public DistributedBinomialLadder(IDistributedResponsibilitySet<RemoteHost> responsibleHosts)
        {
            _responsibleHosts = responsibleHosts;
        }


    }
}
