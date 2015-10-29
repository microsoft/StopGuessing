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
    /// <summary>
    /// A client class for accessing LoginAttempt records locally or remotely.
    /// </summary>
    public class LoginAttemptClient
    {
        const int NumberOfRedundentHostsToCacheEachLoginAttempt = 3; // FIXME -- config
        private const int TimeoutMs = 500;

        private LoginAttemptController _localLoginAttemptController;
        private readonly IDistributedResponsibilitySet<RemoteHost> _responsibleHosts;
        public LoginAttemptClient(IDistributedResponsibilitySet<RemoteHost> responsibleHosts)
        {
            _responsibleHosts = responsibleHosts;
        }

        public void SetLocalLoginAttemptController(LoginAttemptController loginAttemptController)
        {
            _localLoginAttemptController = loginAttemptController;
        }

        public List<RemoteHost> GetServersResponsibleForCachingALoginAttempt(string key)
        {
            return _responsibleHosts.FindMembersResponsible(key, NumberOfRedundentHostsToCacheEachLoginAttempt);
        }

        public List<RemoteHost> GetServersResponsibleForCachingALoginAttempt(LoginAttempt attempt)
        {
            return GetServersResponsibleForCachingALoginAttempt(attempt.AddressOfClientInitiatingRequest.ToString());
        }


        /// <summary>
        /// Add a new login attempt via a REST PUT.  If the 
        /// </summary>
        /// <param name="passwordProvidedByClient"></param>
        /// <param name="loginAttempt"></param>
        /// <param name="serversResponsibleForCachingThisLoginAttempt"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<LoginAttempt> PutAsync(LoginAttempt loginAttempt,
            string passwordProvidedByClient = null,
            List<RemoteHost> serversResponsibleForCachingThisLoginAttempt = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (serversResponsibleForCachingThisLoginAttempt == null)
            {
                serversResponsibleForCachingThisLoginAttempt = GetServersResponsibleForCachingALoginAttempt(loginAttempt);
            }

            return await RestClientHelper.TryServersUntilOneResponds(
                serversResponsibleForCachingThisLoginAttempt,
                new TimeSpan(0, 0, 0, 0, TimeoutMs),
                async (server, timeout) => server.IsLocalHost
                    ? await
                        _localLoginAttemptController.PutAsync(loginAttempt, passwordProvidedByClient,
                            serversResponsibleForCachingThisLoginAttempt,
                            cancellationToken)
                    : await RestClientHelper.PutAsync<LoginAttempt>(server.Uri,
                        "/api/LoginAttempt/" + Uri.EscapeUriString(loginAttempt.UniqueKey), new Object[]
                        {
                            new KeyValuePair<string, string>("passwordProvidedByClient", passwordProvidedByClient),
                            new KeyValuePair<string, LoginAttempt>("loginAttempt", loginAttempt),
                            new KeyValuePair<string, List<RemoteHost>>("serversResponsibleForCachingThisLoginAttempt", serversResponsibleForCachingThisLoginAttempt)
                        },
                        timeout,
                        cancellationToken), cancellationToken);
        }

        public async Task<LoginAttempt> PutCacheOnlyAsync(LoginAttempt loginAttempt,
            RemoteHost server,
            TimeSpan? timeout,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await RestClientHelper.PutAsync<LoginAttempt>(server.Uri,
                "/api/LoginAttempt/" + Uri.EscapeUriString(loginAttempt.UniqueKey), new Object[]
                {
                    new KeyValuePair<string, LoginAttempt>("loginAttempt", loginAttempt),
                    new KeyValuePair<string, bool>("cacheOnly", true),
                },
                timeout,
                cancellationToken);
        }

        public void PutCacheOnlyBackground(LoginAttempt loginAttempt,
            RemoteHost server,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Task.Run(() => 
                PutCacheOnlyAsync(loginAttempt, server, timeout, cancellationToken), cancellationToken);
        }


        /// <summary>
        /// For each IP that has records which can now be separated from likely typo to not a typo, 
        /// update that IP's LoginAttempt records for this account.
        /// </summary>
        /// <param name="loginAttemptsWithUpdatedOutcomes"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task UpdateLoginAttemptOutcomesAsync(
                List<LoginAttempt> loginAttemptsWithUpdatedOutcomes,
                TimeSpan? timeout = null,
                CancellationToken cancellationToken = default(CancellationToken))
        {
            // Group LoginAttempts by the host that is responsible for that is responsbile for storing and maintaining
            // these records.  (We partition based on the client IP in the LoginAttempt)
            Dictionary<RemoteHost, List<LoginAttempt>> serverToLoginAttemptsThatNeedToBeUpdatedInItsCache = 
                new Dictionary<RemoteHost, List<LoginAttempt>>();
            foreach (LoginAttempt loginAttempt in loginAttemptsWithUpdatedOutcomes)
            {
                foreach (RemoteHost server in GetServersResponsibleForCachingALoginAttempt(loginAttempt))
                {
                    if (!serverToLoginAttemptsThatNeedToBeUpdatedInItsCache.ContainsKey(server))
                    {
                        serverToLoginAttemptsThatNeedToBeUpdatedInItsCache[server] = new List<LoginAttempt>();
                    }
                    serverToLoginAttemptsThatNeedToBeUpdatedInItsCache[server].Add(loginAttempt);
                }
            }
            foreach (RemoteHost server in serverToLoginAttemptsThatNeedToBeUpdatedInItsCache.Keys) {
                // If the host is this host, we can call the local controller
                if (server.IsLocalHost && _localLoginAttemptController != null)
                {
                    await
                        _localLoginAttemptController.UpdateLoginAttemptOutcomesAsync(loginAttemptsWithUpdatedOutcomes,
                            cancellationToken);
                }
                else
                {
                    // Kick off a remote request for this host's records in the background.
                    RestClientHelper.PostBackground(server.Uri, "/api/LoginAttempt",
                        new Object[]
                        {
                            new KeyValuePair<string, IEnumerable<LoginAttempt>>(
                                "loginAttemptsWithUpdatedOutcomes",
                                loginAttemptsWithUpdatedOutcomes)
                        }, timeout, cancellationToken);
                }
            }
        }

        public void UpdateLoginAttemptOutcomesInBackground(
            List<LoginAttempt> loginAttemptsWithUpdatedOutcomes,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Task.Run( () => UpdateLoginAttemptOutcomesAsync(loginAttemptsWithUpdatedOutcomes,
                timeout, cancellationToken), cancellationToken);
        }

    }
}
