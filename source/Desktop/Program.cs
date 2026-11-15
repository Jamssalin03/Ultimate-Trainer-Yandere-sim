using System.Text.Json;
using System.Windows;

namespace UltimateTrainer;

internal static class Programa
{
    public static readonly string Datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YandereUltimate");
    public static string Version => Compatibilidad.VersionApp;
    /// <summary>Carpeta de la distribución: contiene Loader\ y YandereUltimate.Bridge.dll.</summary>
    public static string Distribucion => AppContext.BaseDirectory;

    [STAThread]
    public static int Main(string[] args)
    {
        Directory.CreateDirectory(Datos);
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal)) return Consola(args);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { Registrar(e.Exception); MessageBox.Show(e.Exception.Message, "No se completó la operación", MessageBoxButton.OK, MessageBoxImage.Warning); e.Handled = true; };
        return app.Run(new VentanaPrincipal());
    }

    /// <summary>Modo sin ventana: --install, --update, --uninstall, --backup, --probe.</summary>
    static int Consola(string[] args)
    {
        string resultado = Path.Combine(Datos, "install-result.txt");
        try
        {
            string? ruta = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
            switch (args[0])
            {
                case "--install":
                    var op = new OpcionesInstalacion { UsarBepInExExistente = args.Contains("--usar-bepinex-existente"), PermitirNoVerificada = args.Contains("--permitir-no-verificada") };
                    Instalacion.Instalar(ruta ?? throw new ArgumentException("Falta la ruta del juego."), Distribucion, op, Datos);
                    File.WriteAllText(resultado, "Instalado");
                    return 0;
                case "--update": Instalacion.Actualizar(ruta ?? throw new ArgumentException("Falta la ruta del juego."), Distribucion); File.WriteAllText(resultado, "Actualizado"); return 0;
                case "--uninstall": Instalacion.Desinstalar(ruta ?? throw new ArgumentException("Falta la ruta del juego.")); File.WriteAllText(resultado, "Desinstalado"); return 0;
                case "--backup": File.WriteAllText(resultado, Instalacion.Respaldar(ruta ?? throw new ArgumentException("Falta la ruta del juego."), Datos, true)); return 0;
                case "--probe":
                    var datos = Cliente.Instantanea().GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(Datos, "probe.json"), datos.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                default: throw new ArgumentException("Opción desconocida: " + args[0]);
            }
        }
        catch (RequiereConfirmacionException e) { File.WriteAllText(resultado, "Requiere confirmación (" + e.Motivo + "): " + e.Message); return 2; }
        catch (Exception e) { Registrar(e); File.WriteAllText(resultado, e.ToString()); return 1; }
    }

    public static void Registrar(Exception e)
    {
        try { File.AppendAllText(Path.Combine(Datos, "errores.log"), DateTime.Now.ToString("s") + " " + e + Environment.NewLine); } catch (Exception) { }
    }
}
