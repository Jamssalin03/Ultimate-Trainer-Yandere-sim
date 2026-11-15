using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json.Nodes;

namespace UltimateTrainer;

/// <summary>Cliente del canal local autenticado. Una solicitud por conexión; nunca dos a la vez.</summary>
internal static class Cliente
{
    static readonly SemaphoreSlim Turno = new(1, 1);
    public static string Endpoint => Path.Combine(Programa.Datos, "endpoint.json");

    public static async Task<JsonObject> Enviar(JsonObject solicitud)
    {
        await Turno.WaitAsync();
        try
        {
            var endpoint = JsonNode.Parse(await File.ReadAllTextAsync(Endpoint))!;
            int pid = (int)endpoint["pid"]!;
            using (var proceso = Process.GetProcessById(pid))
                if (proceso.HasExited || proceso.ProcessName != "YandereSimulator") throw new IOException("El proceso del juego ya no está disponible.");
            using var cliente = new NamedPipeClientStream(".", (string)endpoint["pipe"]!, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            await cliente.ConnectAsync(2000, limite.Token);
            solicitud["token"] = (string)endpoint["token"]!;
            using var escritor = new StreamWriter(cliente, leaveOpen: true) { AutoFlush = true };
            using var lector = new StreamReader(cliente, leaveOpen: true);
            await escritor.WriteLineAsync(solicitud.ToJsonString().AsMemory(), limite.Token);
            var linea = await lector.ReadLineAsync(limite.Token) ?? throw new IOException("El complemento cerró la conexión.");
            if (linea.Length > 4_000_000) throw new IOException("Respuesta demasiado grande.");
            var respuesta = JsonNode.Parse(linea)!.AsObject();
            if ((bool?)respuesta["ok"] != true) throw new InvalidOperationException((string?)respuesta["error"] ?? "Error de comunicación.");
            return respuesta["data"]!.AsObject();
        }
        catch (ArgumentException) { throw new IOException("El proceso del juego ya no está disponible."); }
        finally { Turno.Release(); }
    }

    public static Task<JsonObject> Instantanea() => Enviar(new JsonObject { ["op"] = "snapshot" });
    public static Task<JsonObject> RestaurarTodo() => Enviar(new JsonObject { ["op"] = "restore" });
    public static Task<JsonObject> Fijar(string id, double valor, bool bloqueado) => Enviar(new JsonObject { ["op"] = "set", ["id"] = id, ["value"] = valor, ["locked"] = bloqueado });
    public static Task<JsonObject> RestaurarValor(string id) => Enviar(new JsonObject { ["op"] = "restoreValue", ["id"] = id });
    public static Task<JsonObject> Interruptor(string id, bool activado) => Enviar(new JsonObject { ["op"] = "toggle", ["id"] = id, ["enabled"] = activado });
    public static Task<JsonObject> Npc(string accion, int npc, double? valor = null, bool? activado = null)
    {
        var s = new JsonObject { ["op"] = "npc", ["id"] = accion, ["npc"] = npc };
        if (valor != null) s["value"] = valor.Value;
        if (activado != null) s["enabled"] = activado.Value;
        return Enviar(s);
    }
}
