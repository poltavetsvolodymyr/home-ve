using System.Security.Cryptography;
using System.Text;
using HomeBackend.Configuration;
using HomeBackend.Security;
using Microsoft.Extensions.Options;

namespace HomeBackend.Features.Auth;

/// <summary>
/// The login password's hash, in <c>&lt;DataDir&gt;/password</c> (PBKDF2, see <see cref="PasswordHash"/>), written by the
/// first-run setup in the web UI.
///
/// Without it, the host waits for that setup: whoever sets the password first owns the host, so it takes the setup
/// code from <c>&lt;DataDir&gt;/setup-code</c>, which only root and this service can read (install.sh prints it).
/// A forgotten password: delete the file and restart the service, and the setup comes back.
/// With mock data and no file, the password is "admin", so the UI can be developed without any setup.
/// </summary>
public sealed class PasswordStore(IOptions<HomeBackendOptions> options)
{
    public const int MinLength = 8;
    public const int MaxLength = 1024;

    // no 0/O, 1/I/L: it is read off a screen and typed on a phone
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;

    private readonly HomeBackendOptions _options = options.Value;
    private readonly Lock _lock = new();
    private string? _savedHash;

    public string HashFile => Path.Combine(_options.DataDir, "password");
    public string SetupCodeFile => Path.Combine(_options.DataDir, "setup-code");

    private string? Hash
    {
        get
        {
            lock (_lock)
            {
                if (_savedHash is null && File.Exists(HashFile))
                {
                    var saved = File.ReadAllText(HashFile).Trim();
                    if (saved.Length > 0) _savedHash = saved;
                }
                return _savedHash;
            }
        }
    }

    /// <summary>No password yet: the web UI shows the setup instead of the login.</summary>
    public bool NeedsSetup => !_options.Mock && Hash is null;

    public bool Verify(string? password) =>
        Hash is { } hash ? PasswordHash.Verify(password ?? "", hash) : _options.Mock && password == "admin";

    /// <summary>The setup code, made once and kept until the setup is done (so a restart doesn't change it).</summary>
    public string EnsureSetupCode()
    {
        lock (_lock)
        {
            if (File.Exists(SetupCodeFile) && Normalize(File.ReadAllText(SetupCodeFile)) is { Length: CodeLength } kept)
                return Format(kept);

            var code = new string(RandomNumberGenerator.GetItems(CodeAlphabet.AsSpan(), CodeLength));
            WritePrivate(SetupCodeFile, Format(code) + "\n");
            return Format(code);
        }
    }

    /// <summary>Saves the first password, if the code is the one in the setup-code file. False: wrong code.</summary>
    /// <exception cref="InvalidOperationException">There is a password already.</exception>
    public bool TrySetUp(string? code, string password)
    {
        lock (_lock)
        {
            if (!NeedsSetup) throw new InvalidOperationException("the password is set already");
            if (!File.Exists(SetupCodeFile)) return false;

            var expected = Encoding.ASCII.GetBytes(Normalize(File.ReadAllText(SetupCodeFile)));
            var given = Encoding.ASCII.GetBytes(Normalize(code ?? ""));
            if (expected.Length != CodeLength || !CryptographicOperations.FixedTimeEquals(expected, given)) return false;

            var hash = PasswordHash.Create(password);
            WritePrivate(HashFile, hash + "\n");
            _savedHash = hash;
            File.Delete(SetupCodeFile);
            return true;
        }
    }

    // typed by hand: any case, with or without the dash and spaces
    private static string Normalize(string code) =>
        new(code.ToUpperInvariant().Where(c => c is not ('-' or ' ' or '\n' or '\r' or '\t')).ToArray());

    private static string Format(string code) => $"{code[..4]}-{code[4..]}";

    // a temp file readable by this service only, then renamed over the old one: never half written
    private static void WritePrivate(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        var fileOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.Delete(temp);
        using (var file = new FileStream(temp, fileOptions))
            file.Write(Encoding.UTF8.GetBytes(text));
        File.Move(temp, path, overwrite: true);
    }
}
