using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
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

        private TableRequestOptions getTableRequestOptions(int? timeLimitInMilliSeconds = null)
        {
            TableRequestOptions tableRequestOptions = new TableRequestOptions();
            if (timeLimitInMilliSeconds.HasValue)
            {
                tableRequestOptions.ServerTimeout = new TimeSpan(TimeSpan.TicksPerMillisecond * timeLimitInMilliSeconds.Value);
            }
            return tableRequestOptions;
        }

        private OperationContext getOperationContext()
        {
            OperationContext operationContext = new OperationContext();
            return operationContext;
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
                await table.CreateIfNotExistsAsync(getTableRequestOptions(), getOperationContext(),  cancellationToken);
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

            TableOperation retriveOperation =
                TableOperation.Retrieve<SuccessfulLoginCookieEntity>(TableKeyEncoding.Encode(hashOfCookie),
                    TableKeyEncoding.Encode(userAccount.UsernameOrAccountId));

            TableResult retrievedResult = await table.ExecuteAsync(retriveOperation, getTableRequestOptions(200), getOperationContext(), cancellationToken);

            return retrievedResult.Result != null;
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
                    whenSeenUtc)), getTableRequestOptions(), getOperationContext(), cancellationToken);
        }


        private const string TableName_RecentIncorrectPhase2Hashes = "StopGuessingIncorrectPhase2Hashes";
        public override async Task<bool> AddIncorrectPhaseTwoHashAsync(
            DbUserAccount userAccount,
            string phase2Hash, DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Retrieve a reference to the table.
            CloudTable table = await GetTableAsync(TableName_RecentIncorrectPhase2Hashes, cancellationToken);

            TableOperation retriveOperation =
                TableOperation.Retrieve<IncorrectPhaseTwoHashEntity>(TableKeyEncoding.Encode(phase2Hash),
                    TableKeyEncoding.Encode(userAccount.UsernameOrAccountId));

            TableResult retrievedResult = await table.ExecuteAsync(retriveOperation, getTableRequestOptions(200), getOperationContext(), cancellationToken);

            if (retrievedResult.Result != null)
                return true;

            // Write the hash to azure tables in the background.
            TaskHelper.RunInBackground(
                table.ExecuteAsync(
                    TableOperation.InsertOrReplace(new IncorrectPhaseTwoHashEntity(userAccount.UsernameOrAccountId, phase2Hash, whenSeenUtc)),
                    getTableRequestOptions(), getOperationContext(), cancellationToken)
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
