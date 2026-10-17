using System.Diagnostics;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

/// <summary>Runs the built application as a separate process, as an operator would.</summary>
public class BuiltProcessTests : IDisposable
{
    private const string Release = "a24495e4-eb70-48cd-b0fa-31a606459d26";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    private readonly string _ipc = Path.Combine(Path.GetTempPath(), "lucifer-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_ipc))
            Directory.Delete(_ipc, recursive: true);
    }

    private sealed record Result(int ExitCode, string Out, string Err, TimeSpan Elapsed);

    private Result Run(string[] args, params (string, string)[] env)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Amp.Nfc.dll");
        Assert.True(File.Exists(dll), $"built application missing: {dll}");

        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(dll);
        foreach (var a in args) info.ArgumentList.Add(a);

        // Reader path points at a device that does not exist: any reader access fails loudly.
        info.Environment["LUCIFER_NFC_DEVICE"] = "/dev/lucifer-test-no-such-reader";
        info.Environment["LUCIFER_IPC_DIR"] = _ipc;
        foreach (var name in new[] { "NAVIDROME_URL", "NAVIDROME_USER", "NAVIDROME_PASSWORD" })
            info.Environment.Remove(name);
        foreach (var (k, v) in env) info.Environment[k] = v;

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Deadline))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"did not finish within {Deadline}: {string.Join(' ', args)}");
        }
        process.WaitForExit();
        return new Result(process.ExitCode, stdout.Result, stderr.Result, clock.Elapsed);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    public void Help_succeeds_and_lists_all_modes(string arg)
    {
        var r = Run([arg]);

        Assert.Equal(0, r.ExitCode);
        foreach (var mode in new[] { "play", "tag read", "tag write --release <uuid>", "check" })
            Assert.Contains(mode, r.Out);
    }

    [Theory]
    [InlineData]
    [InlineData("bogus")]
    [InlineData("tag", "write")]
    [InlineData("tag", "write", "--release", "nope")]
    public void Invalid_invocations_fail_promptly_before_reader_access(params string[] args)
    {
        var r = Run(args);

        Assert.NotEqual(0, r.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(r.Err));
        Assert.DoesNotContain("lucifer-test-no-such-reader", r.Err + r.Out);
        // The reader lock is only created once a reader mode is dispatched.
        Assert.False(Directory.Exists(_ipc) && Directory.EnumerateFileSystemEntries(_ipc).Any());
    }

    [Fact]
    public void Check_reports_configuration_errors_by_field_without_secrets()
    {
        var r = Run(["check"], ("NAVIDROME_URL", "https://u:topsecretpw@bad host"), ("NAVIDROME_USER", " "), ("NAVIDROME_PASSWORD", "topsecretpw"));

        Assert.NotEqual(0, r.ExitCode);
        var all = r.Out + r.Err;
        Assert.Contains("NAVIDROME_URL", all);
        Assert.Contains("NAVIDROME_USER", all);
        Assert.DoesNotContain("topsecretpw", all);
    }

    [Fact]
    public void Check_passes_with_valid_configuration_without_hardware()
    {
        var r = Run(["check"],
            ("NAVIDROME_URL", "https://music.example.test"), ("NAVIDROME_USER", "u"), ("NAVIDROME_PASSWORD", "pw-value"));

        Assert.Equal(0, r.ExitCode);
        Assert.DoesNotContain("pw-value", r.Out + r.Err);
    }

    [Fact]
    public void Writer_mode_does_not_require_navidrome_credentials()
    {
        // With no server settings, the failure must come from the (absent) reader, not configuration.
        var r = Run(["tag", "read"]);

        Assert.NotEqual(0, r.ExitCode);
        Assert.DoesNotContain("NAVIDROME", r.Err + r.Out);
        Assert.Contains("lucifer-test-no-such-reader", r.Err);
    }

    [Theory]
    [InlineData("tag", "read")]
    [InlineData("tag", "write", "--release", Release)]
    public void A_second_reader_owner_is_refused_before_the_reader_is_opened(params string[] args)
    {
        using var owner = ReaderLock.Acquire(_ipc, "/dev/lucifer-test-no-such-reader");

        var r = Run(args);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("already", r.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stop", r.Err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Play_is_refused_when_the_reader_is_owned()
    {
        using var owner = ReaderLock.Acquire(_ipc, "/dev/lucifer-test-no-such-reader");

        var r = Run(["play"],
            ("NAVIDROME_URL", "https://music.example.test"), ("NAVIDROME_USER", "u"), ("NAVIDROME_PASSWORD", "pw"));

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("already", r.Err, StringComparison.OrdinalIgnoreCase);
    }
}
