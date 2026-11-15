using System.Text.Json.Nodes;
using UltimateTrainer;
using Xunit;

namespace YandereUltimate.Pruebas;

/// <summary>Instalación sobre carpetas de juego falsas (no toca ninguna instalación real).</summary>
public class InstalacionPruebas
{
    readonly string raiz = Directory.CreateTempSubdirectory("ys-instalacion-").FullName;
    string Juego => Path.Combine(raiz, "YandereSimulator");
    string Datos => Path.Combine(raiz, "Datos");
    string Origen => Path.Combine(raiz, "Distribucion");
    static readonly OpcionesInstalacion NoVerificada = new() { PermitirNoVerificada = true, RespaldarRegistro = false };

    public InstalacionPruebas()
    {
        Directory.CreateDirectory(Path.Combine(Juego, "YandereSimulator_Data", "Managed"));
        File.WriteAllText(Path.Combine(Juego, "YandereSimulator.exe"), "exe");
        File.WriteAllBytes(Path.Combine(Juego, "YandereSimulator_Data", "Managed", "Assembly-CSharp.dll"), [1, 2, 3, 4]); // huella desconocida
        var partidas = Path.Combine(Juego, "YandereSimulator_Data", "StreamingAssets", "SaveFiles");
        Directory.CreateDirectory(partidas);
        File.WriteAllText(Path.Combine(partidas, "Profile_1_Slot_1.yansave"), "partida");
        File.WriteAllText(Path.Combine(Juego, "UnityPlayer.dll"), "motor");
        // Distribución: el Loader real del repositorio y un complemento de prueba.
        CopiarCarpeta(Path.Combine(AppContext.BaseDirectory, "Loader"), Path.Combine(Origen, "Loader"));
        File.WriteAllText(Path.Combine(Origen, Instalacion.ArchivoComplemento), "complemento 1.1.0");
    }

    static void CopiarCarpeta(string de, string a)
    {
        foreach (var f in Directory.GetFiles(de, "*", SearchOption.AllDirectories))
        {
            var destino = Path.Combine(a, Path.GetRelativePath(de, f));
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Copy(f, destino);
        }
    }

    [Fact]
    public void ValidaLaRutaDelJuego()
    {
        Assert.Contains("Indicá", Assert.Throws<ArgumentException>(() => Instalacion.ValidarRuta(" ")).Message);
        Assert.Contains("absoluta", Assert.Throws<ArgumentException>(() => Instalacion.ValidarRuta("juego")).Message);
        Assert.Contains("no existe", Assert.Throws<ArgumentException>(() => Instalacion.ValidarRuta(Path.Combine(raiz, "nada"))).Message);
        Assert.Contains("YandereSimulator.exe", Assert.Throws<ArgumentException>(() => Instalacion.ValidarRuta(raiz)).Message);
        Assert.Contains("raíz", Assert.Throws<ArgumentException>(() => Instalacion.ValidarRuta(Path.GetPathRoot(raiz))).Message);
        Assert.Equal(Juego, Instalacion.ValidarRuta(Juego + Path.DirectorySeparatorChar));
        Assert.Equal(Juego, Instalacion.ValidarRuta(Path.Combine(Juego, "YandereSimulator.exe")));
        Assert.Equal(Juego, Instalacion.ValidarRuta("\"" + Juego + "\""));
        File.Delete(Path.Combine(Juego, "YandereSimulator_Data", "Managed", "Assembly-CSharp.dll"));
        Assert.Contains("Assembly-CSharp.dll", Assert.Throws<ArgumentException>(() => Instalacion.ValidarRuta(Juego)).Message);
    }

    [Fact]
    public void BuildNoVerificadaPideConfirmacion()
    {
        var e = Assert.Throws<RequiereConfirmacionException>(() => Instalacion.Instalar(Juego, Origen, new OpcionesInstalacion { RespaldarRegistro = false }, Datos));
        Assert.Equal("build", e.Motivo);
        Assert.False(File.Exists(Path.Combine(Juego, "winhttp.dll")));
    }

    [Fact]
    public void InstalacionLimpiaYDesinstalacionSoloDeLoPropio()
    {
        var respaldo = Instalacion.Instalar(Juego, Origen, NoVerificada, Datos);
        Assert.True(File.Exists(Path.Combine(Juego, "winhttp.dll")));
        Assert.True(File.Exists(Path.Combine(Juego, "BepInEx", "core", "BepInEx.dll")));
        Assert.True(File.Exists(Path.Combine(Juego, Instalacion.RutaComplemento)));
        Assert.True(File.Exists(Path.Combine(respaldo, "SaveFiles", "Profile_1_Slot_1.yansave")));
        Assert.True(File.Exists(Path.Combine(respaldo, "Assembly-CSharp.dll")));
        var estado = Instalacion.Examinar(Juego);
        Assert.True(estado.Instalado); Assert.Equal("1.1.0", estado.VersionInstalada);

        // Archivos que genera BepInEx al ejecutarse y un mod ajeno agregado después.
        File.WriteAllText(Path.Combine(Juego, "BepInEx", "LogOutput.log"), "registro");
        Directory.CreateDirectory(Path.Combine(Juego, "BepInEx", "plugins", "OtroMod"));
        File.WriteAllText(Path.Combine(Juego, "BepInEx", "plugins", "OtroMod", "OtroMod.dll"), "mod");

        Assert.Throws<InvalidOperationException>(() => Instalacion.Instalar(Juego, Origen, NoVerificada, Datos)); // ya instalado
        Instalacion.Desinstalar(Juego);

        Assert.False(File.Exists(Path.Combine(Juego, "winhttp.dll")));
        Assert.False(File.Exists(Path.Combine(Juego, "doorstop_config.ini")));
        Assert.False(File.Exists(Path.Combine(Juego, Instalacion.RutaComplemento)));
        Assert.False(Directory.Exists(Path.Combine(Juego, "BepInEx", "core")));                       // carpeta propia vacía: se borra
        Assert.False(Directory.Exists(Path.Combine(Juego, "BepInEx", "plugins", "YandereUltimate")));
        Assert.True(File.Exists(Path.Combine(Juego, "BepInEx", "LogOutput.log")));                     // generado: se conserva
        Assert.True(File.Exists(Path.Combine(Juego, "BepInEx", "plugins", "OtroMod", "OtroMod.dll"))); // ajeno: se conserva
        Assert.True(File.Exists(Path.Combine(Juego, "UnityPlayer.dll")));
        Assert.True(File.Exists(Path.Combine(Juego, "YandereSimulator_Data", "StreamingAssets", "SaveFiles", "Profile_1_Slot_1.yansave")));
        Assert.True(File.Exists(Path.Combine(respaldo, "desinstalado.json")));
        Assert.False(Instalacion.Examinar(Juego).Instalado);
    }

    [Fact]
    public void BepInExConOtrosModsPreguntaYNoSobrescribe()
    {
        // Juego con BepInEx 5 y otro mod instalados por el usuario.
        CopiarCarpeta(Path.Combine(Origen, "Loader"), Juego);
        Directory.CreateDirectory(Path.Combine(Juego, "BepInEx", "plugins"));
        File.WriteAllText(Path.Combine(Juego, "BepInEx", "plugins", "ModAjeno.dll"), "mod");
        var winhttp = File.ReadAllBytes(Path.Combine(Juego, "winhttp.dll"));

        var estado = Instalacion.Examinar(Juego);
        Assert.True(estado.BepInExPresente);
        Assert.StartsWith("5.", estado.VersionBepInEx);
        Assert.Equal(["ModAjeno.dll"], estado.OtrosMods);

        var e = Assert.Throws<RequiereConfirmacionException>(() => Instalacion.Instalar(Juego, Origen, NoVerificada, Datos));
        Assert.Equal("bepinex", e.Motivo);
        Assert.Contains("ModAjeno.dll", e.Message);

        Instalacion.Instalar(Juego, Origen, new OpcionesInstalacion { PermitirNoVerificada = true, UsarBepInExExistente = true, RespaldarRegistro = false }, Datos);
        var manifiesto = JsonNode.Parse(File.ReadAllText(Path.Combine(Juego, "YandereUltimate.install.json")))!;
        Assert.Single(manifiesto["files"]!.AsObject());
        Assert.True((bool)manifiesto["bepinexExistente"]!);
        Assert.Equal(winhttp, File.ReadAllBytes(Path.Combine(Juego, "winhttp.dll")));

        Instalacion.Desinstalar(Juego);
        Assert.True(File.Exists(Path.Combine(Juego, "winhttp.dll")));
        Assert.True(File.Exists(Path.Combine(Juego, "BepInEx", "core", "BepInEx.dll")));
        Assert.True(File.Exists(Path.Combine(Juego, "BepInEx", "plugins", "ModAjeno.dll")));
        Assert.False(File.Exists(Path.Combine(Juego, Instalacion.RutaComplemento)));
        Assert.True(Directory.Exists(Path.Combine(Juego, "BepInEx", "plugins")));
    }

    [Fact]
    public void OtroCargadorSeRechaza()
    {
        File.WriteAllText(Path.Combine(Juego, "winhttp.dll"), "otro cargador");
        var e = Assert.Throws<InvalidOperationException>(() => Instalacion.Instalar(Juego, Origen, NoVerificada, Datos));
        Assert.Contains("no es BepInEx 5", e.Message);
        Assert.Equal("otro cargador", File.ReadAllText(Path.Combine(Juego, "winhttp.dll")));
    }

    [Fact]
    public void ArchivoModificadoBloqueaLaDesinstalacion()
    {
        Instalacion.Instalar(Juego, Origen, NoVerificada, Datos);
        File.WriteAllText(Path.Combine(Juego, "doorstop_config.ini"), "editado por el usuario");
        var e = Assert.Throws<IOException>(() => Instalacion.Desinstalar(Juego));
        Assert.Contains("doorstop_config.ini", e.Message);
        Assert.True(File.Exists(Path.Combine(Juego, Instalacion.RutaComplemento))); // no se borró nada
    }

    [Fact]
    public void ManifiestoConRutasExternasSeRechaza()
    {
        Instalacion.Instalar(Juego, Origen, NoVerificada, Datos);
        var ruta = Path.Combine(Juego, "YandereUltimate.install.json");
        var m = JsonNode.Parse(File.ReadAllText(ruta))!.AsObject();
        m["files"]!.AsObject()[Path.Combine("..", "fuera.txt")] = "X";
        File.WriteAllText(ruta, m.ToJsonString());
        File.WriteAllText(Path.Combine(raiz, "fuera.txt"), "no tocar");
        Assert.Throws<IOException>(() => Instalacion.Desinstalar(Juego));
        Assert.True(File.Exists(Path.Combine(raiz, "fuera.txt")));
    }

    [Fact]
    public void ActualizarReemplazaSoloElComplemento()
    {
        Instalacion.Instalar(Juego, Origen, NoVerificada, Datos);
        File.WriteAllText(Path.Combine(Origen, Instalacion.ArchivoComplemento), "complemento nuevo");
        Instalacion.Actualizar(Juego, Origen);
        Assert.Equal("complemento nuevo", File.ReadAllText(Path.Combine(Juego, Instalacion.RutaComplemento)));
        Instalacion.Desinstalar(Juego); // el manifiesto quedó coherente con el nuevo hash
        Assert.False(File.Exists(Path.Combine(Juego, Instalacion.RutaComplemento)));
    }
}
