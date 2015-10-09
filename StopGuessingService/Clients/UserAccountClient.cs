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
    

    public async Task<bool> TryGetCreditAsync(string accountId, float amountToGet = 1f)
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
                    });
            }
        }

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


        public async Task AddDeviceCookieFromSuccessfulLoginAsync(string accountId, string cookie, CancellationToken cancellationToken)
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(accountId);
            if (host.IsLocalHost)
            {
                await _userAccountController.AddDeviceCookieFromSuccessfulLoginAsync(accountId, cookie, cancellationToken);
            }
            else
            {
                await RestClientHelper.PostAsync(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(accountId), new Object[]
                    {
                        new KeyValuePair<string, string>("cookie", cookie)
                    }, cancellationToken);
            }
        }

        public void AddDeviceCookieFromSuccessfulLoginInBackground(string accountId, string cookie)
        {
            // ReSharper disable once UnusedVariable -- used to indicate background task
            Task dontwaitforme = AddDeviceCookieFromSuccessfulLoginAsync(accountId, cookie, default(CancellationToken));
        }

        public async Task AddLoginAttemptFailureAsync(string accountId, LoginAttempt failedLoginAttempt, CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(accountId);
            if (host.IsLocalHost)
            {
                await _userAccountController.AddLoginAttemptFailureAsync(accountId, failedLoginAttempt, cancellationToken);
            }
            else
            {
                await RestClientHelper.PostAsync(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(accountId), new Object[]
                    {
                        new KeyValuePair<string, LoginAttempt>("failedLoginAttempt", failedLoginAttempt)
                    }, cancellationToken);
            }
        }

        public void AddLoginAttemptFailureInBackground(string accountId, LoginAttempt failedLoginAttempt)
        {
            // ReSharper disable once UnusedVariable -- used to indicate background task
            Task dontwaitforme = AddLoginAttemptFailureAsync(accountId, failedLoginAttempt);
        }


        public async Task AddHashOfRecentIncorrectPasswordAsync(string accountId, byte[] phase2HashOfProvidedPassword, CancellationToken cancellationToken = default(CancellationToken))
        {
            RemoteHost host = _responsibleHosts.FindMemberResponsible(accountId);
            if (host.IsLocalHost)
            {
                await _userAccountController.AddHashOfRecentIncorrectPasswordAsync(accountId, phase2HashOfProvidedPassword, cancellationToken);
            }
            else
            {
                await RestClientHelper.PostAsync(host.Uri,
                    "/api/UserAccount/" + Uri.EscapeUriString(accountId), new Object[]
                    {
                        new KeyValuePair<string, byte[]>("phase2HashOfProvidedPassword", phase2HashOfProvidedPassword)
                    }, cancellationToken);
            }
        }
    

        public void AddHashOfRecentIncorrectPasswordInBackground(string accountId, byte[] phase2HashOfProvidedPassword)
        {
            // ReSharper disable once UnusedVariable -- Used to indicate background task
            Task dontwaitforme = AddHashOfRecentIncorrectPasswordAsync(accountId, phase2HashOfProvidedPassword);
        }


    }
}
