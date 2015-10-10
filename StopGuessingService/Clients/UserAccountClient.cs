using StopGuessing.DataStructures;
using StopGuessing.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Controllers;

namespace StopGuessing.Clients
{
    public class UserAccountClient
    {
        private UserAccountController _userAccountController;
        private readonly IDistributedResponsibilitySet<RemoteHost> _responsibleHosts;

        public UserAccountClient(IDistributedResponsibilitySet<RemoteHost> responsibleHosts)
        {
            _responsibleHosts = responsibleHosts;
        }

        public void SetUserAccountController(UserAccountController userAccountController)
        {
            _userAccountController = userAccountController;
        }


        /// <summary>
        /// Calls UserAccountController.TryGetCreditAsync()
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="amountToGet"></param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        /// <returns></returns>
        public async Task<bool> TryGetCreditAsync(string accountId, float amountToGet = 1f, CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(accountId);

            if (host.IsLocalHost)
            {
                return await _userAccountController.TryGetCreditAsync(accountId, amountToGet);
            }
            else
            {
                return await RestClientHelper.PostAsync<bool>(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(accountId) + "/TryGetCredit", new Object[]
                    {
                        new KeyValuePair<string, float>("amountToGet", 1f)
                    }, cancellationToken);
            }
        }

        /// <summary>
        /// Calls UserAccountController.GetAsync(), via a REST GET request or directly (if the UserAccount is managed locally),
        /// to fetch a UserAccount records by its accountId.
        /// </summary>
        /// <param name="accountId">The unique ID of the account to fetch.</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        /// <returns>The account record that was retrieved.</returns>
        public async Task<UserAccount> GetAsync(string accountId, CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(accountId);
            if (host.IsLocalHost)
            {
                return await _userAccountController.GetAsync(accountId, cancellationToken);
            }
            else
            {
                string pathUri = "/api/UserAccount/" + Uri.EscapeUriString(accountId);
                return await RestClientHelper.GetAsync<UserAccount>(host.Uri, pathUri, cancellationToken: cancellationToken);
            }
        }

        /// <summary>
        /// Calls UserAccountController.PutAsync, via a REST PUT request or directly (if the UserAccount is managed locally),
        /// to store UserAccount to stable store and to the local cache of whichever machines are in charge of maintaining
        /// a copy in memory.
        /// </summary>
        /// <param name="account">The account to store.</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        /// <returns>The account record as stored.</returns>
        public async Task<UserAccount> PutAsync(UserAccount account, CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(account.UsernameOrAccountId);
            if (host.IsLocalHost)
            {
                return await _userAccountController.PutAsync(account.UsernameOrAccountId, account, cancellationToken);
            }
            else
            {
                return await RestClientHelper.PutAsync<UserAccount>(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(account.UsernameOrAccountId), new Object[]
                    {
                        new KeyValuePair<string, UserAccount>("account", account),
                    }, cancellationToken);
            }
        }

        /// <summary>
        /// Calls UserAccountController.UpdateOutcomesUsingTypoAnalysis, via a REST POST request or directly
        /// (if the UserAccount is managed locally).  This 
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="correctPassword"></param>
        /// <param name="phase1HashOfCorrectPassword"></param>
        /// <param name="ipAddressToExcludeFromAnalysis"></param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        /// <returns></returns>
        public async Task UpdateOutcomesUsingTypoAnalysisAsync(string accountId, string correctPassword,
            byte[] phase1HashOfCorrectPassword, System.Net.IPAddress ipAddressToExcludeFromAnalysis,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(accountId);
            if (host.IsLocalHost)
            {
                await _userAccountController.UpdateOutcomesUsingTypoAnalysisAsync(accountId, correctPassword,
                    phase1HashOfCorrectPassword, ipAddressToExcludeFromAnalysis, cancellationToken);
            }
            else
            {
                await RestClientHelper.PostAsync(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(accountId), new Object[]
                    {
                        new KeyValuePair<string, string>("correctPassword", correctPassword),
                        new KeyValuePair<string, byte[]>("phase1HashOfCorrectPassword", phase1HashOfCorrectPassword),
                        new KeyValuePair<string, System.Net.IPAddress>("ipAddressToExcludeFromAnalysis",
                            ipAddressToExcludeFromAnalysis),
                    }, cancellationToken);
            }
        }

        public void UpdateOutcomesUsingTypoAnalysisInBackground(string accountId, string correctPassword,
            byte[] phase1HashOfCorrectPassword, System.Net.IPAddress ipAddressToExcludeFromAnalysis)
        {
            // ReSharper disable once UnusedVariable
            Task dontwaitforme = UpdateOutcomesUsingTypoAnalysisAsync(accountId, correctPassword,
                    phase1HashOfCorrectPassword,ipAddressToExcludeFromAnalysis);
        }


        public async Task UpdateForNewLoginAttemptAsync(string accountId, LoginAttempt attempt,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(accountId);
            if (host.IsLocalHost)
            {
                await _userAccountController.UpdateForNewLoginAttemptAsync(accountId, attempt, cancellationToken);
            }
            else
            {
                await RestClientHelper.PostAsync(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(accountId), new Object[]
                    {
                        new KeyValuePair<string, LoginAttempt>("attempt", attempt)
                    }, cancellationToken);
            }
        }

        public void UpdateForNewLoginAttemptInBackground(string accountId, LoginAttempt attempt,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // ReSharper disable once UnusedVariable -- used to indicate background task
            Task dontwaitforme = UpdateForNewLoginAttemptAsync(accountId, attempt, cancellationToken);
        }

    }
}
