using Amp.Nfc;

using var cancellation = new CancellationTokenSource();
using var signals = StopSignals.Register(cancellation);

return await Cli.RunAsync(
    args,
    Environment.GetEnvironmentVariable,
    Console.Out,
    Console.Error,
    cancellation.Token);
