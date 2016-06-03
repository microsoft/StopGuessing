using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using StopGuessing.DataStructures;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    /// <summary>
    /// A record of a recent failed login attempt with just enough information to determine whether
    /// that attempt failed due to an incorrectly typed password.
    /// </summary>
    public struct LoginAttemptSummaryForTypoAnalysis
    {
        /// <summary>
        /// The unique identifier of the account that was being logged into.
        /// </summary>
        public string UsernameOrAccountId { get; set; }

        /// <summary>
        /// The penalty applied to the blockign score when this login attempt registered as having
        /// an invalid password
        /// </summary>
        public DecayingDouble Penalty { get; set; }

        /// <summary>
        /// When a login attempt is sent with an incorrect password, that incorrect password is encrypted
        /// with the UserAccount's EcPublicAccountLogKey.  That private key to decrypt is encrypted
        /// wiith the phase1 hash of the user's correct password.  If the correct password is provided in the future,
        /// we can go back and audit the incorrect password to see if it was within a short edit distance
        /// of the correct password--which would indicate it was likely a (benign) typo and not a random guess. 
        /// </summary>
        public EncryptedStringField EncryptedIncorrectPassword { get; set; }
    }
}
