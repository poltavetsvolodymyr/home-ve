namespace HomeBackend.Configuration;

/// <summary>
/// The <c>HomeBackend</c> config section. On the host it comes from <see cref="ConfigFile"/>
/// (sample: deploy/config.example.json); in development from appsettings.Development.json.
/// </summary>
public sealed class HomeBackendOptions
{
    public const string Section = "HomeBackend";

    /// <summary>The host's config file. Optional, and it overrides every other config source.</summary>
    public const string ConfigFile = "/etc/home-backend/config.json";

    /// <summary>Kestrel listen URLs. Loopback only: nginx on the host serves the frontend and proxies /api here.</summary>
    public string[] Urls { get; set; } = [];

    /// <summary>Only clients from these networks get any response at all.</summary>
    public string[] AllowedNetworks { get; set; } = [];

    /// <summary>PBKDF2 hash, produced by <c>home-backend set-password</c>.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>One <c>&lt;name&gt;.conf</c> per VM; vm@&lt;name&gt;.service runs it (deploy/vm/vm-run).</summary>
    public string VmConfigDir { get; set; } = "/etc/vm";

    /// <summary>Where a running VM keeps its sockets: <c>&lt;dir&gt;/vm-&lt;name&gt;/vnc.sock</c>.</summary>
    public string VmRuntimeDir { get; set; } = "/run";

    /// <summary>
    /// ISO images for the VMs' CD drives. vm-run only takes images from /var/lib/home-backend/iso, so on the
    /// host this stays as it is; development points it somewhere else.
    /// </summary>
    public string IsoDir { get; set; } = "/var/lib/home-backend/iso";

    /// <summary>Where the VM backups are (deploy/vm/vm-backup): <c>&lt;dir&gt;/&lt;vm&gt;/&lt;vm&gt;-&lt;stamp&gt;.img.zst</c>.</summary>
    public string BackupDir { get; set; } = "/var/backups/vm";

    /// <summary>
    /// LVM volume group of the thin pool "data": a new VM's disk is /dev/&lt;group&gt;/&lt;name&gt;. install.sh fills it
    /// in from the host's LVM; while it is empty, no VM can be created.
    /// </summary>
    public string DiskGroup { get; set; } = "";

    /// <summary>Writable directory for the cookie key ring.</summary>
    public string DataDir { get; set; } = "/var/lib/home-backend";

    /// <summary>Serve fake data (for development without the host).</summary>
    public bool Mock { get; set; }

    // Defaults live here instead of appsettings.json: configuration arrays merge by index,
    // so a shorter list in /etc/home-backend/config.json would otherwise keep the tail of the defaults.
    public HomeBackendOptions WithDefaults()
    {
        if (Urls.Length == 0) Urls = ["http://127.0.0.1:5000"];
        // the private ranges (home networks, VPNs) and the host itself
        if (AllowedNetworks.Length == 0) AllowedNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128"];
        // the mock VMs' disks are in /dev/home
        if (DiskGroup.Length == 0 && Mock) DiskGroup = "home";
        return this;
    }
}
