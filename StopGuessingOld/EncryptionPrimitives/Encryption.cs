using System.Linq;
using System.Security.Cryptography;

namespace StopGuessing.EncryptionPrimitives
{
    public class Encryption
    {

        static readonly byte[] NullIv = new byte[16];

        const int Sha256HmacLength = 32;
 
        /// <summary>
        /// Encrypt a message using AES in CBC (cipher-block chaining) mode.
        /// </summary>
        /// <param name="plaintext">The message (plaintext) to encrypt</param>
        /// <param name="key">An AES key</param>
        /// <param name="iv">The IV to use or null to use a 0 IV</param>
        /// <param name="addHmac">When set, a SHA256-based HMAC (HMAC256) of 32 bytes using the same key is added to the plaintext
        /// before it is encrypted.</param>
        /// <returns>The ciphertext derived by encrypting the orignal message using AES in CBC mode</returns>
        public static byte[] EncryptAesCbc(byte[] plaintext, byte[] key, byte[] iv = null, bool addHmac = false)
        {
            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.Key = key;
                if (iv == null)
                    iv = NullIv;
                aes.Mode = CipherMode.CBC;
                aes.IV = iv;

                // Encrypt the message with the key using CBC and InitializationVector=0
                byte[] cipherText;
                using (System.IO.MemoryStream ciphertext = new System.IO.MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ciphertext, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(plaintext, 0, plaintext.Length);
                        if (addHmac)
                        {
                            byte[] hmac = new HMACSHA256(key).ComputeHash(plaintext);
                            cs.Write(hmac, 0, hmac.Length);
                        }
                        cs.Flush();
                    }
                    cipherText = ciphertext.ToArray();
                }

                return cipherText;
            }
        }

        public static byte[] EncryptAesCbc(string plainText, byte[] key, byte[] iv = null, bool addHmac = false)
        {
            return EncryptAesCbc(System.Text.Encoding.UTF8.GetBytes(plainText), key, iv, addHmac);
        }


        /// <summary>
        /// Decrypt a message using AES in CBC (cipher-block chaining) mode.
        /// </summary>
        /// <param name="ciphertext">The message encrypted with AES in CBC mode</param>
        /// <param name="key">The key used to encrypt the message</param>
        /// <param name="iv">The initialization vector provided, if one was provided.  If you are absolutely certain
        /// the key will only be used once, an IV is not necessary and zero will be used.</param>
        /// <param name="checkAndRemoveHmac">Set if an HMACHSA256 was placed at the end of the plaintext before encrypting.
        /// The HMAC will be removed before the plaintext is returned.  If the HMAC does not match, the method will throw a
        /// System.Security.Cryptography.CryptographicException.</param>
        /// <returns>The plaintext resulting from decrypting the ciphertext with the given key.</returns>
        public static byte[] DecryptAesCbc(byte[] ciphertext, byte[] key, byte[] iv = null, bool checkAndRemoveHmac = false)
        {
            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.Key = key;
                if (iv == null)
                    iv = NullIv;

                aes.IV = iv;
                aes.Mode = CipherMode.CBC;

                // Decrypt the message 
                using (System.IO.MemoryStream plaintextStream = new System.IO.MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(plaintextStream, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(ciphertext, 0, ciphertext.Length);
                    }
                    byte[] plaintext = plaintextStream.ToArray();
                    if (checkAndRemoveHmac)
                    {
                        byte[] hmacProvided = plaintext.Skip(plaintext.Length - Sha256HmacLength).ToArray();
                        plaintext = plaintext.Take(plaintext.Length - Sha256HmacLength).ToArray();
                        byte[] hmacCalculated = new HMACSHA256(key).ComputeHash(plaintext);
                        if (!hmacProvided.SequenceEqual(hmacCalculated))
                            throw new CryptographicException("Message authentication code validation failed.");
                    }
                    return plaintext;
                }
            }
        }

        public static string DecryptAescbcutf8(byte[] ciphertext, byte[] key, byte[] iv = null, bool checkAndRemoveHmac = false)
        {
            return System.Text.Encoding.UTF8.GetString(DecryptAesCbc(ciphertext, key, iv, checkAndRemoveHmac));
        }



        /// <summary>
        /// Encrypt an EC private key with a symmetric key.
        /// </summary>
        /// <param name="ecPrivateKey">The EC private key to encrypt</param>
        /// <param name="symmetricKey">The symmetric key with which to encrypt the EC key.  Must be at least
        /// 16 bytes.  Any additional bytes will be ignored.</param>
        /// <returns></returns>
        public static byte[] EncryptEcPrivateKeyWithAesCbc(ECDiffieHellmanCng ecPrivateKey, byte[] symmetricKey)
        {
            byte[] ecAccountLogKeyAsBytes = ecPrivateKey.Key.Export(CngKeyBlobFormat.EccPrivateBlob);
            return EncryptAesCbc(ecAccountLogKeyAsBytes, symmetricKey.Take(16).ToArray(), addHmac: true);
        }

        /// <summary>
        /// Decrypt an EC private key that has been stored encrypted with AES CBC using a private key
        /// </summary>
        /// <param name="ecPrivateKeyEncryptedWithAesCbc">The EC private key encrypted with AES CBC.</param>
        /// <param name="symmetricKey">The symmetric key with which to encrypt the EC key.  Must be at least
        /// 16 bytes.  Any additional bytes will be ignored.</param>
        /// <returns></returns>
        public static ECDiffieHellmanCng DecryptAesCbcEncryptedEcPrivateKey(
            byte[] ecPrivateKeyEncryptedWithAesCbc,
            byte[] symmetricKey)
        {
            byte[] ecPrivateAccountLogKeyAsBytes = DecryptAesCbc(
                                ecPrivateKeyEncryptedWithAesCbc,
                                symmetricKey.Take(16).ToArray(),
                                checkAndRemoveHmac: true);
            return new ECDiffieHellmanCng(CngKey.Import(ecPrivateAccountLogKeyAsBytes, CngKeyBlobFormat.EccPrivateBlob));
        }


        /// <summary>
        /// Generate key from hashed password. We will need to use stronger hash later.
        /// </summary>
        /// <param name="password">The password to hash.</param>
        /// <param name="salt">The random salt given to a password to prevent cross-account cracking.</param>
        public static byte[] KeyGenFromPwd(string password, byte[] salt)
        {
            Rfc2898DeriveBytes pwdGen = new Rfc2898DeriveBytes(password, salt, 10000);
            return pwdGen.GetBytes(32);
        }
    }
}
