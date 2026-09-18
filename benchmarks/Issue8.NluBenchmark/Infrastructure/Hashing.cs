using System.Security.Cryptography;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>Content hashes recorded in the manifest, the run artifacts and the final report.</summary>
public static class Hashing
{
    public static string Sha256File(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public static string Sha256Text(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
