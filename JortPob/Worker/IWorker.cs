using System.Threading;
using System.Threading.Tasks;

namespace JortPob.Worker
{ 
    public interface IWorker<T>
    {
        public T Go();
    }
}
