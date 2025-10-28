using CsvHelper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PassThrough;


public class RootCertificates
{
    public static async Task Install()
    {
        try
        {
            var store = new RootCertificates(rootCertDir);
            await store.InstallCerts();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    const string rootCertDir = "/cache/root-certs";
    const string rootCertPath = rootCertDir + "/all.pem";


    private int index = 0;
    private string rootDir;
    private List<FileInfo> files = new();

    public RootCertificates(string rootDir)
    {
        this.rootDir = rootDir;
    }

    async Task InstallCerts()
    {

        if (!Directory.Exists(rootDir)) {
            Directory.CreateDirectory(rootDir);
        }

        if (this.IsCertValid())
        {
            return;
        }

        var oldRootDir = this.rootDir;
        this.rootDir = Path.Join("/cache", "fc", DateTime.UtcNow.Ticks.ToString());
        Directory.CreateDirectory(this.rootDir);
        Console.WriteLine($"Downloading root certificates  to {this.rootDir}");

        // certificates are available from
        // https://wiki.mozilla.org/CA/Intermediate_Certificates

        // download root certs...
        await this.DownloadCerts("https://ccadb.my.salesforce-sites.com/mozilla/IncludedCACertificateReportPEMCSV");
        await this.DownloadCerts("https://ccadb.my.salesforce-sites.com/mozilla/PublicAllIntermediateCertsWithPEMCSV");

        var copyPath = "/usr/local/share/ca-certificates/";

        foreach (var f in files)
        {
            File.Copy(f.FullName, Path.Join(copyPath, f.Name), true);
        }

        var tmp = oldRootDir + DateTime.UtcNow.Ticks;
        Directory.Move(oldRootDir, tmp);
        Directory.Move(this.rootDir, oldRootDir);
        Directory.Delete(tmp, true);
        this.rootDir = rootCertDir;

        Console.WriteLine($"Running rehash");


        var ps = new ProcessStartInfo("update-ca-certificates");
        ps.WorkingDirectory = this.rootDir;

        await Process.Start(ps)!.WaitForExitAsync();

        Console.WriteLine($"Certificates installed.");
    }

    private async Task DownloadCerts(string url)
    {
        var client = new HttpClient();
        var certCSV = await client.GetStringAsync(url);

        var regex = new Regex("-----BEGIN CERTIFICATE-----((\n|\r)+)", RegexOptions.Multiline);

        using (var csv = new CsvReader(new StringReader(certCSV), CultureInfo.InvariantCulture))
        {
            csv.Read();
            csv.ReadHeader();
            while (csv.Read())
            {
                var pem = csv.GetField("PEM Info")!.Trim('"')
                    .Trim();

                pem = regex.Replace(pem, "-----BEGIN CERTIFICATE-----\n");
                var certName = $"c{this.index++:0000}.crt";
                var certFile = Path.Join(this.rootDir, certName);
                this.files.Add(new FileInfo(certFile));
                await File.WriteAllTextAsync(certFile, pem);
                await File.AppendAllTextAsync(Path.Join(this.rootDir, "all.pem"), pem);

            }
        }
    }

    bool IsCertValid()
    {
        if (File.Exists(rootCertPath))
        {
            var info = new FileInfo(rootCertPath);
            var maxAge = DateTime.UtcNow.AddDays(-7);
            if(info.CreationTimeUtc > maxAge)
            {
                return true;
            }
        }
        return false;
    }
}
