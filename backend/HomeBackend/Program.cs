using HomeBackend.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddHomeBackend();             // Hosting/HomeBackendServices.cs

var app = builder.Build();
app.PrepareSetup();                   // no password yet: the setup code for the first-run setup
app.UseHomeBackend();                 // Hosting/HomeBackendPipeline.cs
app.Run();
