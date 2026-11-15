using System.Text.Json.Nodes;

namespace UltimateTrainer;

internal sealed record BuildConocida(string Build, string Fecha, string Unity, string Sha256);

/// <summary>Datos de compatibilidad.json, incrustado al compilar (misma fuente que usa el complemento).</summary>
internal static class Compatibilidad
{
    static readonly JsonObject Datos = Cargar();

    static JsonObject Cargar()
    {
        using var s = typeof(Compatibilidad).Assembly.GetManifestResourceStream("compatibilidad.json")
            ?? throw new InvalidOperationException("Falta compatibilidad.json en la compilación.");
        return JsonNode.Parse(s)!.AsObject();
    }

    public static IReadOnlyList<BuildConocida> Builds { get; } =
        Datos["builds_conocidas"]!.AsArray().Select(b => new BuildConocida((string)b!["build"]!, (string)b["fecha"]!, (string?)b["unity"] ?? "", (string)b["sha256_Assembly_CSharp"]!)).ToList();

    public static BuildConocida? Buscar(string hash) => Builds.FirstOrDefault(b => string.Equals(b.Sha256, hash, StringComparison.OrdinalIgnoreCase));

    /// <summary>Versión de la aplicación (InformationalVersion del ensamblado, sin metadatos de compilación).</summary>
    public static string VersionApp { get; } = typeof(Compatibilidad).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "1.1.0";

    public static string Repositorio => (string?)Datos["repositorio"] ?? "";
    /// <summary>Enlace de donaciones configurado por quien publica la versión (vacío = no se muestra).</summary>
    public static string Donaciones => (string?)Datos["donaciones"] ?? "";
    public static string BepInEx => (string?)Datos["bepinex"] ?? "";

    /// <summary>Sólo se abren enlaces https/http absolutos (nunca rutas o comandos locales).</summary>
    public static bool EsEnlaceSeguro(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrEmpty(u.Host);
}
