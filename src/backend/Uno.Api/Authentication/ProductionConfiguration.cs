using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace Uno.Api.Authentication;

public static class ProductionConfiguration
{
    public static void Validate(IConfiguration config)
    {
        string Required(string name) => !string.IsNullOrWhiteSpace(config[name])
            ? config[name]! : throw new InvalidOperationException(name + " is required in production.");
        static bool SecureOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && !uri.IsLoopback && uri.UserInfo.Length == 0
            && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;
        Required("ConnectionStrings:DefaultConnection");
        if (System.Text.Encoding.UTF8.GetByteCount(Required("Jwt:SigningKey")) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");
        var checkpoint = Required("Security:CheckpointKey");
        if (!Convert.TryFromBase64String(checkpoint, new byte[32], out var length) || length != 32)
            throw new InvalidOperationException("Security:CheckpointKey must encode 32 bytes.");
        Required("Email:BrevoApiKey");
        if (!new EmailAddressAttribute().IsValid(Required("Email:Sender")))
            throw new InvalidOperationException("Email:Sender must be an email address.");
        var frontend = Required("Email:FrontendUrl");
        var origins = config.GetSection("Cors:AllowedOrigins").GetChildren().Select(x => x.Value ?? "").ToArray();
        if (!SecureOrigin(frontend) || origins.Length == 0 || origins.Any(x => !SecureOrigin(x) || x.EndsWith('/')) ||
            !origins.Contains(frontend.TrimEnd('/'), StringComparer.Ordinal))
            throw new InvalidOperationException("Email:FrontendUrl and Cors:AllowedOrigins must specify matching HTTPS origins.");
    }
}
