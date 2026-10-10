using HomeBackend.Cli;
using HomeBackend.Hosting;

// `home-backend set-password <config.json>` and friends run instead of the web server
if (CliCommands.TryRun(args, out var exitCode))
    return exitCode;

var builder = WebApplication.CreateBuilder(args);
builder.AddHomeBackend();             // Hosting/HomeBackendServices.cs

var app = builder.Build();
app.PrepareSetup();                   // no password yet: the setup code for the first-run setup
app.UseHomeBackend();                 // Hosting/HomeBackendPipeline.cs
app.Run();
return 0;
