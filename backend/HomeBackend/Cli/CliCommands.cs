using HomeBackend.Security;

namespace HomeBackend.Cli;

/// <summary>
/// Commands that run instead of the web server (deploy/install.sh uses set-password):
/// <list type="bullet">
/// <item><c>home-backend hash-password</c> prints a PBKDF2 hash of a new password.</item>
/// <item><c>home-backend set-password /etc/home-backend/config.json</c> writes it into the config file.</item>
/// </list>
/// The password is prompted for, or read from stdin when that's redirected.
/// </summary>
public static class CliCommands
{
    /// <returns>false when <paramref name="args"/> isn't a command and the web server should start.</returns>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        switch (args)
        {
            case ["hash-password", ..]:
                Console.WriteLine(PasswordHash.Create(PasswordPrompt.ReadNew()));
                return true;

            case ["set-password", var configPath, ..]:
                ConfigFile.SetPasswordHash(configPath, PasswordHash.Create(PasswordPrompt.ReadNew()));
                Console.WriteLine($"password saved to {configPath}");
                return true;

            default:
                return false;
        }
    }
}
