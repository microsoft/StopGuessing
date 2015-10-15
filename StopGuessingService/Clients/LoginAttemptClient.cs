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



        /// <summary>
        /// Add a new login attempt via a REST PUT.  If the 
        /// </summary>
        /// <param name="passwordProvidedByClient"></param>
        /// <param name="loginAttempt"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<LoginAttempt> PutAsync(LoginAttempt loginAttempt, string passwordProvidedByClient = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost hostResponsibleForClientIp =
                _responsibleHosts.FindMemberResponsible(loginAttempt.AddressOfClientInitiatingRequest.ToString());

            if (hostResponsibleForClientIp.IsLocalHost && _localLoginAttemptController != null)
            {
                return await _localLoginAttemptController.PutAsync(loginAttempt, passwordProvidedByClient, cancellationToken);
            }
            else
            {
                return await RestClientHelper.PutAsync<LoginAttempt>(hostResponsibleForClientIp.Uri,
                    "/api/LoginAttempt/" + Uri.EscapeUriString(loginAttempt.UniqueKey), new Object[]
                    {
                        new KeyValuePair<string, string>("passwordProvidedByClient", passwordProvidedByClient),
                        new KeyValuePair<string, LoginAttempt>("loginAttempt", loginAttempt)
                    }, cancellationToken);
            }
        }

        /// <summary>
        // For each IP that has records which can now be separated from likely typo to not a typo, 
        // update that IP's LoginAttempt records for this account.
        /// </summary>
        /// <param name="loginAttemptsWithUpdatedOutcomes"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task UpdateLoginAttemptOutcomesAsync(List<LoginAttempt> loginAttemptsWithUpdatedOutcomes,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Group LoginAttempts by the host that is responsible for that is responsbile for storing and maintaining
            // these records.  (We partition based on the client IP in the LoginAttempt) 
            foreach (IGrouping<RemoteHost, LoginAttempt> loginsForIp in
                loginAttemptsWithUpdatedOutcomes.ToLookup(attempt => _responsibleHosts.FindMemberResponsible(attempt.AddressOfClientInitiatingRequest.ToString())) )
            {
                RemoteHost hostResponsible = loginsForIp.Key;
                // If the host is this host, we can call the local controller
                if (hostResponsible.IsLocalHost && _localLoginAttemptController != null)
                {
                    await
                        _localLoginAttemptController.UpdateLoginAttemptOutcomesAsync(loginAttemptsWithUpdatedOutcomes,
                            cancellationToken);
                }
                else
                {
                    // Kick off a remote request for this record in the background.
                    // FUTURE -- should get timeout and re-try if IP is not availble.
                    Task donotwaitforthisbackgroundtask =
                        Task.Run(() =>
                            RestClientHelper.PostAsync(hostResponsible.Uri, "/api/LoginAttempt",
                                new Object[]
                                {
                                    new KeyValuePair<string, IEnumerable<LoginAttempt>>(
                                        "loginAttemptsWithUpdatedOutcomes",
                                        loginAttemptsWithUpdatedOutcomes)
                                }, cancellationToken), cancellationToken);
                }
            }
        }

        public void UpdateLoginAttemptOutcomesInBackground(List<LoginAttempt> loginAttemptsWithUpdatedOutcomes,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Task.Run( () => UpdateLoginAttemptOutcomesAsync(loginAttemptsWithUpdatedOutcomes, cancellationToken), cancellationToken);
        }

    }
}
