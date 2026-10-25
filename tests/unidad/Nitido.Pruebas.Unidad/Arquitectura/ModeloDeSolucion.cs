using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nitido.Pruebas.Unidad.Arquitectura;

internal enum TipoProyecto
{
    Desconocido,
    Host,
    Compartido,
    CompartidoInfraestructura,
    CompartidoApi,
    Modulo,
    Pruebas,
}

internal enum CapaDeModulo
{
    Dominio,
    Aplicacion,
    Contratos,
    Infraestructura,
    Api,
}

/// <summary>Qué es un proyecto según su nombre (y su ruta, cuando se conoce).</summary>
internal readonly record struct Clasificacion(TipoProyecto Tipo, string? Modulo, CapaDeModulo? Capa);

/// <summary>
/// Un proyecto de la solución, tal como lo declara su .csproj y tal como lo usa su ensamblado.
/// </summary>
/// <param name="Nombre">Nombre del proyecto (el del .csproj sin extensión).</param>
/// <param name="Ruta">Ruta del .csproj relativa a la raíz, con «/» como separador.</param>
/// <param name="Referencias">Nombres de los proyectos que declara con ProjectReference.</param>
/// <param name="Paquetes">Paquetes que declara con PackageReference.</param>
/// <param name="Marcos">Marcos que declara con FrameworkReference (o que trae su SDK).</param>
/// <param name="EnsambladosUsados">
/// Nombres que devuelve GetReferencedAssemblies() del ensamblado del proyecto. Es null cuando
/// el ensamblado no se pudo cargar desde la salida de las pruebas.
/// </param>
/// <param name="ErrorDeCarga">
/// Solo cuando <paramref name="EnsambladosUsados"/> es null: tipo y mensaje de la excepción que
/// lanzó la carga (FileLoadException, BadImageFormatException). Es null cuando el ensamblado
/// simplemente no está en la salida de las pruebas, que es el caso de «el host no lo compone».
/// </param>
/// <param name="ProblemasDeLectura">
/// Construcciones del .csproj que la lectura estática no puede seguir (Include con comodín o
/// propiedad, Import, Reference, Compile fuera de la carpeta). Cada una es una infracción.
/// </param>
/// <param name="RutasDeReferencias">
/// Ruta resuelta (relativa a la raíz, con «/») de cada ProjectReference, cuando se leyó de un
/// .csproj. Es null en los modelos que solo dan los nombres: ahí se compara por nombre.
/// </param>
internal sealed record Proyecto(
    string Nombre,
    string Ruta,
    IReadOnlyList<string> Referencias,
    IReadOnlyList<string> Paquetes,
    IReadOnlyList<string> Marcos,
    IReadOnlyList<string>? EnsambladosUsados,
    string? ErrorDeCarga = null,
    IReadOnlyList<ProblemaDeLectura>? ProblemasDeLectura = null,
    IReadOnlyList<RutaDeReferencia>? RutasDeReferencias = null)
{
    public Clasificacion Clasificacion => Clasificador.Clasificar(Nombre, Ruta);

    public TipoProyecto Tipo => Clasificacion.Tipo;

    public string? Modulo => Clasificacion.Modulo;

    public CapaDeModulo? Capa => Clasificacion.Capa;

    public IReadOnlyList<string> Usados => EnsambladosUsados ?? [];

    public IReadOnlyList<ProblemaDeLectura> Problemas => ProblemasDeLectura ?? [];

    /// <summary>Las rutas resueltas con las que el .csproj referencia a <paramref name="referencia"/>.</summary>
    public IReadOnlyList<string> RutasDe(string referencia) =>
        (RutasDeReferencias ?? [])
            .Where(r => r.Nombre == referencia)
            .Select(r => r.Ruta)
            .ToList();
}

/// <summary>Un ProjectReference: el nombre del proyecto destino y la ruta de su .csproj.</summary>
internal sealed record RutaDeReferencia(string Nombre, string Ruta);

/// <summary>Una construcción de MSBuild que la lectura estática no puede seguir.</summary>
/// <param name="Elemento">Lo que se encontró, por ejemplo «ProjectReference Include="$(Algo)"».</param>
/// <param name="Motivo">Por qué es una infracción.</param>
internal sealed record ProblemaDeLectura(string Elemento, string Motivo);

/// <summary>Archivo de MSBuild que se aplica a varios proyectos y las referencias que agrega.</summary>
internal sealed record ArchivoComun(string Ruta, IReadOnlyList<string> ReferenciasAgregadas);

internal sealed record ModeloSolucion(
    IReadOnlyList<Proyecto> Proyectos,
    IReadOnlyList<ArchivoComun> ArchivosComunes);

internal static class Clasificador
{
    private static readonly Regex PatronDeModulo = new(
        @"^Nitido\.(?<modulo>[A-Z][A-Za-z0-9]*)\.(?<capa>Dominio|Aplicacion|Contratos|Infraestructura|Api)$",
        RegexOptions.CultureInvariant);

    public static Clasificacion Clasificar(string nombre, string? ruta = null)
    {
        switch (nombre)
        {
            case "Nitido.Api":
                return new(TipoProyecto.Host, null, null);
            case "Nitido.Compartido":
                return new(TipoProyecto.Compartido, null, null);
            case "Nitido.Compartido.Infraestructura":
                return new(TipoProyecto.CompartidoInfraestructura, null, null);
            case "Nitido.Compartido.Api":
                return new(TipoProyecto.CompartidoApi, null, null);
        }

        // Un proyecto es «de pruebas» solo si cumple las dos cosas a la vez: está bajo tests/ y se
        // llama Nitido.Pruebas.*. Con una sola de las dos NO es de pruebas: sigue las reglas de
        // producción (reflexión incluida) y la convención lo marca por no coincidir. Sin ruta
        // (una referencia por nombre) solo se puede mirar el nombre.
        if (TieneNombreDePruebas(nombre) && (ruta is null || EstaBajoTests(ruta)))
        {
            return new(TipoProyecto.Pruebas, null, null);
        }

        var coincidencia = PatronDeModulo.Match(nombre);
        if (coincidencia.Success && coincidencia.Groups["modulo"].Value != "Compartido")
        {
            return new(
                TipoProyecto.Modulo,
                coincidencia.Groups["modulo"].Value,
                Enum.Parse<CapaDeModulo>(coincidencia.Groups["capa"].Value));
        }

        return new(TipoProyecto.Desconocido, null, null);
    }

    public static bool EstaBajoTests(string ruta) => ruta.StartsWith("tests/", StringComparison.Ordinal);

    public static bool TieneNombreDePruebas(string nombre) =>
        nombre.StartsWith("Nitido.Pruebas.", StringComparison.Ordinal);

    /// <summary>
    /// Verdadero si el proyecto parece de pruebas por cualquiera de las dos señales (ruta o nombre).
    /// Sirve para que ningún proyecto de producción lo referencie, aunque solo cumpla una.
    /// </summary>
    public static bool ParecePruebas(string nombre, string? ruta) =>
        TieneNombreDePruebas(nombre) || (ruta is not null && EstaBajoTests(ruta));

    /// <summary>La ruta que exige la convención, o null cuando el tipo no tiene una ruta fija.</summary>
    public static string? RutaEsperada(string nombre, Clasificacion clasificacion) =>
        clasificacion.Tipo switch
        {
            TipoProyecto.Host
                or TipoProyecto.Compartido
                or TipoProyecto.CompartidoInfraestructura
                or TipoProyecto.CompartidoApi => $"{nombre}/{nombre}.csproj",
            TipoProyecto.Modulo => $"Modulos/{clasificacion.Modulo}/{clasificacion.Capa}/{nombre}.csproj",
            _ => null,
        };
}

/// <summary>
/// Arma el modelo de la solución real: lee Nitido.slnx y cada .csproj (lo declarado) y carga cada
/// ensamblado para leer GetReferencedAssemblies() (lo usado de verdad).
/// </summary>
internal static class ModeloReal
{
    private static readonly Lazy<ModeloSolucion> Modelo = new(Cargar);

    private static readonly string[] NombresDeArchivosComunes =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
    ];

    // Las carpetas de salida y de datos locales que el .gitignore del repositorio también ignora.
    private static readonly string[] CarpetasQueSeIgnoran =
    [
        "bin",
        "obj",
        "node_modules",
        ".git",
        ".vs",
        "TestResults",
        "coverage",
        "almacen",
    ];

    public static ModeloSolucion Obtener() => Modelo.Value;

    /// <summary>Sube desde la carpeta de las pruebas hasta encontrar Nitido.slnx.</summary>
    public static string BuscarRaiz()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null)
        {
            if (File.Exists(Path.Combine(directorio.FullName, "Nitido.slnx")))
            {
                return directorio.FullName;
            }

            directorio = directorio.Parent;
        }

        throw new InvalidOperationException(
            $"No se encontró Nitido.slnx subiendo desde {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Carga un ensamblado por nombre y devuelve los que usa. Si no está en la salida de las pruebas
    /// (FileNotFoundException) devuelve null sin error: el host no lo compone. Si está pero no se
    /// puede cargar (FileLoadException, BadImageFormatException) devuelve null con el tipo y el
    /// mensaje de la excepción, para no confundirlo con lo anterior.
    /// </summary>
    internal static (List<string>? Usados, string? Error) CargarEnsamblado(
        string nombre,
        Func<string, Assembly> cargar)
    {
        try
        {
            var ensamblado = cargar(nombre);
            var usados = ensamblado
                .GetReferencedAssemblies()
                .Select(r => r.Name ?? string.Empty)
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            return (usados, null);
        }
        catch (FileNotFoundException)
        {
            return (null, null);
        }
        catch (Exception ex) when (ex is FileLoadException or BadImageFormatException)
        {
            return (null, $"{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
        }
    }

    private static ModeloSolucion Cargar()
    {
        var raiz = BuscarRaiz();
        var solucion = XDocument.Load(Path.Combine(raiz, "Nitido.slnx"));

        var rutas = solucion
            .Descendants()
            .Where(e => e.Name.LocalName == "Project")
            .Select(e => (string?)e.Attribute("Path"))
            .Where(ruta => !string.IsNullOrWhiteSpace(ruta))
            .Select(ruta => LectorDeMsBuild.Normalizar(ruta!))
            .ToList();

        var proyectos = rutas.Select(ruta => LeerProyecto(raiz, ruta)).ToList();
        return new ModeloSolucion(proyectos, LeerArchivosComunes(raiz));
    }

    private static Proyecto LeerProyecto(string raiz, string ruta)
    {
        var nombre = Path.GetFileNameWithoutExtension(ruta);
        var documento = XDocument.Load(Path.Combine(raiz, ruta.Replace('/', Path.DirectorySeparatorChar)));
        var lectura = LectorDeMsBuild.LeerProyecto(documento);

        // Un proyecto con una sola de las dos señales de pruebas no es de pruebas: se le hace reflexión.
        var clasificacion = Clasificador.Clasificar(nombre, ruta);
        List<string>? usados = [];
        string? error = null;
        if (clasificacion.Tipo != TipoProyecto.Pruebas)
        {
            (usados, error) = CargarEnsamblado(nombre, n => Assembly.Load(new AssemblyName(n)));
        }

        return new Proyecto(
            nombre,
            ruta,
            lectura.Referencias,
            lectura.Paquetes,
            lectura.Marcos,
            usados,
            error,
            lectura.Problemas,
            LectorDeMsBuild.ResolverRutas(ruta, lectura.IncludesDeReferencias));
    }

    private static List<ArchivoComun> LeerArchivosComunes(string raiz)
    {
        var resultado = new List<ArchivoComun>();
        foreach (var archivo in BuscarArchivosComunes(new DirectoryInfo(raiz)))
        {
            var relativa = LectorDeMsBuild.Normalizar(Path.GetRelativePath(raiz, archivo.FullName));
            resultado.Add(LectorDeMsBuild.LeerArchivoComun(relativa, XDocument.Load(archivo.FullName)));
        }

        return resultado;
    }

    private static IEnumerable<FileInfo> BuscarArchivosComunes(DirectoryInfo carpeta) =>
        BuscarArchivosComunes(carpeta, Listar);

    /// <summary>
    /// Busca los archivos comunes de MSBuild bajo la carpeta, sin entrar a las carpetas ignoradas.
    /// Una carpeta que no se puede leer (UnauthorizedAccessException, DirectoryNotFoundException,
    /// por ejemplo una que desaparece mientras se recorre) se salta y la búsqueda sigue; cualquier
    /// otro error se propaga.
    /// </summary>
    internal static List<FileInfo> BuscarArchivosComunes(
        DirectoryInfo carpeta,
        Func<DirectoryInfo, (IReadOnlyList<FileInfo> Archivos, IReadOnlyList<DirectoryInfo> Carpetas)> listar)
    {
        var encontrados = new List<FileInfo>();
        Recorrer(carpeta, listar, encontrados);
        return encontrados;
    }

    private static void Recorrer(
        DirectoryInfo carpeta,
        Func<DirectoryInfo, (IReadOnlyList<FileInfo> Archivos, IReadOnlyList<DirectoryInfo> Carpetas)> listar,
        List<FileInfo> encontrados)
    {
        IReadOnlyList<FileInfo> archivos;
        IReadOnlyList<DirectoryInfo> hijas;
        try
        {
            (archivos, hijas) = listar(carpeta);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return;
        }

        encontrados.AddRange(archivos.Where(a => NombresDeArchivosComunes.Contains(a.Name)));

        foreach (var hija in hijas.Where(h => !CarpetasQueSeIgnoran.Contains(h.Name)))
        {
            Recorrer(hija, listar, encontrados);
        }
    }

    private static (IReadOnlyList<FileInfo> Archivos, IReadOnlyList<DirectoryInfo> Carpetas) Listar(
        DirectoryInfo carpeta) =>
        (carpeta.GetFiles(), carpeta.GetDirectories());
}

/// <param name="IncludesDeReferencias">
/// Cada ProjectReference tal como se escribió (con «/»), sin resolver contra la carpeta del proyecto.
/// </param>
internal sealed record LecturaDeCsproj(
    IReadOnlyList<string> Referencias,
    IReadOnlyList<string> Paquetes,
    IReadOnlyList<string> Marcos,
    IReadOnlyList<ProblemaDeLectura> Problemas,
    IReadOnlyList<RutaDeReferencia> IncludesDeReferencias);

/// <summary>
/// Lectura estática de un .csproj y de los archivos comunes de MSBuild, sin evaluar MSBuild. Todo
/// lo que no se puede leer sin evaluar (comodines, propiedades, Import, Reference) se devuelve como
/// problema en vez de ignorarse, porque ignorarlo dejaría pasar una referencia que las reglas no ven.
/// MSBuild no distingue mayúsculas en los nombres de elemento: aquí tampoco.
/// </summary>
internal static partial class LectorDeMsBuild
{
    private static readonly string[] ElementosDeReferencia =
    [
        "ProjectReference",
        "PackageReference",
        "FrameworkReference",
    ];

    private static readonly string[] ElementosQueAgreganReferencias =
    [
        "PackageReference",
        "ProjectReference",
        "FrameworkReference",
        "GlobalPackageReference",
    ];

    private static readonly string[] ElementosConocidos =
    [
        .. ElementosQueAgreganReferencias,
        "Import",
        "Reference",
        "Compile",
        "Sdk",
    ];

    /// <summary>Los .csproj usan «\» en Include; CI corre en Linux. Se compara siempre con «/».</summary>
    public static string Normalizar(string ruta) => ruta.Replace('\\', '/');

    /// <summary>
    /// Resuelve cada ProjectReference contra la carpeta del proyecto y devuelve la ruta relativa a la
    /// raíz del repositorio. Si la ruta sale de la raíz queda con «..» al principio, que no coincide
    /// con ningún proyecto de la solución.
    /// </summary>
    public static IReadOnlyList<RutaDeReferencia> ResolverRutas(
        string rutaDelProyecto,
        IReadOnlyList<RutaDeReferencia> includes) =>
        [.. includes.Select(i => new RutaDeReferencia(i.Nombre, Resolver(rutaDelProyecto, i.Ruta)))];

    private static string Resolver(string rutaDelProyecto, string include)
    {
        var ruta = Normalizar(include);
        if (ruta.StartsWith('/') || RutaConUnidad().IsMatch(ruta))
        {
            return ruta;
        }

        var piezas = new List<string>();
        var carpeta = Normalizar(rutaDelProyecto);
        var corte = carpeta.LastIndexOf('/');
        if (corte >= 0)
        {
            piezas.AddRange(carpeta[..corte].Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var pieza in ruta.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (pieza == ".")
            {
                continue;
            }

            if (pieza == ".." && piezas.Count > 0 && piezas[^1] != "..")
            {
                piezas.RemoveAt(piezas.Count - 1);
            }
            else
            {
                piezas.Add(pieza);
            }
        }

        return string.Join('/', piezas);
    }

    public static LecturaDeCsproj LeerProyecto(XDocument documento)
    {
        var referencias = new List<string>();
        var paquetes = new List<string>();
        var marcos = new List<string>();
        var problemas = new List<ProblemaDeLectura>();
        var includes = new List<RutaDeReferencia>();

        foreach (var elemento in documento.Descendants())
        {
            var tipo = NombreDeElemento(elemento);
            var include = Atributo(elemento, "Include");

            if (ElementosDeReferencia.Contains(tipo) && !string.IsNullOrWhiteSpace(include))
            {
                // Include admite varios valores separados por «;». Un valor con comodín o propiedad
                // no se puede resolver sin evaluar MSBuild: es una infracción, no un valor.
                foreach (var valor in Separar(include))
                {
                    if (EsDinamico(valor))
                    {
                        problemas.Add(new($"{tipo} Include=\"{valor}\"", MotivoDeIncludeDinamico));
                        continue;
                    }

                    switch (tipo)
                    {
                        case "ProjectReference":
                            var nombre = Path.GetFileNameWithoutExtension(Normalizar(valor));
                            referencias.Add(nombre);
                            includes.Add(new RutaDeReferencia(nombre, Normalizar(valor)));
                            break;
                        case "PackageReference":
                            paquetes.Add(valor);
                            break;
                        default:
                            marcos.Add(valor);
                            break;
                    }
                }
            }
            else if (tipo == "Import")
            {
                problemas.Add(new(
                    $"Import Project=\"{Atributo(elemento, "Project")}\"",
                    "un .csproj no importa otros archivos de MSBuild: lo que traigan se saltaría la lectura estática"));
            }
            else if (tipo == "Reference")
            {
                problemas.Add(new(
                    $"Reference Include=\"{include}\"",
                    "un .csproj no usa Reference a ensamblados sueltos: se saltaría la lectura estática; "
                    + "las dependencias van con ProjectReference o PackageReference"));
            }
            else if (tipo == "Compile" && !string.IsNullOrWhiteSpace(include))
            {
                foreach (var valor in Separar(include).Where(SaleDeLaCarpeta))
                {
                    problemas.Add(new(
                        $"Compile Include=\"{valor}\"",
                        "un .csproj solo compila archivos de su propia carpeta: el código de otra carpeta "
                        + "o módulo se saltaría las reglas de capas"));
                }
            }
        }

        // El SDK Web trae ASP.NET Core sin que el .csproj lo declare con FrameworkReference.
        if (NombresDeSdk(documento).Any(TraeAspNetCore) && !marcos.Contains("Microsoft.AspNetCore.App"))
        {
            marcos.Add("Microsoft.AspNetCore.App");
        }

        return new LecturaDeCsproj(referencias, paquetes, marcos, problemas, includes);
    }

    /// <summary>
    /// Lee un archivo común de MSBuild. <paramref name="ruta"/> es relativa a la raíz del repositorio:
    /// la cadena estándar de GetPathOfFileAbove solo se acepta en un archivo anidado, porque en el
    /// de la raíz importaría el Directory.Build.* de la carpeta de arriba, que ya está fuera del repositorio.
    /// </summary>
    public static ArchivoComun LeerArchivoComun(string ruta, XDocument documento)
    {
        var rutaNormalizada = Normalizar(ruta);
        var nombreDelArchivo = Path.GetFileName(rutaNormalizada);
        var estaAnidado = rutaNormalizada.Contains('/', StringComparison.Ordinal);
        var agregadas = new List<string>();

        foreach (var elemento in documento.Descendants())
        {
            var tipo = NombreDeElemento(elemento);
            var include = Atributo(elemento, "Include");

            if (ElementosQueAgreganReferencias.Contains(tipo) && include is not null)
            {
                agregadas.Add($"{tipo} {include}");
            }
            else if (tipo == "Reference")
            {
                agregadas.Add($"Reference {include}");
            }
            else if (tipo == "Import"
                && !(estaAnidado && EsCadenaEstandarDeDirectoryBuild(elemento, nombreDelArchivo)))
            {
                agregadas.Add($"Import {Atributo(elemento, "Project")}");
            }
        }

        return new ArchivoComun(ruta, agregadas);
    }

    private const string MotivoDeIncludeDinamico =
        "un Include con comodín o propiedad de MSBuild no se puede leer sin evaluarlo, "
        + "y la lectura estática no sabría qué referencia agrega";

    /// <summary>El nombre del elemento, en su forma canónica si es uno de los conocidos (sin importar mayúsculas).</summary>
    private static string NombreDeElemento(XElement elemento)
    {
        var nombre = elemento.Name.LocalName;
        return ElementosConocidos.FirstOrDefault(c => string.Equals(c, nombre, StringComparison.OrdinalIgnoreCase))
            ?? nombre;
    }

    /// <summary>El valor de un atributo buscado sin importar mayúsculas ni espacio de nombres.</summary>
    private static string? Atributo(XElement elemento, string nombre) =>
        elemento
            .Attributes()
            .FirstOrDefault(a => !a.IsNamespaceDeclaration
                && string.Equals(a.Name.LocalName, nombre, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static string[] Separar(string valor) =>
        valor.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool EsDinamico(string valor) =>
        valor.IndexOfAny(['*', '?']) >= 0
        || valor.Contains("$(", StringComparison.Ordinal)
        || valor.Contains("@(", StringComparison.Ordinal)
        || valor.Contains("%(", StringComparison.Ordinal);

    /// <summary>
    /// Un Compile sale de la carpeta si su ruta es absoluta, sube con «..» o usa una propiedad, lista
    /// o metadato de MSBuild que no se puede resolver sin evaluar. Las únicas propiedades aceptadas
    /// son $(MSBuildThisFileDirectory) y $(MSBuildProjectDirectory) al comienzo, y solo si lo que
    /// sigue no sube con «..».
    /// </summary>
    private static bool SaleDeLaCarpeta(string valor)
    {
        var ruta = PropiedadDeLaCarpeta().Replace(Normalizar(valor), string.Empty);
        return ruta.StartsWith('/')
            || RutaConUnidad().IsMatch(ruta)
            || SubidaDeCarpeta().IsMatch(ruta)
            || ruta.Contains("$(", StringComparison.Ordinal)
            || ruta.Contains("@(", StringComparison.Ordinal)
            || ruta.Contains("%(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Nombres de SDK declarados de cualquier forma: Project Sdk="A;B/1.0", &lt;Sdk Name="A" /&gt; o
    /// &lt;Import Sdk="A" /&gt;. Se quita la versión.
    /// </summary>
    private static IEnumerable<string> NombresDeSdk(XDocument documento)
    {
        var declarados = new List<string?> { documento.Root is null ? null : Atributo(documento.Root, "Sdk") };
        foreach (var elemento in documento.Descendants())
        {
            switch (NombreDeElemento(elemento))
            {
                case "Sdk":
                    declarados.Add(Atributo(elemento, "Name"));
                    break;
                case "Import":
                    declarados.Add(Atributo(elemento, "Sdk"));
                    break;
            }
        }

        return declarados
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .SelectMany(d => Separar(d!))
            .Select(d => d.Split('/')[0].Trim());
    }

    // En el SDK 10 solo Microsoft.NET.Sdk.Web agrega Microsoft.AspNetCore.App sin FrameworkReference.
    // Microsoft.NET.Sdk.Razor no trae el marco por sí solo; se trata como web por precaución, para que
    // un proyecto Razor no pase por una biblioteca sin ASP.NET Core.
    private static bool TraeAspNetCore(string sdk) =>
        sdk.EndsWith(".Web", StringComparison.OrdinalIgnoreCase)
        || sdk.Equals("Microsoft.NET.Sdk.Razor", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Única importación aceptada en un archivo común anidado: la cadena estándar que hereda el
    /// Directory.Build.props o .targets de la carpeta de arriba, escrita en un archivo del mismo nombre.
    /// </summary>
    private static bool EsCadenaEstandarDeDirectoryBuild(XElement importacion, string nombreDelArchivo)
    {
        if (Atributo(importacion, "Sdk") is not null)
        {
            return false;
        }

        var coincidencia = CadenaEstandar().Match(Atributo(importacion, "Project") ?? string.Empty);
        return coincidencia.Success
            && coincidencia.Groups["archivo"].Value == nombreDelArchivo;
    }

    [GeneratedRegex(@"^\$\(\[MSBuild\]::GetPathOfFileAbove\(\s*'(?<archivo>Directory\.Build\.(?:props|targets))'\s*,\s*'\$\(MSBuildThisFileDirectory\)\.\.[/\\]?'\s*\)\)$")]
    private static partial Regex CadenaEstandar();

    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex RutaConUnidad();

    [GeneratedRegex(@"(^|[/)])\.\.(/|$)")]
    private static partial Regex SubidaDeCarpeta();

    [GeneratedRegex(@"^\$\((MSBuildThisFileDirectory|MSBuildProjectDirectory)\)[/\\]?", RegexOptions.IgnoreCase)]
    private static partial Regex PropiedadDeLaCarpeta();
}
