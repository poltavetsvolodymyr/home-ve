using HomeBackend.Security;

namespace HomeBackend.Tests.Security;

public class PasswordHashTests
{
    // made by the router's v0.1 binary (`routerui hash-password`): the same PBKDF2 format, so a hash copied from there works too
    private const string HashFromV01 = "pbkdf2-sha256$600000$09Iz2TFuP9qc7Ku0Jjui7g==$UPU0TP4sgcLCUWjyeIPWo5oi+rnrKAYSL8WH7Irw6IU=";

    [Fact]
    public void Verifies_a_hash_made_by_an_earlier_version()
    {
        Assert.True(PasswordHash.Verify("correct horse battery", HashFromV01));
        Assert.False(PasswordHash.Verify("correct horse batterY", HashFromV01));
    }

    [Fact]
    public void Created_hash_verifies_only_its_own_password()
    {
        var hash = PasswordHash.Create("s3cret-pass");

        Assert.StartsWith("pbkdf2-sha256$600000$", hash);
        Assert.True(PasswordHash.Verify("s3cret-pass", hash));
        Assert.False(PasswordHash.Verify("s3cret-pasS", hash));
    }

    [Fact]
    public void Same_password_gets_a_new_salt_each_time() =>
        Assert.NotEqual(PasswordHash.Create("s3cret-pass"), PasswordHash.Create("s3cret-pass"));

    [Theory]
    [InlineData("")]
    [InlineData("plain-text-password")]
    [InlineData("bcrypt$10$abc$def")]
    [InlineData("pbkdf2-sha256$many$abc$def")]
    [InlineData("pbkdf2-sha256$600000$abc")]
    public void Malformed_stored_value_never_verifies(string stored) =>
        Assert.False(PasswordHash.Verify("anything", stored));
}
