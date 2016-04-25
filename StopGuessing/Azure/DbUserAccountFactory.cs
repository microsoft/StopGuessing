using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Entity;
using StopGuessing.Models;

namespace StopGuessing.Azure
{
    public class DbUserAccountStore : IUserAccountStore
    {
        private DbUserAccountContext context = new DbUserAccountContext();
        private readonly string _usernameOrAccountId;

        public DbUserAccountStore(string usernameOrAccountId)
        {
            _usernameOrAccountId = usernameOrAccountId;
        }

        public async Task<IUserAccount> LoadAsync(CancellationToken? cancellationToken)
        {
            return await context.DbUserAccounts.Where(u => u.UsernameOrAccountId == _usernameOrAccountId).FirstOrDefaultAsync();
        }

        public async Task SaveChangesAsync(CancellationToken? cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken ?? default(CancellationToken));
        }

        public void Dispose()
        {
            context?.Dispose();
        }
    }

    public class DbUserAccountFactory : IUserAccountFactory
    {             
        public IUserAccountStore Create(string usernameOrAccountId)
        {
            return new DbUserAccountStore(usernameOrAccountId);
        }

        public void Dispose()
        {            
        }
    }
}
