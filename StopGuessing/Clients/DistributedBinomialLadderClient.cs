using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.DataStructures;
using StopGuessing.Models;

namespace StopGuessing.Clients
{


    public class DistributedBinomialLadderClient : IBinomialLadderSketch
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

        public async Task<ILadder> GetLadderAsync(string key,
            TimeSpan? timeout = null, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
            List<RemoteHost> hosts = _responsibleHosts.FindMembersResponsible(key, HostsPerLadder);
            int rungsPerHost = HeightOfLadderInRungs / hosts.Count;
            ConcurrentBag<RemoteRungNotYetClimbed> rungsNotYetClimbed = new ConcurrentBag<RemoteRungNotYetClimbed>();
            int rungsCalculated = 0;
            object rungLock = new object();

            await TaskParalllel.ForEachWithWorkers(hosts,
                async (host, itemNumber, iterationCancellationToken) =>
                {
                    int[] indexesOfRungsNotYetClimbed = await RestClientHelper.GetAsync<int[]>(
                        host.Uri,
                        "BinomialLadder/" + Microsoft.Framework.WebEncoders.UrlEncoder.Default.UrlEncode(key),
                        new[]
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
                //(e, exceptionCancellationToken) =>
                //{
                //    // FIXME -- what to do with exceptions?
                //}, 
                cancellationToken: cancellationToken);

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


        public class DistributedBinomialLadder : BinomialLadder<RemoteRungNotYetClimbed>
        {
            public DistributedBinomialLadder(IEnumerable<RemoteRungNotYetClimbed> rungsNotYetClimbed, int heightOfLadderInRungs) : base(rungsNotYetClimbed, heightOfLadderInRungs)
            {
            }

            // ReSharper disable once MemberHidesStaticFromOuterClass
            protected override async Task StepAsync(RemoteRungNotYetClimbed rungToClimb, CancellationToken cancellationToken = default(CancellationToken))
            {
                await DistributedBinomialLadderClient.StepAsync(rungToClimb, cancellationToken);
            }


            // ReSharper disable once MemberHidesStaticFromOuterClass
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
            protected override async Task StepOverTopAsync(CancellationToken cancellationToken = default(CancellationToken))
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
            {
                // FIXME
            }

        }
    }
}
