namespace Nitido.Pruebas.Unidad.Arquitectura;

/// <summary>
/// Las reglas de dependencia de docs/arquitectura-backend.md (secciones 1 y 5.3), escritas como
/// funciones puras sobre un <see cref="ModeloSolucion"/>. Cada una devuelve la lista de
/// infracciones; vacía significa que se cumple. Las mismas funciones se prueban con modelos
/// sintéticos y con la solución real.
/// </summary>
internal static class ReglasDeArquitectura
{
    public const string Convencion = "Solucion_ProyectosSiguenConvencionDeNombresYCarpetas";
    public const string Compartido = "Compartido_SoloDependeDelFramework";
    public const string Dominio = "Dominio_SoloDependeDeCompartido";
    public const string Contratos = "Contratos_SoloDependenDeCompartido";
    public const string Aplicacion = "Aplicacion_NoDependeDeInfraestructuraNiWeb";
    public const string Api = "Api_NoReferenciaInfraestructura";
    public const string Infraestructura = "Infraestructura_NoReferenciaApi";
    public const string Modulos = "Modulos_SoloSeReferencianPorContratos";
    public const string SoloElHost = "InfraestructuraYApi_SoloLasReferenciaElHost";
    public const string Host = "Host_ComponeTodosLosModulos";
    public const string ArchivosComunes = "ArchivosComunesDeMsBuild_NoAgreganReferencias";
    public const string SdkDeNube = "SdkDeNube_SoloEnProyectosDeProveedor";

    /// <summary>
    /// Proyectos de proveedor autorizados a usar un SDK de nube. Arranca vacía y no se afloja sin
    /// un ADR de despliegue: hoy ningún ensamblado Nitido.* puede referenciar un SDK de nube.
    /// </summary>
    public static readonly IReadOnlyList<string> ProveedoresDeNubeAutorizados = [];

    private static readonly string[] PrefijosDeSdkDeNube =
    [
        "Azure.",
        "Amazon",
        "AWSSDK",
        "Google.Cloud",
    ];

    private static readonly string[] PrefijosDeEntityFramework = ["Microsoft.EntityFrameworkCore"];

    private static readonly string[] PrefijosDeTecnologiaProhibidosEnAplicacion =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
        "Microsoft.AspNetCore",
        "Serilog",
        "Hangfire",
    ];

    private static readonly string[] PaquetesPermitidosEnAplicacion =
    [
        "FluentValidation",
        "Microsoft.Extensions.Logging.Abstractions",
    ];

    /// <summary>Todas las reglas por el nombre de la prueba que las aplica a la solución real.</summary>
    public static readonly IReadOnlyDictionary<string, Func<ModeloSolucion, IReadOnlyList<string>>> Todas =
        new Dictionary<string, Func<ModeloSolucion, IReadOnlyList<string>>>
        {
            [Convencion] = ProyectosSiguenLaConvencion,
            [Compartido] = CompartidoSoloDependeDelFramework,
            [Dominio] = DominioSoloDependeDeCompartido,
            [Contratos] = ContratosSoloDependenDeCompartido,
            [Aplicacion] = AplicacionNoDependeDeInfraestructuraNiWeb,
            [Api] = ApiNoReferenciaInfraestructura,
            [Infraestructura] = InfraestructuraNoReferenciaApi,
            [Modulos] = ModulosSoloSeReferencianPorContratos,
            [SoloElHost] = InfraestructuraYApiSoloLasReferenciaElHost,
            [Host] = HostComponeTodosLosModulos,
            [ArchivosComunes] = ArchivosComunesNoAgreganReferencias,
            [SdkDeNube] = modelo => SdkDeNubeSoloEnProyectosDeProveedor(modelo, ProveedoresDeNubeAutorizados),
        };

    /// <summary>Devuelve los nombres que empiezan con el prefijo de un SDK de nube.</summary>
    public static List<string> BuscarSdkDeNube(IEnumerable<string> nombres) =>
        nombres
            .Where(n => EmpiezaCon(n, PrefijosDeSdkDeNube))
            .ToList();

    // ---------------------------------------------------------------------------------------
    // Reglas
    // ---------------------------------------------------------------------------------------

    public static IReadOnlyList<string> ProyectosSiguenLaConvencion(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in modelo.Proyectos)
        {
            var clasificacion = proyecto.Clasificacion;

            // Un proyecto es de pruebas solo con las dos señales a la vez (ruta bajo tests/ y nombre
            // Nitido.Pruebas.*). Con una sola es una infracción, porque de lo contrario quedaría fuera
            // de las reglas de producción (sin reflexión ni reglas de capa) por solo cambiarle la ruta o el nombre.
            var bajoTests = Clasificador.EstaBajoTests(proyecto.Ruta);
            var nombreDePruebas = Clasificador.TieneNombreDePruebas(proyecto.Nombre);
            var desalineado = bajoTests != nombreDePruebas;
            if (desalineado)
            {
                infracciones.Add(
                    $"{proyecto.Nombre} ({proyecto.Ruta}): un proyecto de pruebas debe estar bajo tests/ y "
                    + "llamarse Nitido.Pruebas.<Tipo> a la vez; este cumple "
                    + (bajoTests ? "solo la ruta (tests/)." : "solo el nombre (Nitido.Pruebas.*)."));
            }

            switch (clasificacion.Tipo)
            {
                case TipoProyecto.Desconocido:
                    if (!desalineado)
                    {
                        infracciones.Add(
                            $"{proyecto.Nombre} ({proyecto.Ruta}): no es el host, un proyecto compartido, "
                            + "uno de tests/ ni Modulos/<Modulo>/<Capa>/Nitido.<Modulo>.<Capa>.csproj "
                            + "con Capa en Dominio, Aplicacion, Contratos, Infraestructura o Api.");
                    }

                    break;
                case TipoProyecto.Pruebas:
                    if (!proyecto.Ruta.EndsWith($"/{proyecto.Nombre}.csproj", StringComparison.Ordinal))
                    {
                        infracciones.Add(
                            $"{proyecto.Nombre} ({proyecto.Ruta}): un proyecto de pruebas debe estar en "
                            + $"tests/<tipo>/{proyecto.Nombre}/{proyecto.Nombre}.csproj.");
                    }

                    break;
                default:
                    var esperada = Clasificador.RutaEsperada(proyecto.Nombre, clasificacion);
                    if (esperada is not null && proyecto.Ruta != esperada)
                    {
                        infracciones.Add(
                            $"{proyecto.Nombre} ({proyecto.Ruta}): la ruta debe ser {esperada}.");
                    }

                    break;
            }

            if (clasificacion.Tipo != TipoProyecto.Pruebas)
            {
                foreach (var referencia in proyecto.Referencias.Distinct().Where(r => EsDePruebas(modelo, proyecto, r)))
                {
                    infracciones.Add(
                        $"{proyecto.Nombre} -> ProjectReference {referencia}: "
                        + "ningún proyecto de producción referencia pruebas.");
                }
            }

            // Un proyecto que no está en Nitido.slnx queda fuera de todas las reglas aunque otro lo
            // referencie y lo cargue en el host. Toda ProjectReference, de cualquier proyecto, debe
            // apuntar a uno que el modelo conozca.
            foreach (var referencia in proyecto.Referencias.Distinct().Where(r => !EstaEnLaSolucion(modelo, proyecto, r)))
            {
                infracciones.Add(Infraccion(
                    proyecto,
                    $"ProjectReference {referencia}",
                    "no está en Nitido.slnx; las reglas no pueden revisarlo"));
            }

            // Lo que la lectura estática no puede seguir (Include con comodín o propiedad, Import,
            // Reference, Compile fuera de la carpeta) también es del .csproj, así que vive aquí.
            // Los archivos comunes de MSBuild se revisan en ArchivosComunesNoAgreganReferencias.
            foreach (var problema in proyecto.Problemas)
            {
                infracciones.Add(Infraccion(proyecto, problema.Elemento, problema.Motivo));
            }
        }

        // Sin estos dos, las demás reglas pasarían en vacío.
        foreach (var obligatorio in new[] { "Nitido.Api", "Nitido.Compartido" })
        {
            if (modelo.Proyectos.All(p => p.Nombre != obligatorio))
            {
                infracciones.Add(
                    $"{obligatorio}: no está en Nitido.slnx. Sin él las pruebas de arquitectura pasarían en vacío.");
            }
        }

        return infracciones;
    }

    public static IReadOnlyList<string> CompartidoSoloDependeDelFramework(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in modelo.Proyectos.Where(p => p.Tipo == TipoProyecto.Compartido))
        {
            VerificarDeclarado(
                infracciones,
                proyecto,
                proyectoPermitido: _ => false,
                paquetePermitido: _ => false,
                marcosPermitidos: false,
                quien: "Nitido.Compartido no declara proyectos, paquetes ni marcos");

            foreach (var usado in proyecto.Usados.Where(u => !EsLibreriaBase(u)))
            {
                infracciones.Add(Infraccion(
                    proyecto,
                    $"ensamblado {usado}",
                    "Nitido.Compartido solo puede usar System.*, netstandard o mscorlib"));
            }
        }

        return infracciones;
    }

    public static IReadOnlyList<string> DominioSoloDependeDeCompartido(ModeloSolucion modelo) =>
        SoloDependenDeCompartido(modelo, CapaDeModulo.Dominio);

    public static IReadOnlyList<string> ContratosSoloDependenDeCompartido(ModeloSolucion modelo) =>
        SoloDependenDeCompartido(modelo, CapaDeModulo.Contratos);

    public static IReadOnlyList<string> AplicacionNoDependeDeInfraestructuraNiWeb(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in DeCapa(modelo, CapaDeModulo.Aplicacion))
        {
            VerificarDeclarado(
                infracciones,
                proyecto,
                proyectoPermitido: nombre => EsPermitidoEnAplicacion(proyecto, nombre),
                paquetePermitido: paquete => PaquetesPermitidosEnAplicacion.Contains(paquete),
                marcosPermitidos: false,
                quien: "Aplicacion solo ve su Dominio, los Contratos, Nitido.Compartido, FluentValidation "
                    + "y Microsoft.Extensions.Logging.Abstractions");

            foreach (var usado in proyecto.Usados)
            {
                if (EmpiezaCon(usado, PrefijosDeTecnologiaProhibidosEnAplicacion)
                    || EmpiezaCon(usado, PrefijosDeSdkDeNube))
                {
                    infracciones.Add(Infraccion(
                        proyecto,
                        $"ensamblado {usado}",
                        "Aplicacion no usa EF Core, SqlClient, ASP.NET Core, Serilog, Hangfire ni SDK de nube"));
                }
                else if (usado.StartsWith("Nitido.", StringComparison.Ordinal)
                    && !EsPermitidoEnAplicacion(proyecto, usado))
                {
                    infracciones.Add(Infraccion(
                        proyecto,
                        $"ensamblado {usado}",
                        "Aplicacion solo usa su Dominio, los Contratos y Nitido.Compartido"));
                }
            }
        }

        return infracciones;
    }

    public static IReadOnlyList<string> ApiNoReferenciaInfraestructura(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in DeCapa(modelo, CapaDeModulo.Api))
        {
            foreach (var referencia in proyecto.Referencias)
            {
                var clasificacion = Clasificador.Clasificar(referencia);
                var esSuAplicacion = clasificacion is { Tipo: TipoProyecto.Modulo, Capa: CapaDeModulo.Aplicacion }
                    && clasificacion.Modulo == proyecto.Modulo;
                if (!esSuAplicacion && clasificacion.Tipo != TipoProyecto.CompartidoApi)
                {
                    infracciones.Add(Infraccion(
                        proyecto,
                        $"ProjectReference {referencia}",
                        "Api solo referencia su Aplicacion y Nitido.Compartido.Api"));
                }
            }

            foreach (var paquete in proyecto.Paquetes.Where(p => EmpiezaCon(p, PrefijosDeEntityFramework)))
            {
                infracciones.Add(Infraccion(
                    proyecto,
                    $"PackageReference {paquete}",
                    "Api no declara EF Core"));
            }

            foreach (var usado in proyecto.Usados)
            {
                var clasificacion = Clasificador.Clasificar(usado);
                var esInfraestructura = clasificacion.Tipo == TipoProyecto.CompartidoInfraestructura
                    || clasificacion is { Tipo: TipoProyecto.Modulo, Capa: CapaDeModulo.Infraestructura };
                if (esInfraestructura || EmpiezaCon(usado, PrefijosDeEntityFramework))
                {
                    infracciones.Add(Infraccion(
                        proyecto,
                        $"ensamblado {usado}",
                        "Api no usa Infraestructura ni EF Core"));
                }
            }
        }

        // F0-02b (diseño 1.4): la tabla de docs/arquitectura-backend.md (sección 1) prohíbe EF Core en
        // Nitido.Compartido.Api, y ninguna regla lo revisaba. No es una regla nueva: hace cumplir lo que ya dice la
        // tabla. Sus referencias de proyecto ya las vigila InfraestructuraYApi_SoloLasReferenciaElHost (solo puede
        // depender de Nitido.Compartido); aquí, sus paquetes y lo que su ensamblado usa de verdad.
        foreach (var proyecto in modelo.Proyectos.Where(p => p.Tipo == TipoProyecto.CompartidoApi))
        {
            foreach (var paquete in proyecto.Paquetes.Where(p => EmpiezaCon(p, PrefijosDeEntityFramework)))
            {
                infracciones.Add(Infraccion(
                    proyecto,
                    $"PackageReference {paquete}",
                    "Nitido.Compartido.Api no declara EF Core"));
            }

            foreach (var usado in proyecto.Usados)
            {
                var clasificacion = Clasificador.Clasificar(usado);
                var esInfraestructura = clasificacion.Tipo == TipoProyecto.CompartidoInfraestructura
                    || clasificacion is { Tipo: TipoProyecto.Modulo, Capa: CapaDeModulo.Infraestructura };
                if (esInfraestructura || EmpiezaCon(usado, PrefijosDeEntityFramework))
                {
                    infracciones.Add(Infraccion(
                        proyecto,
                        $"ensamblado {usado}",
                        "Nitido.Compartido.Api no usa Infraestructura ni EF Core"));
                }
            }
        }

        return infracciones;
    }

    public static IReadOnlyList<string> InfraestructuraNoReferenciaApi(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in DeCapa(modelo, CapaDeModulo.Infraestructura))
        {
            foreach (var (nombre, origen) in DependenciasDeNitido(proyecto))
            {
                var clasificacion = Clasificador.Clasificar(nombre);
                string? motivo = null;

                if (clasificacion.Tipo == TipoProyecto.CompartidoApi)
                {
                    motivo = "Infraestructura no referencia Nitido.Compartido.Api";
                }
                else if (clasificacion is { Tipo: TipoProyecto.Modulo, Capa: CapaDeModulo.Api }
                    && clasificacion.Modulo == proyecto.Modulo)
                {
                    motivo = "Infraestructura no referencia el Api de su propio módulo";
                }
                else if (clasificacion is { Tipo: TipoProyecto.Modulo }
                    && clasificacion.Modulo != proyecto.Modulo
                    && clasificacion.Capa != CapaDeModulo.Contratos)
                {
                    motivo = "de otro módulo solo se referencian los Contratos";
                }

                if (motivo is not null)
                {
                    infracciones.Add(Infraccion(proyecto, $"{origen} {nombre}", motivo));
                }
            }
        }

        return infracciones;
    }

    public static IReadOnlyList<string> ModulosSoloSeReferencianPorContratos(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in modelo.Proyectos.Where(p => p.Tipo == TipoProyecto.Modulo))
        {
            foreach (var (nombre, origen) in DependenciasDeNitido(proyecto))
            {
                var clasificacion = Clasificador.Clasificar(nombre);
                if (clasificacion is { Tipo: TipoProyecto.Modulo }
                    && clasificacion.Modulo != proyecto.Modulo
                    && clasificacion.Capa != CapaDeModulo.Contratos)
                {
                    infracciones.Add(Infraccion(
                        proyecto,
                        $"{origen} {nombre}",
                        $"el módulo {proyecto.Modulo} solo puede usar los Contratos del módulo {clasificacion.Modulo}"));
                }
            }
        }

        return infracciones;
    }

    public static IReadOnlyList<string> InfraestructuraYApiSoloLasReferenciaElHost(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in modelo.Proyectos)
        {
            var esHostOPruebas = proyecto.Tipo is TipoProyecto.Host or TipoProyecto.Pruebas;
            var esCompartidoConPuertaCerrada =
                proyecto.Tipo is TipoProyecto.CompartidoInfraestructura or TipoProyecto.CompartidoApi;

            foreach (var (nombre, origen) in DependenciasDeNitido(proyecto))
            {
                var clasificacion = Clasificador.Clasificar(nombre);
                string? motivo = null;

                if (clasificacion is { Tipo: TipoProyecto.Modulo, Capa: CapaDeModulo.Infraestructura or CapaDeModulo.Api }
                    && !esHostOPruebas)
                {
                    motivo = "la Infraestructura y el Api de un módulo solo los referencia el host";
                }
                else if (clasificacion.Tipo == TipoProyecto.CompartidoInfraestructura
                    && !esHostOPruebas
                    && proyecto.Clasificacion is not { Tipo: TipoProyecto.Modulo, Capa: CapaDeModulo.Infraestructura })
                {
                    motivo = "Nitido.Compartido.Infraestructura solo lo referencian las Infraestructura de los módulos y el host";
                }
                else if (clasificacion.Tipo == TipoProyecto.CompartidoApi
                    && !esHostOPruebas
                    && proyecto.Clasificacion is not { Tipo: TipoProyecto.Modulo, Capa: CapaDeModulo.Api })
                {
                    motivo = "Nitido.Compartido.Api solo lo referencian las Api de los módulos y el host";
                }
                else if (esCompartidoConPuertaCerrada && clasificacion.Tipo != TipoProyecto.Compartido)
                {
                    motivo = "los compartidos de Infraestructura y de Api solo pueden depender de Nitido.Compartido";
                }

                if (motivo is not null)
                {
                    infracciones.Add(Infraccion(proyecto, $"{origen} {nombre}", motivo));
                }
            }
        }

        return infracciones.Distinct(StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<string> HostComponeTodosLosModulos(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        var host = modelo.Proyectos.FirstOrDefault(p => p.Tipo == TipoProyecto.Host);
        if (host is null)
        {
            infracciones.Add("Nitido.Api: no hay proyecto host en la solución.");
        }
        else
        {
            var modulos = modelo.Proyectos
                .Where(p => p.Tipo == TipoProyecto.Modulo)
                .Select(p => p.Modulo!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);

            foreach (var modulo in modulos)
            {
                foreach (var capa in new[] { CapaDeModulo.Infraestructura, CapaDeModulo.Api })
                {
                    var nombre = $"Nitido.{modulo}.{capa}";
                    if (!host.Referencias.Contains(nombre))
                    {
                        var existe = modelo.Proyectos.Any(p => p.Nombre == nombre);
                        infracciones.Add(Infraccion(
                            host,
                            $"ProjectReference {nombre}",
                            existe
                                ? $"el host no compone el módulo {modulo}"
                                : $"el host no compone el módulo {modulo} y {nombre} ni siquiera está en la solución"));
                    }
                }
            }
        }

        // Dos fallas distintas: el ensamblado no está en la salida de las pruebas (el host no lo
        // compone) o está pero no se pudo cargar (FileLoadException, BadImageFormatException).
        foreach (var proyecto in modelo.Proyectos.Where(p => p.Tipo != TipoProyecto.Pruebas))
        {
            if (proyecto.EnsambladosUsados is not null)
            {
                continue;
            }

            infracciones.Add(Infraccion(
                proyecto,
                $"ensamblado {proyecto.Nombre}",
                proyecto.ErrorDeCarga is null
                    ? "no se pudo cargar desde la salida de las pruebas: el host no lo compone"
                    : $"no se pudo cargar {proyecto.Nombre}: {proyecto.ErrorDeCarga}"));
        }

        return infracciones;
    }

    public static IReadOnlyList<string> ArchivosComunesNoAgreganReferencias(ModeloSolucion modelo)
    {
        var infracciones = new List<string>();

        foreach (var archivo in modelo.ArchivosComunes)
        {
            foreach (var referencia in archivo.ReferenciasAgregadas)
            {
                infracciones.Add(
                    $"{archivo.Ruta} -> {referencia}: un archivo común de MSBuild no agrega referencias "
                    + "a todos los proyectos ni importa otros archivos (salvo la cadena estándar de "
                    + "GetPathOfFileAbove en un Directory.Build.props/.targets anidado, no en el de la raíz), "
                    + "porque se saltaría la lectura de cada .csproj.");
            }
        }

        return infracciones;
    }

    public static IReadOnlyList<string> SdkDeNubeSoloEnProyectosDeProveedor(
        ModeloSolucion modelo,
        IReadOnlyCollection<string> proveedoresAutorizados)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in modelo.Proyectos.Where(p => !proveedoresAutorizados.Contains(p.Nombre)))
        {
            foreach (var paquete in BuscarSdkDeNube(proyecto.Paquetes))
            {
                infracciones.Add(Infraccion(
                    proyecto,
                    $"PackageReference {paquete}",
                    "regla 7: un SDK de nube solo va en un proyecto de proveedor autorizado"));
            }

            foreach (var usado in BuscarSdkDeNube(proyecto.Usados))
            {
                infracciones.Add(Infraccion(
                    proyecto,
                    $"ensamblado {usado}",
                    "regla 7: un SDK de nube solo va en un proyecto de proveedor autorizado"));
            }
        }

        return infracciones;
    }

    // ---------------------------------------------------------------------------------------
    // Ayudantes
    // ---------------------------------------------------------------------------------------

    private static string Infraccion(Proyecto proyecto, string referencia, string motivo) =>
        $"{proyecto.Nombre} -> {referencia}: {motivo}.";

    private static bool EmpiezaCon(string nombre, IEnumerable<string> prefijos) =>
        prefijos.Any(p => nombre.StartsWith(p, StringComparison.Ordinal));

    private static bool EsLibreriaBase(string nombre) =>
        nombre is "System" or "netstandard" or "mscorlib"
        || nombre.StartsWith("System.", StringComparison.Ordinal);

    /// <summary>
    /// Una referencia es a pruebas si el proyecto referenciado parece de pruebas por su ruta (la del
    /// modelo o la que resuelve el .csproj que lo referencia) o por su nombre. Mirar solo el nombre
    /// dejaba pasar un proyecto de apoyo bajo tests/.
    /// </summary>
    private static bool EsDePruebas(ModeloSolucion modelo, Proyecto origen, string nombreDeProyecto)
    {
        var rutaEnElModelo = modelo.Proyectos.FirstOrDefault(p => p.Nombre == nombreDeProyecto)?.Ruta;
        return Clasificador.ParecePruebas(nombreDeProyecto, rutaEnElModelo)
            || origen.RutasDe(nombreDeProyecto).Any(ruta => Clasificador.ParecePruebas(nombreDeProyecto, ruta));
    }

    /// <summary>
    /// Verdadero si la referencia apunta a un proyecto de Nitido.slnx. Con la ruta resuelta del
    /// .csproj se compara por ruta (un archivo distinto con el mismo nombre no cuenta); sin ella
    /// (modelos que solo dan nombres) se compara por nombre.
    /// </summary>
    private static bool EstaEnLaSolucion(ModeloSolucion modelo, Proyecto origen, string nombreDeProyecto)
    {
        var rutas = origen.RutasDe(nombreDeProyecto);
        return rutas.Count == 0
            ? modelo.Proyectos.Any(p => p.Nombre == nombreDeProyecto)
            : rutas.All(ruta => modelo.Proyectos.Any(p => p.Ruta == ruta));
    }

    private static IEnumerable<Proyecto> DeCapa(ModeloSolucion modelo, CapaDeModulo capa) =>
        modelo.Proyectos.Where(p => p.Tipo == TipoProyecto.Modulo && p.Capa == capa);

    /// <summary>
    /// Lo que un proyecto depende de otros proyectos Nitido.*: lo declarado con ProjectReference y
    /// lo que su ensamblado usa de verdad (que incluye lo que llegó de forma transitiva).
    /// </summary>
    private static IEnumerable<(string Nombre, string Origen)> DependenciasDeNitido(Proyecto proyecto) =>
        proyecto.Referencias
            .Select(r => (Nombre: r, Origen: "ProjectReference"))
            .Concat(proyecto.Usados
                .Where(u => u.StartsWith("Nitido.", StringComparison.Ordinal))
                .Select(u => (Nombre: u, Origen: "ensamblado")))
            .Where(d => d.Nombre != proyecto.Nombre)
            .DistinctBy(d => d.Nombre);

    private static bool EsPermitidoEnAplicacion(Proyecto aplicacion, string nombreDeProyecto)
    {
        if (nombreDeProyecto == "Nitido.Compartido")
        {
            return true;
        }

        var clasificacion = Clasificador.Clasificar(nombreDeProyecto);
        if (clasificacion.Tipo != TipoProyecto.Modulo)
        {
            return false;
        }

        return clasificacion.Capa == CapaDeModulo.Contratos
            || (clasificacion.Capa == CapaDeModulo.Dominio && clasificacion.Modulo == aplicacion.Modulo);
    }

    private static List<string> SoloDependenDeCompartido(ModeloSolucion modelo, CapaDeModulo capa)
    {
        var infracciones = new List<string>();

        foreach (var proyecto in DeCapa(modelo, capa))
        {
            var quien = $"{capa} solo depende de Nitido.Compartido y de la librería base de .NET";

            VerificarDeclarado(
                infracciones,
                proyecto,
                proyectoPermitido: nombre => nombre == "Nitido.Compartido",
                paquetePermitido: _ => false,
                marcosPermitidos: false,
                quien: quien);

            foreach (var usado in proyecto.Usados.Where(u => !EsLibreriaBase(u) && u != "Nitido.Compartido"))
            {
                infracciones.Add(Infraccion(proyecto, $"ensamblado {usado}", quien));
            }
        }

        return infracciones;
    }

    private static void VerificarDeclarado(
        List<string> infracciones,
        Proyecto proyecto,
        Func<string, bool> proyectoPermitido,
        Func<string, bool> paquetePermitido,
        bool marcosPermitidos,
        string quien)
    {
        foreach (var referencia in proyecto.Referencias.Where(r => !proyectoPermitido(r)))
        {
            infracciones.Add(Infraccion(proyecto, $"ProjectReference {referencia}", quien));
        }

        foreach (var paquete in proyecto.Paquetes.Where(p => !paquetePermitido(p)))
        {
            infracciones.Add(Infraccion(proyecto, $"PackageReference {paquete}", quien));
        }

        if (!marcosPermitidos)
        {
            foreach (var marco in proyecto.Marcos)
            {
                infracciones.Add(Infraccion(proyecto, $"FrameworkReference {marco}", quien));
            }
        }
    }
}
