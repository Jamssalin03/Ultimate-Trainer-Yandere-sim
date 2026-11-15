using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using YandereUltimate.Comun;

namespace UltimateTrainer;

internal sealed class VentanaPrincipal : Window
{
    enum EstadoConexion { Desconectado, Conectando, Conectado, Incompatible }

    sealed class Editor
    {
        public required DefValor Def { get; init; }
        public required TextBox Entrada { get; init; }
        public required Slider Deslizador { get; init; }
        public required TextBlock Actual { get; init; }
        public required CheckBox Bloqueo { get; init; }
        public required FrameworkElement Controles { get; init; }
        public bool Inicializado, Editado, BloqueoEditado;
    }
    sealed class Interruptor
    {
        public required string Id { get; init; }
        public required CheckBox Caja { get; init; }
        public required TextBlock Motivo { get; init; }
        public required Func<JsonObject?, string?> Contexto { get; init; }
    }
    sealed record NpcItem(int Id, string Texto) { public override string ToString() => Texto; }

    static readonly string[] Pestanas = { "Estadísticas", "Recursos", "NPC", "Comportamiento", "Tiempo", "Perfiles", "Ajustes", "Acerca de" };

    readonly Configuracion config = Configuracion.Cargar();
    readonly DispatcherTimer sondeo = new() { Interval = TimeSpan.FromMilliseconds(900) };
    readonly DispatcherTimer guardado = new() { Interval = TimeSpan.FromMilliseconds(800) };
    readonly TabControl pestanas = new();
    readonly Dictionary<string, StackPanel> paginas = new();
    readonly Ellipse punto = new() { Width = 11, Height = 11, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock textoEstado = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock etiquetaBuild = new() { FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock contexto = new() { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock mensaje = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    readonly Border aviso = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed, Cursor = Cursors.Hand };
    readonly TextBlock textoAviso = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button botonConexion = new();
    readonly Button botonRestaurar = new() { Content = "Restaurar todo", ToolTip = "Desactiva bloqueos e interruptores, descongela NPC y restaura los valores registrados (Ctrl+R)" };

    readonly Dictionary<string, Editor> editores = new();
    readonly List<Interruptor> interruptores = new();
    readonly Dictionary<string, Button> accionesNpc = new();
    readonly List<(string Funcion, FrameworkElement Control)> controlesNpc = new();
    readonly TextBox buscadorNpc = new() { Margin = new Thickness(0, 0, 0, 10), ToolTip = "Buscar por nombre, número o rol (Ctrl+F)" };
    readonly ListBox listaNpc = new() { Height = 260 };
    readonly TextBlock detalleNpc = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 12) };
    string firmaNpc = "";

    readonly TextBox rutaJuego = new() { Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBlock estadoJuego = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBlock estadoComplemento = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    readonly StackPanel listaDesactivadas = new();
    readonly TextBlock infoCompatibilidad = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    readonly RadioButton temaOscuro = new() { Content = "Oscuro", GroupName = "tema" }, temaClaro = new() { Content = "Claro", GroupName = "tema" };
    readonly ListBox listaPerfiles = new() { Height = 140, Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBox nombrePerfil = new() { Text = "Mi perfil" };

    JsonObject? instantanea;
    EstadoConexion estado = EstadoConexion.Conectando;
    string motivoEstado = "";
    bool autoConectar = true, sondeando, actualizando, cerrando;
    Dictionary<string, string> desactivadas = new();

    public VentanaPrincipal()
    {
        Title = "Yandere Simulator Ultimate Trainer " + Programa.Version;
        Width = 1180; Height = 820; MinWidth = 900; MinHeight = 640;
        FontFamily = new FontFamily("Segoe UI, Segoe UI Symbol"); FontSize = 14;
        AplicarTema(config.Tema);
        SetResourceReference(BackgroundProperty, "Fondo");
        SetResourceReference(ForegroundProperty, "Texto");

        var raiz = new DockPanel();
        Content = raiz;
        var cabecera = CrearCabecera();
        DockPanel.SetDock(cabecera, Dock.Top);
        raiz.Children.Add(cabecera);
        var pie = new Border { Padding = new Thickness(24, 0, 24, 12), Child = mensaje };
        DockPanel.SetDock(pie, Dock.Bottom);
        raiz.Children.Add(pie);
        pestanas.Margin = new Thickness(24, 4, 24, 0);
        raiz.Children.Add(pestanas);

        for (int i = 0; i < Pestanas.Length; i++)
        {
            var pagina = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
            paginas[Pestanas[i]] = pagina;
            var desplazamiento = new ScrollViewer { Content = pagina, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // El contenido heredaría el color del encabezado de la pestaña seleccionada: se fija explícitamente.
            desplazamiento.SetResourceReference(ForegroundProperty, "Texto");
            pestanas.Items.Add(new TabItem { Header = Pestanas[i], ToolTip = "Ctrl+" + (i + 1), Content = desplazamiento });
        }
        ConstruirEstadisticas(); ConstruirRecursos(); ConstruirNpc(); ConstruirComportamiento(); ConstruirTiempo(); ConstruirPerfiles(); ConstruirAjustes(); ConstruirAcercaDe();

        actualizando = true;
        rutaJuego.Text = string.IsNullOrWhiteSpace(config.Instalacion) ? Instalacion.Detectar() ?? "" : config.Instalacion;
        foreach (var (id, texto) in config.Valores) if (editores.TryGetValue(id, out var e)) e.Entrada.Text = texto;
        (config.Tema == "claro" ? temaClaro : temaOscuro).IsChecked = true;
        actualizando = false;
        ActualizarPerfiles();
        ExaminarJuego();

        PreviewKeyDown += Atajos;
        guardado.Tick += (_, _) => { guardado.Stop(); Guardar(); };
        sondeo.Tick += async (_, _) => await Sondear();
        sondeo.Start();
        Closing += AlCerrar;
        FijarEstado(EstadoConexion.Conectando, "Buscando el juego…");
        _ = Sondear();
    }

    // ================= Utilidades de interfaz =================

    static void AplicarTema(string tema)
    {
        var recursos = Application.Current.Resources.MergedDictionaries;
        recursos.Clear();
        recursos.Add((ResourceDictionary)Application.LoadComponent(new Uri(tema == "claro" ? "/YandereSimulatorUltimateTrainer;component/Temas/Claro.xaml" : "/YandereSimulatorUltimateTrainer;component/Temas/Oscuro.xaml", UriKind.Relative)));
        recursos.Add((ResourceDictionary)Application.LoadComponent(new Uri("/YandereSimulatorUltimateTrainer;component/Temas/Estilos.xaml", UriKind.Relative)));
    }

    static TextBlock Texto(string valor, double tamano = 14, FontWeight? peso = null) => new() { Text = valor, FontSize = tamano, FontWeight = peso ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    static TextBlock Suave(string valor, double tamano = 12)
    {
        var t = Texto(valor, tamano);
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextoSuave");
        return t;
    }
    static Brush? Recurso(string clave) => Application.Current.TryFindResource(clave) as Brush;

    Button Boton(string etiqueta, Action accion, string? ayuda = null)
    {
        var b = new Button { Content = etiqueta, ToolTip = ayuda };
        b.Click += (_, _) => { try { accion(); } catch (Exception e) { Fallo(e); } };
        return b;
    }
    Button BotonAsync(string etiqueta, Func<Task> accion, string? ayuda = null)
    {
        var b = new Button { Content = etiqueta, ToolTip = ayuda };
        b.Click += async (_, _) => await Ejecutar(b, accion);
        return b;
    }
    async Task Ejecutar(UIElement? origen, Func<Task> accion)
    {
        try { if (origen != null) origen.IsEnabled = false; await accion(); }
        catch (Exception e) { Fallo(e); }
        finally { if (origen != null) origen.IsEnabled = true; RefrescarControles(); }
    }

    void Fallo(Exception e)
    {
        Programa.Registrar(e);
        mensaje.Text = "Error: " + e.Message;
        mensaje.SetResourceReference(TextBlock.ForegroundProperty, "Peligro");
    }
    void Aviso(string texto)
    {
        mensaje.Text = texto;
        mensaje.SetResourceReference(TextBlock.ForegroundProperty, "TextoSuave");
    }

    Border Tarjeta(string pagina, string titulo, UIElement cuerpo, string? ayuda = null)
    {
        var pila = new StackPanel();
        pila.Children.Add(Texto(titulo, 16, FontWeights.SemiBold));
        if (ayuda != null) { var a = Suave(ayuda); a.Margin = new Thickness(0, 4, 0, 0); pila.Children.Add(a); }
        if (cuerpo is FrameworkElement fe) fe.Margin = new Thickness(0, 12, 0, 0);
        pila.Children.Add(cuerpo);
        var borde = new Border { Child = pila };
        borde.SetResourceReference(StyleProperty, "TarjetaEstilo");
        paginas[pagina].Children.Add(borde);
        return borde;
    }
    void Seccion(string pagina, string titulo)
    {
        var t = Texto(titulo.ToUpper(CultureInfo.CurrentCulture), 12, FontWeights.SemiBold);
        t.SetResourceReference(TextBlock.ForegroundProperty, "Acento");
        t.Margin = new Thickness(2, paginas[pagina].Children.Count == 0 ? 0 : 12, 0, 8);
        paginas[pagina].Children.Add(t);
    }

    static bool LeerNumero(string texto, out double resultado) =>
        double.TryParse(texto, NumberStyles.Float, CultureInfo.CurrentCulture, out resultado) || double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out resultado);
    static string Formato(double v, bool entero) => entero ? Math.Round(v).ToString("0", CultureInfo.CurrentCulture) : v.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>Campo numérico con deslizador sincronizado.</summary>
    (TextBox Entrada, Slider Deslizador) CampoConDeslizador(DefValor def, double inicial, double ancho = 300)
    {
        var entrada = new TextBox { Width = 110, Text = Formato(inicial, def.Entero), Margin = new Thickness(0, 0, 12, 8), VerticalAlignment = VerticalAlignment.Center };
        var deslizador = new Slider { Minimum = def.Min, Maximum = def.Max, Value = inicial, Width = ancho, Margin = new Thickness(0, 4, 16, 8), VerticalAlignment = VerticalAlignment.Center, SmallChange = def.Entero ? 1 : (def.Max - def.Min) / 100, LargeChange = (def.Max - def.Min) / 10, IsSnapToTickEnabled = def.Entero, TickFrequency = def.Entero ? 1 : (def.Max - def.Min) / 100 };
        bool sincronizando = false;
        deslizador.ValueChanged += (_, _) => { if (sincronizando) return; sincronizando = true; entrada.Text = Formato(deslizador.Value, def.Entero); sincronizando = false; };
        entrada.TextChanged += (_, _) => { if (sincronizando || !LeerNumero(entrada.Text, out var v)) return; sincronizando = true; deslizador.Value = Math.Clamp(v, def.Min, def.Max); sincronizando = false; };
        return (entrada, deslizador);
    }

    double Validar(DefValor def, string texto)
    {
        if (!LeerNumero(texto, out var v)) throw new ArgumentException("Introducí un número válido.");
        if (def.Entero) v = Math.Round(v);
        var error = def.Validar(v);
        if (error != null) throw new ArgumentException($"Introducí un número entre {Formato(def.Min, def.Entero)} y {Formato(def.Max, def.Entero)}{(def.Entero ? " (entero)" : "")}.");
        return v;
    }

    // ================= Cabecera (estado siempre visible) =================

    FrameworkElement CrearCabecera()
    {
        var barra = new Border { Padding = new Thickness(24, 16, 24, 14), BorderThickness = new Thickness(0, 0, 0, 1) };
        barra.SetResourceReference(Border.BackgroundProperty, "Barra");
        barra.SetResourceReference(Border.BorderBrushProperty, "Borde");
        var grilla = new Grid();
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grilla.ColumnDefinitions.Add(new ColumnDefinition());
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barra.Child = grilla;

        var marca = new StackPanel { Margin = new Thickness(0, 0, 32, 0), VerticalAlignment = VerticalAlignment.Center };
        marca.Children.Add(Texto("YANDERE", 22, FontWeights.Bold));
        var sub = Texto("ULTIMATE TRAINER · " + Programa.Version, 11, FontWeights.SemiBold);
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Acento");
        marca.Children.Add(sub);
        grilla.Children.Add(marca);

        var centro = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var linea = new StackPanel { Orientation = Orientation.Horizontal };
        linea.Children.Add(punto); linea.Children.Add(textoEstado); linea.Children.Add(etiquetaBuild);
        centro.Children.Add(linea);
        contexto.SetResourceReference(TextBlock.ForegroundProperty, "TextoSuave");
        centro.Children.Add(contexto);
        aviso.Child = textoAviso;
        aviso.MouseLeftButtonUp += (_, _) => { pestanas.SelectedIndex = Array.IndexOf(Pestanas, "Ajustes"); };
        centro.Children.Add(aviso);
        Grid.SetColumn(centro, 1);
        grilla.Children.Add(centro);

        var acciones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        botonConexion.Click += async (_, _) => { if (autoConectar) await Desconectar(); else Conectar(); };
        botonConexion.Margin = new Thickness(0, 0, 8, 0);
        botonRestaurar.SetResourceReference(StyleProperty, "BotonAcento");
        botonRestaurar.Margin = new Thickness(0);
        botonRestaurar.Click += async (_, _) => await Ejecutar(botonRestaurar, RestaurarTodo);
        acciones.Children.Add(botonConexion); acciones.Children.Add(botonRestaurar);
        Grid.SetColumn(acciones, 2);
        grilla.Children.Add(acciones);
        return barra;
    }

    void FijarEstado(EstadoConexion nuevo, string motivo)
    {
        estado = nuevo; motivoEstado = motivo;
        bool verificada = (bool?)instantanea?["verified"] == true;
        (string texto, string color) = nuevo switch
        {
            EstadoConexion.Desconectado => ("Desconectado", "Neutro"),
            EstadoConexion.Conectando => ("Conectando…", "Advertencia"),
            EstadoConexion.Conectado => ("Conectado", "Exito"),
            _ => ("Incompatible", "Peligro"),
        };
        textoEstado.Text = texto;
        punto.SetResourceReference(Shape.FillProperty, color);
        textoEstado.SetResourceReference(TextBlock.ForegroundProperty, color);
        etiquetaBuild.Text = nuevo == EstadoConexion.Conectado ? (verificada ? "· build verificada" : "· build no verificada") : "";
        etiquetaBuild.SetResourceReference(TextBlock.ForegroundProperty, verificada ? "TextoSuave" : "Advertencia");
        contexto.Text = motivo;
        botonConexion.Content = autoConectar ? "Desconectar" : "Conectar";
        botonConexion.ToolTip = autoConectar ? "Restaura la sesión y deja de buscar el juego (Ctrl+D)" : "Buscar el juego y conectar (F5)";
        botonRestaurar.IsEnabled = nuevo == EstadoConexion.Conectado;

        int n = desactivadas.Count;
        if (nuevo == EstadoConexion.Incompatible || (nuevo == EstadoConexion.Conectado && (!verificada || n > 0)))
        {
            textoAviso.Text = nuevo == EstadoConexion.Incompatible
                ? "Las modificaciones están bloqueadas hasta resolver la incompatibilidad. Ver Ajustes › Compatibilidad."
                : $"Build no verificada: se activaron sólo las funciones cuyas referencias existen. {n} función(es) desactivada(s). Ver Ajustes › Compatibilidad.";
            aviso.SetResourceReference(Border.BackgroundProperty, "Boton");
            textoAviso.SetResourceReference(TextBlock.ForegroundProperty, nuevo == EstadoConexion.Incompatible ? "Peligro" : "Advertencia");
            aviso.Visibility = Visibility.Visible;
        }
        else aviso.Visibility = Visibility.Collapsed;
    }

    // ================= Pestañas =================

    void Numerico(string pagina, string titulo, string id, string? ayuda = null, int[]? rapidos = null)
    {
        var def = Definiciones.Valor(id)!;
        var panel = new StackPanel();
        var actual = Suave("Sin conexión", 13);
        actual.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(actual);
        var fila = new WrapPanel();
        var (entrada, deslizador) = CampoConDeslizador(def, def.Min);
        fila.Children.Add(entrada); fila.Children.Add(deslizador);
        var bloqueo = new CheckBox { Content = "No disminuir", Margin = new Thickness(0, 0, 16, 8), ToolTip = "Mantiene como mínimo el valor aplicado. Desmarcar quita el bloqueo inmediatamente." };
        fila.Children.Add(bloqueo);
        var botones = new WrapPanel();
        var aplicar = BotonAsync("Aplicar", async () => { var v = Validar(def, entrada.Text); await Operar(() => Cliente.Fijar(id, v, bloqueo.IsChecked == true)); editores[id].BloqueoEditado = false; Guardar(); }, "Envía el valor al juego (Enter)");
        botones.Children.Add(aplicar);
        botones.Children.Add(BotonAsync("Restaurar", async () => { await Operar(() => Cliente.RestaurarValor(id)); editores[id].BloqueoEditado = false; }, "Recupera el valor que tenía antes de editarlo en esta escena"));
        if (id != "hour" && id != "timeSpeed") botones.Children.Add(Boton("Máximo", () => entrada.Text = Formato(def.Max, def.Entero), "Sólo llena el campo; pulsá Aplicar para enviarlo"));
        foreach (var r in rapidos ?? [])
        {
            var cantidad = r;
            botones.Children.Add(BotonAsync(cantidad.ToString(CultureInfo.CurrentCulture), async () => { entrada.Text = cantidad.ToString(CultureInfo.CurrentCulture); await Operar(() => Cliente.Fijar(id, cantidad, bloqueo.IsChecked == true)); }, "Aplica esta cantidad directamente"));
        }
        fila.Children.Add(botones);
        panel.Children.Add(fila);
        entrada.KeyDown += (_, e) => { if (e.Key == Key.Enter) aplicar.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); };
        entrada.TextChanged += (_, _) => { if (actualizando) return; editores[id].Editado = true; guardado.Stop(); guardado.Start(); };
        bloqueo.Checked += (_, _) => { if (!actualizando) editores[id].BloqueoEditado = true; };
        bloqueo.Unchecked += async (_, _) =>
        {
            if (actualizando) return;
            editores[id].BloqueoEditado = true;
            if (Bloqueado(id) && instantanea?["values"]?[id] is JsonNode actualValor)
                await Ejecutar(null, async () => { await Operar(() => Cliente.Fijar(id, (double)actualValor, false)); editores[id].BloqueoEditado = false; });
        };
        Tarjeta(pagina, titulo, panel, ayuda);
        editores[id] = new Editor { Def = def, Entrada = entrada, Deslizador = deslizador, Actual = actual, Bloqueo = bloqueo, Controles = fila };
    }

    void Conmutador(string pagina, string titulo, string id, string? ayuda = null, Func<JsonObject?, string?>? contexto = null)
    {
        var panel = new StackPanel();
        var caja = new CheckBox { Content = "Desactivado", FontSize = 15 };
        var motivo = Suave("", 12);
        motivo.Margin = new Thickness(0, 6, 0, 0);
        caja.Click += async (_, _) =>
        {
            if (actualizando) return;
            await Ejecutar(caja, async () => { await Operar(() => Cliente.Interruptor(id, caja.IsChecked == true)); });
        };
        panel.Children.Add(caja); panel.Children.Add(motivo);
        Tarjeta(pagina, titulo, panel, ayuda);
        interruptores.Add(new Interruptor { Id = id, Caja = caja, Motivo = motivo, Contexto = contexto ?? (_ => null) });
    }

    static string? RequiereNpc(JsonObject? s) => (s?["npcs"]?.AsArray().Count ?? 0) > 0 ? null : "No hay personajes cargados en esta escena.";
    static Func<JsonObject?, string?> RequiereValor(string id) => s => s?["values"]?[id] != null ? null : "No disponible en esta escena o modo.";

    void ConstruirEstadisticas()
    {
        const string p = "Estadísticas";
        Seccion(p, "Reputación y estudio");
        Numerico(p, "Reputación actual", "reputation");
        Numerico(p, "Reputación permanente", "permanentRep");
        Numerico(p, "Puntos de estudio disponibles", "study", "Abrí la interfaz de clase antes de editar. Son los puntos de la sesión, utilizables al asignarlos a materias.");
        Seccion(p, "Materias");
        foreach (var (etiqueta, id) in new[] { ("Biología", "Biology"), ("Química", "Chemistry"), ("Lenguaje", "Language"), ("Educación Física", "Physical"), ("Psicología", "Psychology") })
        {
            Numerico(p, etiqueta + ": progreso", id);
            Numerico(p, etiqueta + ": nivel", id + "Grade");
        }
        Seccion(p, "Atributos");
        Numerico(p, "Seducción", "seduction"); Numerico(p, "Iluminación", "enlightenment"); Numerico(p, "Insensibilidad", "numbness");
        Seccion(p, "Protagonista");
        Numerico(p, "Estado mental", "sanity");
        Numerico(p, "Salud de combate", "health");
        Numerico(p, "Velocidad al correr", "run");
        Numerico(p, "Velocidad al caminar", "walk");
    }

    void ConstruirRecursos()
    {
        const string p = "Recursos";
        Numerico(p, "Puntos de información", "info", "Disponible en 202X. El bloqueo mantiene como mínimo la cantidad aplicada; elegí una cantidad suficiente para pagar el servicio.", [100, 500, 1000]);
        Numerico(p, "Dinero disponible", "money");
    }

    void ConstruirNpc()
    {
        const string p = "NPC";
        var panel = new StackPanel();
        var etiqueta = Suave("Buscar por nombre, número o rol:");
        etiqueta.Margin = new Thickness(0, 0, 0, 4);
        panel.Children.Add(etiqueta);
        buscadorNpc.TextChanged += (_, _) => { firmaNpc = ""; RefrescarNpc(); };
        panel.Children.Add(buscadorNpc);
        listaNpc.SelectionChanged += (_, _) => DetalleNpc();
        panel.Children.Add(listaNpc);
        detalleNpc.SetResourceReference(TextBlock.ForegroundProperty, "TextoSuave");
        panel.Children.Add(detalleNpc);
        var botones = new WrapPanel();
        void Accion(string etiquetaBoton, string accion, Func<int, Task<JsonObject>> llamada, string? ayuda = null)
        {
            var b = BotonAsync(etiquetaBoton, async () => { var id = NpcSeleccionado(); await Operar(() => llamada(id), "Acción confirmada por el complemento."); }, ayuda);
            botones.Children.Add(b);
            controlesNpc.Add(("npc." + accion, b));
        }
        Accion("Ignorar a la protagonista", "ignore", id => Cliente.Npc("ignore", id, activado: true));
        Accion("Dejar de ignorarla", "ignore", id => Cliente.Npc("ignore", id, activado: false));
        Accion("Enviar a su destino", "toDestination", id => Cliente.Npc("toDestination", id), "Usa el método del propio juego");
        Accion("Llevar a la protagonista", "playerToNpc", id => Cliente.Npc("playerToNpc", id), "Sólo si está libre y hay suelo cerca del personaje");
        Accion("Amistad: 100", "friendship", id => Cliente.Npc("friendship", id, 100));
        Accion("Amistad: 0", "friendship", id => Cliente.Npc("friendship", id, 0));
        Accion("Reputación: 100", "reputation", id => Cliente.Npc("reputation", id, 100));
        panel.Children.Add(botones);
        Tarjeta(p, "Personajes cargados en la escena", panel, "Amistad y reputación son datos persistentes: Restaurar todo revierte los valores registrados, pero no deshace eventos.");

        var personalizados = new StackPanel();
        foreach (var (accion, etiquetaValor, inicial) in new[] { ("friendship", "Amistad (0–100)", 50.0), ("reputation", "Reputación (−100–100)", 0.0), ("speed", "Velocidad al caminar (0,1–8)", 1.0) })
        {
            var def = Definiciones.ValorNpc(accion)!;
            personalizados.Children.Add(Suave(etiquetaValor, 13));
            var fila = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
            var (entrada, deslizador) = CampoConDeslizador(def, inicial, 260);
            fila.Children.Add(entrada); fila.Children.Add(deslizador);
            var a = accion;
            var b = BotonAsync("Aplicar", async () => { var v = Validar(def, entrada.Text); var id = NpcSeleccionado(); await Operar(() => Cliente.Npc(a, id, v), "Acción confirmada por el complemento."); });
            fila.Children.Add(b);
            personalizados.Children.Add(fila);
            controlesNpc.Add(("npc." + accion, fila));
        }
        Tarjeta(p, "Valor personalizado del personaje seleccionado", personalizados, "Los límites se comprueban también en el juego. Restaurar todo recupera estos valores.");

        Seccion(p, "Congelar");
        Conmutador(p, "Congelar todos los NPC", "freezeAll", null, RequiereNpc);
        Conmutador(p, "Congelar estudiantes comunes", "freezeStudents", null, RequiereNpc);
        Conmutador(p, "Congelar profesores", "freezeTeachers", null, RequiereNpc);
        Conmutador(p, "Congelar consejo estudiantil", "freezeCouncil", null, RequiereNpc);
    }

    void ConstruirComportamiento()
    {
        const string p = "Comportamiento";
        Seccion(p, "Inteligencia artificial de los NPC");
        Conmutador(p, "Personajes estúpidos", "stupid", "Bloquea visión, reacción a testigos, audición, persecución e intervención. Activalo antes de ser descubierta.", RequiereNpc);
        foreach (var (etiqueta, id) in new[] { ("Desactivar visión de los NPC", "vision"), ("Desactivar audición de los NPC", "hearing"), ("Desactivar reacciones de testigos", "witness"), ("Desactivar intervención de profesores", "teachers"), ("Desactivar intervención del consejo", "council"), ("Desactivar detección de Senpai", "senpai"), ("Desactivar inicio de reportes y alarmas", "reports") })
            Conmutador(p, etiqueta, id, null, RequiereNpc);
        Seccion(p, "Protagonista");
        Conmutador(p, "Mantener estado mental al máximo", "sanity", null, RequiereValor("sanity"));
        Conmutador(p, "Desactivar penalizaciones pendientes de reputación", "noPenalty", null, RequiereValor("reputation"));
    }

    void ConstruirTiempo()
    {
        const string p = "Tiempo";
        Conmutador(p, "Congelar el reloj", "freezeClock", "Detiene el avance del reloj sin congelar el mundo.", RequiereValor("timeSpeed"));
        Numerico(p, "Hora del día", "hour", "Hora decimal: 13,5 equivale a 13:30. Sólo se permite avanzar (7:00–17:54) para no romper eventos; saltar horas puede omitirlos.");
        Numerico(p, "Velocidad del reloj", "timeSpeed", "0 detiene el reloj. Restaurar recupera la velocidad inicial de esta sesión.");
        Numerico(p, "Atmósfera de la escuela", "atmosphere");
    }

    void ConstruirPerfiles()
    {
        const string p = "Perfiles";
        var panel = new StackPanel();
        panel.Children.Add(listaPerfiles);
        panel.Children.Add(Suave("Nombre del perfil:"));
        panel.Children.Add(nombrePerfil);
        var botones = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        botones.Children.Add(Boton("Guardar perfil", GuardarPerfil));
        botones.Children.Add(Boton("Cargar en el editor", CargarPerfil));
        botones.Children.Add(BotonAsync("Aplicar perfil", AplicarPerfil));
        botones.Children.Add(Boton("Eliminar perfil", EliminarPerfil));
        panel.Children.Add(botones);
        listaPerfiles.SelectionChanged += (_, _) => { if (listaPerfiles.SelectedItem is string n) nombrePerfil.Text = n; };
        Tarjeta(p, "Perfiles personalizados", panel, "Guardan los campos, bloqueos e interruptores del editor. Cargar no cambia nada en el juego; Aplicar envía los valores disponibles (la hora nunca se cambia con perfiles).");
    }

    void ConstruirAjustes()
    {
        const string p = "Ajustes";
        var juego = new StackPanel();
        juego.Children.Add(rutaJuego);
        rutaJuego.LostFocus += (_, _) => { ExaminarJuego(); Guardar(); };
        juego.Children.Add(estadoJuego);
        var botones = new WrapPanel();
        botones.Children.Add(Boton("Buscar…", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Yandere Simulator|YandereSimulator.exe", InitialDirectory = Directory.Exists(rutaJuego.Text) ? rutaJuego.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            if (d.ShowDialog(this) == true) { rutaJuego.Text = System.IO.Path.GetDirectoryName(d.FileName)!; ExaminarJuego(); Guardar(); }
        }));
        botones.Children.Add(Boton("Detectar automáticamente", () =>
        {
            var r = Instalacion.Detectar(null) ?? throw new InvalidOperationException("No se encontró el juego en las carpetas habituales. Usá Buscar….");
            rutaJuego.Text = r; ExaminarJuego(); Guardar(); Aviso("Juego encontrado en " + r);
        }));
        botones.Children.Add(Boton("Iniciar juego", () =>
        {
            var ruta = Instalacion.ValidarRuta(rutaJuego.Text);
            if (Process.GetProcessesByName("YandereSimulator").Length > 0) { Aviso("El juego ya está abierto."); return; }
            Process.Start(new ProcessStartInfo(System.IO.Path.Combine(ruta, "YandereSimulator.exe")) { WorkingDirectory = ruta, UseShellExecute = true });
            if (!autoConectar) Conectar();
        }));
        juego.Children.Add(botones);
        Tarjeta(p, "Juego", juego, "Carpeta que contiene YandereSimulator.exe (por defecto, YandereSimLauncher\\YandereSimulator).");

        var complemento = new StackPanel();
        complemento.Children.Add(estadoComplemento);
        var acciones = new WrapPanel();
        acciones.Children.Add(BotonAsync("Instalar complemento", InstalarComplemento));
        acciones.Children.Add(BotonAsync("Actualizar complemento", async () => { var r = rutaJuego.Text; await Task.Run(() => Instalacion.Actualizar(r, Programa.Distribucion)); ExaminarJuego(); Aviso("Complemento actualizado a " + Programa.Version + "."); }, "Reemplaza sólo el complemento de una instalación hecha por el entrenador"));
        acciones.Children.Add(BotonAsync("Desinstalar complemento", async () =>
        {
            if (MessageBox.Show(this, "Se retirarán sólo los archivos que instaló el entrenador (si no fueron modificados). Otros mods, registros y respaldos se conservan. ¿Continuar?", "Desinstalar complemento", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var r = rutaJuego.Text; await Task.Run(() => Instalacion.Desinstalar(r)); ExaminarJuego(); Aviso("Complemento desinstalado. Se conservaron los respaldos.");
        }));
        complemento.Children.Add(acciones);
        Tarjeta(p, "Complemento (BepInEx)", complemento, "Instalación reversible con manifiesto de hashes. Antes de instalar se respaldan partidas, PlayerPrefs y el ensamblado original. Si ya hay BepInEx con otros mods, se pregunta antes de agregar el complemento y nada se sobrescribe.");

        var respaldos = new StackPanel();
        var r2 = new WrapPanel();
        r2.Children.Add(BotonAsync("Respaldar partidas ahora", async () =>
        {
            if (Process.GetProcessesByName("YandereSimulator").Length > 0 && MessageBox.Show(this, "El juego está abierto: si guarda durante la copia el respaldo puede quedar incompleto. ¿Respaldar igualmente?", "Respaldar partidas", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var r = rutaJuego.Text; var destino = await Task.Run(() => Instalacion.Respaldar(r, Programa.Datos, true)); Aviso("Respaldo creado en " + destino);
        }, "Copia SaveData, SaveFiles, PlayerPrefs y el ensamblado del juego"));
        r2.Children.Add(Boton("Abrir carpeta de datos", () => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Programa.Datos }, UseShellExecute = false })));
        respaldos.Children.Add(r2);
        Tarjeta(p, "Respaldos", respaldos, "Los respaldos, la configuración y el registro de errores están en %LOCALAPPDATA%\\YandereUltimate. Hacé un respaldo antes de probar cambios.");

        var apariencia = new StackPanel { Orientation = Orientation.Horizontal };
        temaOscuro.Checked += (_, _) => CambiarTema("oscuro");
        temaClaro.Checked += (_, _) => CambiarTema("claro");
        apariencia.Children.Add(temaOscuro); apariencia.Children.Add(temaClaro);
        Tarjeta(p, "Apariencia", apariencia, "Ctrl+T alterna entre tema oscuro y claro.");

        var compat = new StackPanel();
        compat.Children.Add(infoCompatibilidad);
        compat.Children.Add(listaDesactivadas);
        Tarjeta(p, "Compatibilidad", compat, "Builds verificadas: " + string.Join(", ", Compatibilidad.Builds.Select(b => $"{b.Build} ({b.Fecha})")) + ". Con otra build el complemento comprueba cada referencia y desactiva sólo lo que falte.");

        var atajos = new StackPanel();
        foreach (var (tecla, accion) in new[] { ("Ctrl+1 … Ctrl+8", "Cambiar de pestaña"), ("Ctrl+R", "Restaurar todo"), ("F5", "Conectar / reintentar"), ("Ctrl+D", "Desconectar"), ("Ctrl+F", "Buscar personaje"), ("Ctrl+T", "Cambiar tema"), ("Enter", "Aplicar el valor del campo") })
        {
            var fila = new Grid();
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            fila.ColumnDefinitions.Add(new ColumnDefinition());
            var t = Texto(tecla, 13, FontWeights.SemiBold);
            var a = Suave(accion, 13); Grid.SetColumn(a, 1);
            fila.Children.Add(t); fila.Children.Add(a);
            atajos.Children.Add(fila);
        }
        Tarjeta(p, "Atajos de teclado", atajos);
    }

    void ConstruirAcercaDe()
    {
        const string p = "Acerca de";
        var info = new StackPanel();
        info.Children.Add(Texto("Yandere Simulator Ultimate Trainer", 20, FontWeights.SemiBold));
        info.Children.Add(Suave("Versión " + Programa.Version + " · complemento BepInEx " + Compatibilidad.BepInEx + " · protocolo " + Definiciones.Protocolo, 13));
        var desc = Texto("Entrenador de escritorio con complemento BepInEx/Harmony. Se comunica con el juego por un canal local autenticado restringido a tu usuario. Sin telemetría ni servicios externos.", 13);
        desc.Margin = new Thickness(0, 10, 0, 10);
        info.Children.Add(desc);
        info.Children.Add(Suave("Repositorio:"));
        info.Children.Add(Enlace(Compatibilidad.Repositorio));
        info.Children.Add(Suave("Donaciones:"));
        if (Compatibilidad.EsEnlaceSeguro(Compatibilidad.Donaciones)) info.Children.Add(Enlace(Compatibilidad.Donaciones));
        else info.Children.Add(Texto("No configurado (se define en compatibilidad.json → «donaciones»).", 13));
        Tarjeta(p, "Acerca de", info);
        Tarjeta(p, "Aviso", Texto("Proyecto independiente y no oficial. No está afiliado, respaldado ni aprobado por los desarrolladores de Yandere Simulator. Yandere Simulator y sus marcas pertenecen a sus respectivos titulares. Las dependencias de terceros conservan sus licencias (LICENCIAS.txt). Algunos antivirus marcan los entrenadores como sospechosos por modificar otro programa: consultá la sección de falsos positivos del README.", 13));
    }

    Button Enlace(string url)
    {
        var b = new Button { Content = url, ToolTip = "Abrir en el navegador" };
        b.SetResourceReference(StyleProperty, "Enlace");
        b.Click += (_, _) =>
        {
            try
            {
                if (!Compatibilidad.EsEnlaceSeguro(url)) throw new InvalidOperationException("Enlace no válido.");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception e) { Fallo(e); }
        };
        return b;
    }

    // ================= Conexión =================

    void Conectar()
    {
        autoConectar = true;
        FijarEstado(EstadoConexion.Conectando, "Buscando el juego…");
        sondeo.Start();
        _ = Sondear();
    }

    async Task Desconectar()
    {
        if (estado == EstadoConexion.Conectado)
            try { await Cliente.RestaurarTodo(); } catch (Exception e) { Programa.Registrar(e); }
        autoConectar = false;
        sondeo.Stop();
        instantanea = null; desactivadas = new();
        FijarEstado(EstadoConexion.Desconectado, "Pulsá Conectar (F5) para buscar el juego. Al desconectar se restauró la sesión.");
        RefrescarControles();
    }

    async Task Sondear()
    {
        if (sondeando || !autoConectar || cerrando) return;
        sondeando = true;
        try
        {
            var procesos = Process.GetProcessesByName("YandereSimulator");
            bool abierto = procesos.Length > 0;
            foreach (var pr in procesos) pr.Dispose();
            if (!abierto) { Sin("Juego cerrado. Iniciá el juego con el complemento instalado (Ajustes › Iniciar juego)."); return; }
            if (!File.Exists(Cliente.Endpoint)) { Sin("Juego abierto: esperando que el complemento publique el canal local. Si no aparece, revisá la instalación en Ajustes."); return; }
            Procesar(await Cliente.Instantanea());
        }
        catch (Exception e) { Sin("Sin respuesta del complemento, reintentando: " + e.Message); }
        finally { sondeando = false; }

        void Sin(string motivo) { instantanea = null; desactivadas = new(); FijarEstado(EstadoConexion.Conectando, motivo); RefrescarControles(); }
    }

    void Procesar(JsonObject datos)
    {
        instantanea = datos;
        desactivadas = datos["disabled"]?.AsArray().Where(n => n != null).ToDictionary(n => (string)n!["id"]!, n => (string?)n!["reason"] ?? "") ?? new();
        int? protocolo = (int?)datos["protocol"];
        string ctx = $"Build {datos["build"]}{((string?)datos["buildDate"] is { } f ? " (" + f + ")" : "")} · Unity {datos["unity"]} · {datos["mode"]} · Perfil {datos["profile"]} · Escena: {datos["scene"]}";
        if (protocolo != Definiciones.Protocolo)
            FijarEstado(EstadoConexion.Incompatible, $"El complemento instalado ({(string?)datos["plugin"] ?? "1.0.0"}) no coincide con el entrenador {Programa.Version}. Cerrá el juego y usá Ajustes › Actualizar complemento.");
        else if ((string?)datos["compatibility"] == "incompatible")
            FijarEstado(EstadoConexion.Incompatible, "Ninguna función es compatible con esta build del juego. " + ctx);
        else
            FijarEstado(EstadoConexion.Conectado, ctx);
        RefrescarControles();
    }

    async Task Operar(Func<Task<JsonObject>> operacion, string exito = "Operación confirmada por el complemento.")
    {
        if (estado != EstadoConexion.Conectado) throw new InvalidOperationException("Conectá el entrenador primero.");
        Procesar(await operacion());
        Aviso(exito);
    }

    async Task RestaurarTodo()
    {
        await Operar(Cliente.RestaurarTodo, "Sesión restaurada: bloqueos e interruptores desactivados, NPC descongelados y valores registrados recuperados.");
        foreach (var e in editores.Values) e.BloqueoEditado = false;
    }

    bool Bloqueado(string id) => instantanea?["locks"]?.AsArray().Any(n => (string?)n == id) == true;
    string? Motivo(string funcion) => estado != EstadoConexion.Conectado ? null : desactivadas.TryGetValue(funcion, out var m) ? m : null;

    // ================= Refresco de controles =================

    void RefrescarControles()
    {
        actualizando = true;
        try
        {
            bool conectado = estado == EstadoConexion.Conectado;
            var valores = instantanea?["values"] as JsonObject;
            foreach (var (id, ed) in editores)
            {
                var v = valores?[id];
                var motivo = Motivo("valor." + id);
                bool disponible = conectado && motivo == null && v != null;
                ed.Controles.IsEnabled = disponible;
                if (!conectado) ed.Actual.Text = "Sin conexión";
                else if (motivo != null) ed.Actual.Text = "Desactivada en esta build: " + motivo;
                else if (v == null) ed.Actual.Text = "No disponible en esta escena o modo";
                else
                {
                    double valor = (double)v;
                    ed.Actual.Text = "Actual: " + Formato(valor, ed.Def.Entero) + (id == "hour" ? $" ({(int)valor:00}:{(int)Math.Round((valor - Math.Floor(valor)) * 60) % 60:00})" : "");
                    if (!ed.Inicializado && !ed.Editado) ed.Entrada.Text = Formato(valor, ed.Def.Entero);
                    ed.Inicializado = true;
                    bool activo = Bloqueado(id);
                    if (!ed.BloqueoEditado) ed.Bloqueo.IsChecked = activo;
                    if (activo) ed.Actual.Text += " · no disminuye";
                }
                ed.Actual.SetResourceReference(TextBlock.ForegroundProperty, conectado && motivo != null ? "Advertencia" : "TextoSuave");
            }

            var activos = instantanea?["toggles"]?.AsArray().Select(n => (string?)n).ToHashSet() ?? new HashSet<string?>();
            foreach (var i in interruptores)
            {
                var motivo = Motivo("interruptor." + i.Id);
                var contextual = conectado ? i.Contexto(instantanea) : null;
                bool activo = activos.Contains(i.Id);
                i.Caja.IsEnabled = conectado && motivo == null && (contextual == null || activo);
                i.Caja.IsChecked = activo;
                i.Caja.Content = activo ? "Activado" : "Desactivado";
                i.Motivo.Text = !conectado ? "Sin conexión" : motivo != null ? "Desactivada en esta build: " + motivo : contextual ?? "";
                i.Motivo.Visibility = i.Motivo.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                i.Motivo.SetResourceReference(TextBlock.ForegroundProperty, motivo != null ? "Advertencia" : "TextoSuave");
            }

            foreach (var (funcion, control) in controlesNpc) control.IsEnabled = conectado && Motivo(funcion) == null && Motivo("npc.lista") == null;
            RefrescarNpc();
            RefrescarCompatibilidad();
        }
        finally { actualizando = false; }
    }

    void RefrescarNpc()
    {
        var npcs = instantanea?["npcs"]?.AsArray().Where(n => n != null).Select(n => n!) ?? [];
        var filtro = buscadorNpc.Text.Trim();
        var comparador = CultureInfo.CurrentCulture.CompareInfo;
        var items = npcs.Select(n => new NpcItem((int)n["id"]!, $"{n["id"]} · {n["name"]} · {((bool)n["teacher"]! ? "Profesor" : (bool)n["council"]! ? "Consejo" : "Estudiante")}"))
            .Where(i => filtro.Length == 0 || comparador.IndexOf(i.Texto, filtro, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .ToList();
        var firma = string.Join("|", items.Select(i => i.Texto));
        if (firma != firmaNpc)
        {
            var seleccionado = (listaNpc.SelectedItem as NpcItem)?.Id;
            firmaNpc = firma;
            listaNpc.ItemsSource = items;
            listaNpc.SelectedItem = items.FirstOrDefault(i => i.Id == seleccionado);
        }
        DetalleNpc();
    }

    void DetalleNpc()
    {
        if (listaNpc.SelectedItem is not NpcItem item) { detalleNpc.Text = instantanea == null ? "Sin conexión." : listaNpc.Items.Count == 0 ? "No hay personajes que coincidan." : "Seleccioná un personaje para consultar su estado."; return; }
        var n = instantanea?["npcs"]?.AsArray().FirstOrDefault(x => (int?)x?["id"] == item.Id);
        if (n == null) { detalleNpc.Text = "Ya no está disponible."; return; }
        static string SiNo(JsonNode? b) => (bool?)b == true ? "Sí" : "No";
        detalleNpc.Text = $"Alarma: {Formato((double?)n["alarm"] ?? 0, false)} · Persiguiendo: {SiNo(n["chasing"])} · Congelado: {SiNo(n["frozen"])} · Ignora a la protagonista: {SiNo(n["ignored"])}\n" +
                          $"Amistad: {n["friendship"]?.ToString() ?? "—"} · Reputación: {n["reputation"]?.ToString() ?? "—"} · Velocidad: {Formato((double?)n["speed"] ?? 0, false)} · Posición: {Formato((double?)n["x"] ?? 0, false)}, {Formato((double?)n["y"] ?? 0, false)}, {Formato((double?)n["z"] ?? 0, false)}";
    }

    int NpcSeleccionado() => (listaNpc.SelectedItem as NpcItem)?.Id ?? throw new InvalidOperationException("Seleccioná un personaje de la lista.");

    void RefrescarCompatibilidad()
    {
        if (instantanea == null) infoCompatibilidad.Text = "Conectá el juego para ver qué funciones están disponibles en la build cargada.";
        else
        {
            bool verificada = (bool?)instantanea["verified"] == true;
            infoCompatibilidad.Text = $"Build cargada: {instantanea["build"]} · {(verificada ? "verificada" : "NO verificada")} · complemento {(string?)instantanea["plugin"] ?? "1.0.0"}\nSHA-256: {instantanea["hash"]}\n" +
                                      (desactivadas.Count == 0 ? "Todas las funciones están disponibles." : $"Funciones desactivadas ({desactivadas.Count}):");
        }
        var firma = string.Join("|", desactivadas.Keys);
        if ((string?)listaDesactivadas.Tag == firma) return;
        listaDesactivadas.Tag = firma;
        listaDesactivadas.Children.Clear();
        foreach (var n in instantanea?["disabled"]?.AsArray() ?? [])
        {
            var t = Texto($"• {(string?)n?["name"]}: {(string?)n?["reason"]}", 13);
            t.SetResourceReference(TextBlock.ForegroundProperty, "Advertencia");
            listaDesactivadas.Children.Add(t);
        }
    }

    // ================= Ajustes =================

    void ExaminarJuego()
    {
        try
        {
            var e = Instalacion.Examinar(rutaJuego.Text);
            estadoJuego.Text = e.Build != null ? $"Build {e.Build.Build} ({e.Build.Fecha}) · verificada · SHA-256 {e.Hash[..12]}…" : $"Build desconocida (no verificada) · SHA-256 {e.Hash[..12]}… · el complemento activará sólo las funciones disponibles.";
            estadoJuego.SetResourceReference(TextBlock.ForegroundProperty, e.Build != null ? "Exito" : "Advertencia");
            estadoComplemento.Text = e.Instalado ? $"Instalado (versión {e.VersionInstalada})." + (e.VersionInstalada != Programa.Version ? " Hay una versión más nueva: usá Actualizar complemento." : "")
                : e.BepInExPresente ? $"No instalado. Hay BepInEx {e.VersionBepInEx ?? "(versión desconocida)"} con {e.OtrosMods.Count} mod(s): se preguntará antes de agregar el complemento."
                : "No instalado.";
        }
        catch (Exception ex)
        {
            estadoJuego.Text = ex.Message;
            estadoJuego.SetResourceReference(TextBlock.ForegroundProperty, "Peligro");
            estadoComplemento.Text = "—";
        }
    }

    async Task InstalarComplemento()
    {
        var ruta = rutaJuego.Text;
        var op = new OpcionesInstalacion();
        while (true)
        {
            try
            {
                var respaldo = await Task.Run(() => Instalacion.Instalar(ruta, Programa.Distribucion, op, Programa.Datos));
                ExaminarJuego();
                Aviso("Complemento instalado. Respaldo previo en " + respaldo + ". Ahora podés iniciar el juego.");
                return;
            }
            catch (RequiereConfirmacionException e)
            {
                if (MessageBox.Show(this, e.Message, "Confirmar instalación", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { Aviso("Instalación cancelada: no se modificó nada."); return; }
                if (e.Motivo == "bepinex") op.UsarBepInExExistente = true; else op.PermitirNoVerificada = true;
            }
        }
    }

    void CambiarTema(string tema)
    {
        if (actualizando || config.Tema == tema) return;
        config.Tema = tema;
        AplicarTema(tema);
        FijarEstado(estado, motivoEstado);
        RefrescarControles();
        Guardar();
    }

    void Guardar()
    {
        config.Instalacion = rutaJuego.Text;
        config.Valores = editores.ToDictionary(e => e.Key, e => e.Value.Entrada.Text);
        config.Guardar();
    }

    // ================= Perfiles =================

    void ActualizarPerfiles() => listaPerfiles.ItemsSource = config.Perfiles.Keys.OrderBy(k => k, StringComparer.CurrentCulture).ToArray();

    void GuardarPerfil()
    {
        var nombre = nombrePerfil.Text.Trim();
        if (nombre.Length == 0) throw new ArgumentException("Escribí un nombre para el perfil.");
        config.Perfiles[nombre] = new Perfil
        {
            Values = editores.ToDictionary(e => e.Key, e => e.Value.Entrada.Text),
            Toggles = interruptores.Where(i => i.Caja.IsChecked == true).Select(i => i.Id).ToList(),
            Locks = editores.Where(e => e.Value.Bloqueo.IsChecked == true).Select(e => e.Key).ToList()
        };
        ActualizarPerfiles(); listaPerfiles.SelectedItem = nombre; Guardar();
        Aviso("Perfil guardado en tu usuario local.");
    }

    Perfil PerfilSeleccionado(out string nombre)
    {
        if (listaPerfiles.SelectedItem is not string n || !config.Perfiles.TryGetValue(n, out var p)) throw new InvalidOperationException("Seleccioná un perfil.");
        nombre = n; return p;
    }

    void CargarPerfil()
    {
        var p = PerfilSeleccionado(out var nombre);
        foreach (var (id, texto) in p.Values) if (editores.TryGetValue(id, out var e)) { e.Entrada.Text = texto; e.Editado = true; }
        nombrePerfil.Text = nombre;
        Aviso("Campos cargados. Pulsá Aplicar perfil para enviarlos al juego.");
    }

    void EliminarPerfil()
    {
        PerfilSeleccionado(out var nombre);
        if (MessageBox.Show(this, $"¿Eliminar el perfil «{nombre}»?", "Perfiles", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        config.Perfiles.Remove(nombre); ActualizarPerfiles(); Guardar();
        Aviso("Perfil eliminado.");
    }

    async Task AplicarPerfil()
    {
        var p = PerfilSeleccionado(out _);
        if (estado != EstadoConexion.Conectado) throw new InvalidOperationException("Conectá el juego antes de aplicar un perfil.");
        int valores = 0, omitidos = 0;
        foreach (var (id, texto) in p.Values)
        {
            var def = Definiciones.Valor(id);
            if (id == "hour" || def == null || instantanea?["values"]?[id] == null || Motivo("valor." + id) != null || !LeerNumero(texto, out var n)) { omitidos++; continue; }
            if (def.Entero) n = Math.Round(n);
            if (def.Validar(n) != null) { omitidos++; continue; }
            await Operar(() => Cliente.Fijar(id, n, p.Locks.Contains(id)));
            valores++;
        }
        foreach (var i in interruptores)
        {
            bool quiere = p.Toggles.Contains(i.Id);
            if (Motivo("interruptor." + i.Id) != null) continue;
            if (quiere != (i.Caja.IsChecked == true)) await Operar(() => Cliente.Interruptor(i.Id, quiere));
        }
        Aviso($"Perfil aplicado: {valores} valores enviados, {omitidos} omitidos (no disponibles, fuera de rango o la hora).");
    }

    // ================= Teclado y cierre =================

    async void Atajos(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ctrl && tecla is >= Key.D1 and <= Key.D8) { pestanas.SelectedIndex = tecla - Key.D1; e.Handled = true; }
        else if (ctrl && tecla is >= Key.NumPad1 and <= Key.NumPad8) { pestanas.SelectedIndex = tecla - Key.NumPad1; e.Handled = true; }
        else if (ctrl && tecla == Key.R) { e.Handled = true; await Ejecutar(botonRestaurar, RestaurarTodo); }
        else if (tecla == Key.F5) { e.Handled = true; Conectar(); }
        else if (ctrl && tecla == Key.D) { e.Handled = true; await Desconectar(); }
        else if (ctrl && tecla == Key.F) { e.Handled = true; pestanas.SelectedIndex = Array.IndexOf(Pestanas, "NPC"); _ = Dispatcher.BeginInvoke(() => { buscadorNpc.Focus(); buscadorNpc.SelectAll(); }, DispatcherPriority.Input); }
        else if (ctrl && tecla == Key.T) { e.Handled = true; (config.Tema == "claro" ? temaOscuro : temaClaro).IsChecked = true; }
    }

    async void AlCerrar(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (cerrando) return;
        e.Cancel = true;
        cerrando = true;
        sondeo.Stop(); guardado.Stop();
        Guardar();
        // Cerrar con funciones activas: se pide al complemento que restaure todo antes de salir.
        if (estado == EstadoConexion.Conectado)
            try { await Cliente.RestaurarTodo(); } catch (Exception ex) { Programa.Registrar(ex); }
        Closing -= AlCerrar;
        Close();
    }
}
