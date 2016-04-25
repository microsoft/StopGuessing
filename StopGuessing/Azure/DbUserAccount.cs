using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure;
using Microsoft.Data.Entity;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using StopGuessing.Controllers;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;
using StopGuessing.Models;
using StopGuessing.Utilities;

namespace StopGuessing.Azure
{

    public class SuccessfulLoginCookieEntity : TableEntity
    {
        public string UsernameOrAccountId => TableKeyEncoding.Decode(RowKey);
        public string HashedValue => TableKeyEncoding.Decode(PartitionKey);
        public DateTime LastSeenUtc { get; set; }

        public SuccessfulLoginCookieEntity(string usernameOrAccountId, string hashOfCookie, DateTime? lastSeenUtc = null)
        {
            PartitionKey = TableKeyEncoding.Encode(hashOfCookie);
            RowKey = TableKeyEncoding.Encode(usernameOrAccountId);
            LastSeenUtc = lastSeenUtc ?? DateTime.UtcNow;
        }
    }

    public class IncorrectPhaseTwoHashEntity : TableEntity
    {
        public string UsernameOrAccountId => TableKeyEncoding.Decode(RowKey);
        public string HashValue => TableKeyEncoding.Decode(PartitionKey);
        public DateTime LastSeenUtc { get; set; }

        public IncorrectPhaseTwoHashEntity(string usernameOrAccountId, string hashValue, DateTime? lastSeenUtc = null)
        {
            PartitionKey = TableKeyEncoding.Encode(hashValue);
            RowKey = TableKeyEncoding.Encode(usernameOrAccountId);
            LastSeenUtc = lastSeenUtc ?? DateTime.UtcNow;
        }
    }


    public class DbUserAccountCreditBalance
    {
        public string DbUserAccountId { get; set; }

        /// <summary>
        /// A decaying double with the amount of credits consumed against the credit limit
        /// used to offset IP blocking penalties.
        /// </summary>
        public double ConsumedCreditsLastValue { get; set; }

        public DateTime? ConsumedCreditsLastUpdatedUtc { get; set; }
    }


    public class DbUserAccount : IUserAccount
    {
        public string DbUserAccountId { get; set; }

        [NotMapped]
        public string UsernameOrAccountId => DbUserAccountId;

        /// <summary>
        /// The salt is a random unique sequence of bytes that is included when the password is hashed (phase 1 of hashing)
        /// to ensure that attackers who might obtain the set of account hashes cannot hash a password once and then compare
        /// the hash against every account.
        /// </summary>
        public byte[] SaltUniqueToThisAccount { get; set; } =
            StrongRandomNumberGenerator.GetBytes(UserAccountController.DefaultSaltLength);

        /// <summary>
        /// The name of the (hopefully) expensive hash function used for the first phase of password hashing.
        /// </summary>
        public string PasswordHashPhase1FunctionName { get; set; } =
            ExpensiveHashFunctionFactory.DefaultFunctionName;

        /// <summary>
        /// The number of iterations to use for the phase 1 hash to make it more expensive.
        /// </summary>
        public int NumberOfIterationsToUseForPhase1Hash { get; set; } =
            UserAccountController.DefaultIterationsForPasswordHash;

        /// <summary>
        /// An EC public encryption symmetricKey used to store log about password failures, which can can only be decrypted when the user 
        /// enters her correct password, the expensive (phase1) hash of which is used to symmetrically encrypt the matching EC private symmetricKey.
        /// </summary>
        public byte[] EcPublicAccountLogKey { get; set; }

        /// <summary>
        /// The EC private symmetricKey encrypted with phase 1 (expensive) hash of the password
        /// </summary>        
        public byte[] EcPrivateAccountLogKeyEncryptedWithPasswordHashPhase1 { get; set; }

        /// <summary>
        /// The Phase2 password hash is the result of hasing the password (and salt) first with the expensive hash function to create a Phase1 hash,
        /// then hasing that Phase1 hash (this time without the salt) using SHA256 so as to make it unnecessary to store the
        /// phase1 hash in this record.  Doing so allows the Phase1 hash to be used as a symmetric encryption symmetricKey for the log. 
        /// </summary>
        public string PasswordHashPhase2 { get; set; }

        /// <summary>
        /// The account's credit limit for offsetting penalties for IP addresses from which
        /// the account has logged in successfully.
        /// </summary>
        public double CreditLimit { get; set; } =
            UserAccountController.DefaultCreditLimit;

        /// <summary>
        /// The half life with which used credits are removed from the system freeing up new credit
        /// </summary>
        public TimeSpan CreditHalfLife { get; set; } =
            new TimeSpan(TimeSpan.TicksPerHour * UserAccountController.DefaultCreditHalfLifeInHours);


        private static readonly HashSet<string> TablesKnownToExist = new HashSet<string>();
        private static async Task<CloudTable> GetTableAsync(string tableName,
            CancellationToken cancellationToken = default(CancellationToken))
        {

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("StorageConnectionString"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

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


        private const string TableName_SuccessfulLoginCookie = "StopGuessingSuccessfulLoginCookie";
        public async Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(string hashOfCookie,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Retrieve a reference to the table.
            CloudTable table = await GetTableAsync(TableName_SuccessfulLoginCookie, cancellationToken);

            TableQuery<IncorrectPhaseTwoHashEntity> query = new TableQuery<IncorrectPhaseTwoHashEntity>().Where(TableQuery.CombineFilters(
                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableKeyEncoding.Encode(hashOfCookie)),
                    TableOperators.And,
                    TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableKeyEncoding.Encode(UsernameOrAccountId))));

            return (await query.CountAsync(cancellationToken: cancellationToken)) > 0;
        }

        public void RecordHashOfDeviceCookieUsedDuringSuccessfulLogin(string hashOfCookie, DateTime? whenSeenUtc = null)
        {
            // Retrieve a reference to the table.
            CloudTable table = GetTableAsync(TableName_RecentIncorrectPhase2Hashes).Result;

            TaskHelper.RunInBackground(
                table.ExecuteAsync(
                    TableOperation.InsertOrReplace(new SuccessfulLoginCookieEntity(UsernameOrAccountId, hashOfCookie, whenSeenUtc)))
                );
        }

        private const string TableName_RecentIncorrectPhase2Hashes = "StopGuessingIncorrectPhase2Hashes";
        public async Task<bool> AddIncorrectPhase2HashAsync(string phase2Hash, DateTime? whenSeenUtc = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Retrieve a reference to the table.
            CloudTable table = await GetTableAsync(TableName_RecentIncorrectPhase2Hashes, cancellationToken);

            TableQuery<IncorrectPhaseTwoHashEntity> query = new TableQuery<IncorrectPhaseTwoHashEntity>().Where(TableQuery.CombineFilters(
                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableKeyEncoding.Encode(phase2Hash)),
                    TableOperators.And,
                    TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableKeyEncoding.Encode(UsernameOrAccountId)) ));

            if ((await query.CountAsync()) > 0)
            {
                // This phase2 hash already exists.
                return true;
            }

            // Write the hash to azure tables in the background.
            TaskHelper.RunInBackground(
                table.ExecuteAsync(
                    TableOperation.InsertOrReplace(new IncorrectPhaseTwoHashEntity(UsernameOrAccountId, phase2Hash, whenSeenUtc)),
                    cancellationToken)
            );

            return false;
        }

        public async Task<double> TryGetCreditAsync(double amountRequested, DateTime timeOfRequestUtc, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (double.IsNaN(amountRequested) ||  amountRequested <= 0)
            {
                // You can't request a negative amount and requesting nothing is free
                return 0;
            }

            double creditRetrieved = 0;

            using (var context = new DbUserAccountContext())
            {
                using (var dbContextTransaction = await context.Database.BeginTransactionAsync(cancellationToken))
                {
                    bool rolledBackDueToConcurrencyException = false;
                    do
                    {
                        try
                        {
                            DbUserAccountCreditBalance balance = await
                                context.DbUserAccountCreditBalances.Where(b => b.DbUserAccountId == DbUserAccountId)
                                    .FirstOrDefaultAsync(cancellationToken: cancellationToken);

                            if (balance == null)
                            {
                                creditRetrieved = Math.Min(amountRequested, CreditLimit);
                                double amountRemaining = CreditLimit - creditRetrieved;
                                context.DbUserAccountCreditBalances.Add(
                                    new DbUserAccountCreditBalance()
                                    {
                                        DbUserAccountId = DbUserAccountId,
                                        ConsumedCreditsLastUpdatedUtc = timeOfRequestUtc,
                                        ConsumedCreditsLastValue = amountRemaining
                                    });
                            }
                            else
                            {
                                double amountAvailable = Math.Min(0, CreditLimit - 
                                    DecayingDouble.Decay(balance.ConsumedCreditsLastValue, CreditHalfLife, 
                                    balance.ConsumedCreditsLastUpdatedUtc, timeOfRequestUtc));
                                if (amountAvailable > 0)
                                creditRetrieved = Math.Min(amountRequested, amountAvailable);
                                double amountRemaining = amountAvailable - creditRetrieved;
                                balance.ConsumedCreditsLastValue = amountRemaining;
                                balance.ConsumedCreditsLastUpdatedUtc = timeOfRequestUtc;
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


        public DbUserAccount()
        {
        }

        public DbUserAccount(
            string usernameOrAccountId,
            string password = null,
            DateTime? currentDateTimeUtc = null)
        {
            DbUserAccountId = usernameOrAccountId;
            if (password != null)
            {
                UserAccountController.SetPassword(this, password);
            }
        }
        
    }
}
