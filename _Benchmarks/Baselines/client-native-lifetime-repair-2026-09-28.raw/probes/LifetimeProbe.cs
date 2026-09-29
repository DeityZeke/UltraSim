using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UltraSim.Client.Godot;

internal static class Program
{
    private static int Main()
    {
        TestStartupClose(); TestNormalClose(); TestFailure(); TestExternalExit();
        TestResources(); TestResourceFailures(); TestThreadAffinity();
        Console.WriteLine("PASS 7/7 lifetime/ownership controls; production lifetime code, deterministic engine doubles.");
        return 0;
    }
    private static void TestStartupClose()
    {
        var host = new Host(); host.Start(); host.Close(); host.Close(7); host.Started();
        Require(host.ApplicationPhase == GodotApplicationPhase.StopRequested, "startup must not resurrect");
        Require(!host.ShutdownCompletion.IsCompleted && host.Order.Count == 0, "request is not completion");
        Godot.Callable.Flush();
        Require(host.Order.SequenceEqual(new[]{"session","services","ui"}), "explicit retirement order");
        Require(host.ApplicationPhase == GodotApplicationPhase.Stopping && host.Tree.QuitCalls == 0, "native tree remains alive");
        host.Tree.Frame(); Require(host.Tree.QuitCalls == 0, "one boundary may precede first deletion flush");
        host.Tree.Frame(); Require(host.Tree.QuitCalls == 1 && host.Tree.ExitCode == 7, "quit follows deletion boundary");
        Require(host.ShutdownCompletion.IsCompletedSuccessfully, "completion before engine quit");
        host.Close(); host.Exiting(); Require(host.Order.Count == 3, "repeat exit is idempotent");
        Console.WriteLine("PASS startup-close: ordered retirement, two frame acknowledgements, one quit");
    }
    private static void TestNormalClose()
    {
        var host = new Host(); host.Start(); host.Started();
        Require(host.ApplicationPhase == GodotApplicationPhase.Running, "running published");
        host.Close(); Godot.Callable.Flush(); host.Tree.Frame(); host.Tree.Frame();
        Require(host.ApplicationPhase == GodotApplicationPhase.Stopped, "normal close completed");
        Console.WriteLine("PASS normal close");
    }
    private static void TestFailure()
    {
        var host = new Host { FailSession = true, FailServices = true }; host.Start(); host.Close();
        Godot.Callable.Flush();
        Require(host.Order.Count == 3, "all cleanup owners attempted");
        Require(host.ApplicationPhase == GodotApplicationPhase.Faulted && host.Tree.QuitCalls == 0, "failure is not completion/quit");
        Require(host.ShutdownCompletion.Exception?.Flatten().InnerExceptions.Count == 2, "both failures observable");
        Console.WriteLine("PASS cleanup failures: both retained, engine quit withheld");
    }
    private static void TestExternalExit()
    {
        var host = new Host(); host.Start(); host.Exiting();
        Require(host.Order.Count == 3 && host.ShutdownCompletion.IsFaulted, "premature external tree exit is observable");
        _ = host.ShutdownCompletion.Exception;
        Console.WriteLine("PASS premature tree exit");
    }
    private static void TestResources()
    {
        List<int> order = [];
        var owner = new GodotOwnedResources();
        var material = owner.Own(new Resource(1,order)); var mesh = owner.Own(new Resource(2,order));
        Throws<InvalidOperationException>(()=>owner.Own(mesh));
        owner.Dispose(); owner.Dispose();
        Require(order.SequenceEqual(new[]{2,1}) && owner.IsRetired && owner.Count==0,"reverse dependency disposal once");
        Throws<InvalidOperationException>(()=>owner.Own(new Resource(3,order)));
        Console.WriteLine("PASS resource transfer/retirement");
    }
    private static void TestResourceFailures()
    {
        List<int> order=[];var owner=new GodotOwnedResources();
        owner.Own(new Resource(1,order,true));owner.Own(new Resource(2,order,true));
        var error=Throws<AggregateException>(owner.Dispose);
        Require(error.InnerExceptions.Count==2 && order.Count==2 && !owner.IsRetired && owner.Count==2,"partial retirement retains owners/failures");
        Require(ReferenceEquals(error,Throws<AggregateException>(owner.Dispose)),"repeat failure not swallowed");
        Console.WriteLine("PASS resource cleanup failure aggregation");
    }
    private static void TestThreadAffinity()
    {
        var owner=new GodotOwnedResources();var life=new GodotApplicationLifetime();Exception? a=null,b=null;
        var thread=new Thread(()=>{try{owner.Dispose();}catch(Exception e){a=e;}try{life.RequestStop(0);}catch(Exception e){b=e;}});
        thread.Start();Require(thread.Join(2000),"test thread terminated");
        Require(a is InvalidOperationException && b is InvalidOperationException,"foreign-thread retirement rejected");
        Console.WriteLine("PASS owner thread affinity");
    }
    private sealed class Resource(int id,List<int> order,bool fail=false):IDisposable
    {public void Dispose(){order.Add(id);if(fail)throw new InvalidOperationException(id.ToString());}}
    private static T Throws<T>(Action action) where T:Exception
    {try{action();}catch(T e){return e;}throw new Exception("Expected "+typeof(T).Name);}
    private static void Require(bool condition,string why){if(!condition)throw new Exception(why);}
}

namespace UltraSim.Client.Godot
{
    public abstract partial class ClientRoot : global::Godot.Control
    {
        public readonly List<string> Order=[];
        public readonly global::Godot.SceneTree Tree=new();
        private Ui? _uiComposition;
        public bool FailSession,FailServices;
        protected ClientRoot(){_uiComposition=new Ui(Order);}
        public void Start()=>BeginApplicationStartup();
        public void Started()=>CompleteApplicationStartup();
        public void Close(int code=0)=>RequestApplicationExit(code);
        public void Exiting()=>RetireOnTreeExit();
        private void RetireCurrentSession(){Order.Add("session");if(FailSession)throw new Exception("session failure");}
        public global::Godot.SceneTree GetTree()=>Tree;
        public Task ToSignal(global::Godot.SceneTree tree,string signal)=>tree.NextFrame;
        private sealed class Ui(List<string> order):IDisposable {public void Dispose()=>order.Add("ui");}
    }
    internal sealed class Host:ClientRoot
    {protected override void RetireApplicationServices(){Order.Add("services");if(FailServices)throw new Exception("services failure");}}
}
namespace Godot
{
    public class Control {public enum ProcessModeEnum{Inherit,Disabled} public ProcessModeEnum ProcessMode{get;set;}}
    public sealed class SceneTree
    {
        public bool AutoAcceptQuit;public int QuitCalls,ExitCode;
        private TaskCompletionSource _frame=new();
        public Task NextFrame=>_frame.Task;
        public static class SignalName {public const string ProcessFrame="process_frame";}
        public void Frame(){var old=_frame;_frame=new();old.SetResult();}
        public void Quit(int code){QuitCalls++;ExitCode=code;}
    }
    public sealed class Callable(Action action)
    {
        private static readonly Queue<Action> Queue=new();
        public static Callable From(Action action)=>new(action);
        public void CallDeferred()=>Queue.Enqueue(action);
        public static void Flush(){while(Queue.TryDequeue(out var action))action();}
    }
    public static class GD {public static void PushError(string text){}}
}
