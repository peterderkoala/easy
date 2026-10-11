using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Easy.Data;
using Easy.MainApi.Participants;

namespace Easy.MainApi.Tests;

// Spec 4 (issue 19), ADR 0001.
public class ParticipantKeysTests
{
    static FileMasterSecret Master(int version = 1) => new(RandomNumberGenerator.GetBytes(32), version);

    static Participant NewParticipant(ParticipantKeys keys, Guid cycleId, string csvId, string name)
    {
        using var key = keys.Create();
        return new()
        {
            CycleId = cycleId,
            DisplayNo = "WS2026-001",
            WrappedKey = key.Wrapped,
            KeyVersion = key.Version,
            CsvId = key.Encrypt(csvId),
            CsvIdHash = keys.BlindIndex(cycleId, csvId),
            Name = key.Encrypt(name),
            ProjectTitle = key.Encrypt("Projekt"),
        };
    }

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        var keys = new ParticipantKeys(Master());
        var p = NewParticipant(keys, Guid.NewGuid(), "4711", "Erika Müßig");

        using var key = keys.Open(p);

        Assert.Equal("Erika Müßig", key.Decrypt(p.Name));
        Assert.Equal("4711", key.Decrypt(p.CsvId));
        using var other = keys.Create();
        Assert.NotEqual(other.Encrypt("Erika Müßig"), p.Name); // random nonce and key per value
    }

    [Fact]
    public void Wrong_master_secret_cannot_unwrap()
    {
        var p = NewParticipant(new ParticipantKeys(Master()), Guid.NewGuid(), "4711", "Erika");

        Assert.ThrowsAny<CryptographicException>(() => new ParticipantKeys(Master()).Open(p));
    }

    [Fact]
    public void Blind_index_is_stable_per_cycle_and_differs_across_cycles()
    {
        var keys = new ParticipantKeys(Master());
        Guid cycle = Guid.NewGuid(), other = Guid.NewGuid();

        Assert.Equal(keys.BlindIndex(cycle, "4711"), keys.BlindIndex(cycle, "4711"));
        Assert.NotEqual(keys.BlindIndex(cycle, "4711"), keys.BlindIndex(other, "4711"));
        Assert.NotEqual(keys.BlindIndex(cycle, "4711"), keys.BlindIndex(cycle, "4712"));
        Assert.Equal(keys.BlindIndex(cycle, "4711"), keys.BlindIndex(cycle, " 4711 "));
        Assert.Equal(32, keys.BlindIndex(cycle, "4711").Length);
    }

    [Fact]
    public async Task Forget_makes_fields_unreadable_and_nulls_blind_index()
    {
        var keys = new ParticipantKeys(Master());
        var p = NewParticipant(keys, Guid.NewGuid(), "4711", "Erika");
        var objects = new FakeObjects();

        await keys.Forget(p, objects, CancellationToken.None);

        Assert.Null(p.WrappedKey);
        Assert.Null(p.CsvIdHash);
        Assert.True(p.KeyForgotten);
        Assert.Equal([p.Id], objects.Deleted);
        Assert.Throws<InvalidOperationException>(() => keys.Open(p));
    }

    [Fact]
    public void Wrapped_key_stores_master_secret_version()
    {
        var master = Master(version: 3);
        var p = NewParticipant(new ParticipantKeys(master), Guid.NewGuid(), "4711", "Erika");

        Assert.Equal(3, p.KeyVersion);
        p.KeyVersion = 2; // a key wrapped under another version is not opened with this one
        Assert.Throws<InvalidOperationException>(() => new ParticipantKeys(master).Open(p));
    }

    [Fact]
    public void Master_secret_file_is_base64_with_trailing_newline()
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "\n");

        try
        {
            Assert.Equal(1, FileMasterSecret.Read(path, version: 1).Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Other_hosts_have_no_access_to_master_secret()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Easy.slnx"))) root = Path.GetDirectoryName(root)!;

        foreach (string file in new[] { "compose.dev.yml", "compose.example.yml" })
        {
            string yaml = File.ReadAllText(Path.Combine(root, file));
            string services = yaml[(yaml.IndexOf("\nservices:\n") + 1)..];
            services = Regex.Split(services, @"\n(?=\S)")[0]; // up to the next top-level key

            var withSecret = Regex.Split(services, @"\n(?=  [a-z-]+:\n)").Skip(1)
                .Where(block => block.Contains("master_secret") || block.Contains("MasterSecretFile"))
                .Select(block => block.Trim().Split(':')[0]);

            Assert.Equal(["main-api"], withSecret);
        }
    }

    sealed class FakeObjects : IParticipantObjects
    {
        public List<Guid> Deleted { get; } = [];

        public Task DeleteAll(Guid participantId, CancellationToken ct)
        {
            Deleted.Add(participantId);
            return Task.CompletedTask;
        }
    }
}
