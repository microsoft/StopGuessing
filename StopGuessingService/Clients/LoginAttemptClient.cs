using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.Models;

namespace StopGuessing.Clients
{
    public class LoginAttemptClient
    {
        private LoginAttemptController _localLoginAttemptController;
        private readonly IDistributedResponsibilitySet<RemoteHost> _responsibleHosts;
        public LoginAttemptClient(IDistributedResponsibilitySet<RemoteHost> responsibleHosts)
        {
            _responsibleHosts = responsibleHosts;
        }

        public void SetLoginAttemptController(LoginAttemptController loginAttemptController)
        {
            _localLoginAttemptController = loginAttemptController;
        }


        public async Task<LoginAttempt> PutLoginAttemptAsync(string passwordProvidedByClient, LoginAttempt loginAttempt, CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost hostResponsibleForClientIp =
                _responsibleHosts.FindMemberResponsible(loginAttempt.AddressOfClientInitiatingRequest.ToString());

            if (hostResponsibleForClientIp.IsLocalHost)
            {
                return await _localLoginAttemptController.PutAsync(loginAttempt.ToUniqueKey(), loginAttempt, passwordProvidedByClient, cancellationToken);
            }
            else
            {
                return await RestClientHelper.PutAsync<LoginAttempt>(hostResponsibleForClientIp.Uri,
                    "/api/LoginAttempt/" + Uri.EscapeUriString(loginAttempt.ToUniqueKey()), new Object[]
                    {
                        new KeyValuePair<string, string>("passwordProvidedByClient", passwordProvidedByClient),
                        new KeyValuePair<string, LoginAttempt>("loginAttempt", loginAttempt)
                    }, cancellationToken);
            }
        }

        public async Task UpdateLoginAttemptOutcomesAsync(List<LoginAttempt> loginAttemptsWithUpdatedOutcomes,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // For each IP that has records which can now be separated from likely typo to not a typo, 
            // update that IP's login records for this account.  If the IP is the same IP as the client
            // of this request, we'll want to wait for the update before proceeeding, since it may
            // effect the rest of our calculations about whether to allow this request to proceed.
            foreach (IGrouping<RemoteHost, LoginAttempt> loginsForIp in
                loginAttemptsWithUpdatedOutcomes.ToLookup(attempt => _responsibleHosts.FindMemberResponsible(attempt.AddressOfClientInitiatingRequest.ToString())) )
            {
                RemoteHost hostResponsible = loginsForIp.Key;
                if (hostResponsible.IsLocalHost)
                {
                    await _localLoginAttemptController.UpdateLoginAttemptOutcomesAsync(loginAttemptsWithUpdatedOutcomes, cancellationToken);
                }
                else
                {
                    // ReSharper disable once UnusedVariable
                    Task dontwaitforme = RestClientHelper.PostAsync(hostResponsible.Uri, "/api/LoginAttempt",
                        new Object[]
                        {
                            new KeyValuePair<string, IEnumerable<LoginAttempt>>("loginAttemptsWithUpdatedOutcomes",
                                loginAttemptsWithUpdatedOutcomes)
                        }, cancellationToken);
                    // Kick off an asynchronous task to update the records for this IP so that the
                    // typo/nontypo status is updated.
                }
            }
        }
    }
}
