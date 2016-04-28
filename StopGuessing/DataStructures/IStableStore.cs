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

    public class MemoryOnlyUserAccountRepository : IRepository<String, IUserAccount> // IStableStoreContext<string, UserAccount>
    {
        private ConcurrentDictionary<string, IUserAccount> _store;

        public MemoryOnlyUserAccountRepository(ConcurrentDictionary<string, IUserAccount> store)
        {
            _store = store;
        }

#pragma warning disable 1998
        public async Task<IUserAccount> LoadAsync(string usernameOrAccountId, CancellationToken? cancellationToken)
#pragma warning restore 1998
        {
            cancellationToken?.ThrowIfCancellationRequested();
            IUserAccount result;
            _store.TryGetValue(usernameOrAccountId, out result);
            return result;
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

    public class MemoryOnlyUserAccountFactory : IFactory<IRepository<string, IUserAccount>>
    {
        private readonly ConcurrentDictionary<string, IUserAccount> _store = new ConcurrentDictionary<string, IUserAccount>();

        public IRepository<string, IUserAccount> Create()
        {
            return new MemoryOnlyUserAccountRepository(_store);
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
