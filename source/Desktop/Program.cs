using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace UltimateTrainer;

internal static class Program
{
    public const string ExpectedHash = "62926A9CC29156037C340E7BD177D88D760D03618FCDFF0C2CD66CCAB78CED09";
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YandereUltimate");
    [STAThread]
    public static void Main(string[] args)
    {
        Directory.CreateDirectory(Data);
        if(args.Length >= 2 && args[0] == "--install") { try { Installation.Install(args[1]); File.WriteAllText(Path.Combine(Data,"install-result.txt"), "Instalado"); } catch(Exception e) { Log(e); File.WriteAllText(Path.Combine(Data,"install-result.txt"),e.ToString()); Environment.ExitCode=1; } return; }
        if(args.Length >= 2 && args[0] == "--uninstall") { try { Installation.Uninstall(args[1]); } catch(Exception e) { Log(e); Environment.ExitCode=1; } return; }
        if(args.Length > 0 && args[0] == "--probe") { try { var result = Bridge.Send(new JsonObject { ["op"]="snapshot" }).GetAwaiter().GetResult(); File.WriteAllText(Path.Combine(Data,"probe.json"), result.ToJsonString(new JsonSerializerOptions { WriteIndented=true })); } catch(Exception e) { Log(e); Environment.ExitCode=1; } return; }
        var app = new Application();
        app.DispatcherUnhandledException += (_,e) => { Log(e.Exception); MessageBox.Show(e.Exception.Message,"No se completó la operación"); e.Handled=true; };
        app.Run(new MainWindow());
    }
    public static void Log(Exception e) { try { File.AppendAllText(Path.Combine(Data,"errores.log"),DateTime.Now.ToString("s")+" "+e+Environment.NewLine); } catch { } }
}

internal static class Bridge
{
    private static readonly SemaphoreSlim Gate = new(1,1);
    public static async Task<JsonObject> Send(JsonObject request)
    {
        await Gate.WaitAsync();
        try
        {
            var endpoint = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Program.Data,"endpoint.json")))!;
            int pid=(int)endpoint["pid"]!;
            using var process=Process.GetProcessById(pid);
            if(process.ProcessName != "YandereSimulator" || process.HasExited) throw new IOException("El proceso del juego ya no está disponible.");
            using var client = new NamedPipeClientStream(".",(string)endpoint["pipe"]!,PipeDirection.InOut,PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            await client.ConnectAsync(2000,timeout.Token);
            request["token"]=(string)endpoint["token"]!;
            using var writer=new StreamWriter(client,leaveOpen:true) { AutoFlush=true };
            using var reader=new StreamReader(client,leaveOpen:true);
            await writer.WriteLineAsync(request.ToJsonString().AsMemory(),timeout.Token);
            var line=await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("El puente cerró la conexión.");
            if(line.Length > 4_000_000) throw new IOException("Respuesta demasiado grande.");
            var response=JsonNode.Parse(line)!.AsObject();
            if((bool?)response["ok"]!=true) throw new InvalidOperationException((string?)response["error"] ?? "Error de comunicación");
            return response["data"]!.AsObject();
        }
        finally { Gate.Release(); }
    }
}

internal static class Installation
{
    const string DefaultGame = @"C:\Users\jansa\Downloads\YandereSimLauncher\YandereSimulator";
    public static string Default => DefaultGame;
    static string Manifest(string game) => Path.Combine(game,"YandereUltimate.install.json");
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static bool Compatible(string game) => File.Exists(Path.Combine(game,"YandereSimulator_Data","Managed","Assembly-CSharp.dll")) && Hash(Path.Combine(game,"YandereSimulator_Data","Managed","Assembly-CSharp.dll"))==Program.ExpectedHash;
    static void RequireClosed() { if(Process.GetProcessesByName("YandereSimulator").Length > 0) throw new InvalidOperationException("Cerrá el juego antes de instalar o desinstalar el complemento."); }
    public static void Install(string game)
    {
        RequireClosed();
        game=Path.GetFullPath(game);
        if(!Compatible(game)) throw new InvalidOperationException("Versión incompatible; se esperaba la build 530 analizada.");
        if(File.Exists(Manifest(game))) throw new InvalidOperationException("El complemento ya está instalado. Desinstalalo antes de actualizarlo.");
        var loader=Path.Combine(AppContext.BaseDirectory,"Loader");
        var plugin=Path.Combine(AppContext.BaseDirectory,"YandereUltimate.Bridge.dll");
        if(!Directory.Exists(loader) || !File.Exists(plugin)) throw new FileNotFoundException("Falta la carpeta Loader o el complemento en la distribución.");
        var files=Directory.GetFiles(loader,"*",SearchOption.AllDirectories).Select(f=>(Source:f,Relative:Path.GetRelativePath(loader,f))).ToList();
        files.Add((plugin,Path.Combine("BepInEx","plugins","YandereUltimate","YandereUltimate.Bridge.dll")));
        foreach(var f in files) if(File.Exists(Path.Combine(game,f.Relative))) throw new IOException("Existe un archivo que no sobrescribiremos: "+f.Relative);
        string backup=Path.Combine(Program.Data,"Respaldos",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.Copy(Path.Combine(game,"YandereSimulator_Data","Managed","Assembly-CSharp.dll"),Path.Combine(backup,"Assembly-CSharp.dll"));
        File.Copy(Path.Combine(game,"GameVersion.txt"),Path.Combine(backup,"GameVersion.txt"));
        var start=new ProcessStartInfo("reg.exe") { CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add("export"); start.ArgumentList.Add(@"HKCU\Software\YandereDev\YandereSimulator"); start.ArgumentList.Add(Path.Combine(backup,"PlayerPrefs.reg")); start.ArgumentList.Add("/y");
        using(var reg=Process.Start(start)!) { reg.WaitForExit(); if(reg.ExitCode!=0) throw new IOException("No se pudo respaldar PlayerPrefs. La instalación fue cancelada."); }
        var installed=new Dictionary<string,string>();
        try
        {
            foreach(var f in files) { var dest=Path.Combine(game,f.Relative); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(f.Source,dest,false); installed[f.Relative]=Hash(dest); }
        }
        finally { File.WriteAllText(Manifest(game),JsonSerializer.Serialize(new { backup,files=installed },new JsonSerializerOptions { WriteIndented=true })); }
    }
    public static void Uninstall(string game)
    {
        RequireClosed();
        game=Path.GetFullPath(game);
        var manifest=JsonNode.Parse(File.ReadAllText(Manifest(game)))!;
        var files=manifest["files"]!.AsObject();
        foreach(var entry in files)
        {
            var target=Path.GetFullPath(Path.Combine(game,entry.Key));
            if(!target.StartsWith(game+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Ruta de manifiesto inválida.");
            if(File.Exists(target) && Hash(target)!=(string)entry.Value!) throw new IOException("Se conserva un archivo modificado; revisalo antes de desinstalar: "+entry.Key);
        }
        foreach(var entry in files) { var target=Path.Combine(game,entry.Key); if(File.Exists(target)) File.Delete(target); }
        File.Copy(Manifest(game),Path.Combine((string)manifest["backup"]!,"desinstalado.json"),true);
        File.Delete(Manifest(game));
        // BepInEx may generate logs/config: preserve those and every unrelated mod.
    }
}

internal sealed class MainWindow : Window
{
    readonly TextBlock status=new(), context=new(), message=new();
    readonly TextBox search=new(), path=new(), profileName=new();
    readonly StackPanel cards=new(), nav=new();
    readonly ComboBox profiles=new();
    readonly ListBox npcList=new();
    readonly TextBlock npcDetails=new();
    readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(900) };
    readonly Dictionary<string,(TextBox Input,TextBlock Live,CheckBox Lock,FrameworkElement Row)> editors=new();
    readonly Dictionary<string,(CheckBox Box,FrameworkElement Row)> switches=new();
    readonly Dictionary<string,string> groups=new();
    readonly HashSet<string> initialized=new(), edited=new();
    readonly HashSet<string> lockEdited=new();
    JsonObject? snapshot;
    bool polling, connecting=true, updating, closing;
    string section="Conexión";
    Dictionary<string,Profile> saved=new();
    readonly Brush foreground=new SolidColorBrush(Color.FromRgb(235,235,241));
    readonly Brush muted=new SolidColorBrush(Color.FromRgb(155,158,174));
    sealed class Profile { public Profile() {} public Dictionary<string,string> Values { get;set; }=new(); public List<string> Toggles {get;set;}=new(); public List<string> Locks {get;set;}=new(); }
    sealed record NpcItem(int Id,string Name) { public override string ToString()=>Name; }

    public MainWindow()
    {
        Title="Yandere Simulator Ultimate Trainer"; Width=1180; Height=810; MinWidth=1040; MinHeight=680;
        Background=new SolidColorBrush(Color.FromRgb(16,17,23)); Foreground=foreground; FontFamily=new FontFamily("Segoe UI"); FontSize=14;
        var style=new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(48,35,43))));
        style.Setters.Add(new Setter(Control.ForegroundProperty,foreground));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(113,48,62))));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(13,8,13,8)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,8,8)));
        Resources[typeof(Button)]=style;
        var textStyle=new Style(typeof(TextBox)); textStyle.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(29,31,41)))); textStyle.Setters.Add(new Setter(Control.ForegroundProperty,foreground)); textStyle.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(65,67,81)))); textStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,6,9,6))); Resources[typeof(TextBox)]=textStyle;
        var checkStyle=new Style(typeof(CheckBox)); checkStyle.Setters.Add(new Setter(Control.ForegroundProperty,foreground)); checkStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center)); Resources[typeof(CheckBox)]=checkStyle;
        var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(218) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); Content=grid;
        var sidebar=new Border { Background=new SolidColorBrush(Color.FromRgb(23,23,32)), Padding=new Thickness(22) }; sidebar.Child=nav; grid.Children.Add(sidebar);
        nav.Children.Add(Text("YANDERE",25,FontWeights.Bold)); nav.Children.Add(new TextBlock { Text="ULTIMATE TRAINER",Foreground=new SolidColorBrush(Color.FromRgb(236,80,106)),Margin=new Thickness(0,3,0,30),FontSize=12 });
        foreach(var name in new[]{"Conexión","Estadísticas","Recursos","Inteligencia artificial","Personajes","Protagonista","Tiempo y escuela","Perfiles"}) nav.Children.Add(Button(name,()=>Navigate(name)));
        nav.Children.Add(new TextBlock { Text="Build 530 · x64 · Mono\nUso local · Sin telemetría",Foreground=muted,Margin=new Thickness(0,30,0,0),FontSize=12 });
        var right=new Grid { Margin=new Thickness(30,22,30,16) }; Grid.SetColumn(right,1); grid.Children.Add(right);
        right.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var heading=new StackPanel(); heading.Children.Add(Text("Tu partida, en tiempo real",27,FontWeights.SemiBold)); status.FontSize=15; status.Foreground=new SolidColorBrush(Color.FromRgb(244,120,138)); status.Margin=new Thickness(0,12,0,5); heading.Children.Add(status); context.Foreground=muted; heading.Children.Add(context); right.Children.Add(heading);
        search.Margin=new Thickness(0,22,0,18); search.ToolTip="Buscar funciones por su nombre"; search.TextChanged+=(_,_)=>Filter(); Grid.SetRow(search,1); right.Children.Add(search);
        var scroll=new ScrollViewer { Content=cards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; Grid.SetRow(scroll,2); right.Children.Add(scroll);
        message.TextWrapping=TextWrapping.Wrap; message.Foreground=muted; message.Margin=new Thickness(0,12,0,0); Grid.SetRow(message,3); right.Children.Add(message);
        BuildCards(); updating=true; LoadSettings(); updating=false; Navigate("Conexión");
        timer.Tick+=async (_,_)=>await Poll(); timer.Start();
        Closing+=async (_,e)=> { if(closing) return; e.Cancel=true; SaveSettings(); timer.Stop(); if(snapshot!=null && connecting) { try { await Bridge.Send(new JsonObject { ["op"]="restore" }); } catch(Exception ex) { Program.Log(ex); } } closing=true; Close(); };
        _=Poll();
    }
    TextBlock Text(string value,double size=14,FontWeight? weight=null)=>new() { Text=value,FontSize=size,FontWeight=weight??FontWeights.Normal,Foreground=foreground,TextWrapping=TextWrapping.Wrap };
    Button Button(string label,Action action) { var b=new Button { Content=label }; b.Click+=(_,_)=> { try { action(); } catch(Exception e) { Fail(e); } }; return b; }
    Button AsyncButton(string label,Func<Task> action) { var b=new Button { Content=label }; b.Click+=async (_,_)=> { try { b.IsEnabled=false; await action(); } catch(Exception e) { Fail(e); } finally { b.IsEnabled=true; } }; return b; }
    void Fail(Exception e) { Program.Log(e); message.Text=e.Message; }
    void Add(string title,string group,FrameworkElement body,string? help=null)
    {
        var stack=new StackPanel(); stack.Children.Add(Text(title,17,FontWeights.SemiBold));
        if(help!=null) stack.Children.Add(new TextBlock { Text=help,Foreground=muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12),FontSize=12 });
        body.Margin=new Thickness(0,12,0,0); stack.Children.Add(body);
        var border=new Border { Background=new SolidColorBrush(Color.FromRgb(25,27,36)),BorderBrush=new SolidColorBrush(Color.FromRgb(40,42,55)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(18),Margin=new Thickness(0,0,0,12),Child=stack,Tag=title };
        groups[title]=group; cards.Children.Add(border);
    }
    void BuildCards()
    {
        var connection=new StackPanel(); path.Text=Installation.Default; path.Margin=new Thickness(0,0,0,12); connection.Children.Add(path);
        var buttons=new WrapPanel();
        buttons.Children.Add(Button("Buscar instalación",()=> { var d=new Microsoft.Win32.OpenFileDialog { Filter="Yandere Simulator|YandereSimulator.exe",InitialDirectory=Directory.Exists(path.Text)?path.Text:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) }; if(d.ShowDialog()==true) { path.Text=Path.GetDirectoryName(d.FileName)!; SaveSettings(); } }));
        buttons.Children.Add(Button("Conectar",()=> { connecting=true; _=Poll(); }));
        buttons.Children.Add(AsyncButton("Desconectar",async()=> { if(snapshot!=null) await Send("restore"); connecting=false; snapshot=null; status.Text="Desconectado"; RefreshControls(); }));
        buttons.Children.Add(Button("Iniciar juego",()=> { if(Process.GetProcessesByName("YandereSimulator").Length==0) Process.Start(new ProcessStartInfo(Path.Combine(path.Text,"YandereSimulator.exe")) { WorkingDirectory=path.Text,UseShellExecute=true }); }));
        connection.Children.Add(buttons);
        var setup=new WrapPanel(); setup.Children.Add(Button("Instalar complemento",()=> { Installation.Install(path.Text); message.Text="Complemento instalado. Ahora podés iniciar el juego."; })); setup.Children.Add(Button("Desinstalar complemento",()=> { Installation.Uninstall(path.Text); message.Text="Componentes añadidos retirados. Respaldos conservados."; })); setup.Children.Add(AsyncButton("Restaurar valores de sesión",async()=>await Send("restore"))); connection.Children.Add(setup);
        Add("Instalación y conexión","Conexión",connection,"Comunicación por canal local autenticado, restringido a tu usuario. Instalación reversible con respaldo previo de PlayerPrefs y del ensamblado original.");
        Add("Compatibilidad comprobada por huella","Conexión",Text("La build 530 usa Unity 6000.2.8f1 y Mono x64. Una actualización del ensamblado bloqueará los cambios hasta revisar la compatibilidad. La disponibilidad depende de la escena y del modo cargados."));
        Numeric("Reputación actual","Estadísticas","reputation",-100,100);
        Numeric("Reputación permanente","Estadísticas","permanentRep",-100,100);
        Numeric("Puntos de estudio disponibles","Estadísticas","study",0,10000,"Abrí la interfaz de clase antes de editar. Estos son los puntos de la sesión, utilizables al asignarlos a materias.");
        var subjects=new[]{("Biología","Biology"),("Química","Chemistry"),("Lenguaje","Language"),("Educación Física","Physical"),("Psicología","Psychology")};
        foreach(var (label,id) in subjects) { Numeric(label+": progreso","Estadísticas",id,0,100); Numeric(label+": nivel","Estadísticas",id+"Grade",0,5); }
        Numeric("Seducción","Estadísticas","seduction",0,5); Numeric("Iluminación","Estadísticas","enlightenment",0,5); Numeric("Insensibilidad","Estadísticas","numbness",0,5);
        Numeric("Puntos de información","Recursos","info",0,100000,"Disponible en 202X. El bloqueo mantiene como mínimo la cantidad aplicada; elegí una cantidad suficiente para pagar el servicio.");
        Numeric("Dinero disponible","Recursos","money",0,1000000);
        Toggle("Personajes estúpidos","Inteligencia artificial","stupid","Bloquea visión, reacción a testigos, audición, persecución y acciones de intervención identificadas en StudentScript. Activá antes de ser descubierta. Pendiente de validación completa en partida.");
        foreach(var (label,id) in new[]{("Desactivar visión de los NPC","vision"),("Desactivar audición de los NPC","hearing"),("Desactivar reacciones de testigos","witness"),("Desactivar intervención de profesores","teachers"),("Desactivar intervención del consejo","council"),("Desactivar detección de Senpai","senpai"),("Desactivar inicio de reportes y alarmas","reports")}) Toggle(label,"Inteligencia artificial",id);
        Toggle("Congelar todos los NPC","Personajes","freezeAll"); Toggle("Congelar estudiantes comunes","Personajes","freezeStudents"); Toggle("Congelar profesores","Personajes","freezeTeachers"); Toggle("Congelar consejo estudiantil","Personajes","freezeCouncil");
        var npcs=new StackPanel(); npcList.Background=new SolidColorBrush(Color.FromRgb(30,32,42)); npcList.Foreground=foreground; npcList.Height=240; npcList.SelectionChanged+=(_,_)=>NpcDetails(); npcs.Children.Add(npcList); npcDetails.Foreground=muted; npcDetails.TextWrapping=TextWrapping.Wrap; npcDetails.Margin=new Thickness(0,10,0,12); npcs.Children.Add(npcDetails);
        var npcButtons=new WrapPanel(); npcButtons.Children.Add(AsyncButton("Ignorar a Ayano",()=>Npc("ignore",true))); npcButtons.Children.Add(AsyncButton("Dejar de ignorarla",()=>Npc("ignore",false))); npcButtons.Children.Add(AsyncButton("Ir a su destino actual",()=>Npc("toDestination"))); npcButtons.Children.Add(AsyncButton("Ayano al personaje",()=>Npc("playerToNpc"))); npcButtons.Children.Add(AsyncButton("Amistad: 100",()=>Npc("friendship",value:100))); npcButtons.Children.Add(AsyncButton("Amistad: 0",()=>Npc("friendship",value:0))); npcButtons.Children.Add(AsyncButton("Reputación: 100",()=>Npc("reputation",value:100))); npcs.Children.Add(npcButtons);
        Add("Personajes cargados en la escena","Personajes",npcs,"El destino usa el método del propio juego. La protagonista sólo se teletransporta si está libre y se encuentra suelo cercano. Amistad y reputación afectan datos persistentes; restaurar sesión revierte los valores registrados en la escena actual, pero no deshace eventos.");
        var npcValues=new StackPanel(); var relationValue=new TextBox { Text="50",Width=120,HorizontalAlignment=HorizontalAlignment.Left }; npcValues.Children.Add(relationValue); var relationsRow=new WrapPanel { Margin=new Thickness(0,12,0,0) }; foreach(var pair in new[]{("Aplicar amistad","friendship"),("Aplicar reputación","reputation"),("Aplicar velocidad de caminar","speed")}) relationsRow.Children.Add(AsyncButton(pair.Item1,async()=> { if(!TryNumber(relationValue.Text,out var number) || !double.IsFinite(number)) throw new ArgumentException("Introducí un número válido."); await Npc(pair.Item2,value:number); })); npcValues.Children.Add(relationsRow); Add("Valor personalizado del personaje seleccionado","Personajes",npcValues,"Amistad: 0–100; reputación: −100–100; velocidad al caminar: 0,1–8. Los límites se comprueban en el juego. Restaurar sesión recupera estos valores mientras se conserve la escena.");
        Numeric("Estado mental","Protagonista","sanity",0,100); Toggle("Mantener estado mental al máximo","Protagonista","sanity"); Numeric("Velocidad al correr","Protagonista","run",1,20); Numeric("Velocidad al caminar","Protagonista","walk",.5,10); Numeric("Salud de combate","Protagonista","health",1,100); Toggle("Desactivar penalizaciones pendientes de reputación","Protagonista","noPenalty");
        Toggle("Congelar el reloj","Tiempo y escuela","freezeClock"); Numeric("Hora del día","Tiempo y escuela","hour",7,17.9,"Hora decimal: 13,5 equivale a 13:30. Sólo se permite avanzar para reducir inconsistencias de eventos; modificar la hora no garantiza que todos los eventos se ejecuten."); Numeric("Velocidad del reloj","Tiempo y escuela","timeSpeed",0,600,"0 congela el avance del reloj. Se conserva el movimiento del mundo. Restaurar recupera la velocidad inicial de esta sesión."); Numeric("Atmósfera de la escuela","Tiempo y escuela","atmosphere",0,1);
        var unsupported=Text("Popularidad independiente: no identificada. Resistencia general, atravesar obstáculos, bloqueo general de derrotas y cambio de día en vivo: no implementados por falta de una ruta estable validada. El modo 1980 usa las clases compartidas detectadas; sus puntos de información se deshabilitan. Las relaciones no restauran automáticamente eventos o consecuencias previas."); unsupported.Foreground=muted; Add("Funciones no compatibles o pendientes","Protagonista",unsupported);
        var profilePanel=new StackPanel(); profiles.MinWidth=200; profiles.Margin=new Thickness(0,0,0,12); profilePanel.Children.Add(profiles); profileName.Text="Mi perfil"; profilePanel.Children.Add(profileName);
        var profileButtons=new WrapPanel { Margin=new Thickness(0,12,0,0) }; profileButtons.Children.Add(Button("Guardar perfil",SaveProfile)); profileButtons.Children.Add(Button("Cargar en el editor",LoadProfile)); profileButtons.Children.Add(AsyncButton("Aplicar perfil cargado",ApplyProfile)); profilePanel.Children.Add(profileButtons);
        Add("Perfiles personalizados","Perfiles",profilePanel,"Se guardan campos y bloqueos del editor. Cargar no activa nada en el juego; Aplicar los envía explícitamente. La configuración se guarda en tu usuario local.");
    }
    void Numeric(string title,string group,string id,double min,double max,string? help=null)
    {
        var panel=new StackPanel(); var live=new TextBlock { Text="Actual: sin conexión",Foreground=muted,Margin=new Thickness(0,0,0,10) }; panel.Children.Add(live);
        var row=new WrapPanel(); var input=new TextBox { Width=115,Text=min.ToString(CultureInfo.CurrentCulture),Margin=new Thickness(0,0,12,8) }; row.Children.Add(input);
        var locked=new CheckBox { Content="No disminuir",Margin=new Thickness(0,6,16,0),ToolTip="Se activa al aplicar el valor; desmarcar elimina el bloqueo inmediatamente." }; row.Children.Add(locked);
        row.Children.Add(AsyncButton("Aplicar",async()=> { if(!TryNumber(input.Text,out double v) || v<min || v>max) throw new ArgumentException($"Introducí un número entre {min} y {max}."); await Send("set",id,v,locked.IsChecked==true); SaveSettings(); }));
        row.Children.Add(AsyncButton("Restaurar",async()=>await Send("restoreValue",id)));
        if(id!="hour" && id!="timeSpeed") row.Children.Add(Button("Máximo",()=>input.Text=max.ToString(CultureInfo.CurrentCulture)));
        if(id=="info") foreach(int points in new[]{100,500,1000}) row.Children.Add(AsyncButton(points.ToString(),async()=> { input.Text=points.ToString(); await Send("set",id,points,locked.IsChecked==true); }));
        panel.Children.Add(row);
        if(max<=600) { var slider=new Slider { Minimum=min,Maximum=max,Width=350,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0) }; slider.ValueChanged+=(_,_)=>input.Text=slider.Value.ToString("0.##",CultureInfo.CurrentCulture); input.LostFocus+=(_,_)=> { if(TryNumber(input.Text,out var v)) slider.Value=Math.Clamp(v,min,max); }; panel.Children.Add(slider); }
        locked.Checked+=(_,_)=> { if(!updating) lockEdited.Add(id); };
        locked.Unchecked+=async(_,_)=> { if(!updating) lockEdited.Add(id); if(!updating && snapshot!=null && snapshot["locks"]!.AsArray().Any(n=>(string?)n==id)) { try { var current=(double?)snapshot["values"]?[id]; if(current!=null) await Send("set",id,current.Value,false); } catch(Exception e) { Fail(e); } } };
        input.TextChanged+=(_,_)=> { if(!updating) edited.Add(id); SaveSettings(); };
        Add(title,group,panel,help); editors[id]=(input,live,locked,(FrameworkElement)cards.Children[cards.Children.Count-1]);
    }
    static bool TryNumber(string text,out double result)=>double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out result) || double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out result);
    void Toggle(string title,string group,string id,string? help=null)
    {
        var check=new CheckBox { Content="Desactivado",FontSize=15 };
        check.Click+=async(_,_)=> { if(updating) return; try { await Send("toggle",id,enabled:check.IsChecked==true); SaveSettings(); } catch(Exception e) { Fail(e); RefreshControls(); } };
        Add(title,group,check,help); switches[id]=(check,(FrameworkElement)cards.Children[cards.Children.Count-1]);
    }
    void Navigate(string value) { section=value; search.Text=""; Filter(); }
    void Filter() { foreach(FrameworkElement card in cards.Children) { string title=(string)card.Tag; card.Visibility=string.IsNullOrWhiteSpace(search.Text) ? (groups[title]==section?Visibility.Visible:Visibility.Collapsed) : (title.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)?Visibility.Visible:Visibility.Collapsed); } }
    async Task<JsonObject> Send(string op,string? id=null,double value=0,bool locked=false,bool enabled=false)
    {
        if(!connecting) throw new InvalidOperationException("Conectá el entrenador primero.");
        var request=new JsonObject { ["op"]=op,["id"]=id,["value"]=value,["locked"]=locked,["enabled"]=enabled };
        var result=await Bridge.Send(request); if(op=="restore") lockEdited.Clear(); else if((op=="set" || op=="restoreValue") && id!=null) lockEdited.Remove(id); snapshot=result; RefreshControls(); message.Text="Operación confirmada por el complemento."; return result;
    }
    async Task Npc(string action,bool enabled=false,double value=0)
    {
        if(!connecting || snapshot==null) throw new InvalidOperationException("El juego no está conectado.");
        if(npcList.SelectedItem is not NpcItem npc) throw new InvalidOperationException("Seleccioná un personaje de la lista.");
        snapshot=await Bridge.Send(new JsonObject { ["op"]="npc",["id"]=action,["npc"]=npc.Id,["enabled"]=enabled,["value"]=value }); RefreshControls(); message.Text="Acción confirmada por el complemento.";
    }
    async Task Poll()
    {
        if(polling || !connecting) return; polling=true;
        try
        {
            var processes=Process.GetProcessesByName("YandereSimulator");
            if(processes.Length==0) { status.Text=File.Exists(Path.Combine(path.Text,"YandereSimulator.exe"))?"Juego encontrado · cerrado":"Juego no encontrado"; snapshot=null; context.Text="Iniciá el juego con el complemento instalado."; RefreshControls(); return; }
            if(!File.Exists(Path.Combine(Program.Data,"endpoint.json"))) { status.Text="Juego encontrado · esperando conexión"; context.Text="El complemento todavía no ha publicado el canal local."; snapshot=null; RefreshControls(); return; }
            snapshot=await Bridge.Send(new JsonObject { ["op"]="snapshot" });
            status.Text=(bool?)snapshot["compatible"]==true?"● Conectado":"Versión incompatible";
            context.Text=$"Build {snapshot["build"]} · Unity {snapshot["unity"]} · {snapshot["mode"]} · Perfil {snapshot["profile"]} · Escena: {snapshot["scene"]}";
            RefreshControls();
        }
        catch(Exception e) { status.Text="Error de comunicación · esperando reconexión"; context.Text=e.Message; snapshot=null; RefreshControls(); }
        finally { polling=false; }
    }
    void RefreshControls()
    {
        updating=true;
        try
        {
            bool compatible=(bool?)snapshot?["compatible"]==true;
            foreach(var (id,editor) in editors)
            {
                var v=snapshot?["values"]?[id]; bool available=compatible && v!=null;
                editor.Row.IsEnabled=available;
                editor.Live.Text=available?$"Actual: {v!.ToString()} · disponible":snapshot==null?"Sin conexión":"No disponible en esta escena o modo";
                if(available) { if(initialized.Add(id) && !edited.Contains(id)) editor.Input.Text=v!.ToString(); bool active=snapshot!["locks"]!.AsArray().Any(n=>(string?)n==id); if(!lockEdited.Contains(id)) editor.Lock.IsChecked=active; if(active) editor.Live.Text+=" · bloqueado"; }
            }
            foreach(var (id,toggle) in switches) { toggle.Row.IsEnabled=compatible && (id=="freezeClock"?snapshot?["values"]?["timeSpeed"]!=null : snapshot?["npcs"]?.AsArray().Count>0 || id=="sanity" && snapshot?["values"]?["sanity"]!=null || id=="noPenalty" && snapshot?["values"]?["reputation"]!=null); bool active=snapshot?["toggles"]?.AsArray().Any(n=>(string?)n==id)==true; toggle.Box.IsChecked=active; toggle.Box.Content=active?"Activado":"Desactivado"; }
            var selected=(npcList.SelectedItem as NpcItem)?.Id;
            var items=snapshot?["npcs"]?.AsArray().Select(n=>new NpcItem((int)n!["id"]!, $"{n["id"]} · {n["name"]} · {((bool)n["teacher"]!?"Profesor":(bool)n["council"]!?"Consejo":"Estudiante")}" )).ToArray()??Array.Empty<NpcItem>();
            npcList.ItemsSource=items; npcList.SelectedItem=items.FirstOrDefault(n=>n.Id==selected); NpcDetails();
        }
        finally { updating=false; }
    }
    void NpcDetails()
    {
        if(npcList.SelectedItem is not NpcItem item) { npcDetails.Text="Seleccioná un personaje para consultar su estado."; return; }
        var n=snapshot?["npcs"]?.AsArray().FirstOrDefault(n=>(int?)n?["id"]==item.Id);
        npcDetails.Text=n==null?"Ya no está disponible":$"Alarma: {n["alarm"]} · Persiguiendo: {((bool)n["chasing"]!?"Sí":"No")} · Congelado: {((bool)n["frozen"]!?"Sí":"No")} · Ignora a Ayano: {((bool)n["ignored"]!?"Sí":"No")}\nAmistad: {n["friendship"]} · Reputación: {n["reputation"]} · Posición: {n["x"]}, {n["y"]}, {n["z"]}";
    }
    void LoadSettings()
    {
        try
        {
            var file=Path.Combine(Program.Data,"config.json"); if(!File.Exists(file)) return;
            var settings=JsonNode.Parse(File.ReadAllText(file))!; path.Text=(string?)settings["installation"]??Installation.Default;
            foreach(var e in editors) if(settings["values"]?[e.Key]!=null) e.Value.Input.Text=(string)settings["values"]![e.Key]!;
            saved=settings["profiles"]?.Deserialize<Dictionary<string,Profile>>()??new(); profiles.ItemsSource=saved.Keys.ToArray();
        }
        catch(Exception e) { Fail(e); }
    }
    void SaveSettings()
    {
        if(editors.Count==0 || string.IsNullOrEmpty(path.Text)) return;
        try { var temp=Path.Combine(Program.Data,"config.tmp"); File.WriteAllText(temp,JsonSerializer.Serialize(new { installation=path.Text,values=editors.ToDictionary(e=>e.Key,e=>e.Value.Input.Text),profiles=saved },new JsonSerializerOptions { WriteIndented=true })); File.Move(temp,Path.Combine(Program.Data,"config.json"),true); } catch(Exception e) { Program.Log(e); }
    }
    void SaveProfile()
    {
        string name=profileName.Text.Trim(); if(name.Length==0) throw new ArgumentException("Escribí un nombre para el perfil.");
        saved[name]=new Profile { Values=editors.ToDictionary(e=>e.Key,e=>e.Value.Input.Text),Toggles=switches.Where(e=>e.Value.Box.IsChecked==true).Select(e=>e.Key).ToList(),Locks=editors.Where(e=>e.Value.Lock.IsChecked==true).Select(e=>e.Key).ToList() }; profiles.ItemsSource=saved.Keys.ToArray(); profiles.SelectedItem=name; SaveSettings(); message.Text="Perfil guardado en tu usuario local.";
    }
    void LoadProfile()
    {
        if(profiles.SelectedItem is not string name || !saved.TryGetValue(name,out var p)) throw new InvalidOperationException("Seleccioná un perfil.");
        foreach(var value in p.Values) if(editors.TryGetValue(value.Key,out var editor)) editor.Input.Text=value.Value;
        profileName.Text=name; message.Text="Campos cargados. Presioná Aplicar perfil cargado para enviarlos al juego.";
    }
    async Task ApplyProfile()
    {
        if(profiles.SelectedItem is not string name || !saved.TryGetValue(name,out var p)) throw new InvalidOperationException("Seleccioná un perfil.");
        if(snapshot==null) throw new InvalidOperationException("Conectá el juego antes de aplicar un perfil.");
        int count=0;
        foreach(var value in p.Values) if(value.Key!="hour" && snapshot?["values"]?[value.Key]!=null && TryNumber(value.Value,out var number)) { await Send("set",value.Key,number,p.Locks.Contains(value.Key)); count++; }
        foreach(var id in switches.Keys) await Send("toggle",id,enabled:p.Toggles.Contains(id));
        message.Text=$"Perfil aplicado: {count} valores disponibles. La hora no se cambia con perfiles.";
    }
}
