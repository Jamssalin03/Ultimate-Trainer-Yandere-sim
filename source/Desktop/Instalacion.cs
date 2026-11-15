// Instalación y desinstalación del complemento. Sin dependencias de WPF: las preguntas al usuario
// se resuelven antes de llamar (OpcionesInstalacion) y cada caso que requiere confirmación se
// informa con RequiereConfirmacionException. Así se puede probar fuera de Windows.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UltimateTrainer;

internal sealed class RequiereConfirmacionException(string pregunta, string motivo) : Exception(pregunta)
{
    /// <summary>"bepinex" o "build".</summary>
    public string Motivo { get; } = motivo;
}

internal sealed class OpcionesInstalacion
{
    /// <summary>Usar un BepInEx 5 ya instalado (con otros mods) y agregar sólo el complemento.</summary>
    public bool UsarBepInExExistente { get; set; }
    /// <summary>Instalar aunque la huella del juego no figure en compatibilidad.json.</summary>
    public bool PermitirNoVerificada { get; set; }
    /// <summary>Respaldar PlayerPrefs del Registro (sólo Windows).</summary>
    public bool RespaldarRegistro { get; set; } = OperatingSystem.IsWindows();
}

internal sealed class EstadoJuego
{
    public required string Ruta { get; init; }
    public required string Hash { get; init; }
    public BuildConocida? Build { get; init; }
    public bool Instalado { get; init; }
    public string? VersionInstalada { get; init; }
    public bool BepInExPresente { get; init; }
    public string? VersionBepInEx { get; init; }
    public IReadOnlyList<string> OtrosMods { get; init; } = [];
}

internal static class Instalacion
{
    public const string ArchivoComplemento = "YandereUltimate.Bridge.dll";
    public static readonly string RutaComplemento = Path.Combine("BepInEx", "plugins", "YandereUltimate", ArchivoComplemento);
    static string Manifiesto(string juego) => Path.Combine(juego, "YandereUltimate.install.json");
    static string Ensamblado(string juego) => Path.Combine(juego, "YandereSimulator_Data", "Managed", "Assembly-CSharp.dll");

    public static string Hash(string ruta) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ruta)));

    /// <summary>Carpetas habituales del launcher, la configuración guardada y la variable YANDERE_SIM_DIR.</summary>
    public static IEnumerable<string> Candidatas(string? guardada = null)
    {
        if (!string.IsNullOrWhiteSpace(guardada)) yield return guardada;
        var variable = Environment.GetEnvironmentVariable("YANDERE_SIM_DIR");
        if (!string.IsNullOrWhiteSpace(variable)) yield return variable;
        var usuario = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var baseDir in new[] { "Downloads", "Descargas", "Desktop", "Escritorio", "Documents", "Documentos", "" })
        {
            var b = Path.Combine(usuario, baseDir);
            yield return Path.Combine(b, "YandereSimLauncher", "YandereSimulator");
            yield return Path.Combine(b, "YandereSimulator");
        }
        foreach (var unidad in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => d.RootDirectory.FullName))
        {
            yield return Path.Combine(unidad, "YandereSimLauncher", "YandereSimulator");
            yield return Path.Combine(unidad, "YandereSimulator");
            yield return Path.Combine(unidad, "Games", "YandereSimulator");
        }
    }

    /// <summary>Primera carpeta válida del juego, o null.</summary>
    public static string? Detectar(string? guardada = null)
    {
        foreach (var c in Candidatas(guardada))
            try { return ValidarRuta(c); } catch (Exception) { }
        return null;
    }

    /// <summary>Normaliza y comprueba que la ruta sea una instalación del juego. Lanza ArgumentException con el motivo.</summary>
    public static string ValidarRuta(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) throw new ArgumentException("Indicá la carpeta del juego.");
        ruta = ruta.Trim().Trim('"');
        if (ruta.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new ArgumentException("La ruta contiene caracteres no válidos.");
        if (!Path.IsPathFullyQualified(ruta)) throw new ArgumentException("La ruta del juego debe ser absoluta.");
        // Si eligieron el ejecutable, se usa su carpeta.
        if (File.Exists(ruta) && Path.GetFileName(ruta).Equals("YandereSimulator.exe", StringComparison.OrdinalIgnoreCase)) ruta = Path.GetDirectoryName(ruta)!;
        ruta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ruta));
        if (!Directory.Exists(ruta)) throw new ArgumentException("La carpeta no existe: " + ruta);
        if (Path.GetPathRoot(ruta) is { } raiz && Path.TrimEndingDirectorySeparator(raiz).Equals(ruta, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("No se puede usar la raíz de una unidad como carpeta del juego.");
        if (!File.Exists(Path.Combine(ruta, "YandereSimulator.exe"))) throw new ArgumentException("No se encontró YandereSimulator.exe en " + ruta);
        if (!File.Exists(Ensamblado(ruta))) throw new ArgumentException("No se encontró YandereSimulator_Data\\Managed\\Assembly-CSharp.dll: la instalación está incompleta o no es una build Mono.");
        return ruta;
    }

    public static EstadoJuego Examinar(string ruta)
    {
        ruta = ValidarRuta(ruta);
        var hash = Hash(Ensamblado(ruta));
        string? instalada = null;
        if (File.Exists(Manifiesto(ruta)))
            try { instalada = (string?)JsonNode.Parse(File.ReadAllText(Manifiesto(ruta)))?["version"] ?? "1.0.0"; } catch (Exception) { instalada = "desconocida"; }
        var nucleo = Path.Combine(ruta, "BepInEx", "core", "BepInEx.dll");
        bool presente = File.Exists(nucleo) || File.Exists(Path.Combine(ruta, "winhttp.dll")) || File.Exists(Path.Combine(ruta, "doorstop_config.ini"));
        string? version = null;
        if (File.Exists(nucleo)) version = FileVersionInfo.GetVersionInfo(nucleo).FileVersion;
        var plugins = Path.Combine(ruta, "BepInEx", "plugins");
        var propio = Path.GetFullPath(Path.Combine(ruta, RutaComplemento));
        var otros = Directory.Exists(plugins)
            ? Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories).Where(f => !Path.GetFullPath(f).Equals(propio, StringComparison.OrdinalIgnoreCase)).Select(f => Path.GetRelativePath(plugins, f)).ToList()
            : [];
        return new EstadoJuego { Ruta = ruta, Hash = hash, Build = Compatibilidad.Buscar(hash), Instalado = instalada != null, VersionInstalada = instalada, BepInExPresente = presente, VersionBepInEx = version, OtrosMods = otros };
    }

    static void ExigirJuegoCerrado()
    {
        if (Process.GetProcessesByName("YandereSimulator").Length > 0) throw new InvalidOperationException("Cerrá el juego antes de instalar o desinstalar el complemento.");
    }

    /// <param name="origen">Carpeta de la distribución (contiene Loader\ y YandereUltimate.Bridge.dll).</param>
    /// <returns>Carpeta del respaldo creado.</returns>
    public static string Instalar(string juego, string origen, OpcionesInstalacion op, string carpetaDatos)
    {
        ExigirJuegoCerrado();
        var estado = Examinar(juego);
        juego = estado.Ruta;
        if (estado.Instalado) throw new InvalidOperationException($"El complemento ya está instalado (versión {estado.VersionInstalada}). Usá Actualizar o desinstalalo primero.");
        if (estado.Build == null && !op.PermitirNoVerificada)
            throw new RequiereConfirmacionException("La build instalada no está verificada (su huella no figura en compatibilidad.json). El complemento activará sólo las funciones cuyas referencias existan. ¿Instalar igualmente?", "build");

        var loader = Path.Combine(origen, "Loader");
        var complemento = Path.Combine(origen, ArchivoComplemento);
        if (!File.Exists(complemento)) throw new FileNotFoundException("Falta " + ArchivoComplemento + " en la distribución.");
        var archivos = new List<(string Origen, string Relativa)>();

        if (estado.BepInExPresente)
        {
            if (estado.VersionBepInEx == null || !estado.VersionBepInEx.StartsWith("5.", StringComparison.Ordinal))
                throw new InvalidOperationException($"Hay un cargador instalado que no es BepInEx 5 ({estado.VersionBepInEx ?? "versión desconocida"}). No se modificará: retiralo manualmente o usá otra copia del juego.");
            if (!op.UsarBepInExExistente)
            {
                var mods = estado.OtrosMods.Count == 0 ? "sin otros mods" : $"con {estado.OtrosMods.Count} mod(s): {string.Join(", ", estado.OtrosMods.Take(5))}{(estado.OtrosMods.Count > 5 ? "…" : "")}";
                throw new RequiereConfirmacionException($"Ya hay BepInEx {estado.VersionBepInEx} instalado {mods}. No se sobrescribirá nada: ¿agregar sólo el complemento a ese BepInEx?", "bepinex");
            }
        }
        else
        {
            if (!Directory.Exists(loader)) throw new FileNotFoundException("Falta la carpeta Loader en la distribución.");
            archivos.AddRange(Directory.GetFiles(loader, "*", SearchOption.AllDirectories).Select(f => (f, Path.GetRelativePath(loader, f))));
        }
        archivos.Add((complemento, RutaComplemento));
        foreach (var f in archivos)
            if (File.Exists(Path.Combine(juego, f.Relativa))) throw new IOException("Existe un archivo que no se sobrescribirá: " + f.Relativa);

        var respaldo = Respaldar(juego, carpetaDatos, op.RespaldarRegistro);

        var instalados = new Dictionary<string, string>();
        var creadas = new List<string>();
        try
        {
            foreach (var f in archivos)
            {
                var destino = Path.Combine(juego, f.Relativa);
                CrearCarpetas(juego, Path.GetDirectoryName(destino)!, creadas);
                File.Copy(f.Origen, destino, false);
                instalados[f.Relativa] = Hash(destino);
            }
            EscribirManifiesto(juego, respaldo, instalados, creadas, estado.BepInExPresente);
        }
        catch
        {
            // Reversión: sólo se borra lo que se acaba de copiar.
            foreach (var f in instalados.Keys) try { File.Delete(Path.Combine(juego, f)); } catch (Exception) { }
            BorrarCarpetasVacias(juego, creadas);
            throw;
        }
        return respaldo;
    }

    /// <summary>Reemplaza sólo el complemento de una instalación propia (p. ej., de 1.0.0 a 1.1.0).</summary>
    public static void Actualizar(string juego, string origen)
    {
        ExigirJuegoCerrado();
        juego = ValidarRuta(juego);
        if (!File.Exists(Manifiesto(juego))) throw new InvalidOperationException("El complemento no está instalado en esta carpeta.");
        var manifiesto = JsonNode.Parse(File.ReadAllText(Manifiesto(juego)))!.AsObject();
        var archivos = manifiesto["files"]!.AsObject();
        var clave = archivos.Select(a => a.Key).FirstOrDefault(k => Path.GetFileName(k).Equals(ArchivoComplemento, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("El manifiesto no registra el complemento.");
        var destino = RutaSegura(juego, clave);
        if (File.Exists(destino) && Hash(destino) != (string?)archivos[clave]) throw new IOException("El complemento instalado fue modificado; revisalo antes de actualizar.");
        var nuevo = Path.Combine(origen, ArchivoComplemento);
        var temporal = destino + ".nuevo";
        File.Copy(nuevo, temporal, true);
        File.Move(temporal, destino, true);
        archivos[clave] = Hash(destino);
        manifiesto["version"] = Compatibilidad.VersionApp;
        File.WriteAllText(Manifiesto(juego), manifiesto.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Desinstalar(string juego)
    {
        ExigirJuegoCerrado();
        juego = ValidarRuta(juego);
        if (!File.Exists(Manifiesto(juego))) throw new InvalidOperationException("No hay una instalación del complemento registrada en esta carpeta.");
        var manifiesto = JsonNode.Parse(File.ReadAllText(Manifiesto(juego)))!;
        var archivos = manifiesto["files"]!.AsObject();
        foreach (var entrada in archivos)
        {
            var destino = RutaSegura(juego, entrada.Key);
            if (File.Exists(destino) && Hash(destino) != (string?)entrada.Value)
                throw new IOException("Se conserva un archivo modificado; revisalo antes de desinstalar: " + entrada.Key);
        }
        foreach (var entrada in archivos) { var destino = RutaSegura(juego, entrada.Key); if (File.Exists(destino)) File.Delete(destino); }
        // Sólo carpetas que creó la instalación y que quedaron vacías. Registros y configuración generados por
        // BepInEx, y cualquier otro mod, se conservan.
        var creadas = manifiesto["directorios"]?.AsArray().Select(n => (string)n!).ToList() ?? [];
        BorrarCarpetasVacias(juego, creadas);
        var respaldo = (string?)manifiesto["backup"];
        if (respaldo != null && Directory.Exists(respaldo)) File.Copy(Manifiesto(juego), Path.Combine(respaldo, "desinstalado.json"), true);
        File.Delete(Manifiesto(juego));
    }

    /// <summary>Copia las partidas (SaveData, SaveFiles), PlayerPrefs y el ensamblado original. Devuelve la carpeta creada.</summary>
    public static string Respaldar(string juego, string carpetaDatos, bool registro)
    {
        juego = ValidarRuta(juego);
        var destino = Path.Combine(carpetaDatos, "Respaldos", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(destino);
        File.Copy(Ensamblado(juego), Path.Combine(destino, "Assembly-CSharp.dll"));
        if (File.Exists(Path.Combine(juego, "GameVersion.txt"))) File.Copy(Path.Combine(juego, "GameVersion.txt"), Path.Combine(destino, "GameVersion.txt"));
        foreach (var carpeta in new[] { "SaveData", "SaveFiles" })
        {
            var partidas = Path.Combine(juego, "YandereSimulator_Data", "StreamingAssets", carpeta);
            if (Directory.Exists(partidas)) CopiarCarpeta(partidas, Path.Combine(destino, carpeta));
        }
        if (registro && OperatingSystem.IsWindows()) RespaldarPlayerPrefs(Path.Combine(destino, "PlayerPrefs.reg"));
        return destino;
    }

    static void RespaldarPlayerPrefs(string archivo)
    {
        const string clave = @"HKCU\Software\YandereDev\YandereSimulator";
        if (!Ejecutar("reg.exe", "query", clave)) return; // primera vez: todavía no hay preferencias
        if (!Ejecutar("reg.exe", "export", clave, archivo, "/y")) throw new IOException("No se pudo respaldar PlayerPrefs. La operación fue cancelada.");
    }

    static bool Ejecutar(string programa, params string[] argumentos)
    {
        var inicio = new ProcessStartInfo(programa) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in argumentos) inicio.ArgumentList.Add(a);
        using var p = Process.Start(inicio)!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    static void CopiarCarpeta(string origen, string destino)
    {
        foreach (var dir in Directory.GetDirectories(origen, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destino, Path.GetRelativePath(origen, dir)));
        Directory.CreateDirectory(destino);
        foreach (var f in Directory.GetFiles(origen, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(destino, Path.GetRelativePath(origen, f)), true);
    }

    static string RutaSegura(string juego, string relativa)
    {
        var destino = Path.GetFullPath(Path.Combine(juego, relativa));
        if (!destino.StartsWith(juego + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Ruta de manifiesto inválida: " + relativa);
        return destino;
    }

    static void CrearCarpetas(string juego, string carpeta, List<string> creadas)
    {
        var pendientes = new Stack<string>();
        for (var d = carpeta; !Directory.Exists(d) && d.Length > juego.Length; d = Path.GetDirectoryName(d)!) pendientes.Push(d);
        foreach (var d in pendientes) { Directory.CreateDirectory(d); creadas.Add(Path.GetRelativePath(juego, d)); }
    }

    static void BorrarCarpetasVacias(string juego, IEnumerable<string> relativas)
    {
        foreach (var r in relativas.OrderByDescending(r => r.Length))
        {
            try
            {
                var d = RutaSegura(juego, r);
                if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
            }
            catch (Exception) { /* se conserva: no es crítico */ }
        }
    }

    static void EscribirManifiesto(string juego, string respaldo, Dictionary<string, string> archivos, List<string> creadas, bool bepinexExistente)
    {
        var datos = new { version = Compatibilidad.VersionApp, fecha = DateTime.Now.ToString("s"), backup = respaldo, bepinexExistente, files = archivos, directorios = creadas };
        File.WriteAllText(Manifiesto(juego), JsonSerializer.Serialize(datos, new JsonSerializerOptions { WriteIndented = true }));
    }
}
