using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StopGuessing.Utilities
{
    public static class TaskHelper
    {
        public static void RunInBackground<T>(Task<T> task) { }
        public static void RunInBackground(Task task) { }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public static async Task<T> PretendToBeAsync<T>(T t)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            return t;
        }
    }
    

}
