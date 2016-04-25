using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace StopGuessing
{


    //public interface IUserAccountContextFactory : IStableStoreFactory<string, UserAccount>
    //{ }

    public class MemoryOnlyUserAccountStore : IUserAccountStore // IStableStoreContext<string, UserAccount>
    {
        private readonly IUserAccount _account = null;

        public MemoryOnlyUserAccountStore(IUserAccount account)
        {
            _account = account;

        }

#pragma warning disable 1998
        public async Task<IUserAccount> LoadAsync(CancellationToken? cancellationToken)
#pragma warning restore 1998
        {
            cancellationToken?.ThrowIfCancellationRequested();
            return _account;
        }

#pragma warning disable 1998
        public async Task SaveChangesAsync(CancellationToken? cancellationToken)
#pragma warning disable 1998
        {
            cancellationToken?.ThrowIfCancellationRequested();
            return;
        }

        public void Dispose()
        {
            
        }

    }

    public class MemoryOnlyUserAccountFactory : IUserAccountFactory
    {
        private readonly ConcurrentDictionary<string, IUserAccount> _store = new ConcurrentDictionary<string, IUserAccount>();

        public IUserAccountStore Create(string usernameOrAccountId)
        {
            IUserAccount account;
            _store.TryGetValue(usernameOrAccountId, out account);
            return new MemoryOnlyUserAccountStore(account);
        }

        public void Add(IUserAccount account)
        {
            _store[account.UsernameOrAccountId] = account;
        }

        public void Dispose()
        {
        }

    }



}
