using System;
using System.Threading.Tasks;

namespace StopGuessing.DataStructures
{
    public class MemoryUsageLimiter
    {

        public class ReduceMemoryUsageEventParameters : EventArgs
        {
            public readonly double FractionOfMemoryToTryToRemove;

            public ReduceMemoryUsageEventParameters(
                    double fractionOfMemoryToTryToRemove)
            {
                FractionOfMemoryToTryToRemove = fractionOfMemoryToTryToRemove;
            }
        }
        
    
        public event EventHandler<ReduceMemoryUsageEventParameters> OnReduceMemoryUsageEventHandler;

        private readonly double _fractionToRemoveOnCleanup;
        public MemoryUsageLimiter(
            double fractionToRemoveOnCleanup = 0.1 /* 10% */,
            long hardMemoryLimit = 0)
        {
            _fractionToRemoveOnCleanup = fractionToRemoveOnCleanup;
            if (hardMemoryLimit == 0)
            {
                Task.Run(() => GenerationalReductionLoop());
            }
            else
            {
                Task.Run(() => GenerationalReductionLoop());
            }
        }
        

        public void ReduceMemoryUsage()
        {
            EventHandler<ReduceMemoryUsageEventParameters> localOnReduceMemoryUsageHandler = OnReduceMemoryUsageEventHandler;
            if (localOnReduceMemoryUsageHandler != null)
            {
                Parallel.ForEach(localOnReduceMemoryUsageHandler.GetInvocationList(),
                    d => {
                        try
                        {
                            d.DynamicInvoke(this, new ReduceMemoryUsageEventParameters(_fractionToRemoveOnCleanup));
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
                    );
            }
        }


        public void GenerationalReductionLoop()
        {
            GC.RegisterForFullGCNotification(15,15);

            while (true)
            {
                int collectionCount = GC.CollectionCount(2);
                GC.WaitForFullGCApproach(-1);



                if (collectionCount == GC.CollectionCount(2))
                    GC.Collect();

                GC.WaitForFullGCComplete();
            }
            // ReSharper disable once FunctionNeverReturns
        }


        public void ThresholdReductionLoop(long hardMemoryLimit)
        {
            while (true)
            {
                try
                {
                    System.Threading.Thread.Sleep(500);

                    long currentMemoryConsumptionInBytes = GC.GetTotalMemory(true);
                    if (currentMemoryConsumptionInBytes > hardMemoryLimit)
                    {
                        ReduceMemoryUsage();                        
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }
            // ReSharper disable once FunctionNeverReturns
        }
    }
}
