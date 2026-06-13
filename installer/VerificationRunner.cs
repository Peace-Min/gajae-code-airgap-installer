using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GajaeCode.AirgapInstaller;

internal sealed class VerificationRunner
{
    private const string ApiKeyEnvironmentVariable = "GJC_INTERNAL_API_KEY";
    private readonly Action<string> _log;

    public VerificationRunner(Action<string> log)
    {
        _log = log;
    }

    public async Task<VerificationSummary> RunAsync(
        string windowsBinaryPath,
        DeploymentSettings deployment,
        bool expectWsl)
    {
        var paths = VerificationPaths.Create();
        Directory.CreateDirectory(paths.ProjectDirectory);
        Directory.CreateDirectory(paths.EvidenceDirectory);
        CreateVerificationProject(paths.ProjectDirectory);

        var checks = new List<VerificationCheck>();
        checks.Add(CheckFile("Windows GJC binary", windowsBinaryPath));
        checks.Add(CheckEnvironmentVariable());
        checks.Add(CheckManagedConfiguration(paths.AgentDirectory, deployment));
        checks.Add(CheckGitBash());
        checks.Add(await CheckProcessAsync(
            "Windows GJC version",
            windowsBinaryPath,
            ["--version"],
            paths.ProjectDirectory,
            null,
            TimeSpan.FromMinutes(1)));

        if (expectWsl)
        {
            checks.Add(await CheckProcessAsync(
                "WSL tmux and Linux GJC",
                "wsl.exe",
                [
                    "-d", "GajaeCode",
                    "-u", "gjc",
                    "--",
                    "bash", "-lc",
                    "source ~/.config/gajae-code/env && tmux -V && gjc --version",
                ],
                paths.ProjectDirectory,
                null,
                TimeSpan.FromMinutes(1)));

            checks.Add(await RunAgentProjectCheckAsync(paths));
        }
        else
        {
            checks.Add(new VerificationCheck(
                "WSL tmux and Linux GJC",
                "warning",
                "WSL2/tmux installation was not selected.",
                null,
                null));
        }

        var overall = checks.Any(check => check.Status == "failed")
            ? "failed"
            : checks.Any(check => check.Status == "warning")
                ? "warning"
                : "passed";
        var summary = new VerificationSummary(
            1,
            DateTimeOffset.Now,
            overall,
            deployment.GatewayBaseUrl,
            deployment.ModelId,
            checks);

        var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(paths.JsonPath, json, new UTF8Encoding(false));
        File.WriteAllText(paths.HtmlPath, RenderHtml(summary), new UTF8Encoding(false));
        _log($"검증 JSON: {paths.JsonPath}");
        _log($"검증 HTML: {paths.HtmlPath}");
        return summary;
    }

    private async Task<VerificationCheck> RunAgentProjectCheckAsync(VerificationPaths paths)
    {
        var prompt =
            "Read AGENTS.md. Fix the intentional defect. You must create PLAN.md before editing, run bash tests/run.sh, " +
            "and create EVIDENCE.md containing the exact test command and result. Do not use web or browser tools.";
        var result = await RunProcessAsync(
            "wsl.exe",
            [
                "-d", "GajaeCode",
                "-u", "gjc",
                "--",
                "bash", "-lc",
                "source ~/.config/gajae-code/env; project=$(wslpath \"$1\"); cd \"$project\"; exec gjc --print \"$2\"",
                "gajaecode-verify",
                paths.ProjectDirectory,
                prompt,
            ],
            paths.ProjectDirectory,
            null,
            TimeSpan.FromMinutes(15));

        var testResult = await RunProcessAsync(
            "wsl.exe",
            [
                "-d", "GajaeCode",
                "-u", "gjc",
                "--",
                "bash", "-lc",
                "project=$(wslpath \"$1\"); cd \"$project\"; bash tests/run.sh",
                "gajaecode-test",
                paths.ProjectDirectory,
            ],
            paths.ProjectDirectory,
            null,
            TimeSpan.FromMinutes(2));

        var planExists = File.Exists(Path.Combine(paths.ProjectDirectory, "PLAN.md"));
        var evidencePath = Path.Combine(paths.ProjectDirectory, "EVIDENCE.md");
        var evidenceExists = File.Exists(evidencePath);
        var evidenceText = evidenceExists ? File.ReadAllText(evidencePath) : string.Empty;
        var evidenceHasCommand = evidenceText.Contains("bash tests/run.sh", StringComparison.Ordinal);
        var passed = result.ExitCode == 0 &&
                     testResult.ExitCode == 0 &&
                     planExists &&
                     evidenceExists &&
                     evidenceHasCommand;
        var details = string.Join(
            Environment.NewLine,
            [
                $"agent_exit={result.ExitCode}",
                $"test_exit={testResult.ExitCode}",
                $"plan={planExists}",
                $"evidence={evidenceExists}",
                $"evidence_command={evidenceHasCommand}",
                result.CombinedOutput,
                testResult.CombinedOutput,
            ]);
        return new VerificationCheck(
            "Agent instruction and tool loop",
            passed ? "passed" : "failed",
            passed
                ? "AGENTS rules, edit, test, and evidence loop completed."
                : "The model or tool-call loop did not satisfy every project requirement.",
            result.ExitCode,
            Sanitize(details));
    }

    private static VerificationCheck CheckFile(string name, string path)
    {
        if (!File.Exists(path))
        {
            return new VerificationCheck(name, "failed", $"Missing file: {path}", null, null);
        }

        using var input = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        return new VerificationCheck(name, "passed", $"SHA-256: {hash}", 0, null);
    }

    private static VerificationCheck CheckEnvironmentVariable()
    {
        var value = Environment.GetEnvironmentVariable(
            ApiKeyEnvironmentVariable,
            EnvironmentVariableTarget.User);
        return string.IsNullOrWhiteSpace(value)
            ? new VerificationCheck(
                "API key environment",
                "failed",
                $"{ApiKeyEnvironmentVariable} is missing.",
                null,
                null)
            : new VerificationCheck(
                "API key environment",
                "passed",
                $"{ApiKeyEnvironmentVariable} is present. Value is not reported.",
                0,
                null);
    }

    private static VerificationCheck CheckManagedConfiguration(
        string agentDirectory,
        DeploymentSettings deployment)
    {
        var modelsPath = Path.Combine(agentDirectory, "models.yml");
        var configPath = Path.Combine(agentDirectory, "config.yml");
        if (!File.Exists(modelsPath) || !File.Exists(configPath))
        {
            return new VerificationCheck(
                "Offline managed configuration",
                "failed",
                "models.yml or config.yml is missing.",
                null,
                null);
        }

        var models = File.ReadAllText(modelsPath);
        var config = File.ReadAllText(configPath);
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        var noSecret = string.IsNullOrEmpty(key) || !models.Contains(key, StringComparison.Ordinal);
        var passed = models.Contains($"baseUrl: \"{deployment.GatewayBaseUrl}\"", StringComparison.Ordinal) &&
                     models.Contains($"apiKeyEnv: \"{ApiKeyEnvironmentVariable}\"", StringComparison.Ordinal) &&
                     config.Contains("checkUpdate: false", StringComparison.Ordinal) &&
                     config.Contains("autoUpdate: off", StringComparison.Ordinal) &&
                     Count(config, "enabled: false") >= 3 &&
                     noSecret;
        return new VerificationCheck(
            "Offline managed configuration",
            passed ? "passed" : "failed",
            passed
                ? "Update, marketplace, web search, browser, and secret-storage checks passed."
                : "One or more offline or secret-storage settings are invalid.",
            passed ? 0 : 1,
            null);
    }

    private static VerificationCheck CheckGitBash()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Git",
            "bin",
            "bash.exe");
        return File.Exists(path)
            ? new VerificationCheck("Git Bash", "passed", path, 0, null)
            : new VerificationCheck("Git Bash", "failed", "Git Bash was not found.", null, null);
    }

    private static async Task<VerificationCheck> CheckProcessAsync(
        string name,
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? standardInput,
        TimeSpan timeout)
    {
        var result = await RunProcessAsync(executable, arguments, workingDirectory, standardInput, timeout);
        return new VerificationCheck(
            name,
            result.ExitCode == 0 ? "passed" : "failed",
            result.ExitCode == 0 ? "Command completed." : "Command failed.",
            result.ExitCode,
            Sanitize(result.CombinedOutput));
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? standardInput,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true,
        };
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
            return new ProcessResult(124, await stdoutTask, $"Timed out after {timeout}.");
        }
        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void CreateVerificationProject(string projectDirectory)
    {
        Directory.CreateDirectory(Path.Combine(projectDirectory, "app"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "tests"));
        File.WriteAllText(
            Path.Combine(projectDirectory, "AGENTS.md"),
            """
            # Verification Agent Contract

            1. Create PLAN.md before editing application files.
            2. Fix only the intentional arithmetic defect in app/calc.sh.
            3. Run `bash tests/run.sh`.
            4. Create EVIDENCE.md with the exact command and observed result.
            5. Do not use web search, browser tools, package installation, or external network access.
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(projectDirectory, "app", "calc.sh"),
            """
            #!/usr/bin/env bash
            set -euo pipefail
            left="$1"
            right="$2"
            echo $((left - right))
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(projectDirectory, "tests", "run.sh"),
            """
            #!/usr/bin/env bash
            set -euo pipefail
            actual="$(bash app/calc.sh 7 5)"
            test "$actual" = "12"
            echo "PASS: 7 + 5 = 12"
            """,
            new UTF8Encoding(false));
        File.Delete(Path.Combine(projectDirectory, "PLAN.md"));
        File.Delete(Path.Combine(projectDirectory, "EVIDENCE.md"));
    }

    private static int Count(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private static string Sanitize(string value)
    {
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        var sanitized = string.IsNullOrEmpty(key)
            ? value
            : value.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        return sanitized.Length <= 12000 ? sanitized : sanitized[..12000] + "\n[truncated]";
    }

    private static string RenderHtml(VerificationSummary summary)
    {
        var rows = string.Join(
            Environment.NewLine,
            summary.Checks.Select(check =>
                $"""
                 <tr class="{WebUtility.HtmlEncode(check.Status)}">
                   <td>{WebUtility.HtmlEncode(check.Name)}</td>
                   <td>{WebUtility.HtmlEncode(check.Status.ToUpperInvariant())}</td>
                   <td>{WebUtility.HtmlEncode(check.Message)}</td>
                   <td><pre>{WebUtility.HtmlEncode(check.Output ?? string.Empty)}</pre></td>
                 </tr>
                 """));
        return $$"""
            <!doctype html>
            <html lang="ko">
            <head>
              <meta charset="utf-8">
              <title>GajaeCode Installation Report</title>
              <style>
                body { font-family: Segoe UI, sans-serif; margin: 32px; color: #202124; }
                h1 { margin-bottom: 4px; }
                .meta { color: #5f6368; margin-bottom: 24px; }
                table { border-collapse: collapse; width: 100%; }
                th, td { border: 1px solid #dadce0; padding: 10px; vertical-align: top; text-align: left; }
                th { background: #f1f3f4; }
                tr.passed td:nth-child(2) { color: #137333; font-weight: 700; }
                tr.warning td:nth-child(2) { color: #b06000; font-weight: 700; }
                tr.failed td:nth-child(2) { color: #b3261e; font-weight: 700; }
                pre { margin: 0; white-space: pre-wrap; max-height: 280px; overflow: auto; }
                code { background: #f1f3f4; padding: 2px 4px; }
              </style>
            </head>
            <body>
              <h1>GajaeCode Installation Report</h1>
              <div class="meta">
                상태: <strong>{{WebUtility.HtmlEncode(summary.Status.ToUpperInvariant())}}</strong><br>
                생성: {{WebUtility.HtmlEncode(summary.CreatedAt.ToString("u"))}}<br>
                게이트웨이: <code>{{WebUtility.HtmlEncode(summary.Gateway)}}</code><br>
                모델: <code>{{WebUtility.HtmlEncode(summary.Model)}}</code><br>
                API Key: configured, value intentionally omitted
              </div>
              <table>
                <thead><tr><th>검사</th><th>상태</th><th>설명</th><th>증거</th></tr></thead>
                <tbody>{{rows}}</tbody>
              </table>
            </body>
            </html>
            """;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => string.Join(
            Environment.NewLine,
            new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(value => value.Length > 0));
    }

    private sealed record VerificationPaths(
        string ProjectDirectory,
        string EvidenceDirectory,
        string AgentDirectory,
        string JsonPath,
        string HtmlPath)
    {
        public static VerificationPaths Create()
        {
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var evidence = Path.Combine(user, ".gjc", "verification");
            return new VerificationPaths(
                Path.Combine(user, "GajaeCode-Verification"),
                evidence,
                Path.Combine(user, ".gjc", "agent"),
                Path.Combine(evidence, "latest.json"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "GajaeCode-Installation-Report.html"));
        }
    }
}

internal sealed record VerificationSummary(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    string Status,
    string Gateway,
    string Model,
    IReadOnlyList<VerificationCheck> Checks);

internal sealed record VerificationCheck(
    string Name,
    string Status,
    string Message,
    int? ExitCode,
    string? Output);
