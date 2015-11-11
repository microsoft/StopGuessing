using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StopGuessing.Models;

namespace Simulator
{
    public class Program
    {
        public async Task Main(string[] args)
        {
            //for (int i = 1; i < 10; i++)
            //{
            //    ExperimentalConfiguration expConfig1 = new ExperimentalConfiguration();
            //    BlockingAlgorithmOptions blockConfig1 = new BlockingAlgorithmOptions();
            //    blockConfig1.FOR_SIMULATION_ONLY_TURN_ON_SSH_STUPID_MODE = true;
            //    //blockConfig1.BlockThresholdUnpopularPassword = 1 * i;
            //    //blockConfig1.BlockThresholdPopularPassword = blockConfig1.BlockThresholdUnpopularPassword;
            //    //
            //    // Industrial-best-practice baseline
            //    //

            //    // Use the same threshold regardless of the popularity of the account password
            //    blockConfig1.BlockThresholdPopularPassword =
            //        blockConfig1.BlockThresholdUnpopularPassword =1*i;
            //    // Make all failures increase the count towards the threshold by one
            //    blockConfig1.PenaltyForInvalidAccount =
            //        blockConfig1.PenaltyMulitiplierForTypo =
            //        blockConfig1.BasePenaltyForInvalidPassword =
            //        1d;
            //    // If the below is empty, the multiplier for any popularity level will be 1.
            //    blockConfig1.PenaltyForReachingEachPopularityThreshold = new List<PenaltyForReachingAPopularityThreshold>();
            //    // Correct passwords shouldn't help
            //    blockConfig1.RewardForCorrectPasswordPerAccount = 0;

            //    //


            //    expConfig1.TotalLoginAttemptsToIssue = 5000;
            //    expConfig1.RecordUnitAttempts = 5000;
            //    //expConfig1.ChanceOfBenignPasswordTypo = 0.2d;
            //    Simulator simulator = new Simulator(expConfig1, blockConfig1);
            //    Console.WriteLine("Unpopularpassword {0}", blockConfig1.BlockThresholdUnpopularPassword);

            //  //  await simulator.Run(blockConfig1);
            //}

            ulong TotalLoginAttemptsToIssue = 200000;
            ulong RecordUnitAttempts = 200000;
            uint MaliciousIP = 2000;
            //1. Vary BlockThresholdUnpopularPassword from 100 to 2100  (in steps of 200)
            for (int i = 0; i < 11; i++)
            {
                ExperimentalConfiguration expConfig1 = new ExperimentalConfiguration();
                BlockingAlgorithmOptions blockConfig1 = new BlockingAlgorithmOptions();
                //blockConfig1.RewardForCorrectPasswordPerAccount = -10 * i - 10;
                //blockConfig1.PenaltyForInvalidAccount = 1 +  (int)(i/3);
                //blockConfig1.BlockThresholdPopularPassword = 20 + 10 * i;
                //blockConfig1.BlockThresholdUnpopularPassword = 4*blockConfig1.BlockThresholdPopularPassword;
                blockConfig1.BlockThresholdUnpopularPassword = 100 + 200 * i;
                expConfig1.TotalLoginAttemptsToIssue = TotalLoginAttemptsToIssue;
                expConfig1.RecordUnitAttempts = RecordUnitAttempts;
                expConfig1.NumberOfIpAddressesControlledByAttacker = MaliciousIP;
                //blockConfig1.BlockThresholdPopularPassword = 60;
                //blockConfig1.BlockThresholdUnpopularPassword = 900;
                //blockConfig1.PenaltyForInvalidAccount = 35;




                //blockConfig1.PenaltyForReachingEachPopularityThreshold = new List<PenaltyForReachingAPopularityThreshold>
                //{
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100*1000d), Penalty = 10*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(10*1000d), Penalty = 20*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(1*1000d), Penalty = 25*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100d), Penalty = 30*Math.Pow(2,0.3*i)},
                //};
                Simulator simulator = new Simulator(expConfig1, blockConfig1);
                Console.WriteLine("unpopularpassword {0}", blockConfig1.BlockThresholdUnpopularPassword);

                await simulator.Run(blockConfig1, "BlockThresholdUnpopularPassword");
            }

            //2. Vary blockthresholdpopularpassword from 20 - 120 and unpopularpassword is 4 times



            for (int i = 0; i < 10; i++)
            {
                ExperimentalConfiguration expConfig1 = new ExperimentalConfiguration();
                BlockingAlgorithmOptions blockConfig1 = new BlockingAlgorithmOptions();
                //blockConfig1.RewardForCorrectPasswordPerAccount = -10 * i - 10;
                //blockConfig1.PenaltyForInvalidAccount = 1 +  (int)(i/3);
                blockConfig1.BlockThresholdPopularPassword = 20 + 10 * i;
                blockConfig1.BlockThresholdUnpopularPassword = 4 * blockConfig1.BlockThresholdPopularPassword;
                //blockConfig1.BlockThresholdUnpopularPassword = 100 + 200 * i;
                expConfig1.TotalLoginAttemptsToIssue = TotalLoginAttemptsToIssue;
                expConfig1.RecordUnitAttempts = RecordUnitAttempts;
                expConfig1.NumberOfIpAddressesControlledByAttacker = 100;
                //blockConfig1.BlockThresholdPopularPassword = 60;
                //blockConfig1.BlockThresholdUnpopularPassword = 900;
                //blockConfig1.PenaltyForInvalidAccount = 35;




                //blockConfig1.PenaltyForReachingEachPopularityThreshold = new List<PenaltyForReachingAPopularityThreshold>
                //{
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100*1000d), Penalty = 10*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(10*1000d), Penalty = 20*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(1*1000d), Penalty = 25*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100d), Penalty = 30*Math.Pow(2,0.3*i)},
                //};
                Simulator simulator = new Simulator(expConfig1, blockConfig1);
                Console.WriteLine("Popularpassword {0}", blockConfig1.BlockThresholdPopularPassword);

                await simulator.Run(blockConfig1, "BlockThresholdPopularPassword");
            }

            //3.Vary PenaltyForInvalidAccount from 1 to 10 (in steps of 1)
            for (int i = 0; i < 10; i++)
            {
                ExperimentalConfiguration expConfig1 = new ExperimentalConfiguration();
                BlockingAlgorithmOptions blockConfig1 = new BlockingAlgorithmOptions();
                //blockConfig1.RewardForCorrectPasswordPerAccount = -10 * i - 10;
                blockConfig1.PenaltyForInvalidAccount = 1 + i;
                //blockConfig1.BlockThresholdPopularPassword = 20 + 10 * i;
                //blockConfig1.BlockThresholdUnpopularPassword = 4 * blockConfig1.BlockThresholdPopularPassword;
                //blockConfig1.BlockThresholdUnpopularPassword = 100 + 200 * i;
                expConfig1.TotalLoginAttemptsToIssue = TotalLoginAttemptsToIssue;
                expConfig1.RecordUnitAttempts = RecordUnitAttempts;
                expConfig1.NumberOfIpAddressesControlledByAttacker = MaliciousIP;
                //blockConfig1.BlockThresholdPopularPassword = 60;
                //blockConfig1.BlockThresholdUnpopularPassword = 900;
                //blockConfig1.PenaltyForInvalidAccount = 35;





                //blockConfig1.PenaltyForReachingEachPopularityThreshold = new List<PenaltyForReachingAPopularityThreshold>
                //{
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100*1000d), Penalty = 10*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(10*1000d), Penalty = 20*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(1*1000d), Penalty = 25*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100d), Penalty = 30*Math.Pow(2,0.3*i)},
                //};
                Simulator simulator = new Simulator(expConfig1, blockConfig1);
                Console.WriteLine("PenaltyForInvalidAccount {0}", blockConfig1.PenaltyForInvalidAccount);

                await simulator.Run(blockConfig1, "PenaltyForInvalidAccount");
            }
            //4.Vary RewardForCorrectPasswordPerAccount from -10 to -100 
            for (int i = 0; i < 10; i++)
            {
                ExperimentalConfiguration expConfig1 = new ExperimentalConfiguration();
                BlockingAlgorithmOptions blockConfig1 = new BlockingAlgorithmOptions();
                blockConfig1.RewardForCorrectPasswordPerAccount = -10 * i - 10;
                //blockConfig1.PenaltyForInvalidAccount = 1 +  (int)(i/3);
                // blockConfig1.BlockThresholdPopularPassword = 20 + 10 * i;
                //blockConfig1.BlockThresholdUnpopularPassword = 4 * blockConfig1.BlockThresholdPopularPassword;
                //blockConfig1.BlockThresholdUnpopularPassword = 100 + 200 * i;
                expConfig1.TotalLoginAttemptsToIssue = TotalLoginAttemptsToIssue;
                expConfig1.RecordUnitAttempts = RecordUnitAttempts;
                expConfig1.NumberOfIpAddressesControlledByAttacker = MaliciousIP;
                //blockConfig1.BlockThresholdPopularPassword = 60;
                //blockConfig1.BlockThresholdUnpopularPassword = 900;
                //blockConfig1.PenaltyForInvalidAccount = 35;




                //blockConfig1.PenaltyForReachingEachPopularityThreshold = new List<PenaltyForReachingAPopularityThreshold>
                //{
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100*1000d), Penalty = 10*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(10*1000d), Penalty = 20*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(1*1000d), Penalty = 25*Math.Pow(2,0.3*i)},
                //    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100d), Penalty = 30*Math.Pow(2,0.3*i)},
                //};
                Simulator simulator = new Simulator(expConfig1, blockConfig1);
                Console.WriteLine("RewardForCorrectPasswordPerAccount {0}", blockConfig1.RewardForCorrectPasswordPerAccount);

                await simulator.Run(blockConfig1, "RewardForCorrectPasswordPerAccount");
            }



            //5.PenaltyForReachingEachPopularityThreshold multiply each by 2 ^{ 0.3 * k} k0-10



            for (int i = 0; i < 11; i++)
            {
                ExperimentalConfiguration expConfig1 = new ExperimentalConfiguration();
                BlockingAlgorithmOptions blockConfig1 = new BlockingAlgorithmOptions();
                //blockConfig1.RewardForCorrectPasswordPerAccount = -10 * i - 10;
                //blockConfig1.PenaltyForInvalidAccount = 1 +  (int)(i/3);
                // blockConfig1.BlockThresholdPopularPassword = 20 + 10 * i;
                //blockConfig1.BlockThresholdUnpopularPassword = 4 * blockConfig1.BlockThresholdPopularPassword;
                //blockConfig1.BlockThresholdUnpopularPassword = 100 + 200 * i;
                expConfig1.TotalLoginAttemptsToIssue = TotalLoginAttemptsToIssue;
                expConfig1.RecordUnitAttempts = RecordUnitAttempts;
                expConfig1.NumberOfIpAddressesControlledByAttacker = MaliciousIP;
                //blockConfig1.BlockThresholdPopularPassword = 60;
                //blockConfig1.BlockThresholdUnpopularPassword = 900;
                //blockConfig1.PenaltyForInvalidAccount = 35;




                blockConfig1.PenaltyForReachingEachPopularityThreshold = new List<PenaltyForReachingAPopularityThreshold>
                {
                    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100*1000d), Penalty = 10*Math.Pow(2,0.3*i)},
                    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(10*1000d), Penalty = 20*Math.Pow(2,0.3*i)},
                    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(1*1000d), Penalty = 25*Math.Pow(2,0.3*i)},
                    new PenaltyForReachingAPopularityThreshold { PopularityThreshold = 1d/(100d), Penalty = 30*Math.Pow(2,0.3*i)},
                };
                Simulator simulator = new Simulator(expConfig1, blockConfig1);
                Console.WriteLine("PenaltyForReachingEachPopularityThreshold{0}", blockConfig1.PenaltyForReachingEachPopularityThreshold);

                await simulator.Run(blockConfig1, "PenaltyForReachingEachPopularityThreshold");
            }










            //ExperimentalConfiguration expConfig = new ExperimentalConfiguration();
            //BlockingAlgorithmOptions blockConfig = new BlockingAlgorithmOptions();
            //Simulator simulator = new Simulator(expConfig, blockConfig);
            //await simulator.Run();
        }





    }

}