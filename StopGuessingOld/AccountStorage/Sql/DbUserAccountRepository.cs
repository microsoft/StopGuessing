using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Infrastructure;
using StopGuessing.Interfaces;
using StopGuessing.Models;

namespace StopGuessing.AccountStorage.Sql
{
    /// <summary>
    /// An implementation of a repository for DbUserAccounts to allow these accounts
    /// to be read and stored into a database. 
    /// </summary>
    public class DbUserAccountRepository : IRepository<string, DbUserAccount>
    {
        private readonly DbUserAccountContext _context;


        public DbUserAccountRepository()
        {
            _context = new DbUserAccountContext();
        }

        public DbUserAccountRepository(DbContextOptions options)
        {
            _context = new DbUserAccountContext(options);
        }


        public async Task<DbUserAccount> LoadAsync(string usernameOrAccountId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _context.DbUserAccounts.Where(u => u.UsernameOrAccountId == usernameOrAccountId).FirstOrDefaultAsync(cancellationToken: cancellationToken);
        }

        public async Task AddAsync(DbUserAccount itemToAdd, CancellationToken cancellationToken = default(CancellationToken))
        {
            _context.DbUserAccounts.Add(itemToAdd);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        public void Dispose()
        {
            _context?.Dispose();
        }

    }
}
