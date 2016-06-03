using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using Microsoft.Framework.WebEncoders;
using System.Text;
using Newtonsoft.Json;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{


    /// <summary>
    /// Describes an attempt to login using a password to an account from a given IP address.
    /// </summary>
    [DataContract]
    public class LoginAttempt
    {
        /// <summary>
        /// A unique ID that identifies the account that the client was attempting to login to.
        /// </summary>
        [DataMember]
        public string UsernameOrAccountId { get; set; }

        /// <summary>
        /// The IP address of the client that was attempting to login to.  This is
        /// </summary>
        [DataMember]
        public System.Net.IPAddress AddressOfClientInitiatingRequest { get; set; }

        /// <summary>
        /// The IP address of the server handling this request.  This is used to create a unique
        /// ID for each request without requiring coordination between servers to ensure they are
        /// generating numbers that other servers are not generating.
        /// </summary>
        [DataMember]
        public System.Net.IPAddress AddressOfServerThatInitiallyReceivedLoginAttempt { get; set; }

        /// <summary>
        /// When a login attempt is sent with an incorrect password, that incorrect password is encrypted
        /// with the UserAccount's EcPublicAccountLogKey.  That private key to decrypt is encrypted
        /// wiith the phase1 hash of the user's correct password.  If the correct password is provided in the future,
        /// we can go back and audit the incorrect password to see if it was within a short edit distance
        /// of the correct password--which would indicate it was likely a (benign) typo and not a random guess. 
        /// </summary>
        [DataMember]
        public EncryptedStringField EncryptedIncorrectPassword = new EncryptedStringField();

        /// <summary>
        /// The phase2 hash of the incorrect password, which is available for future analysis.  We can use
        /// this to determine if a client is sending an incorrect password that was recently attempted for this
        /// account.  This allows us to detect a common-benign behavior: an automated client that is trying the same
        /// incorrect password over and over again.  (Trying a password that you already know to be incorrect again
        /// provides no information to attackers, so they can't exploit the fact that we don't punish attempts to
        /// guess the same wrong password beyond the first attempt.)
        /// </summary>
        [DataMember]
        public string Phase2HashOfIncorrectPassword { get; set; }

        /// <summary>
        /// The time of the attempt.
        /// </summary>
        [DataMember]
        public DateTime TimeOfAttemptUtc { get; set; }

        /// <summary>
        /// The API/protocol over which the attempt was sent.  Can be used to differentiate attempts via web browsers
        /// (which support cookies, javascript, CAPTCHAS, and other goodness) from less flexible clients. 
        /// </summary>
        [DataMember]
        public string Api { get; set; }

        /// <summary>
        /// The hash of a client-identifying cookie provided by the client/browser.  To allow
        /// flexibility in the hash function and save the user of this class from having to choose
        /// a hash function, the hash is automatically performed by using the setter
        /// CookieProvidedByClient.
        /// </summary>
        [DataMember]
        public string HashOfCookieProvidedByBrowser { get; private set; }

        /// <summary>
        /// Will be set to true if, when the login attempt is being processed, it is determined
        /// that the client that initiated the login attempt provided a cookie that was also provided
        /// during a previous successful login attempt.
        /// This indicates a relationship between the legitiamte accountholder and the client (browser)
        /// that helps us allay suspicion that this login attempt is part of a massive untargetted
        /// guessing attack.
        /// This will be set by the analysis and need not be set by the creator of the LoginAttempt record.
        /// </summary>
        [DataMember]
        public bool DeviceCookieHadPriorSuccessfulLoginForThisAccount { get; set; }

        /// <summary>
        /// The outcome of the LoginAttempt, which default to Undetermined and will be set during analysis.
        /// This value does need not be set by the creator of the LoginAttempt record.
        /// </summary>
        [DataMember]
        public AuthenticationOutcome Outcome { get; set; } = AuthenticationOutcome.Undetermined;

        /// <summary>
        /// The popularity of the password provided among the set of past failed guesses observed
        /// by the system.  
        /// This will be set by the analysis and need not be set by the creator of the LoginAttempt record.
        /// </summary>
        [DataMember]
        public int PasswordsHeightOnBinomialLadder { get; set; }

        /// <summary>
        /// A setter used to provide a client-specific cookie.  This cookie should be random value
        /// from a large space assigned to the client the first time it connects and provided on that
        /// and every future login attempt.  If the client does not support cookies (e.g., POP)
        /// then this can be left blank.
        /// </summary>
        [IgnoreDataMember]
        [JsonIgnore]
        [NotMapped]
        public string CookieProvidedByClient { set { SetCookieProvidedByClient(value); } }

        /// <summary>
        /// A getter that provides a key that should uniquely identify this login attempt.
        /// </summary>
        [IgnoreDataMember]
        [JsonIgnore]
        [NotMapped]
        public string UniqueKey => ToUniqueKey();

        /// <summary>
        /// Get the hash of a cookie provided by the client software.
        /// </summary>
        /// <param name="plaintextCookie">The cookie string.</param>
        /// <returns>A one-way hash fo the cookie</returns>
        public static string HashCookie(string plaintextCookie)
        {
            return Convert.ToBase64String(ManagedSHA256.Hash(Encoding.UTF8.GetBytes(plaintextCookie)));
        }

        /// <summary>
        /// Set the cookie provided by the client by storing it's hash.
        /// </summary>
        /// <param name="plaintextCookie">The cookie provided by the client.</param>
        private void SetCookieProvidedByClient(string plaintextCookie)
        {
            if (string.IsNullOrEmpty(plaintextCookie))
                return;
            HashOfCookieProvidedByBrowser = HashCookie(plaintextCookie);
        }

        /// <summary>
        /// Get a unique identifier for this login attempt.
        /// </summary>
        /// <returns></returns>
        private string ToUniqueKey()
        {
            return UrlEncoder.Default.UrlEncode(UsernameOrAccountId) + "&" + 
                UrlEncoder.Default.UrlEncode(AddressOfServerThatInitiallyReceivedLoginAttempt.ToString()) + "&" + 
                TimeOfAttemptUtc.Ticks.ToString();
        }
        
    }

}
