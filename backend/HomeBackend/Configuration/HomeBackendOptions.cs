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

    /// <summary>Writable directory for the cookie key ring.</summary>
    public string DataDir { get; set; } = "/var/lib/home-backend";

    /// <summary>Serve fake data (for development without the host).</summary>
    public bool Mock { get; set; }

    // Defaults live here instead of appsettings.json: configuration arrays merge by index,
    // so a shorter list in /etc/home-backend/config.json would otherwise keep the tail of the defaults.
    public HomeBackendOptions WithDefaults()
    {
        if (Urls.Length == 0) Urls = ["http://127.0.0.1:5000"];
        if (AllowedNetworks.Length == 0) AllowedNetworks = ["192.168.178.0/24", "10.8.0.0/24", "127.0.0.0/8"];
        return this;
    }
}
