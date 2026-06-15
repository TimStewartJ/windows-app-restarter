using System.IO.Pipes;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WindowsAppRestarter;

internal static partial class SingleInstanceActivation
{
    private const string MutexName = @"Local\WindowsAppRestarter";
    private static readonly TimeSpan ClientConnectTimeout = TimeSpan.FromMilliseconds(750);

    public static Mutex CreateMutex(out bool createdNew) => new(true, MutexName, out createdNew);

    public static bool NotifyRunningInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ClientConnectTimeout);
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            AppLogger.Error("Could not activate the running instance.", exception);
            return false;
        }
    }

    public static async Task ListenAsync(Action activationRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                var buffer = new byte[1];
                _ = await server.ReadAsync(buffer, cancellationToken);
                activationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLogger.Error("Activation listener failed.", exception);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private static string PipeName => $"WindowsAppRestarter-{Process.GetCurrentProcess().SessionId}-{PipeNameSafeValue()}";

    private static string PipeNameSafeValue()
    {
        var input = $"{Environment.UserDomainName}-{Environment.UserName}-{Environment.GetEnvironmentVariable("SESSIONNAME")}";
        return PipeNameUnsafeCharacters().Replace(input, "_");
    }

    [GeneratedRegex(@"[^A-Za-z0-9_.-]")]
    private static partial Regex PipeNameUnsafeCharacters();
}
