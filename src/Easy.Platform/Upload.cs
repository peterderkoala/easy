using Microsoft.Extensions.Configuration;

namespace Easy.Platform;

// S06: one upload limit (Upload__MaxBytes), enforced at the gateway route, MainApi's Kestrel and the upload endpoint.
public static class Upload
{
    public static long MaxBytes(IConfiguration config) => config.GetValue<long?>("Upload:MaxBytes") ?? 15 * 1024 * 1024;
}
