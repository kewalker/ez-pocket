using System.Net.Http.Headers;
using System.Security.Cryptography;
using EzPocket.Models;
using EzPocket.Services;
using Xunit;

namespace EzPocket.Tests;

public sealed class FirmwareUpdateServiceTests
{
    [Fact]
    public async Task PrepareDownloadsOfficialFirmwareAndVerifiesPublishedChecksum()
    {
        string root = CreateTempFolder();
        try
        {
            byte[] firmware = [1, 2, 3, 4];
            string md5 = Convert.ToHexString(MD5.HashData(firmware)).ToLowerInvariant();
            var service = new FirmwareUpdateService(new FirmwareHandler(firmware, md5), Path.Combine(root, "staging"), Path.Combine(root, "backups"));
            string pocketPath = Path.Combine(root, "Pocket");
            Directory.CreateDirectory(pocketPath);
            var pocket = new PocketDrive(pocketPath, "Pocket", DriveType.Unknown, 0, 0, 0, [], 0, []);

            FirmwareUpdatePreview preview = await service.PrepareAsync(pocket);

            Assert.Equal("2.7", preview.Release.Version);
            Assert.Equal(md5, preview.Release.Md5);
            Assert.Equal("pocket_firmware_2.7.bin", preview.FileName);
            Assert.Equal(firmware, await File.ReadAllBytesAsync(preview.FirmwareFilePath));
            service.Cleanup(preview);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ez-pocket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FirmwareHandler(byte[] firmware, string md5) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/firmware", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("<h3>Firmware v2.7</h3>") });
            if (path.EndsWith("/firmware/2.7", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent($"<p>MD5 {md5}</p>") });

            var content = new ByteArrayContent(firmware);
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "pocket_firmware_2.7.bin" };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }
}
