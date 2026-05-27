using System.Net;
namespace PortalHelpdeskTI.Services.SAP;

// Exemplo de implementação
public class ServiceLayerCookieStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string b1, string? route)> _map
        = new();

    public void Set(string sessionKey, string b1Session, string routeId) =>
        _map[sessionKey] = (b1Session, routeId);

    public (string? b1, string? route) Get(string sessionKey) =>
        _map.TryGetValue(sessionKey, out var v) ? (v.b1, v.route) : (null, null);

    public void Clear(string sessionKey) =>
        _map.TryRemove(sessionKey, out _);

    // ⚠️ Use a BaseUri e a SessionKey para montar os cookies no domínio/path corretos
    public CookieContainer ToCookieContainer(Uri baseUri, string sessionKey)
    {
        var cc = new CookieContainer();
        if (!_map.TryGetValue(sessionKey, out var v) || string.IsNullOrWhiteSpace(v.b1))
            return cc;

        // Dominio SEM porta
        var domain = baseUri.Host; // NADA de :50000 aqui
        // Path do SL (normalmente /b1s/v1)
        var path = baseUri.AbsolutePath;
        if (string.IsNullOrEmpty(path) || path == "/") path = "/b1s/v1";

        cc.Add(new Cookie("B1SESSION", v.b1)
        {
            Domain = domain,
            Path = path,
            HttpOnly = true,
            Secure = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
        });

        if (!string.IsNullOrWhiteSpace(v.route))
        {
            // ROUTEID costuma aceitar Path "/"
            cc.Add(new Cookie("ROUTEID", v.route)
            {
                Domain = domain,
                Path = "/",
                Secure = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            });
        }

        return cc;
    }
}

