using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace StopGuessing
{
    public interface IStableStore
    {
        Task WriteAccountAsync(UserAccount account, CancellationToken cancellationToken);
        Task<UserAccount> ReadAccountAsync(string usernameOrAccountId, CancellationToken cancellationToken);
        Task WriteLoginAttemptAsync(LoginAttempt attempt, CancellationToken cancellationToken);
        Task<LoginAttempt> ReadLoginAttemptAsync(string key, CancellationToken cancellationToken);
        Task<IEnumerable<LoginAttempt>> ReadMostRecentLoginAttemptsAsync(string usernameOrAccountId, int numberToRead, bool includeSuccesses = true,
            bool includeFailures = true, CancellationToken cancellationToken = default (CancellationToken));
        Task<IEnumerable<LoginAttempt>> ReadMostRecentLoginAttemptsAsync(System.Net.IPAddress clientIpAddress, int numberToRead, bool includeSuccesses = true, 
            bool includeFailures = true, CancellationToken cancellationToken = default(CancellationToken));
        Task<bool> IsIpAddressAlwaysPermittedAsync(System.Net.IPAddress clientIpAddress, CancellationToken cancellationToken);
    }


}
