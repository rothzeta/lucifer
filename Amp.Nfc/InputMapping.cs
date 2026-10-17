using System.Text.Json;

namespace Amp.Nfc;

public sealed record InputMappingResult(InputMapping? Mapping, IReadOnlyList<FieldError> Errors);

/// <summary>
/// Key code to transport command mapping read from an explicit file. There are no built-in codes:
/// which events a controller sends is only known by inspecting it (`input inspect`). Only EV_KEY
/// events are mapped. Error fields are paths into the file, e.g. <c>keys[0].action</c>.
/// </summary>
public sealed class InputMapping
{
    /// <summary>Highest Linux key code (KEY_MAX, 0x2ff).</summary>
    public const int MaxCode = 0x2ff;

    private static readonly (string Name, TransportCommand Command)[] Actions =
    [
        ("play", TransportCommand.Play),
        ("pause", TransportCommand.Pause),
        ("toggle-pause", TransportCommand.TogglePause),
        ("next", TransportCommand.Next),
        ("previous", TransportCommand.Previous),
    ];

    private readonly Dictionary<int, TransportCommand> _keys;

    private InputMapping(Dictionary<int, TransportCommand> keys) => _keys = keys;

    public int Count => _keys.Count;

    public TransportCommand? Lookup(int code) => _keys.TryGetValue(code, out var command) ? command : null;

    public static string Name(TransportCommand command) => Actions.First(a => a.Command == command).Name;

    public static InputMappingResult Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("keys", $"cannot read {path}: {ex.Message}");
        }
        return Parse(text);
    }

    public static InputMappingResult Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            return Fail("keys", $"mapping is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("keys", out var list)
                || list.ValueKind != JsonValueKind.Array)
                return Fail("keys", "must be a JSON object with a \"keys\" array of { \"code\", \"action\" }");

            if (list.GetArrayLength() == 0)
                return Fail("keys", "lists no keys; to run without controls, leave LUCIFER_INPUT_MAP unset");

            var errors = new List<FieldError>();
            var keys = new Dictionary<int, TransportCommand>();
            var index = 0;
            foreach (var entry in list.EnumerateArray())
            {
                var at = $"keys[{index++}]";
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new(at, "must be an object with \"code\" and \"action\""));
                    continue;
                }

                var codeOk = false;
                var code = 0;
                if (!entry.TryGetProperty("code", out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out code))
                    errors.Add(new($"{at}.code", "is required and must be a whole number (the EV_KEY code)"));
                else if (code is < 0 or > MaxCode)
                    errors.Add(new($"{at}.code", $"must be from 0 to {MaxCode}"));
                else if (keys.ContainsKey(code))
                    errors.Add(new($"{at}.code", $"{code} is mapped more than once"));
                else
                    codeOk = true;

                TransportCommand? command = null;
                if (!entry.TryGetProperty("action", out var a) || a.ValueKind != JsonValueKind.String)
                    errors.Add(new($"{at}.action", $"is required; one of {string.Join(", ", Actions.Select(x => x.Name))}"));
                else if (Actions.FirstOrDefault(x => x.Name == a.GetString()) is { Name: not null } found)
                    command = found.Command;
                else
                    errors.Add(new($"{at}.action", $"unknown action; one of {string.Join(", ", Actions.Select(x => x.Name))}"));

                if (codeOk && command is { } known)
                    keys[code] = known;
            }

            return errors.Count > 0 ? new(null, errors) : new(new InputMapping(keys), errors);
        }
    }

    private static InputMappingResult Fail(string field, string message) => new(null, [new(field, message)]);
}
