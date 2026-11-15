using Newtonsoft.Json.Linq;
using Xunit;
using YandereUltimate.Comun;

namespace YandereUltimate.Pruebas;

public class ProtocoloPruebas
{
    static Solicitud Validar(string json) => Protocolo.Validar(Protocolo.Leer(json));
    static string Rechazo(string json) => Assert.Throws<SolicitudInvalidaException>(() => Validar(json)).Message;

    [Fact]
    public void AceptaSolicitudesValidas()
    {
        var s = Validar("""{"token":"x","op":"set","id":"money","value":250.5,"locked":true}""");
        Assert.Equal("set", s.Op); Assert.Equal("money", s.Id); Assert.Equal(250.5, s.Valor); Assert.True(s.Bloqueado);
        Assert.Equal("snapshot", Validar("""{"op":"snapshot","token":"x"}""").Op);
        Assert.True(Validar("""{"op":"toggle","id":"freezeClock","enabled":true}""").Activado);
        var npc = Validar("""{"op":"npc","id":"friendship","npc":9,"value":100}""");
        Assert.Equal(9, npc.Npc); Assert.Equal(100, npc.Valor);
        Assert.True(Validar("""{"op":"npc","id":"ignore","npc":1,"enabled":true}""").Activado);
        Assert.Equal("toDestination", Validar("""{"op":"npc","id":"toDestination","npc":4}""").Id);
    }

    [Theory]
    [InlineData("""{"op":"borrarTodo"}""", "Operación desconocida.")]
    [InlineData("""{"op":"set","id":"godMode","value":1}""", "Estadística no permitida.")]
    [InlineData("""{"op":"toggle","id":"noclip","enabled":true}""", "Función no permitida.")]
    [InlineData("""{"op":"npc","id":"kill","npc":3}""", "Acción no permitida.")]
    [InlineData("""{"op":"snapshot","extra":1}""", "Campo no permitido: extra")]
    [InlineData("""{"op":"snapshot","id":"money"}""", "El campo id no corresponde a esta operación.")]
    [InlineData("""{"op":"set","id":"money","value":"42"}""", "El campo value debe ser numérico.")]
    [InlineData("""{"op":"set","id":"money","value":true}""", "El campo value debe ser numérico.")]
    [InlineData("""{"op":"set","id":"money"}""", "Falta el campo value.")]
    [InlineData("""{"op":"set","id":7,"value":1}""", "El campo id debe ser texto.")]
    [InlineData("""{"op":"set","id":"money","value":1,"locked":"si"}""", "El campo locked debe ser verdadero o falso.")]
    [InlineData("""{"op":"toggle","id":"vision"}""", "Falta el campo enabled.")]
    [InlineData("""{"op":"toggle","id":"vision","enabled":1}""", "El campo enabled debe ser verdadero o falso.")]
    [InlineData("""{"op":"npc","id":"friendship","npc":"9","value":1}""", "El campo npc debe ser un número entero.")]
    [InlineData("""{"op":"npc","id":"friendship","npc":9.5,"value":1}""", "El campo npc debe ser un número entero.")]
    [InlineData("""{"op":"npc","id":"friendship","npc":-1,"value":1}""", "Identificador de personaje fuera de rango.")]
    [InlineData("""{"op":"npc","id":"toDestination","npc":2,"value":3}""", "El campo value no corresponde a esta operación.")]
    [InlineData("""{"op":null}""", "El campo op debe ser texto.")]
    public void RechazaLoQueNoEstaEnLaListaBlanca(string json, string motivo) => Assert.Equal(motivo, Rechazo(json));

    [Theory]
    [InlineData("money", 1000001)]
    [InlineData("reputation", -101)]
    [InlineData("hour", 18)]
    [InlineData("BiologyGrade", 6)]
    public void RechazaValoresFueraDeRango(string id, double valor) =>
        Assert.Contains("debe estar entre", Rechazo($$"""{"op":"set","id":"{{id}}","value":{{valor.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""));

    [Fact]
    public void ExigeEnterosDondeCorresponde()
    {
        Assert.Contains("entero", Rechazo("""{"op":"set","id":"study","value":10.5}"""));
        Assert.Contains("entero", Rechazo("""{"op":"npc","id":"friendship","npc":1,"value":50.5}"""));
        Assert.Equal(10.0, Validar("""{"op":"set","id":"study","value":10.0}""").Valor);
    }

    [Fact]
    public void RechazaNumerosNoFinitos() => Assert.Contains("finito", Rechazo("""{"op":"set","id":"money","value":NaN}"""));

    [Fact]
    public void RechazaJsonMalformadoODuplicado()
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Protocolo.Leer("""{"op":"set","op":"restore"}"""));
        Assert.ThrowsAny<Exception>(() => Protocolo.Leer("""{"op":"snapshot"} {"op":"restore"}"""));
        Assert.ThrowsAny<Exception>(() => Protocolo.Leer("""{"a":{"b":{"c":{"d":{"e":1}}}}}"""));
        Assert.Throws<SolicitudInvalidaException>(() => Protocolo.Leer("""["op"]"""));
    }

    [Fact]
    public void TokenEnTiempoConstante()
    {
        Assert.True(Protocolo.TokenIgual("abc123==", "abc123=="));
        Assert.False(Protocolo.TokenIgual("abc123=!", "abc123=="));
        Assert.False(Protocolo.TokenIgual("abc", "abc123=="));
        Assert.False(Protocolo.TokenIgual("abc123==x", "abc123=="));
        Assert.False(Protocolo.TokenIgual("", "abc"));
        Assert.False(Protocolo.TokenIgual(null, "abc"));
    }

    [Fact]
    public void LaListaBlancaTieneFuncionEnElCatalogo()
    {
        foreach (var v in Definiciones.Valores) Assert.NotNull(Catalogo.Buscar("valor." + v.Id));
        foreach (var i in Definiciones.Interruptores) Assert.NotNull(Catalogo.Buscar("interruptor." + i));
        foreach (var a in Definiciones.AccionesNpc) Assert.NotNull(Catalogo.Buscar("npc." + a));
    }

    [Fact]
    public void RespuestasSonJsonValido()
    {
        Assert.False((bool)JObject.Parse(Protocolo.Error("x \"y\""))["ok"]!);
        Assert.True((bool)JObject.Parse(Protocolo.Correcto(new { a = 1 }))["ok"]!);
    }
}

public class TrabajoPruebas
{
    static Solicitud S() => new() { Op = "snapshot" };

    [Fact]
    public void DevuelveLaRespuestaSiTerminaATiempo()
    {
        var t = new Trabajo(S());
        var hilo = new Thread(() => { Assert.True(t.Reclamar()); t.Completar("{\"ok\":true}"); });
        hilo.Start();
        Assert.Equal("{\"ok\":true}", t.Esperar(5000, 5000));
    }

    [Fact]
    public void UnTrabajoVencidoNuncaSeEjecuta()
    {
        var t = new Trabajo(S());
        var r = t.Esperar(50, 5000);
        Assert.Contains("se canceló sin aplicarse", r);
        Assert.True(t.Vencio);
        Assert.False(t.Reclamar()); // el hilo principal lo descarta
    }

    [Fact]
    public void UnTrabajoYaIniciadoNoSeInformaComoCancelado()
    {
        var t = new Trabajo(S());
        Assert.True(t.Reclamar());
        var hilo = new Thread(() => { Thread.Sleep(300); t.Completar("{\"ok\":true,\"data\":1}"); });
        hilo.Start();
        Assert.Equal("{\"ok\":true,\"data\":1}", t.Esperar(50, 5000));
    }

    [Fact]
    public void SiElJuegoSeCuelgaElResultadoEsDesconocido()
    {
        var t = new Trabajo(S());
        Assert.True(t.Reclamar());
        Assert.Contains("resultado es desconocido", t.Esperar(20, 50));
    }

    [Fact]
    public void CarreraEntreVencimientoYReclamoEsCoherente()
    {
        // Muchas carreras: o se ejecuta y se informa el resultado, o no se ejecuta y se informa cancelado.
        for (int i = 0; i < 300; i++)
        {
            var t = new Trabajo(S());
            bool ejecutado = false;
            var principal = new Thread(() => { if (t.Reclamar()) { ejecutado = true; t.Completar("hecho"); } });
            principal.Start();
            var r = t.Esperar(Random.Shared.Next(0, 2), 5000);
            principal.Join();
            Assert.Equal(ejecutado, r == "hecho");
        }
    }
}

public class RespaldosPruebas
{
    static string CarpetaTemporal() => Directory.CreateTempSubdirectory("ys-respaldos-").FullName;

    [Fact]
    public void ComoMaximoUnoPorSegundo()
    {
        var carpeta = CarpetaTemporal();
        using var r = new Respaldos(carpeta, 50, TimeSpan.FromSeconds(1), null);
        var t0 = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(r.Encolar(t0, "set", "money", () => new { a = 1 }));
        Assert.False(r.Encolar(t0.AddMilliseconds(300), "set", "money", () => throw new Exception("no debe leerse")));
        Assert.False(r.Encolar(t0.AddMilliseconds(999), "set", "money", () => new { a = 1 }));
        Assert.True(r.Encolar(t0.AddSeconds(1), "set", "money", () => new { a = 2 }));
        Assert.True(r.Vaciar(5000));
        Assert.Equal(2, Directory.GetFiles(carpeta, "*.json").Length);
    }

    [Fact]
    public void ConservaComoMaximoCincuentaArchivos()
    {
        var carpeta = CarpetaTemporal();
        using var r = new Respaldos(carpeta, 50, TimeSpan.FromSeconds(1), null);
        var t0 = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 60; i++)
        {
            Assert.True(r.Vaciar(5000)); // la cola admite pocos pendientes: se espera a que se escriban
            Assert.True(r.Encolar(t0.AddSeconds(i), "set", "money", () => new { i }));
        }
        Assert.True(r.Vaciar(10000));
        var archivos = Directory.GetFiles(carpeta, "*.json").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(50, archivos.Count);
        Assert.Equal(t0.AddSeconds(10).ToString("yyyyMMdd-HHmmss-fff") + ".json", archivos.First());
        Assert.Equal(t0.AddSeconds(59).ToString("yyyyMMdd-HHmmss-fff") + ".json", archivos.Last());
    }

    [Fact]
    public void ElDiscoSeEscribeFueraDelHiloQueEncola()
    {
        var archivo = Path.Combine(CarpetaTemporal(), "no-es-carpeta");
        File.WriteAllText(archivo, "x"); // usar un archivo como carpeta provoca un error de escritura
        string? hiloDelError = null;
        using var listo = new ManualResetEventSlim();
        using var r = new Respaldos(archivo, 50, TimeSpan.FromSeconds(1), _ => { hiloDelError = Thread.CurrentThread.Name; listo.Set(); });
        Assert.True(r.Encolar(DateTime.UtcNow, "set", "money", () => new { a = 1 })); // no lanza en el hilo que llama
        Assert.True(listo.Wait(5000));
        Assert.Equal("YandereUltimate.Respaldos", hiloDelError);
    }
}
