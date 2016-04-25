using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
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



    public class DbUserAccount : IUserAccount
    {
        protected string DbUserAccountId { get; set; }

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

        /// <summary>
        /// A decaying double with the amount of credits consumed against the credit limit
        /// used to offset IP blocking penalties.
        /// </summary>
        public double ConsumedCreditsLastValue { get; set; }

        public DateTime? ConsumedCreditsLastUpdatedUtc { get; set; }

        /// <summary>
        /// A decaying double with the amount of credits consumed against the credit limit
        /// used to offset IP blocking penalties.
        /// </summary>
        [NotMapped]
        public DecayingDouble ConsumedCredits
        {
            get { return new DecayingDouble(ConsumedCreditsLastValue, ConsumedCreditsLastUpdatedUtc); }
            set
            {
                ConsumedCreditsLastValue = value.ValueAtTimeOfLastUpdate;
                ConsumedCreditsLastUpdatedUtc = value.LastUpdatedUtc;
            }
        }


        private static readonly HashSet<string> TablesKnownToExist = new HashSet<string>();
        private static async Task<CloudTable> GetTableAsync(string tableName, CancellationToken? cancellationToken = null)
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
                await table.CreateIfNotExistsAsync(cancellationToken ?? default(CancellationToken));
                TablesKnownToExist.Add(tableName);
            }

            return table;
        }


        private const string TableName_SuccessfulLoginCookie = "StopGuessingSuccessfulLoginCookie";
        public async Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(string hashOfCookie, CancellationToken? cancellationToken = null)
        {
            // Retrieve a reference to the table.
            CloudTable table = await GetTableAsync(TableName_SuccessfulLoginCookie, cancellationToken);

            TableQuery<IncorrectPhaseTwoHashEntity> query = new TableQuery<IncorrectPhaseTwoHashEntity>().Where(TableQuery.CombineFilters(
                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableKeyEncoding.Encode(hashOfCookie)),
                    TableOperators.And,
                    TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableKeyEncoding.Encode(UsernameOrAccountId))));

            return (await query.CountAsync()) > 0;
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
        public async Task<bool> AddIncorrectPhase2HashAsync(string phase2Hash, DateTime? whenSeenUtc = null, CancellationToken? cancellationToken = null)
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
                    cancellationToken ?? default(CancellationToken))
            );

            return false;
        }

#pragma warning disable 1998
        public async Task<double> TryGetCreditAsync(IUserAccount userAccount, double amountRequested, DateTime timeOfRequestUtc, CancellationToken? cancellationToken)
#pragma warning restore 1998
        {
            if (double.IsNaN(amountRequested) ||  amountRequested <= 0)
            {
                // You can't request a negative amount and requesting nothing is free
                return 0;
            }
            double amountAvailable = Math.Min(0, userAccount.CreditLimit - userAccount.ConsumedCredits.GetValue(userAccount.CreditHalfLife, timeOfRequestUtc));
            double amountConsumed = Math.Min(amountRequested, amountAvailable);
            DecayingDouble amountRemaining = userAccount.ConsumedCredits.Subtract(userAccount.CreditHalfLife, new DecayingDouble(amountConsumed, timeOfRequestUtc));
            ConsumedCreditsLastUpdatedUtc = amountRemaining.LastUpdatedUtc;
            ConsumedCreditsLastValue = amountRemaining.ValueAtTimeOfLastUpdate;
            return amountConsumed;
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
            ConsumedCredits = new DecayingDouble(0, currentDateTimeUtc);
        }
        
    }
}
