using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace StopGuessing.EncryptionPrimitives
{


    public class EncryptedByteField
    {
        [DataMember]
        public string Ciphertext { get; set; }

        /// <summary>
        /// 
        /// The encryption format is a JSON-encoded EcEncryptedMessageAesCbcHmacSha256.
        /// </summary>
        /// <param name="value">The value to store encrypted.</param>
        /// <param name="ecPublicKeyAsByteArray">The public key used to encrypt the value.</param>
        public void Write(byte[] value, byte[] ecPublicKeyAsByteArray)
        {
            using (ECDiffieHellmanPublicKey ecPublicKey = ECDiffieHellmanCngPublicKey.FromByteArray(ecPublicKeyAsByteArray, CngKeyBlobFormat.EccPublicBlob))
            {
                Write(value, ecPublicKey);
            }
        }

        public void Write(byte[] value, ECDiffieHellmanPublicKey ecPublicKey)
        {
            Ciphertext = JsonConvert.SerializeObject(
                new EcEncryptedMessageAesCbcHmacSha256(ecPublicKey, value));
        }

        /// <summary>
        /// </summary>
        /// <param name="ecPrivateKey">The private key that can be used to decrypt the value.</param>
        /// <returns>The decrypted value.</returns>
        public byte[] Read(ECDiffieHellmanCng ecPrivateKey)
        {
            if (string.IsNullOrEmpty(Ciphertext))
                throw new MemberAccessException("Cannot decrypt a value that has not been written.");

            EcEncryptedMessageAesCbcHmacSha256 messageDeserializedFromJson =
                JsonConvert.DeserializeObject<EcEncryptedMessageAesCbcHmacSha256>(Ciphertext);
            return messageDeserializedFromJson.Decrypt(ecPrivateKey);
        }

        public bool HasValue => !string.IsNullOrEmpty(Ciphertext);
    }

    public class EncryptedStringField : EncryptedByteField
    {
        public void Write(string value, byte[] ecPublicKeyAsByteArray) =>
            Write(Encoding.UTF8.GetBytes(value), ecPublicKeyAsByteArray);

        public void Write(string value, ECDiffieHellmanPublicKey ecPublicKey) =>
            Write(Encoding.UTF8.GetBytes(value), ecPublicKey);

        public new string Read(ECDiffieHellmanCng ecPrivateKey) => Encoding.UTF8.GetString(base.Read(ecPrivateKey));
    }

    public class EncryptedValueField<T> : EncryptedStringField
    {
        public void Write(T value, byte[] ecPublicKeyAsByteArray) =>
            Write(JsonConvert.SerializeObject(value), ecPublicKeyAsByteArray);

        public void Write(T value, ECDiffieHellmanPublicKey ecPublicKey) =>
            Write(JsonConvert.SerializeObject(value), ecPublicKey);

        public new T Read(ECDiffieHellmanCng ecPrivateKey) => JsonConvert.DeserializeObject<T>(base.Read(ecPrivateKey));
    }



}
