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

        public async Task<IUpdatableFrequency> GetFrequencyAsync(string passwordHash,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            List<RemoteHost> hostsResponsibleForThisHash = _responsibleHosts.FindMembersResponsible(passwordHash,
                    HostsPerIncorrectPassword);

            ConcurrentBag<Proportion> proportionsForEachHost = new ConcurrentBag<Proportion>();

            await TaskParalllel.ForEachWithWorkers(hostsResponsibleForThisHash,
                async (host, itemNumber, cancelToken) =>
                {
                    Proportion proportionForThisHost = await RestClientHelper.GetAsync<Proportion>(
                        host.Uri,
                        "IncorrectPasswordFrequency/" +
                        Microsoft.Framework.WebEncoders.UrlEncoder.Default.UrlEncode(passwordHash),
                        timeout: timeout, cancellationToken: cancelToken);
                    proportionsForEachHost.Add(proportionForThisHost);
                },
                //e =>
                //{
                //    // FIXME -- what to do with exceptions?
                //},
                cancellationToken: cancellationToken);

            return new ClientsUpdatableFrequency(hostsResponsibleForThisHash, passwordHash, Proportion.GetLargest(proportionsForEachHost.ToArray()));
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

        public class ClientsUpdatableFrequency : IUpdatableFrequency
        {
            protected IncorrectPasswordFrequencyClient Client;
            protected List<RemoteHost> HostsResponsibleForThisHash;

            protected string HashAsString;

            public Proportion Proportion { get; protected set; }

            public async Task RecordObservationAsync(TimeSpan? timeout = null,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                await Client.RecordObservation(HashAsString, HostsResponsibleForThisHash, timeout, cancellationToken);
            }

            public ClientsUpdatableFrequency(List<RemoteHost> hostsResponsibleForThisHash,
                string hashAsString, Proportion proportion)
            {
                HostsResponsibleForThisHash = hostsResponsibleForThisHash;
                HashAsString = hashAsString;
                this.Proportion = proportion;
            }

        }

    }
}
