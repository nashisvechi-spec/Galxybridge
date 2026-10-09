using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GalaxyBridge.Core;

namespace GalaxyBridge.Windows;

internal sealed class AdbClient
{
    public const string ServerSha256 = "764eb6f79811d5211fe9df341120882ba9994c7a61b897d7bf3fb662e53bc536";
    public string BackendDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "backend");
    public string ServerPath => Path.Combine(BackendDirectory, "scrcpy-server");
    private string AdbPath => Path.Combine(BackendDirectory, "adb.exe");

    public void VerifyBackend()
    {
        foreach (string name in new[] { "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "scrcpy-server" })
            if (!File.Exists(Path.Combine(BackendDirectory, name)))
                throw new FileNotFoundException("Не найдены компоненты подключения. Нужна полная папка backend рядом с GalaxyBridge.exe.");
        using FileStream file = File.OpenRead(ServerPath);
        string hash = Convert.ToHexString(SHA256.HashData(file));
        if (!hash.Equals(ServerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Версия компонента подключения не совпадает с проектом. Используйте scripts/Fetch-Backend.ps1.");
    }

    private ProcessStartInfo StartInfo(IEnumerable<string> arguments)
    {
        ProcessStartInfo info = new(AdbPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken token,
        string? input = null, int timeoutSeconds = 15)
    {
        using Process process = new() { StartInfo = StartInfo(arguments) };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        if (!process.Start()) throw new IOException("Не удалось запустить подключение.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            if (input is not null) await process.StandardInput.WriteLineAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            string stdout = await output, stderr = await error;
            if (process.ExitCode != 0) throw new IOException((stderr + "\n" + stdout).Trim());
            return stdout;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Телефон не ответил вовремя. Проверьте кабель, разрешение доступа и состояние экрана."); }
        finally
        {
            if (!process.HasExited)
            { try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
            // Observe read tasks if cancellation interrupted the process.
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
        }
    }

    public Process StartServer(string serial, string remoteJar, string scid, Action<string> log)
    {
        string[] args = ["-s", serial, "shell", $"CLASSPATH={remoteJar}", "app_process", "/",
            "com.genymobile.scrcpy.Server", ControlProtocol.ServerVersion,
            $"scid={scid}", "log_level=warn", "video=false", "audio=false", "control=true",
            "tunnel_forward=true", "send_dummy_byte=true", "send_device_meta=true",
            "clipboard_autosync=true", "cleanup=true", "power_on=true", "stay_awake=false"];
        Process process = new() { StartInfo = StartInfo(args), EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) log(line); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) log(line); };
        if (!process.Start()) { process.Dispose(); throw new IOException("Не удалось начать управление."); }
        process.StandardInput.Close();
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }

    public async Task<IReadOnlyList<AdbDevice>> DevicesAsync(CancellationToken token)
    {
        VerifyBackend();
        return AdbDevice.Parse(await RunAsync(["devices", "-l"], token));
    }
    public async Task PairAsync(string endpoint, string code, CancellationToken token)
    {
        VerifyBackend();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) throw new FormatException("Нужен шестизначный код с телефона.");
        _ = await RunAsync(["pair", AdbEndpoint.Parse(endpoint).ToString()], token, code, 30);
    }
    public async Task ConnectWifiAsync(string endpoint, CancellationToken token)
    {
        VerifyBackend();
        string result = await RunAsync(["connect", AdbEndpoint.Parse(endpoint).ToString()], token);
        if (!result.Contains("connected to", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Не удалось подключиться: " + result.Trim());
    }
    public Task<string> PushAsync(string serial, string path, CancellationToken token) =>
        RunAsync(["-s", serial, "push", path, "/sdcard/Download/"], token, timeoutSeconds: 120);
}
