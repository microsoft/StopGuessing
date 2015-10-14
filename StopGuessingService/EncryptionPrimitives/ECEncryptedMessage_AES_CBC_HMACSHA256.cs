using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace StopGuessing.EncryptionPrimitives
{
    /// <summary>
    /// A message encrypted with a message recipient's public EC Key by 
    /// (1) generating a one-time EC key,
    /// (2) deriving a session key from the EC public key and the private portion of the one-time EC key
    /// (3) encryption the message with the session key using AES CBC and a SHA256 MAC (and zero'd out IV)
    /// (4) storing the public portion of the one-time EC key and the message
    /// 
    /// The message can be decrypted by providing the private portion of the recipient's EC key,
    /// deriving the session key from that recipeint's private key and the public one-time EC key.
    /// </summary>
    [DataContract]
    public class EcEncryptedMessageAesCbcHmacSha256
    {
        /// <summary>
        /// The public portion of the one-time EC key used to generate a session (encryption) key.
        /// </summary>
        [DataMember]
        public byte[] PublicOneTimeEcKey { get; set; }

        /// <summary>
        /// The messsage encrypted with AES CBC and a SHA256 MAC at the end of the plaintext.
        /// The encryption key is derived from the private portion of the one-time EC key an the
        /// recipient's public EC key.
        /// </summary>
        [DataMember]
        public byte[] EncryptedMessage { get; set; }


        public EcEncryptedMessageAesCbcHmacSha256()
        {}

        /// <summary>
        /// Creates an encrypted message
        /// </summary>
        /// <param name="recipientsEcPublicKey">The public portion of the message recpient's EC key.</param>
        /// <param name="plaintextMessageAsByteArray">The message to encrypt.</param>
        public EcEncryptedMessageAesCbcHmacSha256(ECDiffieHellmanPublicKey recipientsEcPublicKey, byte[] plaintextMessageAsByteArray)
        {
            ECDiffieHellmanCng oneTimeEcKey = new ECDiffieHellmanCng(CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null, new CngKeyCreationParameters()));
            PublicOneTimeEcKey = oneTimeEcKey.PublicKey.ToByteArray();

            byte[] sessionKey = oneTimeEcKey.DeriveKeyMaterial(recipientsEcPublicKey);
            EncryptedMessage = Encryption.EncryptAesCbc(plaintextMessageAsByteArray, sessionKey, addHmac: true);
        }

        public EcEncryptedMessageAesCbcHmacSha256(byte[] publicOneTimeEcKey, byte[] plaintextMessageAsByteArray)
            : this(
                ECDiffieHellmanCngPublicKey.FromByteArray(publicOneTimeEcKey, CngKeyBlobFormat.EccPublicBlob), plaintextMessageAsByteArray)
        {
        }

        /// <summary>
        /// Decrypt the message by providing the recipient's private EC key.
        /// </summary>
        /// <param name="recipientsPrivateEcKey">The private EC key matching the public key provided for encryption.</param>
        /// <returns>The decrypted message as a byte array</returns>
        public byte[] Decrypt(ECDiffieHellmanCng recipientsPrivateEcKey)
        {
            byte[] sessionKey;

            try
            {
                sessionKey = recipientsPrivateEcKey.DeriveKeyMaterial(CngKey.Import(PublicOneTimeEcKey, CngKeyBlobFormat.EccPublicBlob));
            }
            catch (CryptographicException e)
            {
                throw new Exception("Failed to Decrypt log entry", e);
            }

            return Encryption.DecryptAescbc(EncryptedMessage, sessionKey, checkAndRemoveHmac: true);
        }

        
        public byte[] Decrypt(byte[] ecPrivateKey)
        {
            return Decrypt(new ECDiffieHellmanCng(CngKey.Import(ecPrivateKey, CngKeyBlobFormat.EccPrivateBlob)));
        }
    }

}
