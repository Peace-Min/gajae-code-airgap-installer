using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace GajaeCode.AirgapInstaller;

internal sealed class WslOfflineInstaller
{
    private const string DistroName = "GajaeCode";
    private const string RunOnceName = "GajaeCodeAirgapSetupResume";
    private const string ApiKeyEnvironmentVariable = "GJC_INTERNAL_API_KEY";

    private readonly Action<string> _log;

    public WslOfflineInstaller(Action<string> log)
    {
        _log = log;
    }

    public static bool HasEmbeddedPayload()
    {
        var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        return names.Contains("GajaeCode.LinuxPayload", StringComparer.Ordinal) &&
               names.Contains("GajaeCode.WslRootfs", StringComparer.Ordinal) &&
               names.Contains("GajaeCode.WslKernelMsi", StringComparer.Ordinal);
    }

    public async Task<WslInstallResult> InstallAsync(
        string apiKey,
        string gatewayBaseUrl,
        string modelId,
        string providerId)
    {
        var paths = WslPaths.Create();
        Directory.CreateDirectory(paths.PayloadDirectory);
        Directory.CreateDirectory(paths.DistroDirectory);
        Directory.CreateDirectory(paths.InstallerDirectory);

        ExtractResource("GajaeCode.LinuxPayload", "GajaeCode.LinuxPayloadHash", paths.LinuxBinaryPath);
        ExtractResource("GajaeCode.WslRootfs", "GajaeCode.WslRootfsHash", paths.RootfsPath);
        ExtractResource("GajaeCode.WslKernelMsi", "GajaeCode.WslKernelMsiHash", paths.KernelMsiPath);
        _log("WSL 오프라인 payload SHA-256 검증 성공.");

        var restartRequired = false;
        restartRequired |= await EnableWindowsFeatureAsync("Microsoft-Windows-Subsystem-Linux");
        restartRequired |= await EnableWindowsFeatureAsync("VirtualMachinePlatform");
        if (restartRequired)
        {
            ScheduleResume(paths);
            return new WslInstallResult(true, false, "Windows 기능 활성화를 완료하려면 재부팅이 필요합니다.");
        }

        if (!await HasModernWslAsync())
        {
            var kernelResult = await RunProcessAsync(
                "msiexec.exe",
                ["/i", paths.KernelMsiPath, "/qn", "/norestart"],
                null,
                TimeSpan.FromMinutes(5));
            if (kernelResult.ExitCode is not (0 or 3010 or 1641))
            {
                throw new InvalidOperationException(
                    $"WSL2 커널 MSI 설치 실패 (exit {kernelResult.ExitCode}): {kernelResult.CombinedOutput}");
            }
            if (kernelResult.ExitCode is 3010 or 1641)
            {
                ScheduleResume(paths);
                return new WslInstallResult(true, false, "WSL2 커널 설치를 완료하려면 재부팅이 필요합니다.");
            }
        }
        else
        {
            _log("설치된 최신 WSL을 확인해 레거시 커널 MSI 적용을 건너뜁니다.");
        }

        var defaultVersion = await RunProcessAsync(
            "wsl.exe",
            ["--set-default-version", "2"],
            null,
            TimeSpan.FromMinutes(1),
            Encoding.Unicode);
        if (defaultVersion.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"WSL2 기본 버전 설정 실패: {defaultVersion.CombinedOutput}");
        }

        var distroExists = await DistroExistsAsync();
        if (distroExists && !await ExistingDistroHasRequiredToolsAsync())
        {
            _log("기존 GajaeCode WSL 배포판에 필수 런타임 도구가 부족해 새 배포판으로 재생성합니다.");
            var unregisterResult = await RunProcessAsync(
                "wsl.exe",
                ["--unregister", DistroName],
                null,
                TimeSpan.FromMinutes(3),
                Encoding.Unicode);
            if (unregisterResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"기존 WSL 배포판 제거 실패: {unregisterResult.CombinedOutput}");
            }

            distroExists = false;
        }

        if (!distroExists)
        {
            _log("GajaeCode WSL2 배포판을 등록합니다.");
            var importResult = await RunProcessAsync(
                "wsl.exe",
                ["--import", DistroName, paths.DistroDirectory, paths.RootfsPath, "--version", "2"],
                null,
                TimeSpan.FromMinutes(10),
                Encoding.Unicode);
            if (importResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"WSL 배포판 등록 실패: {importResult.CombinedOutput}");
            }
        }
        else
        {
            _log("기존 GajaeCode WSL 배포판을 재사용합니다.");
        }

        WriteConfigureScript(paths.ConfigureScriptPath);
        var configureScriptWslPath = ConvertWindowsPathToWslMountPath(paths.ConfigureScriptPath);
        var linuxBinaryWslPath = ConvertWindowsPathToWslMountPath(paths.LinuxBinaryPath);
        var configureResult = await RunProcessAsync(
            "wsl.exe",
            [
                "-d", DistroName,
                "-u", "root",
                "--",
                "bash", "-lc",
                "exec bash \"$1\" \"$2\" \"$3\" \"$4\" \"$5\"",
                "gajaecode-configure",
                configureScriptWslPath,
                linuxBinaryWslPath,
                gatewayBaseUrl,
                modelId,
                providerId,
            ],
            apiKey + "\n",
            TimeSpan.FromMinutes(3));
        if (configureResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"WSL GJC/tmux 설정 실패: {configureResult.CombinedOutput}");
        }

        var verifyResult = await RunProcessAsync(
            "wsl.exe",
            [
                "-d", DistroName,
                "-u", "gjc",
                "--",
                "bash", "-lc",
                "source ~/.config/gajae-code/env && tmux -V && gjc --version",
            ],
            null,
            TimeSpan.FromMinutes(1));
        if (verifyResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"WSL GJC/tmux 검증 실패: {verifyResult.CombinedOutput}");
        }
        _log(verifyResult.CombinedOutput);

        CreateLaunchers(paths);
        RemoveResume();
        return new WslInstallResult(false, true, "WSL2, tmux, Linux GJC 설치가 완료되었습니다.");
    }

    private async Task<bool> EnableWindowsFeatureAsync(string featureName)
    {
        var result = await RunProcessAsync(
            "dism.exe",
            [
                "/online",
                "/enable-feature",
                $"/featurename:{featureName}",
                "/all",
                "/norestart",
                "/English",
            ],
            null,
            TimeSpan.FromMinutes(10));
        if (result.ExitCode is not (0 or 3010 or 1641))
        {
            throw new InvalidOperationException(
                $"Windows 기능 활성화 실패 ({featureName}, exit {result.ExitCode}): {result.CombinedOutput}");
        }

        _log($"Windows 기능 확인: {featureName} (exit {result.ExitCode})");
        return result.ExitCode is 3010 or 1641;
    }

    private static async Task<bool> DistroExistsAsync()
    {
        var result = await RunProcessAsync(
            "wsl.exe",
            ["--list", "--quiet"],
            null,
            TimeSpan.FromMinutes(1),
            Encoding.Unicode);
        return result.ExitCode == 0 &&
               result.StandardOutput.Split(
                       ['\r', '\n', '\0'],
                       StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Any(line => line.Equals(DistroName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> HasModernWslAsync()
    {
        var result = await RunProcessAsync(
            "wsl.exe",
            ["--version"],
            null,
            TimeSpan.FromMinutes(1),
            Encoding.Unicode);
        return result.ExitCode == 0;
    }

    private static async Task<bool> ExistingDistroHasRequiredToolsAsync()
    {
        var result = await RunProcessAsync(
            "wsl.exe",
            [
                "-d", DistroName,
                "-u", "root",
                "--",
                "bash", "-lc",
                "command -v bash git rg tmux node npm python3 >/dev/null && python3 -m pip --version >/dev/null",
            ],
            null,
            TimeSpan.FromMinutes(1));
        return result.ExitCode == 0;
    }

    private void ExtractResource(string resourceName, string hashResourceName, string destinationPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var payload = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"설치기에 {resourceName} payload가 없습니다.");
        using var hashStream = assembly.GetManifestResourceStream(hashResourceName)
            ?? throw new InvalidOperationException($"설치기에 {hashResourceName} 체크섬이 없습니다.");
        using var reader = new StreamReader(hashStream, Encoding.ASCII);
        var expectedHash = reader.ReadToEnd().Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        var temporaryPath = destinationPath + ".new";
        using (var output = File.Create(temporaryPath))
        {
            payload.CopyTo(output);
        }
        string actualHash;
        using (var input = File.OpenRead(temporaryPath))
        {
            actualHash = Convert.ToHexString(SHA256.HashData(input));
        }

        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            FileOperations.DeleteWithRetry(temporaryPath);
            throw new InvalidOperationException($"{resourceName} SHA-256 검증에 실패했습니다.");
        }

        FileOperations.MoveReplacingWithRetry(temporaryPath, destinationPath);
    }

    private static void WriteConfigureScript(string path)
    {
        const string script = """
            #!/usr/bin/env bash
            set -euo pipefail
            umask 077

            linux_binary="$1"
            gateway="$2"
            model="$3"
            provider="$4"
            IFS= read -r api_key

            if ! id gjc >/dev/null 2>&1; then
              useradd --create-home --shell /bin/bash gjc
            fi

            install -m 0755 "$linux_binary" /usr/local/bin/gjc
            install -d -m 0700 -o gjc -g gjc /home/gjc/.gjc/agent
            install -d -m 0700 -o gjc -g gjc /home/gjc/.config/gajae-code

            cat >/home/gjc/.gjc/agent/models.yml <<EOF
            providers:
              ${provider}:
                baseUrl: "${gateway}"
                apiKeyEnv: "GJC_INTERNAL_API_KEY"
                api: anthropic-messages
                authHeader: true
                auth: apiKey
                disableStrictTools: true
                models:
                  - id: "${model}"
                    reasoning: true
                    input: [text]
                    cost:
                      input: 0
                      output: 0
                      cacheRead: 0
                      cacheWrite: 0
            modelBindings:
              modelRoles:
                default: "${provider}/${model}"
              agentModelOverrides:
                executor: "${provider}/${model}"
                architect: "${provider}/${model}"
                planner: "${provider}/${model}"
                critic: "${provider}/${model}"
            EOF

            cat >/home/gjc/.gjc/agent/config.yml <<'EOF'
            startup:
              checkUpdate: false
            starReminder:
              enabled: false
            marketplace:
              autoUpdate: off
            web_search:
              enabled: false
            browser:
              enabled: false
            task:
              eager: false
              maxConcurrency: 2
              maxRecursionDepth: 1
              maxRuntimeMs: 1800000
              enableLsp: false
              forkContext:
                enabled: false
            retry:
              requestMaxRetries: 4
              streamMaxRetries: 20
              maxRetries: 3
              maxDelayMs: 30000
            EOF

            printf 'export GJC_INTERNAL_API_KEY=%q\n' "$api_key" >/home/gjc/.config/gajae-code/env
            chown -R gjc:gjc /home/gjc/.gjc /home/gjc/.config/gajae-code
            chmod 0600 /home/gjc/.config/gajae-code/env

            cat >/usr/local/bin/gjc-tmux <<'EOF'
            #!/usr/bin/env bash
            set -euo pipefail
            source "$HOME/.config/gajae-code/env"
            exec tmux new-session -A -s gjc "gjc"
            EOF
            chmod 0755 /usr/local/bin/gjc-tmux

            cat >/etc/wsl.conf <<'EOF'
            [automount]
            enabled=true
            mountFsTab=true
            options=metadata,umask=22,fmask=11

            [network]
            generateResolvConf=true

            [interop]
            enabled=true
            appendWindowsPath=false

            [user]
            default=gjc
            EOF

            su - gjc -c 'source ~/.config/gajae-code/env && gjc setup defaults --force'
            tmux -V
            """;
        FileOperations.WriteAllTextReplacingWithRetry(
            path,
            script.Replace("\r\n", "\n"),
            new UTF8Encoding(false));
    }

    private static void CreateLaunchers(WslPaths paths)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var launcherPath = Path.Combine(desktop, "GajaeCode tmux.cmd");
        const string launcher = """
            @echo off
            where wt.exe >nul 2>nul
            if %errorlevel%==0 (
              start "" wt.exe wsl.exe -d GajaeCode -- bash -lc "exec gjc-tmux"
            ) else (
              start "" wsl.exe -d GajaeCode -- bash -lc "exec gjc-tmux"
            )
            """;
        FileOperations.WriteAllTextReplacingWithRetry(
            launcherPath,
            launcher.Replace("\n", "\r\n"),
            Encoding.ASCII);

        var logsPath = Path.Combine(desktop, "GajaeCode diagnostics.cmd");
        var diagnostics = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GajaeCode",
            "diagnostics");
        FileOperations.WriteAllTextReplacingWithRetry(
            logsPath,
            $"@echo off\r\nstart \"\" explorer.exe \"{diagnostics}\"\r\n",
            Encoding.ASCII);
    }

    private static void ScheduleResume(WslPaths paths)
    {
        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("현재 설치기 실행 경로를 확인할 수 없습니다.");
        var stableExecutable = Path.Combine(paths.InstallerDirectory, "GajaeCode-Airgap-Setup.exe");
        if (!SystemPrerequisites.PathsEqual(currentExecutable, stableExecutable))
        {
            FileOperations.CopyReplacingWithRetry(currentExecutable, stableExecutable);
        }
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce");
        key.SetValue(RunOnceName, $"\"{stableExecutable}\" --resume-wsl", RegistryValueKind.String);
    }

    private static void RemoveResume()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            writable: true);
        key?.DeleteValue(RunOnceName, false);
    }

    internal static string ConvertWindowsPathToWslMountPath(string windowsPath)
    {
        var fullPath = Path.GetFullPath(windowsPath);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root) ||
            root.Length < 2 ||
            root[1] != ':' ||
            !char.IsAsciiLetter(root[0]))
        {
            throw new InvalidOperationException($"WSL /mnt 경로로 변환할 수 없는 Windows 경로입니다: {windowsPath}");
        }

        var drive = char.ToLowerInvariant(root[0]);
        var relativePath = fullPath[root.Length..]
            .Replace('\\', '/')
            .TrimStart('/');
        return $"/mnt/{drive}/{relativePath}";
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? standardInput,
        TimeSpan timeout,
        Encoding? redirectedOutputEncoding = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
        };
        if (redirectedOutputEncoding is not null)
        {
            startInfo.StandardOutputEncoding = redirectedOutputEncoding;
            startInfo.StandardErrorEncoding = redirectedOutputEncoding;
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // Process may already have exited.
            }
            throw new TimeoutException($"{executable} 실행 제한시간을 초과했습니다.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => string.Join(
            Environment.NewLine,
            new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(value => value.Length > 0));
    }

    private sealed record WslPaths(
        string PayloadDirectory,
        string DistroDirectory,
        string InstallerDirectory,
        string LinuxBinaryPath,
        string RootfsPath,
        string KernelMsiPath,
        string ConfigureScriptPath)
    {
        public static WslPaths Create()
        {
            var localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GajaeCode");
            var payload = Path.Combine(localRoot, "wsl-payload");
            return new WslPaths(
                payload,
                Path.Combine(localRoot, "wsl-distro"),
                Path.Combine(localRoot, "installer"),
                Path.Combine(payload, "gjc-linux-x64"),
                Path.Combine(payload, "gajaecode-wsl-rootfs.tar"),
                Path.Combine(payload, "wsl_update_x64.msi"),
                Path.Combine(payload, "configure-wsl.sh"));
        }
    }
}

internal sealed record WslInstallResult(bool RestartRequired, bool Installed, string Message);
