#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.Network;
using UltraSim.Game.Admission;
using UltraSim.Network;

namespace UltraSim.Client.Application.Godot;

public partial class ClientHost
{
    private ClientServerTrustRegistry? _serverTrust;
    private ClientServerTrustFileStore? _serverTrustStore;
    private ClientDirectConnectSession? _remoteSession;
    private ClientProfileDecisionFileStore? _profileDecisionStore;
    private ClientProfileDecisionDocument _profileDecisions = ClientProfileDecisionDocument.Empty;
    private RemoteClientProfileClient? _remoteProfile;
    private RemoteConceptDemoClient? _remotePlayable;
    private string _directEndpoint = "127.0.0.1:7777";
    private string _directFingerprint = string.Empty;
    private string? _trustLoadWarning;
    private string? _profileDecisionLoadWarning;
    private bool _remoteAdmission;
    private bool _remoteMismatch;
    private bool _remoteRetiring;
    private bool _remoteProfileActivated;
    private bool _allowInsecurePrivateContent;
    private bool _remoteSmokeStarted;
    private bool _remoteSmokeDisconnectStarted;
    private bool _remoteSmokeBarrierReadyPublished;
    private bool _remoteProcessObserved;
    private string _remoteGuestName = string.Empty;
    private GameGuestBody _remoteGuestBody = GameGuestBody.HumanFemale;
    private string? _remoteGuestError;
    private ClientDirectConnectState _lastRemoteState;

    private void ProcessRemote(double delta)
    {
        if (!_remoteProcessObserved)
        {
            _remoteProcessObserved = true;
            if (IsRemoteSmoke()) GD.Print("A0.8 remote process loop started");
        }
        TryStartRemoteSmoke();
        if (_remoteSession is null) return;
        try
        {
            _remoteSession.Pump();
            _remoteProfile?.ProcessReceived();
            _remotePlayable?.Pump(ShellSnapshot().Focus.GameplayInputAllowed);
            ClientDirectConnectState current = _remoteSession.State;
            if (_remoteRetiring)
            {
                if (current is ClientDirectConnectState.Closed or ClientDirectConnectState.Failed)
                {
                    _remoteSession.Dispose();
                    _remoteSession = null;
                    _remoteRetiring = false;
                    if (_remoteSmokeDisconnectStarted)
                        GetTree().Quit(0);
                }
                return;
            }
            if (_remotePlayable is not null)
            {
                RemoteConceptDemoClientSnapshot playable = _remotePlayable.GetSnapshot();
                if (playable.State == RemoteConceptDemoClientState.Faulted)
                {
                    string fault = playable.Fault ?? "Remote playable session failed.";
                    GD.PushError($"A0.8 remote playable fault: {fault}");
                    FailSession(BoundedShellMessage(fault));
                    if (IsRemoteSmoke()) GetTree().Quit(1);
                    return;
                }
                if (playable.State == RemoteConceptDemoClientState.AwaitingGuest &&
                    ShellSnapshot().Screen == ClientShellScreen.Loading)
                {
                    if (IsRemoteSmoke())
                    {
                        _ = _remotePlayable.TrySubmitGuest(new GameGuestAdmissionRequest(
                            GameGuestBody.HumanFemale, "A0.8 Native Guest"));
                    }
                    else
                    {
                        BeginAdmission("Required remote content is ready. Choose a temporary guest identity.");
                    }
                }
                if (playable.State == RemoteConceptDemoClientState.Active &&
                    ShellSnapshot().Screen != ClientShellScreen.InSession)
                    CommitSession("Remote Concept Demo active.");
                if (playable.SmokeComplete &&
                    playable.Actors.ActiveActors >= RemoteSmokeExpectedActors() &&
                    !_remoteSmokeDisconnectStarted)
                {
                    if (!SharedRemoteSmokeBarrierReleased()) return;
                    ValidateRemoteSmoke(playable);
                    _remoteSmokeDisconnectStarted = true;
                    GD.Print(
                        $"A0.8 CONTENTLESS ONLINE PASS mode={ReadArgument("--a0-8-expect=") ?? "unspecified"} " +
                        $"catalog={playable.Session.Content.ResolvedCatalogFingerprint} " +
                        $"world={playable.Session.StructuralWorld.WorldPackFingerprint} " +
                        $"chunks={playable.RenderedChunks} triangles={playable.TerrainTriangles} " +
                        $"filesFetched={playable.Session.Content.Acquisition.FilesFetched} " +
                        $"bytesFetched={playable.Session.Content.Acquisition.BytesFetched} " +
                        $"proofs={playable.Session.Content.ProofsQueued} " +
                        $"ui={playable.OpenUiViews} chat={playable.Session.Chat.OccurrencesApplied} " +
                        $"actors={playable.Actors.ActiveActors} primitiveActors=" +
                        $"{playable.Actors.PrimitiveFallbackActors} nameplates=" +
                        $"{playable.Actors.VisibleNameplates} overhead=" +
                        $"{playable.Actors.VisibleOverheadLabels} publicMetadataProofs=" +
                        $"{playable.Session.PublicPresentation.ProofsQueued}");
                    RetireCurrentSession();
                    return;
                }
            }
            if (_remoteProfile is not null && _remotePlayable is null)
            {
                RemoteClientProfileSnapshot profile = _remoteProfile.GetSnapshot();
                if (profile.State == RemoteClientProfileState.Faulted)
                {
                    FailSession(BoundedShellMessage(
                        profile.Detail ?? "The server client profile was rejected."));
                    if (IsRemoteSmoke()) GetTree().Quit(1);
                    return;
                }
                if (profile.State == RemoteClientProfileState.AwaitingDecision)
                {
                    if (IsRemoteSmoke())
                    {
                        if (!_remoteProfile.TryChoose(ClientProfileApplicationMode.UseServerDefaults))
                            throw new InvalidOperationException(
                                _remoteProfile.GetSnapshot().Detail ??
                                "The native smoke profile decision could not be persisted.");
                    }
                    else if (ShellSnapshot().Screen != ClientShellScreen.Admission)
                        BeginAdmission("Review this server's declarative client profile.");
                    return;
                }
                if (profile.State == RemoteClientProfileState.Applied)
                {
                    if (!_remoteProfileActivated)
                    {
                        ActivateServerProfile(
                            _remoteSession.GetSnapshot().SettingsScope,
                            _remoteProfile.Recommendation,
                            profile.Mode);
                        _remoteProfileActivated = true;
                        GD.Print(
                            $"F0.3c CLIENT PROFILE APPLIED realm={profile.Realm} " +
                            $"revision={profile.ProfileRevision} hash={profile.ProfileHash} " +
                            $"mode={profile.Mode} remembered={profile.RememberedDecision} " +
                            $"grammar={EffectiveSettings.ControlGrammar} camera={EffectiveSettings.CameraProfile}");
                    }
                    if (ShellSnapshot().Screen == ClientShellScreen.Settings) return;
                    ClientDirectConnectSnapshot connected = _remoteSession.GetSnapshot();
                    StartRemotePlayable(connected);
                    BeginSessionLoading(
                        $"Client profile {profile.ProfileRevision}/{profile.ProfileHash} applied; " +
                        "acquiring required content…");
                }
            }
            if (current == _lastRemoteState) return;
            _lastRemoteState = current;
            switch (current)
            {
                case ClientDirectConnectState.AwaitingTrust:
                    BeginAdmission("Verify this untrusted deployment-key candidate before continuing.");
                    break;
                case ClientDirectConnectState.Connecting:
                    BeginSessionLoading("Connecting with the selected deployment key pinned…");
                    break;
                case ClientDirectConnectState.AwaitingRealm:
                    BeginSessionLoading("Transport authenticated; verifying application realm…");
                    break;
                case ClientDirectConnectState.Established:
                {
                    ClientDirectConnectSnapshot snapshot = _remoteSession.GetSnapshot();
                    EnsureProfileDecisionStore();
                    _remoteProfile = new RemoteClientProfileClient(
                        _remoteSession.Peer ?? throw new InvalidOperationException(
                            "The authenticated remote peer is absent."),
                        snapshot.ApplicationSessionEpoch,
                        snapshot.Realm,
                        _profileDecisions,
                        _profileDecisionStore!);
                    BeginSessionLoading(
                        $"Connected to realm {snapshot.Realm}; awaiting its declarative client profile…");
                    GD.Print(
                        $"F0.3a DIRECT CONNECT ESTABLISHED endpoint={snapshot.Endpoint} " +
                        $"realm={snapshot.Realm} session={snapshot.ApplicationSessionEpoch} " +
                        $"connection={snapshot.Connection} fingerprint={snapshot.CandidateFingerprint}");
                    break;
                }
                case ClientDirectConnectState.Failed:
                {
                    ClientDirectConnectSnapshot snapshot = _remoteSession.GetSnapshot();
                    if (snapshot.Failure == ClientDirectConnectFailure.TrustedKeyMismatch)
                    {
                        _remoteMismatch = true;
                        BeginAdmission("Connection blocked: the deployment key does not match the saved pin.");
                    }
                    else
                        FailSession(snapshot.Detail ?? $"Direct Connect failed: {snapshot.Failure}.");
                    if (IsRemoteSmoke())
                    {
                        GD.PushError(
                            $"A0.8 Direct Connect smoke failed: {snapshot.Failure}: {snapshot.Detail}");
                        GetTree().Quit(1);
                    }
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            GD.PushError($"A0.8 remote client failure: {exception}");
            FailSession(BoundedShellMessage(
                $"Direct Connect failed safely: {exception.Message}"));
            if (IsRemoteSmoke())
            {
                GetTree().Quit(1);
            }
        }
    }

    private void ExitRemote()
    {
        ReleaseRemotePlayable();
        _remoteSession?.Dispose();
        _remoteSession = null;
        _remoteProfile = null;
        _remoteProfileActivated = false;
    }

    protected override void BeginCapability(ClientCapability capability)
    {
        if (capability != ClientCapability.Multiplayer)
        {
            base.BeginCapability(capability);
            return;
        }
        if (_remoteSession is not null)
        {
            _remoteSession.Dispose();
            _remoteSession = null;
        }
        ReleaseRemotePlayable();
        _remoteProfile = null;
        _remoteProfileActivated = false;
        ClearServerProfile();
        EnsureTrustStore();
        _remoteAdmission = true;
        _remoteMismatch = false;
        _remoteRetiring = false;
        BeginAdmission("Connect directly to an UltraSim deployment.");
    }

    protected override void BuildCapabilityAdmissionSurface(in ClientShellSnapshot snapshot)
    {
        if (!_remoteAdmission)
        {
            base.BuildCapabilityAdmissionSurface(snapshot);
            return;
        }
        if (_remoteMismatch)
        {
            BuildTrustMismatchSurface();
            return;
        }
        if (_remotePlayable?.State == RemoteConceptDemoClientState.AwaitingGuest)
        {
            BuildRemoteGuestSurface();
            return;
        }
        if (_remoteProfile?.GetSnapshot().State == RemoteClientProfileState.AwaitingDecision)
        {
            BuildClientProfileSurface(_remoteProfile.GetSnapshot());
            return;
        }
        if (_remoteSession?.State == ClientDirectConnectState.AwaitingTrust)
        {
            BuildTrustCandidateSurface(_remoteSession.GetSnapshot());
            return;
        }
        BuildDirectConnectSurface();
    }

    protected override void RetireCurrentSession()
    {
        if (_remoteSession is not null)
        {
            // Presentation consumers share the authenticated peer but do not own its transport
            // lifetime. Retire them before Direct Connect begins closing/disposes that peer.
            ReleaseRemotePlayable();
            _remoteProfile = null;
            _remoteProfileActivated = false;
            if (_remoteSession.State == ClientDirectConnectState.Established)
            {
                _remoteSession.BeginDisconnect();
                _remoteRetiring = true;
            }
            else
            {
                _remoteSession.Dispose();
                _remoteSession = null;
                _remoteRetiring = false;
            }
        }
        _remoteAdmission = false;
        _remoteMismatch = false;
        _remoteProfileActivated = false;
        ClearServerProfile();
        base.RetireCurrentSession();
    }

    protected override void OnEffectiveSettingsChanged(ResolvedClientSettings settings)
    {
        base.OnEffectiveSettingsChanged(settings);
        _remotePlayable?.ApplySettings(settings);
    }

    private void BuildClientProfileSurface(in RemoteClientProfileSnapshot profile)
    {
        ClientProfileRecommendation recommendation = _remoteProfile!.Recommendation;
        VBoxContainer column = CreateScrollableCenteredColumn(620);
        column.AddChild(Title("Server Client Profile"));
        column.AddChild(CenteredLabel(
            "This authenticated server recommends stock client controls and camera settings. " +
            "It cannot provide scripts or take away reserved client controls."));
        column.AddChild(new Label
        {
            Text = $"Realm: {profile.Realm}\nRevision: {profile.ProfileRevision}\n" +
                $"Hash: {profile.ProfileHash}\nGrammar: {recommendation.ControlGrammar}\n" +
                $"Camera: {recommendation.CameraProfile}\nDistance: {recommendation.CameraDistance:0.0} m\n" +
                $"FOV: {recommendation.CameraFovDegrees:0.#}°",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        if (!string.IsNullOrWhiteSpace(_profileDecisionLoadWarning))
            column.AddChild(new Label
            {
                Text = _profileDecisionLoadWarning,
                Modulate = new Color(1f, 0.62f, 0.28f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        foreach (ClientPhysicalBinding binding in recommendation.Bindings)
        {
            if (ClientReservedControls.IsReserved(binding.Action)) continue;
            column.AddChild(new Label
            {
                Text = $"{binding.Action}: {binding.Input.Modifiers}{binding.Input.Code}",
            });
        }
        Button defaults = MenuButton("Use Server Defaults");
        defaults.Name = "ClientProfileUseDefaults";
        ConnectDeferred(defaults, () => ChooseRemoteProfile(
            ClientProfileApplicationMode.UseServerDefaults, customize: false));
        column.AddChild(defaults);
        Button keep = MenuButton("Keep My Controls");
        keep.Name = "ClientProfileKeepControls";
        ConnectDeferred(keep, () => ChooseRemoteProfile(
            ClientProfileApplicationMode.KeepMyControls, customize: false));
        column.AddChild(keep);
        Button customize = MenuButton("Customize for This Server");
        customize.Name = "ClientProfileCustomize";
        ConnectDeferred(customize, () => ChooseRemoteProfile(
            ClientProfileApplicationMode.Customize, customize: true));
        column.AddChild(customize);
        Button disconnect = MenuButton("Disconnect / Back");
        ConnectDeferred(disconnect, () =>
        {
            RetireCurrentSession();
            ReturnToMainMenu("Server profile was not accepted.");
        });
        column.AddChild(disconnect);
        defaults.GrabFocus();
    }

    private void ChooseRemoteProfile(ClientProfileApplicationMode mode, bool customize)
    {
        if (_remoteProfile?.TryChoose(mode) != true)
        {
            FailSession(_remoteProfile?.GetSnapshot().Detail ??
                "The server profile decision could not be saved.");
            return;
        }
        RemoteClientProfileSnapshot profile = _remoteProfile.GetSnapshot();
        _profileDecisions = _remoteProfile.DecisionDocument;
        ActivateServerProfile(
            _remoteSession!.GetSnapshot().SettingsScope,
            _remoteProfile.Recommendation,
            profile.Mode);
        _remoteProfileActivated = true;
        if (customize)
        {
            OpenServerProfileCustomization();
            return;
        }
        BeginSessionLoading("Applying the accepted server profile…");
    }

    private void BuildDirectConnectSurface()
    {
        VBoxContainer column = CreateCenteredColumn(520);
        column.AddChild(Title("Direct Connect"));
        column.AddChild(CenteredLabel(
            "The address is a location hint, not server identity. The current Hosted transport accepts IPv4 endpoints."));
        if (!string.IsNullOrWhiteSpace(_trustLoadWarning))
        {
            column.AddChild(new Label
            {
                Text = _trustLoadWarning,
                Modulate = new Color(1f, 0.62f, 0.28f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }
        column.AddChild(new Label { Text = "Address and port" });
        var endpoint = new LineEdit
        {
            Name = "DirectConnectEndpoint",
            Text = _directEndpoint,
            PlaceholderText = "127.0.0.1:7777",
            MaxLength = ClientTrustedDeployment.MaximumEndpointCharacters,
        };
        column.AddChild(endpoint);
        column.AddChild(new Label { Text = "Expected SHA-256 fingerprint (optional invite)" });
        var fingerprint = new LineEdit
        {
            Name = "DirectConnectFingerprint",
            Text = _directFingerprint,
            PlaceholderText = "Leave blank to review the live candidate",
            MaxLength = 96,
        };
        column.AddChild(fingerprint);
        var privateContent = new CheckBox
        {
            Name = "DirectConnectAllowPrivateContent",
            Text = "Allow this deployment to source hash-verified content over private/insecure HTTP",
            ButtonPressed = _allowInsecurePrivateContent,
        };
        column.AddChild(privateContent);
        Button connect = MenuButton("Connect");
        connect.Name = "DirectConnectSubmit";
        ConnectDeferred(connect, () =>
        {
            _allowInsecurePrivateContent = privateContent.ButtonPressed;
            SubmitDirectConnect(endpoint.Text, fingerprint.Text);
        });
        endpoint.TextSubmitted += _ => connect.EmitSignal(BaseButton.SignalName.Pressed);
        fingerprint.TextSubmitted += _ => connect.EmitSignal(BaseButton.SignalName.Pressed);
        column.AddChild(connect);
        Button back = MenuButton("Back");
        back.Name = "DirectConnectBack";
        ConnectDeferred(back, () =>
        {
            _remoteAdmission = false;
            ReturnToMainMenu();
        });
        column.AddChild(back);
        endpoint.GrabFocus();
    }

    private void BuildTrustCandidateSurface(in ClientDirectConnectSnapshot candidate)
    {
        VBoxContainer column = CreateCenteredColumn(620);
        column.AddChild(Title("Trust this server?"));
        column.AddChild(CenteredLabel(
            "This deployment key is self-signed and is not trusted yet. Compare the fingerprint with the host or invite before continuing."));
        column.AddChild(new Label { Text = $"Endpoint: {candidate.Endpoint}" });
        column.AddChild(new Label
        {
            Name = "CandidateFingerprint",
            Text = GroupFingerprint(candidate.CandidateFingerprint.ToString()),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        Button trust = MenuButton("Trust and Continue");
        trust.Name = "TrustCandidate";
        ConnectDeferred(trust, () =>
        {
            if (_remoteSession?.TryAcceptCandidate() != true)
            {
                FailSession("The deployment candidate was no longer available.");
                return;
            }
            _lastRemoteState = ClientDirectConnectState.Connecting;
            BeginSessionLoading("Connecting with the selected deployment key pinned…");
        });
        column.AddChild(trust);
        Button cancel = MenuButton("Cancel");
        ConnectDeferred(cancel, () =>
        {
            RetireCurrentSession();
            ReturnToMainMenu("Server trust was not changed.");
        });
        column.AddChild(cancel);
        trust.GrabFocus();
    }

    private void BuildTrustMismatchSurface()
    {
        ClientDirectConnectSnapshot mismatch = _remoteSession!.GetSnapshot();
        VBoxContainer column = CreateCenteredColumn(620);
        column.AddChild(Title("Server identity changed"));
        column.AddChild(CenteredLabel(
            "The endpoint presented a different deployment key. UltraSim blocked the connection and kept the existing pin."));
        column.AddChild(new Label { Text = $"Endpoint: {mismatch.Endpoint}" });
        column.AddChild(new Label
        {
            Text = $"Untrusted candidate: {GroupFingerprint(mismatch.CandidateFingerprint.ToString())}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        Button forget = MenuButton("Forget Existing Pin");
        forget.Name = "ForgetTrustedServer";
        ConnectDeferred(forget, () =>
        {
            try
            {
                if (_remoteSession?.TryForgetMismatchedTrust() != true)
                {
                    FailSession("The existing deployment pin could not be removed.");
                    return;
                }
                _remoteSession.Dispose();
                _remoteSession = null;
                _remoteMismatch = false;
                _remoteAdmission = false;
                ReturnToMainMenu(
                    "Pin forgotten. Connect again to perform a new trust ceremony.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                FailSession($"The deployment pin could not be removed: {exception.Message}");
            }
        });
        column.AddChild(forget);
        Button cancel = MenuButton("Keep Pin / Main Menu");
        ConnectDeferred(cancel, () =>
        {
            RetireCurrentSession();
            ReturnToMainMenu("Connection blocked; existing deployment pin retained.");
        });
        column.AddChild(cancel);
        cancel.GrabFocus();
    }

    private void SubmitDirectConnect(string endpoint, string fingerprint)
    {
        _directEndpoint = endpoint.Trim();
        _directFingerprint = fingerprint.Trim();
        _remoteSession?.Dispose();
        _remoteSession = new ClientDirectConnectSession(_serverTrust!, _serverTrustStore!);
        if (!_remoteSession.TryStart(_directEndpoint, _directFingerprint))
        {
            ClientDirectConnectSnapshot failed = _remoteSession.GetSnapshot();
            FailSession(failed.Detail ?? $"Direct Connect failed: {failed.Failure}.");
            return;
        }
        _lastRemoteState = ClientDirectConnectState.Discovering;
        BeginSessionLoading("Discovering the deployment-key candidate…");
    }

    private void StartRemotePlayable(in ClientDirectConnectSnapshot connected)
    {
        if (_remotePlayable is not null) return;
        NetworkProtocolPeer peer = _remoteSession?.Peer ??
            throw new InvalidOperationException("The authenticated remote peer is absent.");
        string cacheRoot = ReadArgument("--a0-8-cache=") ??
            ProjectSettings.GlobalizePath("user://content-cache");
        var playable = new RemoteConceptDemoClient { Name = "RemoteConceptDemoSession" };
        playable.Configure(
            peer,
            connected.ApplicationSessionEpoch,
            connected.Realm,
            cacheRoot,
            HudLayer,
            EffectiveSettings,
            _allowInsecurePrivateContent,
            IsRemoteSmoke());
        WorldRoot.AddChild(playable);
        _remotePlayable = playable;
    }

    private void BuildRemoteGuestSurface()
    {
        VBoxContainer column = CreateCenteredColumn(440);
        column.AddChild(Title("Enter Remote Demo"));
        column.AddChild(CenteredLabel(
            "Required World/UI content is verified and applied. This identity exists only for the current session."));
        column.AddChild(new Label { Text = "Name" });
        var name = new LineEdit
        {
            Name = "RemoteGuestDisplayName",
            Text = _remoteGuestName,
            PlaceholderText = "1–24 characters",
            MaxLength = GameGuestAdmissionPolicy.MaximumDisplayNameUtf8Bytes,
        };
        column.AddChild(name);
        column.AddChild(new Label { Text = "Body" });
        var body = new OptionButton { Name = "RemoteGuestBody" };
        body.AddItem("X / Human Female", (int)GameGuestBody.HumanFemale);
        body.AddItem("Y / Human Male", (int)GameGuestBody.HumanMale);
        body.Select(_remoteGuestBody == GameGuestBody.HumanMale ? 1 : 0);
        column.AddChild(body);
        if (!string.IsNullOrWhiteSpace(_remoteGuestError))
            column.AddChild(new Label
            {
                Name = "RemoteGuestAdmissionError",
                Text = _remoteGuestError,
                Modulate = new Color(1f, 0.48f, 0.42f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        Button join = MenuButton("Join");
        join.Name = "RemoteGuestJoin";
        ConnectDeferred(join, () => SubmitRemoteGuest(name.Text, (GameGuestBody)body.GetSelectedId()));
        column.AddChild(join);
        Button back = MenuButton("Disconnect / Back");
        ConnectDeferred(back, () =>
        {
            RetireCurrentSession();
            ReturnToMainMenu("Remote session disconnected.");
        });
        column.AddChild(back);
        name.GrabFocus();
    }

    private void SubmitRemoteGuest(string name, GameGuestBody body)
    {
        _remoteGuestName = name;
        _remoteGuestBody = body;
        var request = new GameGuestAdmissionRequest(body, name);
        if (!GameGuestAdmissionPolicy.TryNormalize(
                request, out GameGuestIdentity identity, out _, out string? error))
        {
            _remoteGuestError = error;
            BeginAdmission(error ?? "The guest identity was rejected.");
            return;
        }
        _remoteGuestError = null;
        if (_remotePlayable?.TrySubmitGuest(new GameGuestAdmissionRequest(
                identity.Body, identity.DisplayName!)) != true)
        {
            _remoteGuestError = "The remote guest request could not be queued.";
            BeginAdmission(_remoteGuestError);
            return;
        }
        BeginSessionLoading("Remote Game admission pending…");
    }

    private void ReleaseRemotePlayable()
    {
        if (_remotePlayable is null) return;
        _remotePlayable.DisposeComposition();
        _remotePlayable.GetParent()?.RemoveChild(_remotePlayable);
        _remotePlayable.QueueFree();
        _remotePlayable = null;
    }

    private void TryStartRemoteSmoke()
    {
        if (_remoteSmokeStarted || !IsRemoteSmoke()) return;
        _remoteSmokeStarted = true;
        string endpoint = ReadArgument("--a0-8-endpoint=") ??
            throw new InvalidOperationException("A0.8 smoke requires --a0-8-endpoint.");
        string fingerprint = ReadArgument("--a0-8-fingerprint=") ??
            throw new InvalidOperationException("A0.8 smoke requires --a0-8-fingerprint.");
        _allowInsecurePrivateContent = OS.GetCmdlineUserArgs().Contains(
            "--a0-8-allow-private-content", StringComparer.Ordinal);
        EnsureTrustStore();
        _remoteAdmission = true;
        SubmitDirectConnect(endpoint, fingerprint);
    }

    private void ValidateRemoteSmoke(in RemoteConceptDemoClientSnapshot snapshot)
    {
        string expected = ReadArgument("--a0-8-expect=") ?? "cold";
        bool cold = string.Equals(expected, "cold", StringComparison.OrdinalIgnoreCase);
        bool warm = string.Equals(expected, "warm", StringComparison.OrdinalIgnoreCase);
        if (!cold && !warm || snapshot.RenderedChunks != 9 || snapshot.TerrainTriangles <= 0 ||
            snapshot.OpenUiViews != 1 || snapshot.Session.Chat.OccurrencesApplied == 0 ||
            snapshot.Actors.ActiveActors < 2 || snapshot.Actors.PrimitiveFallbackActors < 2 ||
            snapshot.Actors.VisibleNameplates < 2 || snapshot.Actors.VisibleOverheadLabels < 1 ||
            snapshot.Session.PublicPresentation.Applied == 0 ||
            snapshot.Session.PublicPresentation.ProofsQueued == 0 ||
            snapshot.Session.Content.ProofsQueued == 0 ||
            snapshot.Session.StructuralWorld.State != RemoteStructuralWorldState.Ready ||
            snapshot.Session.World.AppliedSnapshots == 0 ||
            cold && snapshot.Session.Content.Acquisition.BytesFetched == 0 ||
            warm && snapshot.Session.Content.Acquisition.BytesFetched != 0)
            throw new InvalidOperationException(
                $"The A0.8 {expected} native acceptance did not converge: {snapshot}.");
    }

    private static bool IsRemoteSmoke() => OS.GetCmdlineUserArgs().Contains(
        "--a0-8-smoke", StringComparer.Ordinal);

    private static int RemoteSmokeExpectedActors()
    {
        string? value = ReadArgument("--m0-0-expect-actors=");
        return value is null
            ? 2
            : int.TryParse(value, out int expected) && expected is >= 2 and <= 64
                ? expected
                : throw new InvalidOperationException(
                    "--m0-0-expect-actors must be between 2 and 64.");
    }

    private bool SharedRemoteSmokeBarrierReleased()
    {
        string? ready = ReadArgument("--m0-0-client-ready=");
        if (ready is null) return true;
        string release = ReadArgument("--m0-0-client-release=") ??
            throw new InvalidOperationException(
                "The M0.0 client-ready barrier requires a release path.");
        if (!_remoteSmokeBarrierReadyPublished)
        {
            string full = Path.GetFullPath(ready);
            string? directory = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException(
                    "The M0.0 client-ready marker has no parent directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(full, "ready" + global::System.Environment.NewLine);
            _remoteSmokeBarrierReadyPublished = true;
        }
        return File.Exists(release);
    }

    private static string? ReadArgument(string prefix) => OS.GetCmdlineUserArgs()
        .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?
        [prefix.Length..];

    private static string BoundedShellMessage(string message) =>
        message.Length <= ClientShellCoordinator.MaximumMessageCharacters
            ? message
            : message[..(ClientShellCoordinator.MaximumMessageCharacters - 1)] + "…";

    private void EnsureTrustStore()
    {
        if (_serverTrust is not null) return;
        string path = ReadArgument("--a0-8-trust=") ??
            ProjectSettings.GlobalizePath("user://settings/trusted-servers.json");
        _serverTrustStore = new ClientServerTrustFileStore(path);
        ClientServerTrustLoadResult loaded = _serverTrustStore.Load();
        _serverTrust = loaded.Registry;
        if (loaded.Status == ClientServerTrustLoadStatus.Invalid)
            _trustLoadWarning =
                $"Invalid saved server trust was ignored (fail closed): {loaded.Error}";
    }

    private void EnsureProfileDecisionStore()
    {
        if (_profileDecisionStore is not null) return;
        string path = ReadArgument("--f0-3c-profile-decisions=") ??
            ProjectSettings.GlobalizePath("user://settings/server-profiles.json");
        _profileDecisionStore = new ClientProfileDecisionFileStore(path);
        ClientProfileDecisionLoadResult loaded = _profileDecisionStore.Load();
        _profileDecisions = loaded.Document;
        if (loaded.Status == ClientProfileDecisionLoadStatus.InvalidFallback)
            _profileDecisionLoadWarning =
                $"Invalid saved server-profile decisions were ignored: {loaded.Error}";
    }

    private static string GroupFingerprint(string fingerprint)
    {
        if (fingerprint.Length != 64) return fingerprint;
        return string.Join(':', Enumerable.Range(0, 32)
            .Select(index => fingerprint.Substring(index * 2, 2)));
    }
}
#endif
