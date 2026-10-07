using HomeBackend.Cli;
using HomeBackend.Hosting;

// `home-backend set-password <config.json>` and friends run instead of the web server
if (CliCommands.TryRun(args, out var exitCode))
    return exitCode;

var builder = WebApplication.CreateBuilder(args);
builder.AddHomeBackend();             // Hosting/HomeBackendServices.cs

var app = builder.Build();
if (!app.HasPasswordOrMockData())
{
    await app.DisposeAsync();      // flushes the console log, so the reason reaches the journal
    return 1;
}

app.UseHomeBackend();                 // Hosting/HomeBackendPipeline.cs
app.Run();
return 0;
