using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ControllerPlayground.Input.Keyboard;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel;

namespace ControllerPlayground.Services;

// Explicit Debug opt-in only. Exercises real package-to-package lifecycle IPC
// with disposable text, never a chat field, input injection, or a sent message.
internal static class KeyboardV2Smoke {
    public static async Task RunIfRequested(KeyboardHostClient client, DispatcherQueue dispatcher, Action<string> log, CancellationToken stop) {
#if DEBUG
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "phase2-smoke.log"))) return;
        try {
            await Task.Delay(1200, stop);
            async Task<bool> OnUi(Func<bool> action, CancellationToken token) {
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!dispatcher.TryEnqueue(() => {
                    try { done.TrySetResult(!token.IsCancellationRequested && action()); }
                    catch (Exception) { done.TrySetResult(false); }
                })) done.TrySetResult(false);
                return await done.Task.WaitAsync(token);
            }
            void Check(bool valid, string name) { log($"PHASE2_SMOKE {(valid ? "PASS" : "FAIL")} {name}"); if (!valid) throw new IOException("Smoke failed: " + name); }
            async Task CancelSession(string label) {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop);
                bool ready = false, closed = false;
                var result = await client.RequestV2TextAsync("Phase 2 disposable smoke text", null, async (activate, token) => {
                    ready = await OnUi(activate, token); cancel.CancelAfter(200); return ready;
                }, trace: line => { log(line); if(line.Contains("receive=KeyboardClosed"))closed=true; }, cancellationToken: cancel.Token);
                Check(ready && closed && result.Status == KeyboardLifecycleStatus.Cancelled, label);
                await Task.Delay(300, stop);
            }
            await CancelSession("open-ready-cancel-close");
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop)) {
                bool lateReady = false; cancel.CancelAfter(100);
                var result = await client.RequestV2TextAsync("startup cancellation", null, (_,_) => {lateReady=true;return Task.FromResult(true);}, trace:log,cancellationToken:cancel.Token);
                Check(result.Status==KeyboardLifecycleStatus.Cancelled&&!lateReady,"cancel-before-ready");
            }
            await Task.Delay(400, stop);
            uint helper = 0;
            var exited = await client.RequestV2TextAsync("helper exit",null,async (activate,token)=>{
                bool focused=await OnUi(activate,token);
                _=Task.Run(async()=>{
                    await Task.Delay(150,stop);
                    using var process=Process.GetProcessById((int)helper);
                    var policy=new KeyboardPeerPolicy("2d72be25-6768-4b4e-96a4-f3705bd815ff_"+Package.Current.Id.PublisherId,"ControllerPlayground.KeyboardHost.exe");
                    if(policy.Accepts(process,process.MainModule!.FileName))process.CloseMainWindow();
                },stop);
                return focused;
            },trace:log,cancellationToken:stop,peerVerified:pid=>helper=pid);
            Check(exited.Status==KeyboardLifecycleStatus.Failed,"helper-exit-detected");
            await Task.Delay(700,stop);
            await CancelSession("relaunch-after-helper-exit");
            await Malformed(client,log,stop); await Task.Delay(500,stop);
            await CancelSession("recovery-after-malformed-frame");
            using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(stop)) {
                bool ready=false;
                var result=await client.RequestTextAsync("legacy smoke",_=>{ready=true;cancel.CancelAfter(200);},cancel.Token);
                Check(ready&&result==null,"legacy-open-cancel-still-works");
            }
            log("PHASE2_SMOKE COMPLETE");
        } catch(Exception error) {log("PHASE2_SMOKE ERROR "+error.GetType().Name+" "+error.Message);}
#else
        await Task.CompletedTask;
#endif
    }
    private static async Task Malformed(KeyboardHostClient client,Action<string>log,CancellationToken stop) {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop);timeout.CancelAfter(6000);
        using var pipe=new NamedPipeClientStream(".",KeyboardPipeNames.Version2,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(1500,timeout.Token);
        using var peer=KeyboardPipePeer.Verify(pipe,new("2d72be25-6768-4b4e-96a4-f3705bd815ff_"+Package.Current.Id.PublisherId,"ControllerPlayground.KeyboardHost.exe"));
        await using var wire=new KeyboardFramedStream(pipe,true);
        var hello=new KeyboardEnvelope(new(2,Guid.NewGuid(),Guid.NewGuid(),Guid.Empty,Guid.Empty,KeyboardPeer.Shell,0,1),new KeyboardHello(2,2,KeyboardProtocol.LifecycleCapabilities));
        await wire.WriteAsync(hello,timeout.Token);
        var identity=KeyboardConnectionIdentity.Negotiate(hello,(await wire.ReadAsync(timeout.Token))!);
        var session=new KeyboardSession(identity,Guid.NewGuid(),KeyboardPeer.Shell);ulong sequence=1;
        KeyboardEnvelope Message(KeyboardMessage body)=>new(new(2,identity.ConnectionId,identity.ShellInstanceId,identity.HostInstanceId,session.SessionId,KeyboardPeer.Shell,1,sequence++),body);
        var open=Message(new KeyboardOpen("malformed cleanup smoke",null));session.Apply(open);await wire.WriteAsync(open,timeout.Token);
        while(true) {
            var frame=await wire.ReadAsync(timeout.Token)??throw new IOException("Missing smoke Ready.");session.Apply(frame);
            if(frame.Message is KeyboardPing ping){var pong=Message(new KeyboardPong(ping.Nonce));session.Apply(pong);await wire.WriteAsync(pong,timeout.Token);}
            if(frame.Message is KeyboardReady ready){if(!peer.ValidateWindow(ready))throw new IOException("Invalid smoke window.");break;}
        }
        await pipe.WriteAsync(new byte[]{0,0,0,0},timeout.Token);
        try {while(await wire.ReadAsync(timeout.Token)!=null){}}catch(IOException){}
        log("PHASE2_SMOKE PASS malformed-frame-peer-closed");
    }
}
