using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DMT.Setup.Models;

namespace DMT.Setup.Services;

public sealed class ListenerDetailService
{
    public ListenerDetails Inspect(ListeningEndpoint endpoint)
    {
        var description = "";
        var product = "";
        var company = "";
        var publisher = "";
        bool? signed = null;

        if (!string.IsNullOrWhiteSpace(endpoint.ProcessPath) && File.Exists(endpoint.ProcessPath))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(endpoint.ProcessPath);
                description = info.FileDescription ?? "";
                product = info.ProductName ?? "";
                company = info.CompanyName ?? "";
            }
            catch { }

            try
            {
#pragma warning disable SYSLIB0057 // Authenticode extraction from signed PE files has no direct X509CertificateLoader equivalent.
                using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(endpoint.ProcessPath));
#pragma warning restore SYSLIB0057
                publisher = cert.GetNameInfo(X509NameType.SimpleName, false);
                if (string.IsNullOrWhiteSpace(publisher)) publisher = cert.Subject;
                signed = true;
            }
            catch (CryptographicException)
            {
                signed = false;
            }
            catch
            {
                signed = null;
            }
        }

        var (titleKey, bodyKey) = ResolveKnowledge(endpoint);
        return new ListenerDetails
        {
            Endpoint = endpoint,
            FileDescription = description,
            ProductName = product,
            CompanyName = company,
            Publisher = publisher,
            IsSigned = signed,
            KnowledgeTitleKey = titleKey,
            KnowledgeBodyKey = bodyKey
        };
    }

    private static (string TitleKey, string BodyKey) ResolveKnowledge(ListeningEndpoint endpoint)
    {
        var services = endpoint.Services.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (endpoint.Port == 135 || services.Any(x => x.Equals("RpcEptMapper", StringComparison.OrdinalIgnoreCase) || x.Equals("RpcSs", StringComparison.OrdinalIgnoreCase)))
            return ("setup.network.knowledge.rpc.title", "setup.network.knowledge.rpc.body");

        if (endpoint.Port == 445 || services.Any(x => x.Equals("LanmanServer", StringComparison.OrdinalIgnoreCase)))
            return ("setup.network.knowledge.smb.title", "setup.network.knowledge.smb.body");

        if (services.Any(x => x.Equals("LMS", StringComparison.OrdinalIgnoreCase)))
            return ("setup.network.knowledge.lms.title", "setup.network.knowledge.lms.body");

        if (services.Any(x => x.Equals("TermService", StringComparison.OrdinalIgnoreCase)) || endpoint.Port == 3389)
            return ("setup.network.knowledge.rdp.title", "setup.network.knowledge.rdp.body");

        if (services.Any(x => x.Equals("WinRM", StringComparison.OrdinalIgnoreCase)) || endpoint.Port is 5985 or 5986)
            return ("setup.network.knowledge.winrm.title", "setup.network.knowledge.winrm.body");

        if (endpoint.ProcessName.Equals("Dropbox", StringComparison.OrdinalIgnoreCase))
            return ("setup.network.knowledge.dropbox.title", "setup.network.knowledge.dropbox.body");

        return ("setup.network.knowledge.unknown.title", "setup.network.knowledge.unknown.body");
    }
}
