using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Entity;
using Microsoft.WindowsAzure.Storage.Table;

namespace StopGuessing.Azure
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

    //public class SuccessfulLoginCookieEntityController
    //{
    //    public async Task<bool> HasClientWithThisHashedCookieSuccessfullyLoggedInBeforeAsync(
    //        string hashOfCookie,
    //        CancellationToken cancellationToken = default(CancellationToken))
    //    {
    //        // Retrieve a reference to the table.
    //        CloudTable table = await GetTableAsync(TableName_SuccessfulLoginCookie, cancellationToken);

    //        TableQuery<IncorrectPhaseTwoHashEntity> query = new TableQuery<IncorrectPhaseTwoHashEntity>().Where(TableQuery.CombineFilters(
    //                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableKeyEncoding.Encode(hashOfCookie)),
    //                TableOperators.And,
    //                TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableKeyEncoding.Encode(UsernameOrAccountId))));

    //        return (await query.CountAsync(cancellationToken: cancellationToken)) > 0;
    //    }

    //}


}
