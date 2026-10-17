using System.Buffers.Binary;
using System.Diagnostics;
using Amp.Nfc;

namespace Amp.Nfc.Tests;

// LUC-09: the evdev record reader, the mapping file, the input options and the `input inspect`
// command. Devices here are regular files and FIFOs holding struct input_event records written by
// the test. They prove the application's decoding, validation and command ordering; they do not
// prove that any real controller produces these codes (LUC-08 / LUC-12 on the Pi).
public class InputAdapterTests : IDisposable
{
    private readonly string _dir = Path.Combine("/tmp", "in-" + Guid.NewGuid().ToString("N")[..8]);

    public InputAdapterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static byte[] Encode(InputEvent ev, InputLayout layout, long seconds = 1_700_000_000, long micros = 123_456)
    {
        var size = InputEventFormat.RecordSize(layout);
        var bytes = new byte[size];
        var word = layout == InputLayout.Bits64 ? 8 : 4;
        if (word == 8)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(0), seconds);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), micros);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0), (int)seconds);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), (int)micros);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * word), ev.Type);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * word + 2), ev.Code);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2 * word + 4), ev.Value);
        return bytes;
    }

    private string WriteDevice(InputLayout layout, params InputEvent[] events)
    {
        var path = Path.Combine(_dir, "event0");
        File.WriteAllBytes(path, events.SelectMany(e => Encode(e, layout)).ToArray());
        return path;
    }

    private static async Task<List<InputEvent>> ReadAll(IInputDevice device)
    {
        var all = new List<InputEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var ev in device.ReadAsync(timeout.Token))
            all.Add(ev);
        return all;
    }

    // ---- record layout --------------------------------------------------------------------

    [Fact]
    public void Record_sizes_match_the_kernel_layout_for_64_and_32_bit_userland()
    {
        Assert.Equal(24, InputEventFormat.RecordSize(InputLayout.Bits64));
        Assert.Equal(16, InputEventFormat.RecordSize(InputLayout.Bits32));
        Assert.Equal(IntPtr.Size == 8 ? InputLayout.Bits64 : InputLayout.Bits32, InputEventFormat.Native);
    }

    [Theory]
    [InlineData(InputLayout.Bits64)]
    [InlineData(InputLayout.Bits32)]
    public void Decode_reads_type_code_and_value_after_the_timestamp(InputLayout layout)
    {
        var bytes = Encode(new InputEvent(InputEvent.Key, 164, 2), layout);

        var ev = InputEventFormat.Decode(bytes, layout);

        Assert.Equal(new InputEvent(1, 164, 2), ev);
    }

    [Theory]
    [InlineData(InputLayout.Bits64)]
    [InlineData(InputLayout.Bits32)]
    public void Decode_keeps_negative_values(InputLayout layout) =>
        Assert.Equal(-5, InputEventFormat.Decode(Encode(new InputEvent(2, 8, -5), layout), layout).Value);

    [Theory]
    [InlineData(InputLayout.Bits64)]
    [InlineData(InputLayout.Bits32)]
    public async Task A_device_file_is_read_as_press_release_and_repeat_records_in_order(InputLayout layout)
    {
        var path = WriteDevice(layout,
            new InputEvent(InputEvent.Key, 200, InputEvent.Press),
            new InputEvent(0, 0, 0),
            new InputEvent(InputEvent.Key, 200, InputEvent.Repeat),
            new InputEvent(InputEvent.Key, 200, InputEvent.Release));

        using var device = EvdevInputDevice.Open(path, layout);
        var events = await ReadAll(device);

        Assert.Equal(
            [new InputEvent(1, 200, 1), new InputEvent(0, 0, 0), new InputEvent(1, 200, 2), new InputEvent(1, 200, 0)],
            events);
    }

    [Fact]
    public async Task A_truncated_final_record_ends_the_stream_without_a_bogus_event()
    {
        var path = Path.Combine(_dir, "event0");
        var bytes = Encode(new InputEvent(1, 5, 1), InputLayout.Bits64);
        File.WriteAllBytes(path, [.. bytes, .. bytes.Take(10)]);

        using var device = EvdevInputDevice.Open(path, InputLayout.Bits64);

        Assert.Equal([new InputEvent(1, 5, 1)], await ReadAll(device));
    }

    [Fact]
    public void A_missing_device_is_reported_as_an_input_device_error()
    {
        var ex = Assert.Throws<InputDeviceException>(() => EvdevInputDevice.Open(Path.Combine(_dir, "nope")));

        Assert.Contains("nope", ex.Message);
    }

    // ---- device path validation -----------------------------------------------------------

    [Theory]
    [InlineData("/dev/input/event3")]
    [InlineData("/dev/input/by-id/usb-KB1K-event-kbd")]
    public void Absolute_paths_are_accepted(string path) => Assert.Null(InputDevicePath.Validate(path));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("event3")]
    [InlineData("./event3")]
    [InlineData("/dev/input/../sda")]
    [InlineData("/dev/input/event3\0x")]
    [InlineData("/dev/input/")]
    public void Relative_traversing_blank_or_directory_paths_are_rejected(string path) =>
        Assert.NotNull(InputDevicePath.Validate(path));

    // ---- mapping file ---------------------------------------------------------------------

    [Fact]
    public void A_valid_mapping_resolves_codes_to_commands()
    {
        var result = InputMapping.Parse("""
            { "keys": [
              { "code": 300, "action": "toggle-pause" },
              { "code": 301, "action": "next" },
              { "code": 302, "action": "previous" },
              { "code": 303, "action": "play" },
              { "code": 304, "action": "pause" } ] }
            """);

        Assert.Empty(result.Errors);
        var map = result.Mapping!;
        Assert.Equal(5, map.Count);
        Assert.Equal(TransportCommand.TogglePause, map.Lookup(300));
        Assert.Equal(TransportCommand.Next, map.Lookup(301));
        Assert.Equal(TransportCommand.Previous, map.Lookup(302));
        Assert.Equal(TransportCommand.Play, map.Lookup(303));
        Assert.Equal(TransportCommand.Pause, map.Lookup(304));
        Assert.Null(map.Lookup(305));
    }

    [Theory]
    [InlineData("not json", "keys")]
    [InlineData("[]", "keys")]
    [InlineData("{}", "keys")]
    [InlineData("""{ "keys": [] }""", "keys")]
    [InlineData("""{ "keys": [ { "code": 1, "action": "volume-up" } ] }""", "keys[0].action")]
    [InlineData("""{ "keys": [ { "code": 1 } ] }""", "keys[0].action")]
    [InlineData("""{ "keys": [ { "action": "next" } ] }""", "keys[0].code")]
    [InlineData("""{ "keys": [ { "code": -1, "action": "next" } ] }""", "keys[0].code")]
    [InlineData("""{ "keys": [ { "code": 768, "action": "next" } ] }""", "keys[0].code")]
    [InlineData("""{ "keys": [ { "code": "164", "action": "next" } ] }""", "keys[0].code")]
    [InlineData("""{ "keys": [ { "code": 1, "action": "next" }, { "code": 1, "action": "previous" } ] }""", "keys[1].code")]
    public void Invalid_mappings_name_the_failing_field(string json, string field)
    {
        var result = InputMapping.Parse(json);

        Assert.Null(result.Mapping);
        Assert.Contains(result.Errors, e => e.Field == field);
    }

    [Fact]
    public void An_unreadable_mapping_file_is_an_error()
    {
        var result = InputMapping.Load(Path.Combine(_dir, "absent.json"));

        Assert.Null(result.Mapping);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void The_example_mapping_file_in_the_repository_is_valid()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "input-map.example.json")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        var result = InputMapping.Load(Path.Combine(dir!, "input-map.example.json"));

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Mapping);
    }

    // ---- options --------------------------------------------------------------------------

    private static OptionsResult Load(Mode mode, params (string, string)[] values)
    {
        var env = new Dictionary<string, string>
        {
            ["NAVIDROME_URL"] = "https://music.example.test",
            ["NAVIDROME_USER"] = "u",
            ["NAVIDROME_PASSWORD"] = "p",
        };
        foreach (var (k, v) in values) env[k] = v;
        return LuciferOptions.Load(k => env.GetValueOrDefault(k), mode);
    }

    [Fact]
    public void Controls_have_no_defaults()
    {
        var o = Load(Mode.Play).Options!;

        Assert.Null(o.InputDevice);
        Assert.Null(o.InputMapPath);
        Assert.False(o.InputRequired);
        Assert.Equal(TimeSpan.FromMilliseconds(250), o.InputDebounce);
    }

    [Fact]
    public void Input_settings_are_read_and_validated()
    {
        var o = Load(Mode.Play,
            ("LUCIFER_INPUT_DEVICE", "/dev/input/event3"),
            ("LUCIFER_INPUT_MAP", "/etc/lucifer/input-map.json"),
            ("LUCIFER_INPUT_REQUIRED", "true"),
            ("LUCIFER_INPUT_DEBOUNCE_MS", "0")).Options!;

        Assert.Equal("/dev/input/event3", o.InputDevice);
        Assert.Equal("/etc/lucifer/input-map.json", o.InputMapPath);
        Assert.True(o.InputRequired);
        Assert.Equal(TimeSpan.Zero, o.InputDebounce);
    }

    [Theory]
    [InlineData("LUCIFER_INPUT_DEVICE", "event3")]
    [InlineData("LUCIFER_INPUT_DEVICE", "")]
    [InlineData("LUCIFER_INPUT_MAP", " ")]
    [InlineData("LUCIFER_INPUT_REQUIRED", "yes")]
    [InlineData("LUCIFER_INPUT_DEBOUNCE_MS", "-1")]
    [InlineData("LUCIFER_INPUT_DEBOUNCE_MS", "5001")]
    [InlineData("LUCIFER_INPUT_DEBOUNCE_MS", "x")]
    public void Invalid_input_settings_are_rejected_by_name(string name, string value)
    {
        var result = Load(Mode.Play, (name, value));

        Assert.Null(result.Options);
        Assert.Contains(result.Errors, e => e.Field == name);
    }

    [Theory]
    [InlineData("LUCIFER_INPUT_DEVICE")]
    [InlineData("LUCIFER_INPUT_MAP")]
    public void Required_controls_need_both_a_device_and_a_mapping(string present)
    {
        var other = present == "LUCIFER_INPUT_DEVICE" ? "LUCIFER_INPUT_MAP" : "LUCIFER_INPUT_DEVICE";
        var result = Load(Mode.Play, ("LUCIFER_INPUT_REQUIRED", "true"), (present, "/x/y"));

        Assert.Null(result.Options);
        Assert.Contains(result.Errors, e => e.Field == other);
    }

    // ---- command line ---------------------------------------------------------------------

    [Fact]
    public void Input_inspect_parses_with_and_without_options()
    {
        var bare = CommandLine.Parse(["input", "inspect"]);
        var full = CommandLine.Parse(["input", "inspect", "--device", "/dev/input/event3", "--count", "5"]);

        Assert.Null(bare.Error);
        Assert.Equal(Mode.InputInspect, bare.Mode);
        Assert.Null(bare.Device);
        Assert.Null(full.Error);
        Assert.Equal("/dev/input/event3", full.Device);
        Assert.Equal(5, full.Count);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("input", "dump")]
    [InlineData("input", "inspect", "--device")]
    [InlineData("input", "inspect", "--count", "0")]
    [InlineData("input", "inspect", "--count", "x")]
    [InlineData("input", "inspect", "--bogus", "1")]
    [InlineData("input", "inspect", "extra")]
    public void Bad_input_arguments_are_usage_errors(params string[] args) =>
        Assert.NotNull(CommandLine.Parse(args).Error);

    // ---- input inspect ----------------------------------------------------------------------

    private async Task<(int Code, string Out, string Err)> Cli_(string[] args, params (string, string)[] env)
    {
        var values = env.ToDictionary(e => e.Item1, e => e.Item2);
        values.TryAdd("LUCIFER_IPC_DIR", _dir);
        var output = new StringWriter();
        var error = new StringWriter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var code = await Amp.Nfc.Cli.RunAsync(args, k => values.GetValueOrDefault(k), output, error, cts.Token);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Inspect_prints_type_code_and_value_for_each_event_and_skips_sync_records()
    {
        var path = WriteDevice(InputEventFormat.Native,
            new InputEvent(InputEvent.Key, 164, 1), new InputEvent(0, 0, 0), new InputEvent(InputEvent.Key, 164, 2), new InputEvent(InputEvent.Key, 164, 0), new InputEvent(4, 4, 458_792));

        var (code, output, _) = await Cli_(["input", "inspect", "--device", path]);

        Assert.Equal(Amp.Nfc.Cli.Ok, code);
        Assert.Contains("EV_KEY code=164 value=1 (press)", output);
        Assert.Contains("EV_KEY code=164 value=2 (repeat)", output);
        Assert.Contains("EV_KEY code=164 value=0 (release)", output);
        Assert.Contains("EV_MSC code=4 value=458792", output);
        Assert.DoesNotContain("EV_SYN", output);
    }

    [Fact]
    public async Task Inspect_stops_after_count_events()
    {
        var path = WriteDevice(InputEventFormat.Native,
            new InputEvent(InputEvent.Key, 1, 1), new InputEvent(InputEvent.Key, 2, 1), new InputEvent(InputEvent.Key, 3, 1));

        var (code, output, _) = await Cli_(["input", "inspect", "--device", path, "--count", "2"]);

        Assert.Equal(Amp.Nfc.Cli.Ok, code);
        Assert.Contains("code=1 ", output);
        Assert.Contains("code=2 ", output);
        Assert.DoesNotContain("code=3 ", output);
    }

    [Fact]
    public async Task Inspect_takes_the_device_from_the_environment_when_no_argument_is_given()
    {
        var path = WriteDevice(InputEventFormat.Native, new InputEvent(InputEvent.Key, 9, 1));

        var (code, output, _) = await Cli_(["input", "inspect"], ("LUCIFER_INPUT_DEVICE", path));

        Assert.Equal(Amp.Nfc.Cli.Ok, code);
        Assert.Contains("code=9 ", output);
    }

    [Fact]
    public async Task Inspect_with_an_invalid_device_argument_fails_before_opening_anything()
    {
        var (code, output, error) = await Cli_(["input", "inspect", "--device", "relative/event3"]);

        Assert.Equal(Amp.Nfc.Cli.Usage, code);
        Assert.Contains("--device", error);
        Assert.Empty(output);
    }

    [Fact]
    public async Task Inspect_without_any_device_is_a_usage_error()
    {
        var (code, _, error) = await Cli_(["input", "inspect"]);

        Assert.Equal(Amp.Nfc.Cli.Usage, code);
        Assert.Contains("LUCIFER_INPUT_DEVICE", error);
    }

    [Fact]
    public async Task Inspect_with_a_missing_device_fails_with_a_message()
    {
        var (code, _, error) = await Cli_(["input", "inspect", "--device", Path.Combine(_dir, "gone")]);

        Assert.Equal(Amp.Nfc.Cli.Failed, code);
        Assert.Contains("gone", error);
    }

    [Fact]
    public async Task Inspect_does_not_need_server_settings_and_does_not_take_the_reader_lock()
    {
        var path = WriteDevice(InputEventFormat.Native, new InputEvent(InputEvent.Key, 9, 1));
        // The player (or a tag command) owns the reader.
        using var owner = ReaderLock.Acquire(_dir, "/dev/ttyUSB0");

        var (code, output, _) = await Cli_(["input", "inspect", "--device", path]);

        Assert.Equal(Amp.Nfc.Cli.Ok, code);
        Assert.Contains("code=9 ", output);
    }

    // ---- check ------------------------------------------------------------------------------

    private string Catalogue()
    {
        var path = Path.Combine(_dir, "catalogue.json");
        File.WriteAllText(path, """{ "source": "navidrome", "albums": {} }""");
        return path;
    }

    private Task<(int Code, string Out, string Err)> Check(params (string, string)[] env) =>
        Cli_(["check"], [.. env, ("NAVIDROME_URL", "https://music.example.test"), ("NAVIDROME_USER", "u"),
            ("NAVIDROME_PASSWORD", "p"), ("LUCIFER_CATALOGUE_PATH", Catalogue())]);

    [Fact]
    public async Task Check_warns_that_controls_are_disabled_when_nothing_is_configured()
    {
        var (code, output, _) = await Check();

        Assert.Equal(Amp.Nfc.Cli.Ok, code);
        Assert.Contains("controls:", output);
        Assert.Contains("disabled", output);
        Assert.Contains("LUCIFER_INPUT_MAP", output);
    }

    [Fact]
    public async Task Check_warns_when_a_device_is_set_without_a_mapping()
    {
        var (code, output, _) = await Check(("LUCIFER_INPUT_DEVICE", "/dev/input/event3"));

        Assert.Equal(Amp.Nfc.Cli.Ok, code);
        Assert.Contains("disabled", output);
        Assert.Contains("LUCIFER_INPUT_MAP", output);
    }

    [Fact]
    public async Task Check_reports_enabled_controls_without_opening_the_device()
    {
        var map = Path.Combine(_dir, "map.json");
        File.WriteAllText(map, """{ "keys": [ { "code": 7, "action": "next" } ] }""");

        var (code, output, _) = await Check(("LUCIFER_INPUT_DEVICE", Path.Combine(_dir, "not-there")), ("LUCIFER_INPUT_MAP", map));

        Assert.Equal(Amp.Nfc.Cli.Ok, code);
        Assert.Contains("controls:", output);
        Assert.Contains("1 key", output);
        Assert.DoesNotContain("disabled", output);
    }

    [Fact]
    public async Task Check_fails_on_an_invalid_mapping_file()
    {
        var map = Path.Combine(_dir, "map.json");
        File.WriteAllText(map, """{ "keys": [ { "code": 7, "action": "louder" } ] }""");

        var (code, _, error) = await Check(("LUCIFER_INPUT_DEVICE", "/dev/input/event3"), ("LUCIFER_INPUT_MAP", map));

        Assert.Equal(Amp.Nfc.Cli.Failed, code);
        Assert.Contains("keys[0].action", error);
    }
}
