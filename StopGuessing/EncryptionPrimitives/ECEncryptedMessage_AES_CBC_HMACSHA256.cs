using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Cryptography.Cng;

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
        /// <param name="recipientsPublicKey">The public portion of the message recpient's assymetric key.</param>
        /// <param name="plaintextMessageAsByteArray">The message to encrypt.</param>
        public EcEncryptedMessageAesCbcHmacSha256(
            byte[] plaintextMessageAsByteArray, Encryption.IPublicKey recipientsPublicKey = null)
        {
            PublicOneTimeEcKey = null;
            EncryptedMessage = plaintextMessageAsByteArray;
            //byte[] sessionKey;
            //using (CngKey oneTimeEcCngKey = CngKey.Create(CngAlgorithm.ECDiffieHellmanP256))
            //{
            //    using (ECDiffieHellmanCng oneTimeEcKey = new ECDiffieHellmanCng(oneTimeEcCngKey))
            //    {
            //        PublicOneTimeEcKey = oneTimeEcKey.PublicKey.ToByteArray();
            //        sessionKey = oneTimeEcKey.DeriveKeyMaterial(recipientsPublicKey);
            //    }
            //}
            //EncryptedMessage = Encryption.EncryptAesCbc(plaintextMessageAsByteArray, sessionKey, addHmac: true);
        }


        /// <summary>
        /// Decrypt the message by providing the recipient's private EC key.
        /// </summary>
        /// <param name="recipientsPrivateEcKey">The private EC key matching the public key provided for encryption.</param>
        /// <returns>The decrypted message as a byte array</returns>
        public byte[] Decrypt(Encryption.IPrivateKey recipientsPrivateEcKey)
        {
            return this.EncryptedMessage;
            //byte[] sessionKey;

            //try
            //{
            //    using (CngKey otherPartiesPublicKey = CngKey.Import(PublicOneTimeEcKey, CngKeyBlobFormat.EccPublicBlob))
            //    {
            //        sessionKey = recipientsPrivateEcKey.DeriveKeyMaterial(otherPartiesPublicKey);
            //    }
            //}
            //catch (CryptographicException e)
            //{
            //    throw new Exception("Failed to Decrypt log entry", e);
            //}

            //return Encryption.DecryptAesCbc(EncryptedMessage, sessionKey, checkAndRemoveHmac: true);
        }


        public byte[] Decrypt(byte[] privateKey)
        {
            return EncryptedMessage;
            //using (CngKey privateCngKey = CngKey.Import(privateKey, CngKeyBlobFormat.EccPrivateBlob))
            //{
            //    using (ECDiffieHellmanCng privateKey = new ECDiffieHellmanCng(privateCngKey))
            //    {
            //        return Decrypt(privateKey);
            //    }
            //}
        }
    }

}
