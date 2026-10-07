namespace HomeBackend.Features.Vms;

/// <summary>
/// What a VM is made of: one <c>/etc/vm/&lt;name&gt;.conf</c>, read by deploy/vm/vm-run when vm@&lt;name&gt; starts.
/// </summary>
/// <param name="MemoryMb">RAM in MiB.</param>
/// <param name="Disk">Block device of the system disk, e.g. /dev/home/router (a thin LVM volume).</param>
/// <param name="Nets">Network cards in order: the first is the guest's first NIC.</param>
/// <param name="Autostart">Started at boot by vm-autostart.service.</param>
public sealed record VmConfig(string Name, int Cpus, int MemoryMb, string Disk, IReadOnlyList<VmNet> Nets, bool Autostart);

/// <summary>One network card: the host bridge it's plugged into and its MAC address.</summary>
public sealed record VmNet(string Bridge, string Mac);
