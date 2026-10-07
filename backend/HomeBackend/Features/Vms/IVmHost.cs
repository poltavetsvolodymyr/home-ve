namespace HomeBackend.Features.Vms;

/// <summary>The VMs' configs (/etc/vm) and their systemd units.</summary>
public interface IVmHost
{
    /// <summary>Every VM with a valid config, by name. Broken files are skipped and logged.</summary>
    IReadOnlyList<VmConfig> ReadConfigs();

    /// <summary>Replaces a VM's config file.</summary>
    void WriteConfig(VmConfig config);

    /// <summary>The unit state of each VM, in the order given.</summary>
    Task<IReadOnlyList<VmUnitState>> ReadUnitsAsync(IReadOnlyList<string> names, CancellationToken ct);

    Task RunAsync(string name, VmAction action, CancellationToken ct);

    /// <summary>CPU time used so far (seconds) and resident memory (bytes) of a process; null when it's gone.</summary>
    (double CpuSeconds, long RssBytes)? ReadProcess(int pid);

    /// <summary>Bridges a VM's network card can be plugged into.</summary>
    IReadOnlyList<string> ReadBridges();
}
