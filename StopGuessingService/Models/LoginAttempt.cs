using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;
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
        [DataMember]
        public string Account { get; set; }

        [DataMember]
        public System.Net.IPAddress AddressOfClientInitiatingRequest { get; set; }

        [DataMember]
        public System.Net.IPAddress AddressOfServerThatInitiallyReceivedLoginAttempt { get; set; }

        [DataMember]
        public string EncryptedIncorrectPassword { get; set; }

        [DataMember]
        public string Phase2HashOfIncorrectPassword { get; set; }

        [DataMember]
        public DateTimeOffset TimeOfAttempt { get; set; }

        [DataMember]
        public string Api { get; set; }

        [DataMember]
        public string Sha256HashOfCookieProvidedByBrowserBase64Encoded { get; set; }

        [DataMember]
        public bool DeviceCookieHadPriorSuccessfulLoginForThisAccount { get; set; }

        [DataMember]
        public AuthenticationOutcome Outcome { get; set; } = AuthenticationOutcome.Undetermined;

        [DataMember]
        public double PasswordsPopularityAmongFailedGuesses { get; set; }

        /// <summary>
        /// When calculating the likelihood that an IP address is conducting a brute-force attack,
        /// successful logins can be used to offset failures (reducing the likelihood that the IP will
        /// be deemed as attacking).  To prevent attackers from generating lots of successes using
        /// accounts they control, we only allow successes to counter failures if there is enough
        /// anti-blocking currency in the account.  We set this value to true if anti-blocking
        /// currency has been provided so that this success (if it is one) can offset the failure.
        /// </summary>
        [DataMember]
        public bool HasReceivedCreditForUseToReduceBlockingScore { get; set; }

        [IgnoreDataMember]
        [JsonIgnore]
        public string CookieProvidedByBrowser { set { SetCookieProvidedByBrowser(value); } }

        [IgnoreDataMember]
        [JsonIgnore]
        public string UniqueKey => ToUniqueKey();

        private void SetCookieProvidedByBrowser(string plaintextCookie)
        {
            if (string.IsNullOrEmpty(plaintextCookie))
                return;
            Sha256HashOfCookieProvidedByBrowserBase64Encoded =
                    Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(plaintextCookie)));
        }

        private string ToUniqueKey()
        {
            return UrlEncoder.Default.UrlEncode(Account) + "&" + AddressOfServerThatInitiallyReceivedLoginAttempt + "&" + TimeOfAttempt.UtcTicks.ToString();
        }

        /// <summary>
        /// Encrypt a plaintext incorrect password with the account's EC Diffie Helman public key so that it can
        /// be safely stored until the correct password is provided in the future.  Store it in the
        /// EncryptedIncorrectPassword field.  (This doesn't expose information about the correct password because
        /// the corresponding EC secret key needed to decrypt this incorrect password is itself encrypted with the
        /// phase1 hash of the correct password.)
        /// 
        /// The encryption format is a JSON-encoded EcEncryptedMessageAesCbcHmacSha256.
        /// </summary>
        /// <param name="incorrectPassword">The incorrect password to be stored into the EncryptedIncorrectPassword
        /// field.</param>
        /// <param name="ecPublicLogKey">The public key used to encrypt the incorrect password.</param>
        public void EncryptAndWriteIncorrectPassword(string incorrectPassword, ECDiffieHellmanPublicKey ecPublicLogKey)
        {
            EncryptedIncorrectPassword = JsonConvert.SerializeObject(
                new EcEncryptedMessageAesCbcHmacSha256(ecPublicLogKey, Encoding.UTF8.GetBytes(incorrectPassword)));
        }

        /// <summary>
        /// Decrypt an EncryptedIncorrectPassword by provide the EC Diffie Helman private key
        /// matching the public key used to encrypt it.
        /// 
        /// Note that any decryption exceptions, such as occur if the data is corrupted or the wrong
        /// private key provided, will be passed up to the caller.
        /// </summary>
        /// <param name="ecPrivateLogKey">The private key that can be used to decrypt the encrypted incorrect password.</param>
        /// <returns>The password that was provided during this login attempt.</returns>
        public string DecryptAndGetIncorrectPassword(ECDiffieHellmanCng ecPrivateLogKey)
        {
            EcEncryptedMessageAesCbcHmacSha256 messageDeserializedFromJson =
                JsonConvert.DeserializeObject<EcEncryptedMessageAesCbcHmacSha256>(EncryptedIncorrectPassword);
            byte[] passwordAsUtf8 = messageDeserializedFromJson.Decrypt(ecPrivateLogKey);
            return Encoding.UTF8.GetString(passwordAsUtf8);
        }



    }

}
