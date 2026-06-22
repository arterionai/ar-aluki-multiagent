using System.Net;
using Aluki.Runtime.Abstractions.Security;
using Aluki.Runtime.Abstractions.Skills.LinkCapture;

namespace Aluki.Runtime.Host.Security;

public sealed class LinkEnrichmentPolicyEvaluator : ILinkEnrichmentPolicyEvaluator
{
    public PolicyEvaluationResult Evaluate(string canonicalUrl)
    {
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var uri))
            return new PolicyEvaluationResult(LinkPolicyDecision.Block, "invalid_url");

        var host = uri.Host;

        // Loopback: localhost, 127.x.x.x, [::1]
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase) ||
            host == "::1")
            return new PolicyEvaluationResult(LinkPolicyDecision.Block, "loopback");

        // Link-local: 169.254.x.x
        if (host.StartsWith("169.254.", StringComparison.Ordinal))
            return new PolicyEvaluationResult(LinkPolicyDecision.Block, "link_local");

        // Try to parse as IP for range checks
        if (IPAddress.TryParse(host, out var ip))
        {
            // Loopback (covers 127.0.0.0/8 and ::1)
            if (IPAddress.IsLoopback(ip))
                return new PolicyEvaluationResult(LinkPolicyDecision.Block, "loopback");

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();

                // 0.0.0.0
                if (bytes[0] == 0)
                    return new PolicyEvaluationResult(LinkPolicyDecision.Block, "private_network");

                // 10.0.0.0/8
                if (bytes[0] == 10)
                    return new PolicyEvaluationResult(LinkPolicyDecision.Block, "private_network");

                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    return new PolicyEvaluationResult(LinkPolicyDecision.Block, "private_network");

                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                    return new PolicyEvaluationResult(LinkPolicyDecision.Block, "private_network");

                // 169.254.0.0/16 (link-local via parsed IP)
                if (bytes[0] == 169 && bytes[1] == 254)
                    return new PolicyEvaluationResult(LinkPolicyDecision.Block, "link_local");
            }
        }

        return new PolicyEvaluationResult(LinkPolicyDecision.Allow, "allowed");
    }
}
