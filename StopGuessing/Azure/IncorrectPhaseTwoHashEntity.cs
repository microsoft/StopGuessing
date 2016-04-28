using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace StopGuessing.Azure
{

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
