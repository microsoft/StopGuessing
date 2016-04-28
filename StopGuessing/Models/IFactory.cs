using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StopGuessing.Models
{

    public interface IFactory<T>
    {
        T Create();
    }

}
