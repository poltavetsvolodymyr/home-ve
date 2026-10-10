using HomeBackend.Hosting;
using HomeBackend.Installer;

// `home-backend installer`: the installer image's page instead of the VM host's web UI (Installer/InstallerHost.cs)
if (args is ["installer", .. var rest])
    return InstallerHost.Run(rest);

var builder = WebApplication.CreateBuilder(args);
builder.AddHomeBackend();             // Hosting/HomeBackendServices.cs

var app = builder.Build();
app.PrepareSetup();                   // no password yet: the setup code for the first-run setup
app.UseHomeBackend();                 // Hosting/HomeBackendPipeline.cs
app.Run();
return 0;
