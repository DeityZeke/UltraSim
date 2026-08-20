using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UltraSim.Network;
using UltraSim.Server;
using Xunit;

namespace Server.Tests
{
    public sealed class ServerRuntimeAutosaveTests : System.IDisposable
    {
        private sealed class TestSaveSnapshot : IServerRuntimeSaveSnapshot
        {
        }

        private sealed class FakeTransport : INetworkTransport
        {
            public void Start() { }
            public void Stop() { }
            public int PollInbound(System.Span<InboundDatagram> destination) => 0;
            public void EnqueueOutbound(in OutboundDatagram datagram) { }
            public void FlushOutbound() { }
            public void CompleteInboundConsume() { }
            public void Dispose() { }
        }

        public ServerRuntimeAutosaveTests() => ServerTestRuntime.ResetStatics();
        public void Dispose() => ServerTestRuntime.ResetStatics();

        [Fact]
        public void Tick_AutoSaveInterval_TriggersAndResetsOnManualSave()
        {
            int saveCalls = 0;
            var runtime = new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "autosave_test",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false,
                    AutoSaveEverySeconds: 1f),
                hooks: new ServerRuntimeHooks
                {
                    Save = _ => saveCalls++,
                });

            runtime.Start();

            runtime.Tick(0.40f);
            runtime.Tick(0.50f);
            Assert.Equal(0, saveCalls);

            runtime.Tick(0.20f);
            Assert.Equal(1, saveCalls);

            runtime.Tick(0.30f);
            Assert.Equal(1, saveCalls);

            Assert.True(runtime.Save());
            Assert.Equal(2, saveCalls);

            runtime.Tick(0.70f);
            Assert.Equal(2, saveCalls);

            runtime.Tick(0.40f);
            Assert.Equal(3, saveCalls);

            runtime.Stop();
        }

        [Fact]
        public void RequestSave_DefersSaveUntilEndOfNextTick()
        {
            int saveCalls = 0;
            var runtime = new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "barrier_test",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false),
                hooks: new ServerRuntimeHooks
                {
                    Save = _ => saveCalls++,
                });

            runtime.Start();

            // RequestSave must not execute the save immediately.
            runtime.RequestSave();
            Assert.Equal(0, saveCalls);

            // Save executes at the end-of-tick barrier inside the next Tick() call.
            runtime.Tick(0.016f);
            Assert.Equal(1, saveCalls);

            // A second Tick() without a new request must not re-save.
            runtime.Tick(0.016f);
            Assert.Equal(1, saveCalls);

            // A second RequestSave fires again on the next Tick().
            runtime.RequestSave();
            runtime.Tick(0.016f);
            Assert.Equal(2, saveCalls);

            runtime.Stop();
        }

        [Fact]
        public async Task RequestSave_WithAsyncSnapshotHooks_QueuesBackgroundWrite_And_StopWaitsForCompletion()
        {
            int saveCalls = 0;
            int captureCalls = 0;
            int writeCalls = 0;
            int tickCalls = 0;
            using var writeStarted = new ManualResetEventSlim(false);
            using var allowWriteComplete = new ManualResetEventSlim(false);

            var runtime = new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "async_save_test",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false),
                hooks: new ServerRuntimeHooks
                {
                    Tick = (_, _) => Interlocked.Increment(ref tickCalls),
                    Save = _ => Interlocked.Increment(ref saveCalls),
                    CaptureSaveSnapshot = _ =>
                    {
                        Interlocked.Increment(ref captureCalls);
                        return new TestSaveSnapshot();
                    },
                    WriteSaveSnapshot = _ =>
                    {
                        Interlocked.Increment(ref writeCalls);
                        writeStarted.Set();
                        allowWriteComplete.Wait();
                    },
                });

            runtime.Start();
            runtime.RequestSave();
            runtime.Tick(0.016f);

            Assert.Equal(1, saveCalls);
            Assert.Equal(1, captureCalls);
            Assert.Equal(1, tickCalls);
            Assert.True(writeStarted.Wait(1000));

            runtime.Tick(0.016f);
            Assert.Equal(2, tickCalls);

            Task stopTask = Task.Run(runtime.Stop);
            Assert.False(await Task.WhenAny(stopTask, Task.Delay(100)) == stopTask);

            allowWriteComplete.Set();
            Assert.True(await Task.WhenAny(stopTask, Task.Delay(1000)) == stopTask);
            Assert.Equal(1, writeCalls);
        }

        [Fact]
        public void RequestSave_WhileAsyncWriteIsRunning_CoalescesToOneFollowUpSave()
        {
            int captureCalls = 0;
            int writeCalls = 0;
            using var firstWriteStarted = new ManualResetEventSlim(false);
            using var allowFirstWriteComplete = new ManualResetEventSlim(false);
            using var firstWriteCompleted = new ManualResetEventSlim(false);

            var runtime = new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "async_save_coalesce_test",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false),
                hooks: new ServerRuntimeHooks
                {
                    CaptureSaveSnapshot = _ =>
                    {
                        Interlocked.Increment(ref captureCalls);
                        return new TestSaveSnapshot();
                    },
                    WriteSaveSnapshot = _ =>
                    {
                        int call = Interlocked.Increment(ref writeCalls);
                        if (call == 1)
                        {
                            firstWriteStarted.Set();
                            allowFirstWriteComplete.Wait();
                            firstWriteCompleted.Set();
                        }
                    },
                });

            runtime.Start();

            runtime.RequestSave();
            runtime.Tick(0.016f);
            Assert.True(firstWriteStarted.Wait(1000));
            Assert.Equal(1, captureCalls);

            runtime.RequestSave();
            runtime.RequestSave();
            runtime.Tick(0.016f);
            Assert.Equal(1, captureCalls);

            allowFirstWriteComplete.Set();
            Assert.True(firstWriteCompleted.Wait(1000));

            Stopwatch secondCaptureTimer = Stopwatch.StartNew();
            while (captureCalls < 2 && secondCaptureTimer.ElapsedMilliseconds < 500)
            {
                runtime.Tick(0.016f);
                Thread.Sleep(5);
            }

            Assert.Equal(2, captureCalls);
            Assert.Equal(2, writeCalls);

            runtime.Stop();
        }
    }
}
