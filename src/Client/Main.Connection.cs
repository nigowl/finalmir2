using Crystal.Native;
using Godot;

public partial class Main
{
    internal const int ConnectionAttempts = 5;
    internal const int ConnectionAttemptMilliseconds = 30000;
    private string serverAddress = "";
    private ConfirmationDialog connectionDialog = null!;
    private Label connectionNotice = null!;
    private CancellationTokenSource? connectionCancellation;
    private TaskCompletionSource? connectionHandshake;
    private Task connectionTask = Task.CompletedTask;
    private int connectionAttempt;
    private long connectionDeadline;
    private string connectionError = "";
    private bool restartingConnection;

    private void BuildConnectionDialog()
    {
        connectionDialog = new ConfirmationDialog { Name = "ConnectionDialog", Title = "连接游戏服务器",
            DialogHideOnOk = false, DialogCloseOnEscape = false, Unresizable = true, Exclusive = true,
            Size = new Vector2I(360, 150), Transient = true };
        AddChild(connectionDialog);
        connectionNotice = new Label { Name = "ConnectionNotice", CustomMinimumSize = new Vector2(328, 82),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        connectionDialog.AddChild(connectionNotice);
        connectionDialog.GetOkButton().Text = "重试"; connectionDialog.GetCancelButton().Text = "退出";
        connectionDialog.AddThemeConstantOverride("buttons_min_width", 88);
        connectionDialog.AddThemeConstantOverride("buttons_min_height", ClientUi.ButtonHeight);
        connectionDialog.Confirmed += RetryConnection;
        connectionDialog.Canceled += () => _ = Quit();
        connectionDialog.CloseRequested += () => _ = Quit();
    }

    public Task Connect(string address)
    {
        if (connecting) return connectionTask;
        serverAddress = address;
        connectionTask = ConnectAttempts();
        return connectionTask;
    }

    private async Task ConnectAttempts()
    {
        connecting = true;
        connectionCancellation?.Dispose();
        connectionCancellation = new CancellationTokenSource();
        var token = connectionCancellation.Token;
        connectionDialog.PopupCentered();
        try
        {
            for (var number = 1; number <= ConnectionAttempts; number++)
            {
                connectionAttempt = number;
                connectionDeadline = Environment.TickCount64 + ConnectionAttemptMilliseconds;
                connectionError = "";
                UpdateConnectionDialog(Environment.TickCount64);
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                attempt.CancelAfter(ConnectionAttemptMilliseconds);
                try
                {
                    Connection.Dispose(); StopAudio(); publicPurchases.Reset(); ResetAccountRequests(); State.Reset();
                    SetScene("login");
                    lastResourceError = "";
                    if (!Assets.HasStartupSprite("Title", 276)) originalButtonThemeReady = false;
                    foreach (var (rect, library, index) in pictures)
                        if (IsInstanceValid(rect) && !Assets.HasStartupSprite(library, index)) rect.Texture = null;
                    HandshakeReady = false; status.Text = "正在连接服务器…";
                    await Assets.Initialize(serverAddress, attempt.Token);
                    State.PlayerTiming = Assets.PlayerTiming;
                    connectionHandshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    await Connection.Connect(Assets.Host, Assets.GamePort, attempt.Token);
                    await connectionHandshake.Task.WaitAsync(attempt.Token);
                    HandshakeReady = true; status.Text = "已连接服务器";
                    connectionDialog.Hide();
                    _ = Assets.WarmSounds(Enumerable.Range(10103, 16).Concat(Enumerable.Range(10001, 8)).Concat(new[] { 10050, 10056, 10070, 10138, 10139 }));
                    _ = PlaySound(10146, true);
                    return;
                }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    Connection.Dispose(); HandshakeReady = false;
                    connectionError = error is OperationCanceledException ? "连接超时" : error.Message;
                    status.Text = connectionError;
                    GD.PrintErr($"连接服务器失败（{number}/{ConnectionAttempts}）：{error}");
                }
                finally { connectionHandshake = null; }
                if (number == ConnectionAttempts) break;
                var remaining = connectionDeadline - Environment.TickCount64;
                if (remaining > 0) await Task.Delay((int)remaining, token);
            }
            ShowConnectionFailure($"已尝试 {ConnectionAttempts} 次，仍无法连接服务器。\n请检查网络或稍后重试。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { connecting = false; }
    }

    private async void RetryConnection()
    {
        if (restartingConnection) return;
        restartingConnection = true;
        try
        {
            connectionCancellation?.Cancel();
            await connectionTask;
            _ = Connect(serverAddress);
        }
        finally { restartingConnection = false; }
    }

    private void UpdateConnectionDialog(long now)
    {
        if (!connecting) return;
        var remaining = Math.Max(0, (connectionDeadline - now + 999) / 1000);
        var message = connectionError.Length == 0
            ? $"正在连接服务器…\n第 {connectionAttempt}/{ConnectionAttempts} 次尝试，剩余 {remaining} 秒"
            : $"{connectionError}\n{remaining} 秒后重试（{connectionAttempt}/{ConnectionAttempts}）";
        if (connectionNotice.Text != message) connectionNotice.Text = message;
    }

    private void ShowConnectionFailure(string message)
    {
        connectionNotice.Text = message;
        if (!connectionDialog.Visible) connectionDialog.PopupCentered();
    }
}
