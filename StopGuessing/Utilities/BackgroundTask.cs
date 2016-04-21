using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StopGuessing.Utilities
{
    public static class BackgroundTask
    {
        public static void Run<T>(Task<T> task) { }
        public static void Run(Task task) { }
    }
}
