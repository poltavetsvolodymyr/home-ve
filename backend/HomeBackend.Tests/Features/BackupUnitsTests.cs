using HomeBackend.Features.Backups;

namespace HomeBackend.Tests.Features;

public class BackupUnitsTests
{
    [Theory]
    [InlineData("router-20261007-034112.img.zst", "20261007-034112")]
    [InlineData("router-20261007-034112.img.zst.part", null)]
    [InlineData("router-20261007-034112.conf", null)]
    [InlineData("router-copy-20261007-034112.img.zst", null)]
    [InlineData("router-20261399-034112.img.zst", null)]
    [InlineData("router-2026100-0341120.img.zst", null)]
    public void Only_backup_images_of_that_vm_have_an_id(string file, string? id) =>
        Assert.Equal(id, BackupUnits.IdOf("router", file));

    [Theory]
    [InlineData("20261007-034112", true)]
    [InlineData("20261007-034112.service", false)]
    [InlineData("../20261007-034112", false)]
    [InlineData("20261007-256112", false)]
    public void Ids_are_a_valid_time_and_nothing_else(string id, bool valid) =>
        Assert.Equal(valid, BackupUnits.IsValidId(id));

    [Fact]
    public void Unit_names_match_the_polkit_rule() =>
        Assert.Equal("vm-restore@router:20261007-034112.service", BackupUnits.Restore("router", "20261007-034112"));
}
