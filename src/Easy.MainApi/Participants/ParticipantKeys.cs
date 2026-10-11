using System.Security.Cryptography;
using System.Text;
using Easy.Data;

namespace Easy.MainApi.Participants;

// ADR 0001: a random data key per participant, wrapped by a master secret that never enters the database.
// The seam for a KMS later: it wraps, unwraps and keys the blind index; nothing else sees the secret.
public interface IMasterSecret
{
    int Version { get; }
    byte[] Wrap(byte[] dataKey);
    byte[] Unwrap(byte[] wrapped);
    byte[] BlindIndex(byte[] data);
}

// Docker secret file (S16): base64 of at least 32 random bytes. The version is configuration, bumped on manual re-wrap.
public sealed class FileMasterSecret(byte[] secret, int version) : IMasterSecret
{
    readonly byte[] wrapKey = Derive(secret, "easy wrap");
    readonly byte[] indexKey = Derive(secret, "easy blind index");

    public int Version => version;

    public static FileMasterSecret Read(string path, int version)
    {
        byte[] secret = Convert.FromBase64String(File.ReadAllText(path).Trim());
        if (secret.Length < 32) throw new InvalidOperationException("Master secret must be at least 32 bytes.");
        return new(secret, version);
    }

    public byte[] Wrap(byte[] dataKey) => Aead.Seal(wrapKey, dataKey);
    public byte[] Unwrap(byte[] wrapped) => Aead.Open(wrapKey, wrapped);
    public byte[] BlindIndex(byte[] data) => HMACSHA256.HashData(indexKey, data);

    static byte[] Derive(byte[] secret, string purpose) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, info: Encoding.UTF8.GetBytes(purpose));
}

// Port to the object store (SeaweedFS), implemented in spec 7.
public interface IParticipantObjects
{
    Task DeleteAll(Guid participantId, CancellationToken ct);
}

public sealed class ParticipantKeys(IMasterSecret master)
{
    // New participant: use Wrapped and Version for the row, then encrypt its fields.
    public DataKey Create()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        return new(key, master.Wrap(key), master.Version);
    }

    // Per request; never cache the result.
    public DataKey Open(Participant p) =>
        p.WrappedKey is null ? throw new InvalidOperationException("Participant key was forgotten.") : Open(p.WrappedKey, p.KeyVersion);

    public DataKey Open(byte[] wrapped, int version)
    {
        // ponytail: one master-secret version at a time; re-wrap all keys before bumping it, or let IMasterSecret hold old versions when rotation is real
        if (version != master.Version) throw new InvalidOperationException($"Key wrapped under master-secret version {version}, current is {master.Version}.");
        return new(master.Unwrap(wrapped), wrapped, version);
    }

    // S02: HMAC over cycle and identification number, for the (cycle_id, csv_id_hash) unique index.
    public byte[] BlindIndex(Guid cycleId, string csvId) => master.BlindIndex(Encoding.UTF8.GetBytes($"{cycleId:N}:{csvId.Trim()}"));

    // S02 forget: the record stays for statistics, personal fields become unreadable. The caller saves the row.
    public async Task Forget(Participant p, IParticipantObjects objects, CancellationToken ct)
    {
        await objects.DeleteAll(p.Id, ct);
        p.WrappedKey = null;
        p.CsvIdHash = null;
        p.KeyForgotten = true;
    }
}

// Plain data key in memory. Not a record, so it never prints its bytes; zeroed on dispose.
public sealed class DataKey(byte[] key, byte[] wrapped, int version) : IDisposable
{
    public byte[] Wrapped => wrapped;
    public int Version => version;

    public byte[] Encrypt(string value) => Aead.Seal(key, Encoding.UTF8.GetBytes(value));
    public string Decrypt(byte[] value) => Encoding.UTF8.GetString(Aead.Open(key, value));
    public byte[] EncryptBytes(byte[] value) => Aead.Seal(key, value);
    public byte[] DecryptBytes(byte[] value) => Aead.Open(key, value);

    public void Dispose() => CryptographicOperations.ZeroMemory(key);
}

// AES-256-GCM, stored as nonce | tag | ciphertext.
// ponytail: no associated data, so ciphertexts can be swapped between columns or rows by someone with DB write access; bind participant id and field when the threat model grows beyond a read leak
static class Aead
{
    const int NonceSize = 12, TagSize = 16;

    public static byte[] Seal(byte[] key, byte[] plain)
    {
        var sealedBytes = new byte[NonceSize + TagSize + plain.Length];
        var nonce = sealedBytes.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize));
        return sealedBytes;
    }

    public static byte[] Open(byte[] key, byte[] sealedBytes)
    {
        if (sealedBytes.Length < NonceSize + TagSize) throw new CryptographicException("Ciphertext too short.");
        var plain = new byte[sealedBytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize + TagSize), sealedBytes.AsSpan(NonceSize, TagSize), plain);
        return plain;
    }
}
