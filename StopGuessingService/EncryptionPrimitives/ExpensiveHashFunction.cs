using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StopGuessing.EncryptionPrimitives
{

    public delegate byte[] ExpensiveHashFunction(string password, byte[] saltBytes);

    public static class ExpensiveHashFunctionFactory
    {
        private static readonly Dictionary<string, ExpensiveHashFunction> ExpensiveHashFunctions = new Dictionary<string, ExpensiveHashFunction>
            {
            // FUTURE -- remove before v1 and put something here that is actually expensive.
                {"SHA256Once", (pwd, salt) =>
                  SHA256.Create().ComputeHash(salt.Concat(Encoding.UTF8.GetBytes(pwd)).ToArray())}
            };

        public const string DefaultFunctionName = "SHA256Once";

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
