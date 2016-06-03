using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace StopGuessing.AccountStorage.Sql
{
    /// <summary>
    /// Used for storing into an azure table a record that maps a account identifier to sets of incorrect
    /// passwords that have been submitted for that account.
    /// </summary>
    public class IncorrectPhaseTwoHashEntity : TableEntity
    {
        public string UsernameOrAccountId => TableKeyEncoding.Decode(RowKey);
        public string HashValue => TableKeyEncoding.Decode(PartitionKey);
        public DateTime LastSeenUtc { get; set; }

        public IncorrectPhaseTwoHashEntity()
        {
        }

        public IncorrectPhaseTwoHashEntity(string usernameOrAccountId, string hashValue, DateTime? lastSeenUtc = null)
        {
            PartitionKey = TableKeyEncoding.Encode(hashValue);
            RowKey = TableKeyEncoding.Encode(usernameOrAccountId);
            LastSeenUtc = lastSeenUtc ?? DateTime.UtcNow;
        }
    }
}
