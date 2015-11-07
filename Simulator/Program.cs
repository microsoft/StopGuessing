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
            ExperimentalConfiguration expConfig = new ExperimentalConfiguration();
            BlockingAlgorithmOptions blockConfig = new BlockingAlgorithmOptions();
            Simulator simulator = new Simulator(expConfig, blockConfig);
            await simulator.Run();
        }
    }
}
