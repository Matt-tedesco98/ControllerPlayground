using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Pipes;
using ControllerPlayground.Input;
using ControllerPlayground.Input.Keyboard;

internal static class KeyboardMediatedTests {
    private sealed class Source : IControllerInputSource {
        public ControllerInputFrame Frame = new(Array.Empty<ControllerDeviceSnapshotV2>(), Array.Empty<ControllerCaptureEventV2>());
        public ControllerInputFrame ReadFrame() => Frame;
    }
    private static void Check(bool value, string message = "mediated assertion failed") { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition) {
        using var timeout = new CancellationTokenSource(5000);
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Pair : IAsyncDisposable {
        private readonly KeyboardDuplexConnection _shellWire, _hostWire;
        private readonly CancellationTokenSource _stop = new();
        public readonly CancellationTokenSource Request = new();
        public readonly ConcurrentQueue<string> Trace = new();
        public readonly ConcurrentQueue<ControllerAction> Executed = new();
        public readonly ConcurrentQueue<ControllerFamily> Glyphs = new();
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly KeyboardLifecycleTransport Host;
        public readonly Task<KeyboardLifecycleResult> ClientTask, HostTask;
        public readonly Guid ShellCollector = Guid.NewGuid(), HostCollector = Guid.NewGuid();
        public ImmutableArray<KeyboardDeviceRef> ShellDevices, CaptureDevices;
        public TaskCompletionSource? BlockUi;
        public TaskCompletionSource? BlockInventory;
        public Func<Action, bool>? SavedAuthorization;
        public bool Shift;
        public string Text = "";
        private ulong _captureSequence;
        public KeyboardDeviceRef Xbox => CaptureDevices.First(d => d.Family == ControllerFamily.Xbox);
        public KeyboardDeviceRef PlayStation => CaptureDevices.First(d => d.Family == ControllerFamily.PlayStation);
        private Pair(NamedPipeServerStream server, NamedPipeClientStream client, bool playStation) {
            KeyboardDeviceRef Device(Guid collector, byte id) => new(collector, Program.Id(id).ToString(), Program.Id(id).ToString(), 1,
                id == 1 ? ControllerFamily.Xbox : ControllerFamily.PlayStation);
            ShellDevices = ImmutableArray.Create(Device(ShellCollector,1),Device(ShellCollector,2));
            CaptureDevices = ImmutableArray.Create(Device(HostCollector,1),Device(HostCollector,2));
            _hostWire = new(server); _shellWire = new(client);
            KeyboardLifecycleTransport? host = null;
            host = Host = KeyboardLifecycleTransport.Host(_hostWire, Guid.NewGuid(), (_,_,_) =>
                Task.FromResult(new KeyboardReady(1,1,HostCollector,CaptureDevices)), (_,_) => Task.CompletedTask,
                timeouts:new(TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(100),TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(1)), trace:Trace.Enqueue, controllers:new() {
                    HostOwnerChanged = (binding,current,token) => {
                        Check(current()); Glyphs.Enqueue(binding.ShellDevice.Family); return Task.CompletedTask;
                    },
                    HostExecute = async (input,authorize,token) => {
                        SavedAuthorization = authorize;
                        if (BlockUi is { } block) await block.Task.WaitAsync(token);
                        return authorize(() => {
                            Executed.Enqueue(input.Action);
                            if(input.Action==ControllerAction.RightTrigger)Shift=!Shift;
                            if(input.Action==ControllerAction.Accept){Text+=Shift?"Q":"q";Shift=false;}
                            if(input.Action==ControllerAction.Menu)host!.Commit(host.SessionId,Text);
                            if(input.Action==ControllerAction.Back)host!.Cancel(host.SessionId);
                        });
                    }
                });
            var shell = KeyboardLifecycleTransport.Client(_shellWire,Guid.NewGuid(),new("",ShellDevices[playStation?1:0]),_=>true,
                (_,_)=>{Ready.TrySetResult();return Task.FromResult(true);}, KeyboardProtocol.RequiredCapabilities,
                timeouts:new(TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(100),TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(1)), trace:Trace.Enqueue, controllers:new() {
                    ShellDevices = async token=>{if(BlockInventory is {} block)await block.Task.WaitAsync(token);return ShellDevices;},
                    ShellOwnerChanged = (_,_)=>Task.FromResult(true)
                });
            HostTask=Host.RunAsync(_stop.Token); ClientTask=shell.RunAsync(Request.Token);
        }
        public static async Task<Pair> Create(bool playStation=true) {
            string name="ControllerPlayground.MediatedTests."+Guid.NewGuid().ToString("N");
            var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            using var timeout=new CancellationTokenSource(5000);
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token),client.ConnectAsync(timeout.Token));
            var pair=new Pair(server,client,playStation); await pair.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(()=>pair.Host.ControllerOwner!=null); await pair.Arm(); return pair;
        }
        public void Publish(IEnumerable<(KeyboardDeviceRef Device, ControllerAction Action)>? actions=null,bool neutral=true,bool foreground=true) {
            var values=(actions??Array.Empty<(KeyboardDeviceRef,ControllerAction)>()).Select(a=>
                new KeyboardCapturedAction(a.Device,++_captureSequence,a.Action,false,100,(long)_captureSequence)).ToImmutableArray();
            Host.PublishCapture(new(Host.SessionId,HostCollector,CaptureDevices,
                neutral?CaptureDevices.ToImmutableHashSet():ImmutableHashSet<KeyboardDeviceRef>.Empty,foreground,values));
        }
        public async Task Arm() {
            int old=Trace.Count(t=>t.Contains("receive=KeyboardOwnerReady"));
            using var timeout=new CancellationTokenSource(5000);
            do {Publish();await Task.Delay(15,timeout.Token);}while(Trace.Count(t=>t.Contains("receive=KeyboardOwnerReady"))==old);
        }
        public async ValueTask DisposeAsync() {
            Request.Cancel(); _stop.Cancel(); BlockUi?.TrySetResult();
            await _hostWire.DisposeAsync();await _shellWire.DisposeAsync();
            await Task.WhenAll(ClientTask,HostTask).WaitAsync(TimeSpan.FromSeconds(5));
            Request.Dispose();_stop.Dispose();
        }
    }
    public static async Task<int> Run() {
        int count=0;
        async Task Test(string name,Func<Task> body){await body();count++;Console.WriteLine("PASS mediated: "+name);}
        await Test("full endpoint/root bindings are unique and collector generations remain independent",()=>{
            var a=new KeyboardDeviceRef(Guid.NewGuid(),Program.Id(1).ToString(),Program.Id(1).ToString(),3,ControllerFamily.Xbox);
            var b=a with {CollectorId=Guid.NewGuid(),Generation=7};
            var binding=KeyboardControllerBinding.Resolve(a,new[]{a},new[]{b});Check(binding?.CaptureDevice==b);
            Check(KeyboardControllerBinding.Resolve(a,new[]{a},new[]{b,b})==null);
            Check(KeyboardControllerBinding.Resolve(a,new[]{a},new[]{b with {DeviceId=Program.Id(2).ToString()}})==null);
            Check(KeyboardControllerBinding.Resolve(a,new[]{a},new[]{b with {RootId=Program.Id(2).ToString()}})==null);
            return Task.CompletedTask;
        });
        await Test("simultaneous modifier is retained and ordered before Accept",()=>{
            var device=new ControllerDevice(Program.Id(1),Program.Id(1),1,0,0,ControllerFamily.Xbox);Guid collector=Guid.NewGuid();
            var actions=new[]{new ControllerActionEvent(collector,1,device,ControllerAction.Accept,100,1,false),
                new ControllerActionEvent(collector,2,device,ControllerAction.RightTrigger,100,1,false)};
            ulong seq=0;var batch=KeyboardControllerBinding.OrderBatch(actions,ref seq);
            Check(batch.Length==2&&batch[0].Action==ControllerAction.RightTrigger&&batch[1].Action==ControllerAction.Accept&&batch[1].Sequence==2);
            return Task.CompletedTask;
        });
        await Test("managed capture retains RT plus button and RT plus direction with existing repeat timing",()=>{
            var source = new Source(); long now = 0; var service = new ControllerService(source, () => now);
            source.Frame = new(new[]{Program.Sample(1)},Array.Empty<ControllerCaptureEventV2>()); service.PollActions();
            source.Frame = new(new[]{Program.Sample(1,time:2,buttons:(uint)ControllerButtons.Accept,trigger:1)},Array.Empty<ControllerCaptureEventV2>());
            Check(service.PollActions().Select(a=>a.Action).SequenceEqual(new[]{ControllerAction.Accept,ControllerAction.RightTrigger}));
            source.Frame = new(new[]{Program.Sample(1,time:3)},Array.Empty<ControllerCaptureEventV2>());service.PollActions();
            source.Frame = new(new[]{Program.Sample(1,time:4,buttons:(uint)ControllerButtons.DpadRight,trigger:1)},Array.Empty<ControllerCaptureEventV2>());
            Check(service.PollActions().Select(a=>a.Action).SequenceEqual(new[]{ControllerAction.RightTrigger,ControllerAction.NavigateRight}));
            now=399;Check(service.PollActions().Count==0);now=400;
            var repeat=service.PollActions();Check(repeat.Count==1&&repeat[0].Action==ControllerAction.NavigateRight&&repeat[0].IsRepeat);
            source.Frame=new(new[]{Program.Sample(1,time:5)},Array.Empty<ControllerCaptureEventV2>());service.PollActions();now=1000;Check(service.PollActions().Count==0);
            return Task.CompletedTask;
        });
        await Test("shell handoff transfer is scoped and cannot revive a stale generation",()=>{
            var xbox=new ControllerDevice(Program.Id(1),Program.Id(1),1,0,0,ControllerFamily.Xbox);
            var ps=new ControllerDevice(Program.Id(2),Program.Id(2),1,0,0,ControllerFamily.PlayStation);
            var router=new ControllerInputRouter(Guid.NewGuid(),()=>{});router.UpdateDevices(new[]{xbox,ps});
            router.Push(ControllerScopeKind.Friends,ps);router.Push(ControllerScopeKind.Chat);var handoff=router.Push(ControllerScopeKind.KeyboardHandoff);
            Check(!router.TransferKeyboardOwner(Guid.NewGuid(),xbox));Check(!router.TransferKeyboardOwner(handoff.Id,xbox with {ConnectionGeneration=2}));
            Check(router.TransferKeyboardOwner(handoff.Id,xbox)&&router.Scopes.All(s=>s.Owner==xbox));
            router.Remove(handoff.Id);Check(!router.TransferKeyboardOwner(handoff.Id,ps));return Task.CompletedTask;
        });
        await Test("PlayStation owns execution and glyphs while Xbox buttons and Guide/View are discarded",async()=>{
            await using var p=await Pair.Create();Check(p.Glyphs.Last()==ControllerFamily.PlayStation);
            p.Publish(new[]{(p.Xbox,ControllerAction.Accept),(p.Xbox,ControllerAction.Menu),(p.Xbox,ControllerAction.Back),
                (p.Xbox,ControllerAction.RightTrigger),(p.Xbox,ControllerAction.Guide),(p.PlayStation,ControllerAction.View),(p.PlayStation,ControllerAction.Accept)});
            await Until(()=>p.Executed.Count==1);Check(p.Executed.Single()==ControllerAction.Accept&&p.Text=="q"&&!p.ClientTask.IsCompleted);
        });
        await Test("RT plus Accept executes both once and commit returns the shifted exact draft",async()=>{
            await using var p=await Pair.Create(false);
            p.Publish(new[]{(p.Xbox,ControllerAction.RightTrigger),(p.Xbox,ControllerAction.Accept)});
            await Until(()=>p.Executed.Count==2);Check(p.Text=="Q");
            p.Publish(new[]{(p.Xbox,ControllerAction.Menu)});
            var result=await p.ClientTask.WaitAsync(TimeSpan.FromSeconds(5));Check(result.Status==KeyboardLifecycleStatus.Committed&&result.Text=="Q");
            Check(!p.SavedAuthorization!(()=>throw new Exception("late callback executed")));
        });
        await Test("disconnect revokes a queued UI ticket; fresh other-controller Accept claims without typing",async()=>{
            await using var p=await Pair.Create();p.BlockUi=new(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Publish(new[]{(p.PlayStation,ControllerAction.Accept)});await Until(()=>p.SavedAuthorization!=null);
            p.CaptureDevices=ImmutableArray.Create(p.Xbox);p.Publish();p.BlockUi.TrySetResult();
            await Task.Delay(70);Check(p.Executed.IsEmpty);
            p.Publish(new[]{(p.Xbox,ControllerAction.Accept)});
            await Until(()=>p.Glyphs.Last()==ControllerFamily.Xbox);await p.Arm();Check(p.Executed.IsEmpty);
            p.Publish(new[]{(p.Xbox,ControllerAction.Accept)});await Until(()=>p.Executed.Count==1);
        });
        await Test("reconnection cannot silently restore a lease and requires neutral plus a fresh claim",async()=>{
            await using var p=await Pair.Create();var old=p.PlayStation;
            p.CaptureDevices=ImmutableArray.Create(p.Xbox);p.Publish();await Task.Delay(50);
            var next=old with {Generation=2};p.CaptureDevices=p.CaptureDevices.Add(next);
            p.ShellDevices=p.ShellDevices.Select(d=>d.Family==ControllerFamily.PlayStation?d with {Generation=2}:d).ToImmutableArray();
            p.Publish();await Task.Delay(50);Check(p.Glyphs.Count==1);
            p.Publish(new[]{(next,ControllerAction.Accept)});await Until(()=>p.Glyphs.Count==2);
            p.Publish(new[]{(next,ControllerAction.Accept)},neutral:false);await Task.Delay(40);Check(p.Executed.IsEmpty);
            await p.Arm();p.Publish(new[]{(next,ControllerAction.Accept)});await Until(()=>p.Executed.Count==1);
        });
        await Test("old queued UI completion cannot abort a newer owner epoch",async()=>{
            await using var p=await Pair.Create();p.BlockUi=new(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Publish(new[]{(p.PlayStation,ControllerAction.Accept)});await Until(()=>p.SavedAuthorization!=null);
            p.CaptureDevices=ImmutableArray.Create(p.Xbox);p.Publish();await Task.Delay(60);
            p.Publish(new[]{(p.Xbox,ControllerAction.Accept)});await Until(()=>p.Glyphs.Last()==ControllerFamily.Xbox);await p.Arm();
            p.BlockUi.TrySetResult();await Task.Delay(50);Check(p.Executed.IsEmpty&&!p.ClientTask.IsCompleted);
            p.Publish(new[]{(p.Xbox,ControllerAction.Accept)});await Until(()=>p.Executed.Count==1);
        });
        await Test("foreground loss revokes queued callbacks and stale actions are never replayed",async()=>{
            await using var p=await Pair.Create();p.BlockUi=new(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Publish(new[]{(p.PlayStation,ControllerAction.Accept)});await Until(()=>p.SavedAuthorization!=null);
            p.Publish(foreground:false);p.BlockUi.TrySetResult();await Task.Delay(80);Check(p.Executed.IsEmpty);
            Check(!p.SavedAuthorization!(()=>throw new Exception("revoked callback executed")));
        });
        await Test("shell-side owner removal prevents forwarding despite delayed helper inventory",async()=>{
            await using var p=await Pair.Create();p.ShellDevices=p.ShellDevices.Where(d=>d.Family!=ControllerFamily.PlayStation).ToImmutableArray();
            p.Publish(new[]{(p.PlayStation,ControllerAction.Accept)});await Task.Delay(90);Check(p.Executed.IsEmpty);
            Check(p.Trace.Any(t=>t.Contains("KeyboardSuspend")));
        });
        await Test("session cancellation seals the result and blocks queued execution",async()=>{
            await using var p=await Pair.Create();p.BlockUi=new(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Publish(new[]{(p.PlayStation,ControllerAction.Accept)});await Until(()=>p.SavedAuthorization!=null);
            p.Request.Cancel();var result=await p.ClientTask.WaitAsync(TimeSpan.FromSeconds(5));p.BlockUi.TrySetResult();
            Check(result.Status==KeyboardLifecycleStatus.Cancelled&&result.Text==null&&p.Executed.IsEmpty);
            Check(!p.SavedAuthorization!(()=>throw new Exception("cancelled callback executed")));
        });
        await Test("an expired UI ticket fails the session instead of leaving a silent dead keyboard",async()=>{
            await using var p=await Pair.Create();p.BlockUi=new(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Publish(new[]{(p.PlayStation,ControllerAction.Accept)});await Until(()=>p.SavedAuthorization!=null);
            await Task.Delay(300);p.BlockUi.TrySetResult();
            Check((await p.ClientTask.WaitAsync(TimeSpan.FromSeconds(5))).Status==KeyboardLifecycleStatus.Failed&&p.Executed.IsEmpty);
        });
        await Test("owner Back cancels while non-owner Back cannot terminate",async()=>{
            await using var p=await Pair.Create();p.Publish(new[]{(p.Xbox,ControllerAction.Back)});await Task.Delay(50);Check(!p.ClientTask.IsCompleted);
            p.Publish(new[]{(p.PlayStation,ControllerAction.Back)});Check((await p.ClientTask.WaitAsync(TimeSpan.FromSeconds(5))).Status==KeyboardLifecycleStatus.Cancelled);
        });
        await Test("the UI authorization closure cannot execute the same ticket twice",async()=>{
            await using var p=await Pair.Create();p.Publish(new[]{(p.PlayStation,ControllerAction.Accept)});await Until(()=>p.Executed.Count==1);
            Check(!p.SavedAuthorization!(()=>throw new Exception("duplicate execution")));
            Check(p.Executed.Count==1&&!p.ClientTask.IsCompleted);
        });
        await Test("an unresponsive shell inventory callback has a bounded failure deadline",async()=>{
            await using var p=await Pair.Create();p.BlockInventory=new(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Publish(new[]{(p.PlayStation,ControllerAction.Accept)});
            Check((await p.ClientTask.WaitAsync(TimeSpan.FromSeconds(3))).Status==KeyboardLifecycleStatus.Failed&&p.Executed.IsEmpty);
        });
        return count;
    }
}
