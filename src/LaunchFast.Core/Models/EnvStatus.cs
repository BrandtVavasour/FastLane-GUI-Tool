namespace LaunchFast.Core.Models;

public sealed record EnvStatus(
    IReadOnlyList<string> Satisfied,
    IReadOnlyList<string> Missing);
