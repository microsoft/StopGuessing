using System;

namespace StopGuessing.AccountStorage.Sql
{
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

}
