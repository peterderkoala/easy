extern alias mainapi;

using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using mainapi::Easy.MainApi.Documents;
using mainapi::Easy.MainApi.Participants;
using Microsoft.Extensions.Configuration;

namespace Easy.IntegrationTests;

// Spec 7 spike (ADR 0002): does the pinned SeaweedFS release enforce SSE-C on read and store ciphertext?
[Collection(nameof(Infra))]
public class SseCSpikeTests(Infra infra)
{
    readonly ParticipantKeys keys = new(new FileMasterSecret(RandomNumberGenerator.GetBytes(32), 1));
    readonly byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nsecret participant documentation\n%%EOF");

    DocumentStore Store() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Seaweed:Endpoint"] = infra.SeaweedS3,
        ["Seaweed:AccessKey"] = "test-access",
        ["Seaweed:SecretKey"] = "test-secret",
    }).Build());

    [Fact]
    public async Task Read_with_right_key_round_trips()
    {
        var store = Store();
        string key = DocumentStore.Key(Guid.NewGuid(), Guid.NewGuid());
        using var dataKey = keys.Create();

        await store.Put(key, pdf, dataKey, default);

        Assert.Equal(pdf, await store.Get(key, dataKey, default));
    }

    [Fact]
    public async Task Read_with_wrong_key_fails()
    {
        var store = Store();
        string key = DocumentStore.Key(Guid.NewGuid(), Guid.NewGuid());
        using var dataKey = keys.Create();
        using var otherKey = keys.Create();
        await store.Put(key, pdf, dataKey, default);

        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.Get(key, otherKey, default));
    }

    [Fact]
    public async Task Read_without_key_fails()
    {
        var store = Store();
        string key = DocumentStore.Key(Guid.NewGuid(), Guid.NewGuid());
        using var dataKey = keys.Create();
        await store.Put(key, pdf, dataKey, default);

        // Signed with valid credentials, but without SSE-C headers.
        using var s3 = new AmazonS3Client(new Amazon.Runtime.BasicAWSCredentials("test-access", "test-secret"),
            new AmazonS3Config { ServiceURL = infra.SeaweedS3, ForcePathStyle = true });
        await Assert.ThrowsAsync<AmazonS3Exception>(() => s3.GetObjectAsync("easy-documents", key));
    }

    [Fact]
    public async Task Stored_bytes_are_not_plaintext()
    {
        var store = Store();
        string key = DocumentStore.Key(Guid.NewGuid(), Guid.NewGuid());
        using var dataKey = keys.Create();
        await store.Put(key, pdf, dataKey, default);

        // The filer API bypasses S3 and returns what is on disk.
        using var http = new HttpClient();
        byte[] stored = await http.GetByteArrayAsync($"{infra.SeaweedFiler}/buckets/easy-documents/{key}");
        Assert.True(stored.Length >= pdf.Length);
        Assert.DoesNotContain("secret participant", Encoding.ASCII.GetString(stored));
    }
}
