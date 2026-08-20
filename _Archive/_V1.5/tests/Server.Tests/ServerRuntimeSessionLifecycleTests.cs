using UltraSim.Network;
using UltraSim.Server;
using UltraSim.Server.Gameplay;
using UltraSim.Shared.Gameplay;
using Xunit;

namespace Server.Tests
{
    /// <summary>
    /// Regression tests for session lifecycle edge cases identified in review pass 1:
    ///   1. Quit-to-character-select: Ready → Authenticated transition unbinds entity and clears spawn state.
    ///   2. Disconnect during character select: clean disconnection from Authenticated (never-Ready) state.
    ///   3. Autosave during disconnect: save fires after disconnect lifecycle is processed, observing post-disconnect ECS state.
    ///   4. Archetype parity fallback: null requestedPrefabId uses connection-id parity to choose archetype.
    /// </summary>
    public sealed class ServerRuntimeSessionLifecycleTests : System.IDisposable
    {
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

        public ServerRuntimeSessionLifecycleTests() => ServerTestRuntime.ResetStatics();
        public void Dispose() => ServerTestRuntime.ResetStatics();

        // Advances a fresh session through Connecting → Handshaking → Authenticated.
        private static long SimulateLogin(NetworkSessionManager sessions, int connectionId)
        {
            sessions.TryCreateSession(connectionId, nowTicks: 0, out long sessionId, out _);
            sessions.TryMarkHandshaking(sessionId, nowTicks: 0);
            sessions.TryMarkAuthenticated(sessionId, nowTicks: 0);
            return sessionId;
        }

        [Fact]
        public void QuitToCharacterSelect_UnbindsEntityAndClearsSessionSlot()
        {
            var runtime = new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "qtcs_test",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false));

            runtime.Start();

            const int connectionId = 1;
            long sessionId = SimulateLogin(runtime.Sessions, connectionId);

            // Set pending spawn metadata (simulates what the host sets before marking ready).
            runtime.SetPendingSessionSpawnPrefabId(sessionId, PlayerArchetypeIds.DefaultMale);
            runtime.SetPendingSessionSpawnDisplayName(sessionId, "TestPlayer");

            // Transition to Ready — the next Tick drains the lifecycle event and binds the entity.
            runtime.Sessions.TryMarkReady(sessionId, nowTicks: 0);
            runtime.Tick(0.016f);

            Assert.True(runtime.PlayerWorld.TryGetEntityForSession(sessionId, out _));
            Assert.Equal(1, runtime.PlayerWorld.PlayerCount);

            // Quit-to-character-select: Ready → Authenticated.
            runtime.TryReturnSessionToAuthenticated(sessionId, nowTicks: 0);
            runtime.Tick(0.016f);

            // Entity must be unbound; spawn slot must be freed.
            Assert.False(runtime.PlayerWorld.TryGetEntityForSession(sessionId, out _));
            Assert.Equal(0, runtime.PlayerWorld.PlayerCount);
            Assert.Equal(0, runtime.PlayerWorld.Bindings.SessionBindingCount);

            runtime.Stop();
        }

        [Fact]
        public void Disconnect_DuringCharacterSelect_NeverCreatesEntity()
        {
            var runtime = new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "disc_cs_test",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false));

            runtime.Start();

            const int connectionId = 2;
            long sessionId = SimulateLogin(runtime.Sessions, connectionId);

            // Simulate host setting spawn state while player is on character-select screen.
            runtime.SetPendingSessionSpawnPrefabId(sessionId, PlayerArchetypeIds.DefaultMale);
            runtime.SetPendingSessionSpawnDisplayName(sessionId, "TestPlayer");

            // Disconnect before ever transitioning to Ready — entity must never be spawned.
            runtime.Sessions.TryDisconnectSession(sessionId, SessionDisconnectReason.Timeout, nowTicks: 0);

            runtime.Tick(0.016f);

            Assert.Equal(0, runtime.PlayerWorld.Bindings.SessionBindingCount);
            Assert.Equal(0, runtime.PlayerWorld.PlayerCount);

            runtime.Stop();
        }

        [Fact]
        public void RequestSave_DuringTick_WithConcurrentDisconnect_ObservesPostDisconnectState()
        {
            int savedPlayerCount = -1;

            var runtime = new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "save_disc_test",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false),
                hooks: new ServerRuntimeHooks
                {
                    Save = r => savedPlayerCount = r.PlayerWorld.PlayerCount,
                });

            runtime.Start();

            const int connectionId = 3;
            long sessionId = SimulateLogin(runtime.Sessions, connectionId);
            runtime.Sessions.TryMarkReady(sessionId, nowTicks: 0);
            runtime.Tick(0.016f);

            Assert.Equal(1, runtime.PlayerWorld.PlayerCount);

            // Disconnect and request save before the next tick.
            // Disconnect lifecycle event is drained at tick start; save fires at end-of-tick barrier.
            runtime.Sessions.TryDisconnectSession(sessionId, SessionDisconnectReason.Timeout, nowTicks: 0);
            runtime.RequestSave();

            runtime.Tick(0.016f);

            // The save hook runs after the disconnect lifecycle event is processed,
            // so it must observe zero bound players.
            Assert.Equal(0, savedPlayerCount);

            runtime.Stop();
        }

        [Fact]
        public void BindSession_WithoutPrefabId_UsesConnectionIdParityFallback()
        {
            // Even connection IDs → DefaultFemale; odd → DefaultMale (per parity comment in ResolveSpawnDefinition).
            var world = new ServerPlayerSessionWorld();

            world.BindSession(sessionId: 1, connectionId: 2);  // even
            Assert.True(world.TryGetPrefabIdForSession(1, out var evenPrefab));
            Assert.Equal(PlayerArchetypeIds.DefaultFemale, evenPrefab.PrefabId);

            world.BindSession(sessionId: 2, connectionId: 3);  // odd
            Assert.True(world.TryGetPrefabIdForSession(2, out var oddPrefab));
            Assert.Equal(PlayerArchetypeIds.DefaultMale, oddPrefab.PrefabId);
        }
    }
}
