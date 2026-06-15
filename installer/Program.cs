using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace GajaeCode.AirgapInstaller;

internal static class Program
{
    private const string InstallerMutexName = @"Local\GajaeCodeAirgapInstaller";
    public static bool ResumeWslRequested { get; private set; }

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(false, InstallerMutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                MessageBox.Show(
                    "가재코드 설치 프로그램이 이미 실행 중입니다.",
                    "중복 실행",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ResumeWslRequested = Environment.GetCommandLineArgs()
                .Any(argument => argument.Equals("--resume-wsl", StringComparison.OrdinalIgnoreCase));
            ApplicationConfiguration.Initialize();
            Application.Run(new InstallerForm());
        }
        finally
        {
            if (ownsMutex)
            {
                mutex.ReleaseMutex();
            }
        }
    }
}

internal sealed class InstallerForm : Form
{
    private const string ApiKeyEnvironmentVariable = "GJC_INTERNAL_API_KEY";
    private readonly DeploymentSettings _deployment = DeploymentSettings.Load();
    private string? _logPath;
    private string? _statePath;
    private string _currentStage = "not-started";
    private string _activeApiKey = string.Empty;
    private bool _installationRunning;

    private readonly TextBox _apiKey = new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true,
        TabIndex = 0,
    };

    private readonly Button _installButton = new()
    {
        Text = "설치 및 설정",
        AutoSize = true,
        TabIndex = 1,
    };

    private readonly CheckBox _installWslTmux = new()
    {
        Text = "WSL2 + tmux + Linux GJC 설치",
        Checked = true,
        AutoSize = true,
        TabIndex = 1,
    };

    private readonly TextBox _status = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = SystemColors.Window,
        TabStop = false,
    };

    public InstallerForm()
    {
        Text = "가재코드 폐쇄망 설치";
        Width = 680;
        Height = 430;
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;
        AcceptButton = _installButton;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var description = new Label
        {
            AutoSize = true,
            Text = $"내부 게이트웨이: {_deployment.GatewayBaseUrl}\r\n모델: {_deployment.ModelId}",
            Margin = new Padding(0, 0, 0, 14),
        };
        layout.Controls.Add(description, 0, 0);
        layout.SetColumnSpan(description, 2);

        var keyLabel = new Label
        {
            AutoSize = true,
            Text = "API Key:",
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 10, 4),
        };
        layout.Controls.Add(keyLabel, 0, 1);
        layout.Controls.Add(_apiKey, 1, 1);
        layout.Controls.Add(_installWslTmux, 1, 2);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0),
        };
        buttonPanel.Controls.Add(_installButton);
        layout.Controls.Add(buttonPanel, 0, 3);
        layout.SetColumnSpan(buttonPanel, 2);

        layout.Controls.Add(_status, 0, 5);
        layout.SetColumnSpan(_status, 2);
        Controls.Add(layout);

        _installButton.Click += async (_, _) => await InstallAsync();
        FormClosing += (_, eventArgs) =>
        {
            if (!_installationRunning)
            {
                return;
            }

            eventArgs.Cancel = true;
            MessageBox.Show(
                this,
                "설치 작업이 진행 중입니다. 현재 단계가 완료될 때까지 창을 닫지 마십시오.",
                "설치 진행 중",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        Shown += async (_, _) =>
        {
            if (Program.ResumeWslRequested)
            {
                _installWslTmux.Checked = true;
                _apiKey.Text = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) ?? string.Empty;
                if (_apiKey.Text.Length > 0)
                {
                    await InstallAsync();
                    return;
                }
            }
            _apiKey.Focus();
        };
    }

    private async Task InstallAsync()
    {
        var apiKey = _apiKey.Text.Trim();
        if (apiKey.Length == 0)
        {
            MessageBox.Show(this, "서버에서 발급한 API Key를 입력하십시오.", "입력 필요",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _apiKey.Focus();
            return;
        }
        if (apiKey.Length < 8)
        {
            MessageBox.Show(
                this,
                "API Key가 너무 짧습니다. 서버에서 발급된 전체 키를 입력하십시오.",
                "입력 확인",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _apiKey.Focus();
            return;
        }

        _installationRunning = true;
        _installButton.Enabled = false;
        _apiKey.Enabled = false;
        _status.Clear();
        _activeApiKey = apiKey;

        try
        {
            var paths = InstallPaths.Create();
            Directory.CreateDirectory(paths.InstallDirectory);
            Directory.CreateDirectory(paths.AgentDirectory);
            Directory.CreateDirectory(paths.DiagnosticsDirectory);
            InitializeDiagnostics(paths);
            SetStage("initialize", "running");
            Log("설치를 시작합니다.");

            var bashPath = SystemPrerequisites.FindGitBash();
            if (bashPath is null)
            {
                throw new InvalidOperationException(
                    "Git Bash를 찾을 수 없습니다. Git for Windows 설치 상태와 C:\\Program Files\\Git\\bin\\bash.exe 경로를 확인하십시오.");
            }
            Log($"Git Bash 확인: {bashPath}");

            SetStage("install-binary", "running");
            EnsureGjcNotRunning(paths.BinaryPath);
            ExtractAndVerifyBinary(paths.BinaryPath);
            Log($"GJC 설치: {paths.BinaryPath}");

            SetStage("configure-credentials", "running");
            Environment.SetEnvironmentVariable(
                ApiKeyEnvironmentVariable,
                apiKey,
                EnvironmentVariableTarget.Process);
            AddDirectoryToUserPath(paths.InstallDirectory);
            Log("설치 프로세스용 인증 정보를 준비했습니다.");

            SetStage("write-config", "running");
            WriteManagedConfiguration(paths, bashPath, _deployment);
            Log("모델 및 폐쇄망 설정을 생성했습니다.");

            SetStage("install-defaults", "running");
            var defaultsResult = await RunProcessAsync(
                paths.BinaryPath,
                ["setup", "defaults", "--force"],
                paths.InstallDirectory,
                TimeSpan.FromMinutes(1),
                apiKey);
            if (defaultsResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"기본 스킬 설치 실패 (exit {defaultsResult.ExitCode})\r\n{defaultsResult.CombinedOutput}");
            }
            Log("기본 GJC 워크플로 스킬을 설치했습니다.");

            SetStage("verify-gateway", "running");
            await VerifyGatewayAsync(apiKey, _deployment);
            Log("게이트웨이 API 연결 검증 성공.");

            SetStage("verify-gjc", "running");
            var gjcResult = await RunProcessAsync(
                paths.BinaryPath,
                ["--model", $"{_deployment.ProviderId}/{_deployment.ModelId}", "--print", "Reply with exactly GJC_OK and nothing else."],
                paths.InstallDirectory,
                TimeSpan.FromMinutes(3),
                apiKey);
            if (gjcResult.ExitCode != 0 ||
                !gjcResult.StandardOutput.Contains("GJC_OK", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"GJC 모델 호출 검증 실패 (exit {gjcResult.ExitCode})\r\n{gjcResult.CombinedOutput}");
            }

            Log("GJC 모델 호출 검증 성공.");

            SetStage("persist-credentials", "running");
            Environment.SetEnvironmentVariable(
                ApiKeyEnvironmentVariable,
                apiKey,
                EnvironmentVariableTarget.User);
            Log($"사용자 환경변수 등록: {ApiKeyEnvironmentVariable}");

            if (_installWslTmux.Checked)
            {
                SetStage("install-wsl-tmux", "running");
                if (!WslOfflineInstaller.HasEmbeddedPayload())
                {
                    throw new InvalidOperationException(
                        "설치기에 WSL2/tmux payload가 없습니다. Linux GJC, rootfs, WSL 커널 MSI를 포함해 다시 빌드하십시오.");
                }

                var wslInstaller = new WslOfflineInstaller(Log);
                var wslResult = await wslInstaller.InstallAsync(
                    apiKey,
                    _deployment.GatewayBaseUrl,
                    _deployment.ModelId,
                    _deployment.ProviderId);
                Log(wslResult.Message);
                if (wslResult.RestartRequired)
                {
                    SetStage("restart-required", "pending");
                    var restart = MessageBox.Show(
                        this,
                        "WSL2 설치를 계속하려면 Windows를 재부팅해야 합니다. 지금 재부팅하시겠습니까?",
                        "재부팅 필요",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);
                    if (restart == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            Arguments = "/r /t 10 /c \"GajaeCode WSL2 installation\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });
                    }
                    return;
                }
            }

            SetStage("verification", "running");
            var verifier = new VerificationRunner(Log);
            var verification = await verifier.RunAsync(
                paths.BinaryPath,
                _deployment,
                _installWslTmux.Checked);
            if (verification.Status == "failed")
            {
                throw new InvalidOperationException(
                    "설치는 완료됐지만 자동 검증에 실패했습니다. 바탕화면 HTML 보고서와 진단 로그를 확인하십시오.");
            }

            SetStage("complete", "succeeded");
            Log("설치가 완료되었습니다. 새 터미널에서 gjc를 실행하십시오.");
            MessageBox.Show(this, "가재코드 설치와 서버 연결 설정이 완료되었습니다.",
                "설치 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            Log($"오류: {exception.Message}");
            LogDiagnosticException(exception);
            try
            {
                SetStage(_currentStage, "failed", exception.Message);
            }
            catch (Exception stateException) when (
                stateException is IOException or UnauthorizedAccessException)
            {
                Log($"진단 상태 파일 기록 실패: {stateException.Message}");
            }
            MessageBox.Show(this,
                $"설정이 완료되지 않았습니다.\r\n\r\n{exception.Message}\r\n\r\n진단 경로: {_logPath ?? "생성 전 실패"}\r\n\r\n입력한 API Key는 화면이나 로그에 출력되지 않았습니다.",
                "설치 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _installationRunning = false;
            _activeApiKey = string.Empty;
            _apiKey.Clear();
            _apiKey.Enabled = true;
            _installButton.Enabled = true;
            _apiKey.Focus();
        }
    }

    private void ExtractAndVerifyBinary(string destinationPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var payload = assembly.GetManifestResourceStream("GajaeCode.Payload")
            ?? throw new InvalidOperationException(
                "설치기 안에 GJC 바이너리가 없습니다. build.ps1로 설치기를 다시 생성하십시오.");
        using var expectedHashStream = assembly.GetManifestResourceStream("GajaeCode.PayloadHash")
            ?? throw new InvalidOperationException("설치기 안에 GJC 체크섬이 없습니다.");
        using var hashReader = new StreamReader(expectedHashStream, Encoding.ASCII);
        var expectedHash = hashReader.ReadToEnd().Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

        var temporaryPath = destinationPath + ".new";
        using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            payload.CopyTo(output);
        }

        string actualHash;
        using (var file = File.OpenRead(temporaryPath))
        {
            actualHash = Convert.ToHexString(SHA256.HashData(file));
        }

        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            FileOperations.DeleteWithRetry(temporaryPath);
            throw new InvalidOperationException("내장 GJC 바이너리 SHA-256 검증에 실패했습니다.");
        }

        FileOperations.MoveReplacingWithRetry(temporaryPath, destinationPath);
    }

    private static void EnsureGjcNotRunning(string binaryPath)
    {
        foreach (var process in Process.GetProcessesByName("gjc"))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName is { } processPath &&
                        SystemPrerequisites.PathsEqual(processPath, binaryPath))
                    {
                        throw new InvalidOperationException(
                            "실행 중인 GJC가 있습니다. 모든 GJC 창과 프로세스를 종료한 뒤 설치를 다시 실행하십시오.");
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // An unrelated elevated process with the same name is ignored.
                }
            }
        }
    }

    private static void AddDirectoryToUserPath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
        var entries = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!entries.Any(entry => SystemPrerequisites.PathsEqual(entry, directory)))
        {
            var updated = string.IsNullOrWhiteSpace(current)
                ? directory
                : $"{current.TrimEnd(';')};{directory}";
            Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
        }

        var processPath = Environment.GetEnvironmentVariable("Path") ?? string.Empty;
        if (!processPath.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(entry => SystemPrerequisites.PathsEqual(entry, directory)))
        {
            Environment.SetEnvironmentVariable("Path", $"{processPath.TrimEnd(';')};{directory}");
        }
    }

    private static void WriteManagedConfiguration(
        InstallPaths paths,
        string? bashPath,
        DeploymentSettings deployment)
    {
        BackupIfPresent(paths.ModelsPath);
        BackupIfPresent(paths.ConfigPath);

        var modelsYaml = $$"""
            providers:
              {{deployment.ProviderId}}:
                baseUrl: "{{deployment.GatewayBaseUrl}}"
                apiKeyEnv: "{{ApiKeyEnvironmentVariable}}"
                api: anthropic-messages
                authHeader: true
                auth: apiKey
                disableStrictTools: true
                models:
                  - id: "{{deployment.ModelId}}"
                    name: "Internal Qwen 3.6 27B"
                    reasoning: true
                    input: [text]
                    cost:
                      input: 0
                      output: 0
                      cacheRead: 0
                      cacheWrite: 0
            modelBindings:
              modelRoles:
                default: "{{deployment.ProviderId}}/{{deployment.ModelId}}"
              agentModelOverrides:
                executor: "{{deployment.ProviderId}}/{{deployment.ModelId}}"
                architect: "{{deployment.ProviderId}}/{{deployment.ModelId}}"
                planner: "{{deployment.ProviderId}}/{{deployment.ModelId}}"
                critic: "{{deployment.ProviderId}}/{{deployment.ModelId}}"
            """;

        var shellSetting = bashPath is null
            ? string.Empty
            : $"shellPath: \"{EscapeYamlDoubleQuoted(bashPath)}\"\r\n";
        var configYaml = $$"""
            {{shellSetting}}startup:
              checkUpdate: false
            starReminder:
              enabled: false
            marketplace:
              autoUpdate: off
            web_search:
              enabled: false
            browser:
              enabled: false
            completion:
              notify: on
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
            """;

        FileOperations.WriteAllTextReplacingWithRetry(
            paths.ModelsPath,
            NormalizeNewLines(modelsYaml),
            new UTF8Encoding(false));
        FileOperations.WriteAllTextReplacingWithRetry(
            paths.ConfigPath,
            NormalizeNewLines(configYaml),
            new UTF8Encoding(false));
    }

    private static void BackupIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var backupPath = $"{path}.backup-{timestamp}";
        var suffix = 0;
        while (File.Exists(backupPath))
        {
            suffix++;
            backupPath = $"{path}.backup-{timestamp}-{suffix}";
        }
        FileOperations.CopyReplacingWithRetry(path, backupPath);
    }

    private static string EscapeYamlDoubleQuoted(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal)
            .Trim() + Environment.NewLine;

    private static async Task VerifyGatewayAsync(string apiKey, DeploymentSettings deployment)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        var body = JsonSerializer.Serialize(new
        {
            model = deployment.ModelId,
            max_tokens = 8,
            stream = false,
            messages = new[] { new { role = "user", content = "Reply only OK." } },
        });
        using var response = await client.PostAsync(
            $"{deployment.GatewayBaseUrl}/v1/messages",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var safeBody = responseBody.Length > 800 ? responseBody[..800] : responseBody;
            throw new InvalidOperationException(
                $"게이트웨이 응답 오류: HTTP {(int)response.StatusCode} {response.ReasonPhrase}\r\n{safeBody}");
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        string apiKey)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment[ApiKeyEnvironmentVariable] = apiKey;

        using var process = new Process { StartInfo = startInfo };
        process.Start();
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
            throw new TimeoutException($"명령 실행 제한시간({timeout.TotalSeconds:0}초)을 초과했습니다.");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {Redact(message)}";
        _status.AppendText($"{line}{Environment.NewLine}");
        if (_logPath is not null)
        {
            try
            {
                FileOperations.AppendAllTextWithRetry(
                    _logPath,
                    $"{line}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // A diagnostics lock must not replace the installation result.
            }
        }
    }

    private void InitializeDiagnostics(InstallPaths paths)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        _logPath = Path.Combine(paths.DiagnosticsDirectory, $"install-{timestamp}.log");
        _statePath = Path.Combine(paths.DiagnosticsDirectory, "latest-install-state.json");

        var recoveryPath = Path.Combine(paths.DiagnosticsDirectory, "RECOVERY.md");
        var recovery = $"""
            # GajaeCode installation recovery

            Latest state: `{_statePath}`

            Installation logs: `{paths.DiagnosticsDirectory}\install-*.log`

            Managed files:

            - Binary: `{paths.BinaryPath}`
            - Models: `{paths.ModelsPath}`
            - Runtime config: `{paths.ConfigPath}`

            Recovery procedure:

            1. Open `latest-install-state.json` and identify `stage`, `status`, and `error`.
            2. Read the matching installation log without copying any API key into notes or Git.
            3. Check timestamped `*.backup-*` configuration files before changing managed YAML.
            4. Fix the installer source or deployment payload on the connected staging PC.
            5. Rebuild the installer, verify SHA-256, and run it again. The installer is idempotent.
            6. Do not delete failed logs until a succeeding report has been produced.

            The API key value is intentionally absent from all diagnostic artifacts.
            """;
        FileOperations.WriteAllTextReplacingWithRetry(
            recoveryPath,
            NormalizeNewLines(recovery),
            new UTF8Encoding(false));
    }

    private void SetStage(string stage, string status, string? error = null)
    {
        _currentStage = stage;
        if (_statePath is null)
        {
            return;
        }

        var state = new
        {
            schemaVersion = 1,
            updatedAt = DateTimeOffset.Now,
            stage,
            status,
            error = error is null ? null : Redact(error),
            logPath = _logPath,
            gateway = _deployment.GatewayBaseUrl,
            model = _deployment.ModelId,
            provider = _deployment.ProviderId,
            apiKeyEnvironmentVariable = ApiKeyEnvironmentVariable,
            apiKeyPresent = _activeApiKey.Length > 0,
            gitBashRequired = true,
        };
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        FileOperations.WriteAllTextReplacingWithRetry(
            _statePath,
            json,
            new UTF8Encoding(false));
    }

    private void LogDiagnosticException(Exception exception)
    {
        if (_logPath is null)
        {
            return;
        }

        var detail = Redact(exception.ToString());
        try
        {
            FileOperations.AppendAllTextWithRetry(
                _logPath,
                $"{Environment.NewLine}--- exception detail ---{Environment.NewLine}{detail}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch (Exception diagnosticsException) when (
            diagnosticsException is IOException or UnauthorizedAccessException)
        {
            // The message box and state file still retain the sanitized failure.
        }
    }

    private string Redact(string value)
    {
        var redacted = value;
        if (_activeApiKey.Length > 0)
        {
            redacted = redacted.Replace(_activeApiKey, "[REDACTED]", StringComparison.Ordinal);
        }
        var storedKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (!string.IsNullOrEmpty(storedKey) && storedKey.Length >= 8)
        {
            redacted = redacted.Replace(storedKey, "[REDACTED]", StringComparison.Ordinal);
        }
        return redacted;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => string.Join(
            Environment.NewLine,
            new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(value => value.Length > 0));
    }

    private sealed record InstallPaths(
        string InstallDirectory,
        string BinaryPath,
        string AgentDirectory,
        string ModelsPath,
        string ConfigPath,
        string DiagnosticsDirectory)
    {
        public static InstallPaths Create()
        {
            var installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GajaeCode",
                "bin");
            var agentDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".gjc",
                "agent");
            return new InstallPaths(
                installDirectory,
                Path.Combine(installDirectory, "gjc.exe"),
                agentDirectory,
                Path.Combine(agentDirectory, "models.yml"),
                Path.Combine(agentDirectory, "config.yml"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GajaeCode",
                    "diagnostics"));
        }
    }
}
