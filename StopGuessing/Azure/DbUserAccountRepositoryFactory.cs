using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Infrastructure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using StopGuessing.Models;

namespace StopGuessing.Azure
{
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


        public async Task<DbUserAccount> LoadAsync(string usernameOrAccountId, CancellationToken cancellationToken = default (CancellationToken))
        {
            return await _context.DbUserAccounts.Where(u => u.UsernameOrAccountId == usernameOrAccountId).FirstOrDefaultAsync();
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

    public class DbUserAccountRepositoryFactory : IUserAccountRepositoryFactory<DbUserAccount>
    {
        private readonly DbContextOptions<DbUserAccountContext> _options;

        public DbUserAccountRepositoryFactory()
        {
            _options = null;
        }

        public DbUserAccountRepositoryFactory(DbContextOptions<DbUserAccountContext> options)
        {
            _options = options;
        }

        public DbUserAccountRepositoryFactory(Action<DbContextOptionsBuilder<DbUserAccountContext>> optionsAction)
        {
            DbContextOptionsBuilder<DbUserAccountContext> optionsBuilder = new DbContextOptionsBuilder<DbUserAccountContext>();
            optionsAction.Invoke(optionsBuilder);
            _options = optionsBuilder.Options;
        }

        public DbUserAccountRepository CreateDbUserAccountRepository()
        {
            return _options != null ? new DbUserAccountRepository(_options) : new DbUserAccountRepository();
        }

        public IRepository<string, DbUserAccount> Create()
        {
            return CreateDbUserAccountRepository();
        }

        public void Dispose()
        {
        }
    }
}
