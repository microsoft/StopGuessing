using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace StopGuessing.AccountStorage.Sql
{

    public class SuccessfulLoginCookieEntity : TableEntity
    {
        public string UsernameOrAccountId => TableKeyEncoding.Decode(RowKey);
        public string HashedValue => TableKeyEncoding.Decode(PartitionKey);
        public DateTime LastSeenUtc { get; set; }

        public SuccessfulLoginCookieEntity()
        {
        }

        public SuccessfulLoginCookieEntity(string usernameOrAccountId, string hashOfCookie, DateTime? lastSeenUtc = null)
        {
            PartitionKey = TableKeyEncoding.Encode(hashOfCookie);
            RowKey = TableKeyEncoding.Encode(usernameOrAccountId);
            LastSeenUtc = lastSeenUtc ?? DateTime.UtcNow;
        }
    }

}
