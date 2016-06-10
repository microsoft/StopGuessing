using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.Interfaces;
using StopGuessing.Models;

namespace StopGuessing.Clients
{
    public interface ILoginAttemptClient : ILoginAttemptController
    {
    }

    /// <summary>
    /// A client class for accessing LoginAttempt records locally or remotely.
    /// </summary>
    public class LoginAttemptClient<TUserAccount> : ILoginAttemptClient where TUserAccount : IUserAccount
    {
        int NumberOfRedundentHostsToCacheEachLoginAttempt => Math.Min(3, _responsibleHosts.Count); // FUTURE -- use configuration file value

        private LoginAttemptController<TUserAccount> _localLoginAttemptController;
        private readonly IDistributedResponsibilitySet<RemoteHost> _responsibleHosts;
        private RemoteHost _localHost;

        public LoginAttemptClient(IDistributedResponsibilitySet<RemoteHost> responsibleHosts, RemoteHost localHost)
        {
            _localHost = localHost;
            _responsibleHosts = responsibleHosts;
        }

        public void SetLocalLoginAttemptController(LoginAttemptController<TUserAccount> loginAttemptController)
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

        private TimeSpan DefaultTimeout { get; } = new TimeSpan(0, 0, 0, 0, 500); // FUTURE use configuration value

        /// <summary>
        /// Add a new login attempt via a REST PUT.  If the 
        /// </summary>
        /// <param name="passwordProvidedByClient"></param>
        /// <param name="loginAttempt"></param>
        /// <param name="serversResponsibleForCachingThisLoginAttempt"></param>
        /// <param name="timeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<LoginAttempt> PutAsync(LoginAttempt loginAttempt,
            string passwordProvidedByClient = null,
            List<RemoteHost> serversResponsibleForCachingThisLoginAttempt = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (serversResponsibleForCachingThisLoginAttempt == null)
            {
                serversResponsibleForCachingThisLoginAttempt = GetServersResponsibleForCachingALoginAttempt(loginAttempt);
            }

            return await RestClientHelper.TryServersUntilOneRespondsWithResult(
                serversResponsibleForCachingThisLoginAttempt,
                timeout ?? DefaultTimeout,
                async (server, localTimeout) => 
                    await RestClientHelper.PutAsync<LoginAttempt>(server.Uri,
                        "/api/LoginAttempt/" + Uri.EscapeUriString(loginAttempt.UniqueKey), new Object[]
                        {
                            new KeyValuePair<string, LoginAttempt>("loginAttempt", loginAttempt),
                            new KeyValuePair<string, string>("passwordProvidedByClient", passwordProvidedByClient),
                            new KeyValuePair<string, List<RemoteHost>>("serversResponsibleForCachingThisLoginAttempt",
                                serversResponsibleForCachingThisLoginAttempt)
                        },
                        localTimeout,
                        cancellationToken),
            cancellationToken);
        }

        public async Task<LoginAttempt> PutAsync(LoginAttempt loginAttempt,
            string passwordProvidedByClient = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await PutAsync(loginAttempt,passwordProvidedByClient, null, null, cancellationToken);
        }



    }
}
