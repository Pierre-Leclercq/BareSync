using System.IO.Hashing;

namespace BareSync.Infra;

internal static class Crc64Service
{
    private const int BufferSize = 64 * 1024;

    public static async Task<string> ComputeCrc64HexAsync(string fullPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var crc64 = new Crc64();
        var buffer = new byte[BufferSize];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            crc64.Append(buffer.AsSpan(0, bytesRead));
        }

        var hash = crc64.GetHashAndReset();
        return Convert.ToHexString(hash);
    }
}
