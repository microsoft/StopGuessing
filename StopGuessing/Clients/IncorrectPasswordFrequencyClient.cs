using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.DataStructures;
using StopGuessing.Models;

namespace StopGuessing.Clients
{


    public class IncorrectPasswordFrequencyClient : IFrequenciesProvider<string>
    {
        /// <summary>
        /// The number of hosts to distribute a ladder over
        /// </summary>
        public int HostsPerIncorrectPassword;

        private readonly IDistributedResponsibilitySet<RemoteHost> _responsibleHosts;

        public async Task<IFrequencies> GetFrequenciesAsync(string passwordHash,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            List<RemoteHost> hostsResponsibleForThisHash = _responsibleHosts.FindMembersResponsible(passwordHash,
                    HostsPerIncorrectPassword);

            ConcurrentBag<Proportion[]> proportionsForEachHost = new ConcurrentBag<Proportion[]>();

            await TaskParalllel.ForEachWithWorkers(hostsResponsibleForThisHash,
                async (host, itemNumber, cancelToken) =>
                {
                    Proportion[] proportionsForThisHost = await RestClientHelper.GetAsync<Proportion[]>(
                        host.Uri,
                        "IncorrectPasswordFrequency/" +
                        Microsoft.Framework.WebEncoders.UrlEncoder.Default.UrlEncode(passwordHash),
                        timeout: timeout, cancellationToken: cancelToken);
                    proportionsForEachHost.Add(proportionsForThisHost);
                },
                //e =>
                //{
                //    // FIXME -- what to do with exceptions?
                //},
                cancellationToken: cancellationToken);

            List<Proportion> proportions = new List<Proportion>();
            foreach (Proportion[] proportionsForHost in proportionsForEachHost)
            {
                for (int i = 0; i < proportionsForHost.Length; i++)
                {
                    Proportion p = proportionsForHost[i];
                    if (proportions.Count <= i)
                        proportions.Add(p);
                    else if (p.AsDouble > proportions[i].AsDouble)
                        proportions[i] = p;
                }
            }

            return new FrequencyTrackerFrequencies(hostsResponsibleForThisHash, passwordHash, proportions.ToArray());
        }

        protected async Task RecordObservation(string passwordHash,
            List<RemoteHost> hostsResponsibleForThisHash = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (hostsResponsibleForThisHash == null)
                hostsResponsibleForThisHash = _responsibleHosts.FindMembersResponsible(passwordHash,
                    HostsPerIncorrectPassword);

            await TaskParalllel.ForEachWithWorkers(hostsResponsibleForThisHash,
                async (host, itemNumber, cancelToken) =>
                {
                    await RestClientHelper.PostAsync(
                        host.Uri,
                        "IncorrectPasswordFrequency/" +
                        Microsoft.Framework.WebEncoders.UrlEncoder.Default.UrlEncode(passwordHash),
                        null,
                        timeout: timeout,
                        cancellationToken: cancelToken);
                },
                //e =>
                //{
                //    // FIXME -- what to do with exceptions?
                //},
                cancellationToken: cancellationToken);
        }


        public IncorrectPasswordFrequencyClient(IDistributedResponsibilitySet<RemoteHost> responsibleHosts,
            int hostsPerIncorrectPassword)
        {
            _responsibleHosts = responsibleHosts;
            HostsPerIncorrectPassword = hostsPerIncorrectPassword;
        }

        public class FrequencyTrackerFrequencies : IFrequencies
        {
            protected IncorrectPasswordFrequencyClient Client;
            protected List<RemoteHost> HostsResponsibleForThisHash;

            protected string HashAsString;

            public Proportion[] Proportions { get; protected set; }

            public async Task RecordObservationAsync(TimeSpan? timeout = null,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                await Client.RecordObservation(HashAsString, HostsResponsibleForThisHash, timeout, cancellationToken);
            }

            public FrequencyTrackerFrequencies(List<RemoteHost> hostsResponsibleForThisHash,
                string hashAsString, Proportion[] proportions)
            {
                HostsResponsibleForThisHash = hostsResponsibleForThisHash;
                HashAsString = hashAsString;
                Proportions = proportions;
            }

        }

    }
}
