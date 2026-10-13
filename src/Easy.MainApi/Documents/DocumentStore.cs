using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Easy.MainApi.Participants;

namespace Easy.MainApi.Documents;

// S06, ADR 0002: participant documents in SeaweedFS (S3 API), each object encrypted with SSE-C under the
// participant's data key. Only MainApi talks to SeaweedFS.
public sealed class DocumentStore : IParticipantObjects
{
    readonly AmazonS3Client s3;
    readonly string bucket;
    bool bucketExists;

    public DocumentStore(IConfiguration config)
    {
        // SeaweedFS admin identity from its AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY (compose: SEAWEEDFS_* in .env).
        s3 = new(new BasicAWSCredentials(Required(config, "Seaweed:AccessKey"), Required(config, "Seaweed:SecretKey")), new AmazonS3Config
        {
            ServiceURL = Required(config, "Seaweed:Endpoint"),
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED, // SeaweedFS has no trailing-checksum support
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
        bucket = config["Seaweed:Bucket"] ?? "easy-documents";
    }

    static string Required(IConfiguration config, string key) => config[key] ?? throw new InvalidOperationException($"{key} is not configured.");

    // Object key = participant prefix + generated ID; the prefix lets forget delete everything of one participant.
    public static string Key(Guid participantId, Guid documentId) => $"participants/{participantId}/{documentId}";

    public async Task Put(string key, byte[] content, DataKey dataKey, CancellationToken ct)
    {
        await EnsureBucket(ct);
        await s3.PutObjectAsync(new()
        {
            BucketName = bucket,
            Key = key,
            InputStream = new MemoryStream(content),
            ContentType = "application/pdf",
            ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
            ServerSideEncryptionCustomerProvidedKey = dataKey.SseCustomerKey,
        }, ct);
    }

    public async Task<byte[]> Get(string key, DataKey dataKey, CancellationToken ct)
    {
        await using var stream = await OpenRead(key, dataKey, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    // The decrypted object as it arrives; the caller disposes it. The SSE-C key goes with this request only, so the
    // data key may be disposed once this returns.
    public async Task<Stream> OpenRead(string key, DataKey dataKey, CancellationToken ct) =>
        (await s3.GetObjectAsync(new()
        {
            BucketName = bucket,
            Key = key,
            ServerSideEncryptionCustomerMethod = ServerSideEncryptionCustomerMethod.AES256,
            ServerSideEncryptionCustomerProvidedKey = dataKey.SseCustomerKey,
        }, ct)).ResponseStream;

    // Forget (S02): removes every object under the participant's prefix.
    public async Task DeleteAll(Guid participantId, CancellationToken ct)
    {
        await EnsureBucket(ct);
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = $"participants/{participantId}/" };
        ListObjectsV2Response page;
        do
        {
            page = await s3.ListObjectsV2Async(request, ct);
            foreach (var o in page.S3Objects ?? [])
                await s3.DeleteObjectAsync(bucket, o.Key, ct);
            request.ContinuationToken = page.NextContinuationToken;
        } while (page.IsTruncated == true);
    }

    async Task EnsureBucket(CancellationToken ct)
    {
        if (bucketExists) return;
        try
        {
            await s3.PutBucketAsync(bucket, ct);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists") { }
        bucketExists = true;
    }
}
