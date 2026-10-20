namespace NothingOnWindows.UI;

public sealed class SingleInstanceSession : IDisposable
{
    private readonly Mutex mutex;
    private readonly string eventName;
    private readonly EventWaitHandle? activation;
    private RegisteredWaitHandle? listener;
    public bool IsPrimary { get; }
    public SingleInstanceSession(string name="NothingOnWindows")
    {
        mutex=new Mutex(false,@"Local\"+name+".Main",out bool first);
        IsPrimary=first;eventName=@"Local\"+name+".Show";
        if(first)activation=new EventWaitHandle(false,EventResetMode.AutoReset,eventName);
    }
    public void Listen(Action activated)
    {
        if(activation==null)throw new InvalidOperationException("Only the primary app listens for activation.");
        listener=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>activated(),null,Timeout.Infinite,false);
    }
    public bool SignalPrimary()
    {
        for(int i=0;i<10;i++)
        {
            try{using var existing=EventWaitHandle.OpenExisting(eventName);return existing.Set();}
            catch(WaitHandleCannotBeOpenedException){Thread.Sleep(100);}
        }
        return false;
    }
    public void Dispose(){listener?.Unregister(null);activation?.Dispose();mutex.Dispose();}
}
