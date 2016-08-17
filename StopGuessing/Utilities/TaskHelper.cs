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
    }
    

}
