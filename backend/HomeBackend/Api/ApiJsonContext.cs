using System.Text.Json;
using System.Text.Json.Serialization;
using HomeBackend.Features.Auth;
using HomeBackend.Features.Host;
using HomeBackend.Features.Logs;
using HomeBackend.Features.Update;
using HomeBackend.Features.Vms;
using Microsoft.AspNetCore.Mvc;

namespace HomeBackend.Api;

/// <summary>
/// Every type the API reads or writes, in camelCase. The trimmed build has no reflection-based JSON,
/// so a new request or response type must be added here (the build warns about it otherwise).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HostResponse))]
[JsonSerializable(typeof(IReadOnlyList<VmInfo>))]
[JsonSerializable(typeof(VmInfo))]
[JsonSerializable(typeof(VmSettingsRequest))]
[JsonSerializable(typeof(IReadOnlyList<LogEntry>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(UpdateStatus))]
[JsonSerializable(typeof(ProblemDetails))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
