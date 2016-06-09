using System;
using Microsoft.EntityFrameworkCore;
using StopGuessing.Interfaces;

namespace StopGuessing.AccountStorage.Sql
{

    /// <summary>
    /// A factory for generating repositories that can read and write user accounts to an Azure database.
    /// </summary>
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
