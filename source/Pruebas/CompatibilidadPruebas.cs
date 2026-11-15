using Mono.Cecil;
using Xunit;
using YandereUltimate.Comun;
using Insp = YandereUltimate.Inspector.Programa;

namespace YandereUltimate.Pruebas;

/// <summary>
/// Pruebas del catálogo y del modo "build no verificada". Las que necesitan el juego usan la variable
/// YS_JUEGO (carpeta del juego o de Managed) y YS_PUENTE (YandereUltimate.Bridge.dll compilado);
/// Compilar.ps1 las define. Sin ellas, esas pruebas se omiten.
/// </summary>
public class CompatibilidadPruebas
{
    static string? Managed() => Environment.GetEnvironmentVariable("YS_JUEGO") is { Length: > 0 } j ? Insp.Managed(j) : null;

    [Fact]
    public void CatalogoBienFormado()
    {
        var vistos = new HashSet<string>();
        foreach (var f in Catalogo.Funciones)
        {
            Assert.True(vistos.Add(f.Id), "Id duplicado: " + f.Id);
            foreach (var d in f.Depende) Assert.True(vistos.Contains(d), $"{f.Id} depende de {d}, que no está declarada antes");
            foreach (var r in f.Requisitos) Assert.Matches(@"^(T:[\w.`/\[\]]+|F:[\w.`/]+\.\w+:[\w.`/\[\]<>,]+|M:[\w.`/]+\.[\w.]+\((\*|[\w.`/\[\]<>,&]*)\):[\w.`/\[\]<>,]+)$", r);
        }
    }

    [Fact]
    public void BuildsConocidasConservanLa530()
    {
        var builds = Compatibilidad_Builds();
        Assert.Contains(builds, b => b.Build == "530" && b.Sha256 == "62926A9CC29156037C340E7BD177D88D760D03618FCDFF0C2CD66CCAB78CED09");
        Assert.Contains(builds, b => b.Build == "532" && b.Sha256 == "E06B2209AA75DDA4EC6EE15E927566FD02F91B1BBC01A34908082D559CEB4F2C");
        Assert.All(builds, b => Assert.Matches("^[0-9A-F]{64}$", b.Sha256));
        Assert.Equal(builds.Count, builds.Select(b => b.Sha256).Distinct().Count());
    }
    static IReadOnlyList<UltimateTrainer.BuildConocida> Compatibilidad_Builds() => UltimateTrainer.Compatibilidad.Builds;

    [SkippableFact]
    public void TodasLasFuncionesDisponiblesEnLaBuildInstalada()
    {
        var managed = Managed();
        Skip.If(managed == null, "YS_JUEGO no definido");
        var estados = Insp.EvaluarCatalogo(managed!);
        Assert.Empty(estados.Where(e => !e.Disponible).Select(e => e.Id + ": " + e.Motivo));
    }

    [SkippableFact]
    public void ElPuenteSoloUsaReferenciasExistentesYDeclaradas()
    {
        var managed = Managed();
        var puente = Environment.GetEnvironmentVariable("YS_PUENTE");
        Skip.If(managed == null || string.IsNullOrEmpty(puente) || !File.Exists(puente), "YS_JUEGO o YS_PUENTE no definidos");
        var (referencias, ausentes) = Insp.Referencias(puente!, managed!);
        Assert.NotEmpty(referencias);
        Assert.Empty(ausentes);
        var declarados = Catalogo.Funciones.SelectMany(f => f.Requisitos).ToHashSet();
        Assert.DoesNotContain(referencias, r => !declarados.Contains(r));
    }

    /// <summary>Simula una build futura: renombra y cambia miembros en una copia de Assembly-CSharp.</summary>
    [SkippableFact]
    public void BuildNoVerificadaDesactivaSoloLoQueFalta()
    {
        var managed = Managed();
        Skip.If(managed == null, "YS_JUEGO no definido");
        var carpeta = Directory.CreateTempSubdirectory("ys-build-futura-").FullName;
        var copia = Path.Combine(carpeta, "Assembly-CSharp.dll");
        var resolutor = new DefaultAssemblyResolver();
        resolutor.AddSearchDirectory(managed!);
        using (var modulo = ModuleDefinition.ReadModule(Path.Combine(managed!, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolutor }))
        {
            modulo.GetType("ClockScript").Fields.Single(f => f.Name == "TimeSpeed").Name = "TimeScale";
            foreach (var m in modulo.GetType("StudentScript").Methods.Where(m => m.Name == "CanSeeObject")) m.Name = "CanSee";
            modulo.GetType("InventoryScript").Fields.Single(f => f.Name == "Money").FieldType = modulo.TypeSystem.Double;
            modulo.Write(copia);
        }
        Assert.NotEqual(Insp.Inspeccionar(managed!, null).Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(copia))));

        var estados = Insp.EvaluarCatalogo(managed!, copia).ToDictionary(e => e.Id);
        Assert.Contains("ClockScript.TimeSpeed", estados["valor.timeSpeed"].Motivo);
        Assert.Contains("ClockScript.TimeSpeed", estados["interruptor.freezeClock"].Motivo);
        Assert.Contains("CanSeeObject", estados["parche.vision"].Motivo);
        Assert.StartsWith("Depende de «Parche de visión»", estados["interruptor.vision"].Motivo);
        Assert.False(estados["interruptor.stupid"].Disponible);
        Assert.False(estados["npc.ignore"].Disponible);
        Assert.Contains("cambió de tipo (System.Single → System.Double)", estados["valor.money"].Motivo);
        // Lo demás sigue disponible.
        foreach (var id in new[] { "valor.reputation", "valor.hour", "valor.info", "interruptor.hearing", "interruptor.witness", "interruptor.freezeAll", "npc.friendship", "npc.speed" })
            Assert.True(estados[id].Disponible, id + ": " + estados[id].Motivo);
    }

    [Fact]
    public void DependenciasSePropaganAlDesactivarEnEjecucion()
    {
        var catalogo = new[]
        {
            new DefFuncion("congelar", "Congelación", false, []),
            new DefFuncion("interruptor.freezeAll", "Congelar todos", true, [], "congelar"),
            new DefFuncion("interruptor.freezeCouncil", "Congelar consejo", true, [], "congelar"),
            new DefFuncion("interruptor.vision", "Visión", true, []),
        };
        var estados = Sondeo.Evaluar(catalogo, new ResolutorReflexion(_ => null));
        Assert.All(estados, e => Assert.True(e.Disponible));
        Sondeo.Desactivar(estados, "congelar", "Falló durante la partida");
        var porId = estados.ToDictionary(e => e.Id);
        Assert.False(porId["interruptor.freezeAll"].Disponible);
        Assert.Equal("Depende de «Congelación»: Falló durante la partida", porId["interruptor.freezeCouncil"].Motivo);
        Assert.True(porId["interruptor.vision"].Disponible);
    }

    [Fact]
    public void RequisitoAusenteSeInformaEnEspanol()
    {
        var r = new ResolutorReflexion(n => n == "System.String" ? typeof(string) : null);
        Assert.Null(Sondeo.Verificar("T:System.String", r));
        Assert.Null(Sondeo.Verificar("M:System.String.Concat(System.String,System.String):System.String", r));
        Assert.Null(Sondeo.Verificar("M:System.String.get_Length():System.Int32", r));
        Assert.Equal("falta el tipo ClockScript", Sondeo.Verificar("F:ClockScript.TimeSpeed:System.Single", r));
        Assert.Equal("falta el método System.String.Volar", Sondeo.Verificar("M:System.String.Volar():System.Void", r));
        Assert.Equal("el método System.String.Concat cambió de firma", Sondeo.Verificar("M:System.String.Concat(System.Int64):System.Void", r));
        Assert.Equal("el campo System.String.Empty cambió de tipo (System.Int32 → System.String)", Sondeo.Verificar("F:System.String.Empty:System.Int32", r));
    }
}
