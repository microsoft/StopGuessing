using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace StopGuessing
{

    public interface IStableStoreContext<in TId, TValue>
    {
        Task<TValue> ReadAsync(TId id, CancellationToken cancellationToken = default(CancellationToken));

        Task WriteNewAsync(TId id, TValue value, CancellationToken cancellationToken = default(CancellationToken));

        Task SaveChangesAsync(TId id, TValue value, CancellationToken cancellationToken = default(CancellationToken));
    }


    public interface IStableStoreFactory<in TId, TValue>
    {
        IStableStoreContext<TId, TValue> Get();
    }

    //public interface IUserAccountContextFactory : IStableStoreFactory<string, UserAccount>
    //{ }

    public class MemoryOnlyUserAccountFactory : IUserAccountFactory // IStableStoreContext<string, UserAccount>
    {
        private readonly ConcurrentDictionary<string, IUserAccount> _store = new ConcurrentDictionary<string,  IUserAccount>();

#pragma warning disable 1998
        public async Task<IUserAccount> LoadAsync(string usernameOrAccountId, CancellationToken? cancellationToken)
#pragma warning restore 1998
        {
            cancellationToken?.ThrowIfCancellationRequested();
            IUserAccount account = null;
            _store.TryGetValue(usernameOrAccountId, out account);
            return account;
        }

        public void Add(IUserAccount account)
        {
            _store[account.UsernameOrAccountId] = account;
        }

    }

    //public class MemoryOnlyAccountContextFactory : IUserAccountContextFactory
    //{
    //    private readonly MemoryOnlyAccountStore _memoryOnlyAccountStore = new MemoryOnlyAccountStore();
    //    public IStableStoreContext<string, UserAccount> Get()
    //    {
    //        return _memoryOnlyAccountStore;
    //    }
    //}


}
