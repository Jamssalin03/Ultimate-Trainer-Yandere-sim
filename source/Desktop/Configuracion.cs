using System.Text.Json;
using System.Text.Json.Nodes;

namespace UltimateTrainer;

internal sealed class Perfil
{
    public Dictionary<string, string> Values { get; set; } = new();
    public List<string> Toggles { get; set; } = new();
    public List<string> Locks { get; set; } = new();
}

/// <summary>%LOCALAPPDATA%\YandereUltimate\config.json (compatible con el formato de 1.0.0).</summary>
internal sealed class Configuracion
{
    public string Instalacion { get; set; } = "";
    public string Tema { get; set; } = "oscuro";
    public Dictionary<string, string> Valores { get; set; } = new();
    public Dictionary<string, Perfil> Perfiles { get; set; } = new();

    static string Archivo => Path.Combine(Programa.Datos, "config.json");

    public static Configuracion Cargar()
    {
        var c = new Configuracion();
        try
        {
            if (!File.Exists(Archivo)) return c;
            var n = JsonNode.Parse(File.ReadAllText(Archivo))!;
            c.Instalacion = (string?)n["installation"] ?? "";
            c.Tema = (string?)n["theme"] == "claro" ? "claro" : "oscuro";
            c.Valores = n["values"]?.Deserialize<Dictionary<string, string>>() ?? new();
            c.Perfiles = n["profiles"]?.Deserialize<Dictionary<string, Perfil>>() ?? new();
        }
        catch (Exception e) { Programa.Registrar(e); }
        return c;
    }

    public void Guardar()
    {
        try
        {
            var temporal = Path.Combine(Programa.Datos, "config.tmp");
            File.WriteAllText(temporal, JsonSerializer.Serialize(new { installation = Instalacion, theme = Tema, values = Valores, profiles = Perfiles }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporal, Archivo, true);
        }
        catch (Exception e) { Programa.Registrar(e); }
    }
}
