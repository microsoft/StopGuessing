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
        /// to fetch a UserAccount records by its usernameOrAccountId.
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
        /// When a user has provided the correct password for an account, use it to decrypt the key that stores
        /// previous failed password attempts, use that key to decrypt that passwords used in those attempts,
        /// and determine whether they passwords were incorrect because they were typos--passwords similar to,
        /// but a small edit distance away from, the correct password.
        /// </summary>
        /// <param name="usernameOrAccountId">The username or account ID of the account for which the client has authenticated using the correct password.</param>
        /// <param name="correctPassword">The correct password provided by the client.</param>
        /// <param name="phase1HashOfCorrectPassword">The phase 1 hash of the correct password
        /// (we could re-derive this, the hash should be expensive to calculate and so we don't want to replciate the work unnecessarily.)</param>
        /// <param name="ipAddressToExcludeFromAnalysis">This is used to prevent the analysis fro examining LoginAttempts from this IP.
        /// We use it because it's more efficient to perform the analysis for that IP as part of the process of evaluting whether
        /// that IP should be blocked or not.</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        /// <returns>The number of LoginAttempts updated as a result of the analyis.</returns>
        public async Task UpdateOutcomesUsingTypoAnalysisAsync(string usernameOrAccountId, string correctPassword,
            byte[] phase1HashOfCorrectPassword, System.Net.IPAddress ipAddressToExcludeFromAnalysis,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(usernameOrAccountId);
            if (host.IsLocalHost)
            {
                await _userAccountController.UpdateOutcomesUsingTypoAnalysisAsync(usernameOrAccountId, correctPassword,
                    phase1HashOfCorrectPassword, ipAddressToExcludeFromAnalysis, cancellationToken);
            }
            else
            {
                await RestClientHelper.PostAsync(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(usernameOrAccountId), new Object[]
                    {
                        new KeyValuePair<string, string>("correctPassword", correctPassword),
                        new KeyValuePair<string, byte[]>("phase1HashOfCorrectPassword", phase1HashOfCorrectPassword),
                        new KeyValuePair<string, System.Net.IPAddress>("ipAddressToExcludeFromAnalysis",
                            ipAddressToExcludeFromAnalysis),
                    }, cancellationToken);
            }
        }


        public void UpdateOutcomesUsingTypoAnalysisInBackground(string usernameOrAccountId, string correctPassword,
            byte[] phase1HashOfCorrectPassword, System.Net.IPAddress ipAddressToExcludeFromAnalysis,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Task.Run(() => UpdateOutcomesUsingTypoAnalysisAsync(usernameOrAccountId, correctPassword,
                    phase1HashOfCorrectPassword,ipAddressToExcludeFromAnalysis, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Update to UserAccount record to incoroprate what we've learned from a LoginAttempt.
        /// 
        /// If the login attempt was successful (Outcome==CrednetialsValid) then we will want to
        /// track the cookie used by the client as we're more likely to trust this client in the future.
        /// If the login attempt was a failure, we'll want to add this attempt to the length-limited
        /// sequence of faield login attempts.
        /// </summary>
        /// <param name="usernameOrAccountId">The username or account id that uniquely identifies the account to update.</param>
        /// <param name="attempt">The attempt to incorporate into the account's records</param>
        /// <param name="cancellationToken">To allow the async call to be cancelled, such as in the event of a timeout.</param>
        public async Task UpdateForNewLoginAttemptAsync(string usernameOrAccountId, LoginAttempt attempt,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(usernameOrAccountId);
            if (host.IsLocalHost)
            {
                await _userAccountController.UpdateForNewLoginAttemptAsync(usernameOrAccountId, attempt, cancellationToken);
            }
            else
            {
                await RestClientHelper.PostAsync(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(usernameOrAccountId), new Object[]
                    {
                        new KeyValuePair<string, LoginAttempt>("attempt", attempt)
                    }, cancellationToken);
            }
        }

        public void UpdateForNewLoginAttemptInBackground(string accountId, LoginAttempt attempt,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // ReSharper disable once UnusedVariable -- used to indicate background task
            Task.Run(() => UpdateForNewLoginAttemptAsync(accountId, attempt, cancellationToken), cancellationToken);
        }

    }
}
