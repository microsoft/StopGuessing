using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using Microsoft.Framework.WebEncoders;
using System.Text;
using StopGuessing.EncryptionPrimitives;

namespace StopGuessing.Models
{
    /// <summary>
    /// Describes an attempt to login using a password to an account from a given IP address.
    /// </summary>
    [DataContract]
    public class LoginAttempt
    {
        //delegate bool DoesLoginApiSupportDeviceCookies(string api);
        [DataMember]
        public string Account { get; set; }

        [DataMember]
        public System.Net.IPAddress AddressOfClientInitiatingRequest { get; set; }

        [DataMember]
        public System.Net.IPAddress AddressOfServerThatInitiallyReceivedLoginAttempt { get; set; }

        [DataMember]
        public string EncryptedIncorrectPassword { get; set; }

        [DataMember]
        public byte[] Phase2HashOfIncorrectPassword { get; set; }

        [DataMember]
        public DateTimeOffset TimeOfAttempt { get; set; }

        [DataMember]
        public string Api { get; set; }

        [DataMember]
        public string CookieProvidedByBrowser { get; set; }

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


        public string ToUniqueKey()
        {
            // FIXME -- ensure key is compliant with uses
            return UrlEncoder.Default.UrlEncode(Account) + "&" + AddressOfServerThatInitiallyReceivedLoginAttempt + "&" + TimeOfAttempt.UtcTicks.ToString();
        }

        public void EncryptAndWriteIncorrectPassword(string incorrectPassword, ECDiffieHellmanPublicKey ecPublicLogKey)
        {
            EncryptedIncorrectPassword = Newtonsoft.Json.JsonConvert.SerializeObject(
                new EcEncryptedMessageAesCbcHmacSha256(ecPublicLogKey, Encoding.UTF8.GetBytes(incorrectPassword)));
        }

        public string DecryptAndGetIncorrectPassword(ECDiffieHellmanCng ecPrivateLogKey)
        {
            EcEncryptedMessageAesCbcHmacSha256 messageDeserializedFromJson =
                Newtonsoft.Json.JsonConvert.DeserializeObject<EcEncryptedMessageAesCbcHmacSha256>(EncryptedIncorrectPassword);
            byte[] passwordAsUtf8 = messageDeserializedFromJson.Decrypt(ecPrivateLogKey);
            return Encoding.UTF8.GetString(passwordAsUtf8);
        }



    }

}
