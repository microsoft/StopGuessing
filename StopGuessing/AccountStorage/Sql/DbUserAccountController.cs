using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Infrastructure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table.Queryable;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.Utilities;

namespace StopGuessing.AccountStorage.Sql
{
    public class DbUserAccountControllerFactory : IUserAccountControllerFactory<DbUserAccount>
    {
        protected readonly CloudStorageAccount CloudStorageAccountForTables;
        protected readonly DbContextOptions<DbUserAccountContext> DbOptions;

        public DbUserAccountControllerFactory(CloudStorageAccount cloudStorageAccount, Action<DbContextOptionsBuilder> optionsAction)
        {
            CloudStorageAccountForTables = cloudStorageAccount;
            DbContextOptionsBuilder<DbUserAccountContext> optionsBuilder = new DbContextOptionsBuilder<DbUserAccountContext>();
            optionsAction.Invoke(optionsBuilder);
            DbOptions = optionsBuilder.Options;
        }

        public DbUserAccountControllerFactory(CloudStorageAccount cloudStorageAccount, DbContextOptions<DbUserAccountContext> dbOptions)
        {
            CloudStorageAccountForTables = cloudStorageAccount;
            DbOptions = dbOptions;
        }

        public DbUserAccountController CreateDbUserAccountController()
        {
            return new DbUserAccountController(CloudStorageAccountForTables, DbOptions);
        }

        public IUserAccountController<DbUserAccount> Create()
        {
            return new DbUserAccountController(CloudStorageAccountForTables, DbOptions);
        }
    }

    public class DbUserAccountController : UserAccountController<DbUserAccount>
    {
        protected CloudStorageAccount CloudStorageAccountForTables;
        protected readonly DbContextOptions<DbUserAccountContext> DbOptions;

        private static readonly ConcurrentBag<string> TablesKnownToExist = new ConcurrentBag<string>();

        public DbUserAccountController(CloudStorageAccount cloudStorageAccount, DbContextOptions<DbUserAccountContext> dbOptions)
        {
            CloudStorageAccountForTables = cloudStorageAccount;
            DbOptions = dbOptions;
        }

        public async Task<CloudTable> GetTableAsync(string tableName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Create the table client.
            CloudTableClient tableClient = CloudStorageAccountForTables.CreateCloudTableClient();

            // Retrieve a reference to the table.
            CloudTable table = tableClient.GetTableReference(tableName);

            // Create the table if it doesn't exist. // remove to optimize
            if (!TablesKnownToExist.Contains(tableName))
            {
                await table.CreateIfNotExistsAsync(cancellationToken);
                TablesKnownToExist.Add(tableName);
            }

            return table;
        }

        public DbUserAccount Create(
            string usernameOrAccountId,
            string password = null,
            int numberOfIterationsToUseForHash = 0,
            string passwordHashFunctionName = null)
        {
            DbUserAccount account = new DbUserAccount()
            {
                DbUserAccountId = usernameOrAccountId
            };

            Initialize(account, password, numberOfIterationsToUseForHash, passwordHashFunctionName);
            
            return account;
        }


        private const string TableName_SuccessfulLoginCookie = "StopGuessingSuccessfulLoginCookie";
        public override async Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
            DbUserAccount userAccount,
            string hashOfCookie,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Retrieve a reference to the table.
            CloudTable table = await GetTableAsync(TableName_SuccessfulLoginCookie, cancellationToken);

            //TableQuery<IncorrectPhaseTwoHashEntity> query = new TableQuery<IncorrectPhaseTwoHashEntity>().Where(TableQuery.CombineFilters(
            //        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableKeyEncoding.Encode(hashOfCookie)),
            //        TableOperators.And,
            //        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableKeyEncoding.Encode(UsernameOrAccountId))));

            var query =
                        (from e in table.CreateQuery<SuccessfulLoginCookieEntity>()
                         where e.PartitionKey == TableKeyEncoding.Encode(hashOfCookie) &&
                               e.RowKey == TableKeyEncoding.Encode(userAccount.UsernameOrAccountId)
                         select e).AsTableQuery();

            int count = 0;

            TableContinuationToken continuationToken = null;
            do
            {
                var partialResult = await query.ExecuteSegmentedAsync(continuationToken, cancellationToken);
                count += partialResult.Results.Count;
                continuationToken = partialResult.ContinuationToken;
            } while (continuationToken != null);

            return (count > 0);
        }

        public override async Task RecordHashOfDeviceCookieUsedDuringSuccessfulLoginAsync(
            DbUserAccount userAccount,
            string hashOfCookie,
            DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Retrieve a reference to the table.
            CloudTable table = await GetTableAsync(TableName_SuccessfulLoginCookie, cancellationToken);

            await table.ExecuteAsync(
                TableOperation.InsertOrReplace(new SuccessfulLoginCookieEntity(userAccount.UsernameOrAccountId, hashOfCookie,
                    whenSeenUtc)), cancellationToken);
        }


        private const string TableName_RecentIncorrectPhase2Hashes = "StopGuessingIncorrectPhase2Hashes";
        public override async Task<bool> AddIncorrectPhaseTwoHashAsync(
            DbUserAccount userAccount,
            string phase2Hash, DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Retrieve a reference to the table.
            CloudTable table = await GetTableAsync(TableName_RecentIncorrectPhase2Hashes, cancellationToken);

            //TableQuery<IncorrectPhaseTwoHashEntity> query = new TableQuery<IncorrectPhaseTwoHashEntity>().Where(TableQuery.CombineFilters(
            //        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableKeyEncoding.Encode(phase2Hash)),
            //        TableOperators.And,
            //        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableKeyEncoding.Encode(UsernameOrAccountId)) ));

            var query =
            (from e in table.CreateQuery<IncorrectPhaseTwoHashEntity>()
             where e.PartitionKey == TableKeyEncoding.Encode(phase2Hash) &&
                   e.RowKey == TableKeyEncoding.Encode(userAccount.UsernameOrAccountId)
             select e).AsTableQuery();

            int count = 0;

            TableContinuationToken continuationToken = null;
            do
            {
                var partialResult = await query.ExecuteSegmentedAsync(continuationToken, cancellationToken);
                count += partialResult.Results.Count;
                continuationToken = partialResult.ContinuationToken;
            } while (continuationToken != null);

            if (count > 0)
            {
                // This phase2 hash already exists.
                return true;
            }

            // Write the hash to azure tables in the background.
            TaskHelper.RunInBackground(
                table.ExecuteAsync(
                    TableOperation.InsertOrReplace(new IncorrectPhaseTwoHashEntity(userAccount.UsernameOrAccountId, phase2Hash, whenSeenUtc)),
                    cancellationToken)
            );

            return false;
        }

        public override async Task<double> TryGetCreditAsync(
            DbUserAccount userAccount,
            double amountRequested,
            DateTime? timeOfRequestUtc = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (Double.IsNaN(amountRequested) || amountRequested <= 0)
            {
                // You can't request a negative amount and requesting nothing is free
                return 0;
            }

            DateTime timeOfRequestOrNowUtc = timeOfRequestUtc ?? DateTime.Now;

            double creditRetrieved = 0;
            
            using (var context = new DbUserAccountContext(DbOptions))
            {
                using (var dbContextTransaction = await context.Database.BeginTransactionAsync(cancellationToken))
                {
                    bool rolledBackDueToConcurrencyException = false;
                    do
                    {
                        try
                        {
                            DbUserAccountCreditBalance balance = await
                                context.DbUserAccountCreditBalances.Where(b => b.DbUserAccountId == userAccount.DbUserAccountId)
                                    .FirstOrDefaultAsync(cancellationToken: cancellationToken);

                            if (balance == null)
                            {
                                creditRetrieved = Math.Min(amountRequested, userAccount.CreditLimit);
                                double amountRemaining = userAccount.CreditLimit - creditRetrieved;
                                context.DbUserAccountCreditBalances.Add(
                                    new DbUserAccountCreditBalance()
                                    {
                                        DbUserAccountId = userAccount.DbUserAccountId,
                                        ConsumedCreditsLastUpdatedUtc = timeOfRequestOrNowUtc,
                                        ConsumedCreditsLastValue = amountRemaining
                                    });
                            }
                            else
                            {
                                double amountAvailable = Math.Max(0, userAccount.CreditLimit -
                                    DecayingDouble.Decay(balance.ConsumedCreditsLastValue, userAccount.CreditHalfLife,
                                    balance.ConsumedCreditsLastUpdatedUtc, timeOfRequestOrNowUtc));
                                if (amountAvailable > 0)
                                    creditRetrieved = Math.Min(amountRequested, amountAvailable);
                                double amountRemaining = amountAvailable - creditRetrieved;
                                balance.ConsumedCreditsLastValue = amountRemaining;
                                balance.ConsumedCreditsLastUpdatedUtc = timeOfRequestOrNowUtc;
                            }

                            await context.SaveChangesAsync(cancellationToken);

                            dbContextTransaction.Commit();
                        }
                        catch (DbUpdateConcurrencyException)
                        {
                            dbContextTransaction.Rollback();
                            rolledBackDueToConcurrencyException = true;
                        }
                    } while (rolledBackDueToConcurrencyException);
                }
            }

            return creditRetrieved;
        }
    }
}
