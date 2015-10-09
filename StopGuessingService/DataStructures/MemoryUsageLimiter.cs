using System;
using System.Threading.Tasks;

namespace StopGuessing.DataStructures
{
    public class MemoryUsageLimiter
    {

        public class ReduceMemoryUsageEventParameters : EventArgs
        {
            public readonly long TargetReductionRequestedInBytes;
            public readonly double TargetReductionAsFractionOfCurrentConsumption;
            public readonly long MemoryConsumptionThatTriggeredRecoveryInBytes;

            public ReduceMemoryUsageEventParameters(long targetReductionRequestedInBytes,
                    double targetReductionAsFractionOfCurrentConsumption,
                    long memoryConsumptionThatTriggeredRecoveryInBytes)
            {
                TargetReductionRequestedInBytes = targetReductionRequestedInBytes;
                TargetReductionAsFractionOfCurrentConsumption = targetReductionAsFractionOfCurrentConsumption;
                MemoryConsumptionThatTriggeredRecoveryInBytes = memoryConsumptionThatTriggeredRecoveryInBytes;
            }
        }

        public int MillisecondsToSleepBetweenChecksToSeeIfCleanupIsNeeded = 1000;

        public long MemoryCeilingThatTriggersCleanupInBytes;
        public long TargetMemoryUsageAfterCleanupInBytes;        

        public double MemoryCeilingThatTriggersCleanupAsFractionOfTotalPhysicalMemory
        {
            get { return ((double)MemoryCeilingThatTriggersCleanupInBytes) / TotalPhysicalMemoryInBytesAsDouble; }
            set { MemoryCeilingThatTriggersCleanupInBytes = (long)(TotalPhysicalMemoryInBytesAsDouble * value); }
        }

        public double TargetMemoryUsageAfterCleanupAsFractionOfTotalPhysicalMemory
        {
            get { return ((double)TargetMemoryUsageAfterCleanupInBytes) / TotalPhysicalMemoryInBytesAsDouble; }
            set { TargetMemoryUsageAfterCleanupInBytes = (long)(TotalPhysicalMemoryInBytesAsDouble * value); }
        }
    
        public event EventHandler<ReduceMemoryUsageEventParameters> OnReduceMemoryUsageEventHandler;
//        delegate void OnReduceMemoryUsageEventHandlerDelegate(object sender, ReduceMemoryUsageEventParameters e);

        public static readonly long TotalPhysicalMemoryInBytes = (long) 1024*1024*1024*8;//FIXME (new Microsoft.VisualBasic.ComputerInfo()).TotalPhysicalMemory;
        private static readonly double TotalPhysicalMemoryInBytesAsDouble = (double)TotalPhysicalMemoryInBytes;



        public MemoryUsageLimiter(long memoryConsumptionThatTriggersCleanup, long targetMemoryConsumptionToReduceToOnCleanup, EventHandler<ReduceMemoryUsageEventParameters> onRecoverMemory = null)
        {
            MemoryCeilingThatTriggersCleanupInBytes = memoryConsumptionThatTriggersCleanup;
            TargetMemoryUsageAfterCleanupInBytes = targetMemoryConsumptionToReduceToOnCleanup;
            if (onRecoverMemory != null)
            {
                OnReduceMemoryUsageEventHandler += onRecoverMemory;
            }

            // Launch memory check thread
            System.Threading.Thread memoryLimitCheckThread = new System.Threading.Thread(PeriodicMemoryCheck);
            memoryLimitCheckThread.Start();
        }
        
        public MemoryUsageLimiter(double fractionOfPhysicalMemoryThatTriggersCleanup, double targetFractionOfPhysicalMemoryToReduceToOnCleanup, EventHandler<ReduceMemoryUsageEventParameters> onRecoverMemory = null)
            : this((long)(TotalPhysicalMemoryInBytesAsDouble * fractionOfPhysicalMemoryThatTriggersCleanup),
                   (long)(TotalPhysicalMemoryInBytesAsDouble * targetFractionOfPhysicalMemoryToReduceToOnCleanup),
                   onRecoverMemory)
        {}

        public void PeriodicMemoryCheck()
        {
            while (true)
            {
                try
                {
                    System.Threading.Thread.Sleep(MillisecondsToSleepBetweenChecksToSeeIfCleanupIsNeeded);

                    long currentMemoryConsumptionInBytes = GC.GetTotalMemory(true);
                    if (currentMemoryConsumptionInBytes > (long)MemoryCeilingThatTriggersCleanupInBytes)
                    {
                        long targetReductionRequestedInBytes = currentMemoryConsumptionInBytes - TargetMemoryUsageAfterCleanupInBytes;
                        double targetReductionAsFractionOfCurrentConsumption = ((double)targetReductionRequestedInBytes) / (double)currentMemoryConsumptionInBytes;
                        EventHandler<ReduceMemoryUsageEventParameters> localOnReduceMemoryUsageHandler = OnReduceMemoryUsageEventHandler;
                        if (localOnReduceMemoryUsageHandler != null)
                        {
                            Parallel.ForEach(localOnReduceMemoryUsageHandler.GetInvocationList(), 
                                d => {
                                    try { 
                                        d.DynamicInvoke(this, 
                                        new ReduceMemoryUsageEventParameters(targetReductionRequestedInBytes,
                                        targetReductionAsFractionOfCurrentConsumption,
                                        currentMemoryConsumptionInBytes));
                                    }
                                    catch (Exception)
                                    {
                                        // ignored
                                    }
                                }
                                );
                        }

                            //LocalOnReduceMemoryUsageHandler(this, new ReduceMemoryUsageEventParameters(TargetReductionRequested_InBytes,
                            //    TargetReduction_AsFractionOfCurrentConsumption,
                            //    CurrentMemoryConsumption_InBytes));
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
