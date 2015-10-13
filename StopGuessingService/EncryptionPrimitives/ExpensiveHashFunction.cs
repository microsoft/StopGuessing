using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StopGuessing.EncryptionPrimitives
{

    public delegate byte[] ExpensiveHashFunction(string password, byte[] saltBytes);

    public static class ExpensiveHashFunctionFactory
    {

        public const string DefaultFunctionName = "PBKDF2_1000";

        private static readonly Dictionary<string, ExpensiveHashFunction> ExpensiveHashFunctions = new Dictionary<string, ExpensiveHashFunction>
            {
                {DefaultFunctionName, (pwd, salt) =>
                                    new Rfc2898DeriveBytes(pwd, salt, 1000).GetBytes(16) }
            };


        public static ExpensiveHashFunction Get(string hashFunctionName)
        {
            return ExpensiveHashFunctions[hashFunctionName];
        }

        public static void Add(string name, ExpensiveHashFunction function)
        {
            ExpensiveHashFunctions[name] = function;
        }
    }
}
