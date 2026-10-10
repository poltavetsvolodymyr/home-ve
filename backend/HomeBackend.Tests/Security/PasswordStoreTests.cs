using HomeBackend.Configuration;
using HomeBackend.Features.Auth;
using HomeBackend.Security;
using Microsoft.Extensions.Options;

namespace HomeBackend.Tests.Security;

public sealed class PasswordStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "home-backend-tests-" + Guid.NewGuid().ToString("N"));

    private PasswordStore Store(string configHash = "") =>
        new(Options.Create(new HomeBackendOptions { DataDir = _dir, PasswordHash = configHash }));

    [Fact]
    public void The_setup_code_stays_the_same_until_the_setup_is_done()
    {
        var code = Store().EnsureSetupCode();

        Assert.Equal(code, Store().EnsureSetupCode()); // a restart: a new store, the same file
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_dir, "setup-code")));
    }

    [Fact]
    public void Setup_takes_the_code_typed_by_hand_and_then_is_done()
    {
        var store = Store();
        var code = store.EnsureSetupCode();

        Assert.False(store.TrySetUp("ABCD-EFGH", "secret-password"));
        Assert.True(store.NeedsSetup);
        Assert.True(store.TrySetUp(" " + code.Replace("-", "").ToLowerInvariant(), "secret-password"));

        Assert.False(store.NeedsSetup);
        Assert.False(File.Exists(Path.Combine(_dir, "setup-code")));
        Assert.True(store.Verify("secret-password"));
        Assert.True(Store().Verify("secret-password")); // after a restart, from the file
        Assert.False(Store().Verify("secret"));
        Assert.Throws<InvalidOperationException>(() => store.TrySetUp(code, "another-password"));
    }

    [Fact]
    public void Without_a_setup_code_file_no_code_is_right()
    {
        var store = Store();
        var code = store.EnsureSetupCode();
        File.Delete(Path.Combine(_dir, "setup-code"));

        Assert.False(store.TrySetUp(code, "secret-password"));
    }

    [Fact]
    public void The_config_hash_wins_so_set_password_resets_a_forgotten_one()
    {
        var store = Store();
        store.TrySetUp(store.EnsureSetupCode(), "forgotten-password");

        var reset = Store(PasswordHash.Create("new-password"));

        Assert.True(reset.Verify("new-password"));
        Assert.False(reset.Verify("forgotten-password"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }
}
