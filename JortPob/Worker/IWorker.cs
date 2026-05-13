namespace JortPob.Worker
{ 
    public interface IWorker<T>
    {
        public T Go();
    }
}
