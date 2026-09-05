#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.Network;

namespace UltraSim.Client.Application.Godot;

public partial class ClientHost
{
    private ClientServerTrustRegistry? _serverTrust;
    private ClientServerTrustFileStore? _serverTrustStore;
    private ClientDirectConnectSession? _remoteSession;
    private string _directEndpoint = "127.0.0.1:7777";
    private string _directFingerprint = string.Empty;
    private string? _trustLoadWarning;
    private bool _remoteAdmission;
    private bool _remoteMismatch;
    private bool _remoteRetiring;
    private ClientDirectConnectState _lastRemoteState;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_remoteSession is null) return;
        try
        {
            _remoteSession.Pump();
            ClientDirectConnectState current = _remoteSession.State;
            if (_remoteRetiring)
            {
                if (current is ClientDirectConnectState.Closed or ClientDirectConnectState.Failed)
                {
                    _remoteSession.Dispose();
                    _remoteSession = null;
                    _remoteRetiring = false;
                }
                return;
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
                    CommitSession(
                        $"Connected to realm {snapshot.Realm} at {snapshot.Endpoint}. " +
                        "Guest entry and world admission follow in F0.3b.");
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
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailSession($"Direct Connect failed safely: {exception.Message}");
        }
    }

    public override void _ExitTree()
    {
        _remoteSession?.Dispose();
        _remoteSession = null;
        base._ExitTree();
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
        base.RetireCurrentSession();
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
        Button connect = MenuButton("Connect");
        connect.Name = "DirectConnectSubmit";
        ConnectDeferred(connect, () => SubmitDirectConnect(endpoint.Text, fingerprint.Text));
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

    private void EnsureTrustStore()
    {
        if (_serverTrust is not null) return;
        string path = ProjectSettings.GlobalizePath("user://settings/trusted-servers.json");
        _serverTrustStore = new ClientServerTrustFileStore(path);
        ClientServerTrustLoadResult loaded = _serverTrustStore.Load();
        _serverTrust = loaded.Registry;
        if (loaded.Status == ClientServerTrustLoadStatus.Invalid)
            _trustLoadWarning =
                $"Invalid saved server trust was ignored (fail closed): {loaded.Error}";
    }

    private static string GroupFingerprint(string fingerprint)
    {
        if (fingerprint.Length != 64) return fingerprint;
        return string.Join(':', Enumerable.Range(0, 32)
            .Select(index => fingerprint.Substring(index * 2, 2)));
    }
}
#endif
