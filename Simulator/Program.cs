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
            for(int i=0; i<11; i++)
            {
                ExperimentalConfiguration expConfig1 = new ExperimentalConfiguration();
                BlockingAlgorithmOptions blockConfig1 = new BlockingAlgorithmOptions();
                blockConfig1.BlockThresholdUnpopularPassword = 100 + 200 * i;
                Simulator simulator = new Simulator(expConfig1, blockConfig1);
                Console.WriteLine("Unpopularpassword {0}", blockConfig1.BlockThresholdUnpopularPassword);
                
                await simulator.Run(blockConfig1);
            }


            //ExperimentalConfiguration expConfig = new ExperimentalConfiguration();
            //BlockingAlgorithmOptions blockConfig = new BlockingAlgorithmOptions();
            //Simulator simulator = new Simulator(expConfig, blockConfig);
            //await simulator.Run();
        }
    }
}
