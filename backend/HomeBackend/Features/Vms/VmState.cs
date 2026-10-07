namespace HomeBackend.Features.Vms;

/// <summary>What systemd knows about vm@&lt;name&gt;.service.</summary>
/// <param name="ActiveState">active, activating, deactivating, inactive or failed.</param>
/// <param name="Since">When it last became active; null if it isn't.</param>
/// <param name="MainPid">The QEMU process; null when not running.</param>
public sealed record VmUnitState(string ActiveState, string SubState, DateTimeOffset? Since, int? MainPid);

/// <summary>A VM as the pages show it.</summary>
/// <param name="State">running, starting, stopping, stopped or failed.</param>
/// <param name="CpuPercent">Share of the VM's own CPUs in use, 0–100; null when not running or not measured yet.</param>
/// <param name="MemoryBytes">RAM the VM really occupies on the host (QEMU's resident memory).</param>
public sealed record VmInfo(string Name, string State, DateTimeOffset? Since, double? CpuPercent, long? MemoryBytes, VmConfig Config);

/// <summary>Buttons on a VM's page. Shutdown and reboot ask the guest; poweroff pulls the plug.</summary>
public enum VmAction { Start, Shutdown, Reboot, Poweroff }
