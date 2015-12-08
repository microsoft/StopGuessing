using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Simulator
{
    public delegate void ParameterSettingFunction<in T>(ExperimentalConfiguration config, T iterationParameter);

    public interface IParameterSweeper
    {
        int GetParameterCount();
        void SetParameter(ExperimentalConfiguration config, int parameterIndex);
        string GetParameterString(int parameterIndex);
    }


    public class ParameterSweeper<T> : IParameterSweeper
    {
        public string Name;
        public T[] Parameters;
        public ParameterSettingFunction<T> ParameterSetter;

        public int GetParameterCount()
        {
            return Parameters.Length;
        }

        public void SetParameter(ExperimentalConfiguration config, int parameterIndex)
        {
            ParameterSetter(config, Parameters[parameterIndex]);
        }

        public string GetParameterString(int parameterIndex)
        {
            return Parameters[parameterIndex].ToString();
        }
    }
}
