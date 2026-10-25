using System.Xml.Linq;

namespace Nitido.Pruebas.Unidad.Arquitectura;

/// <summary>
/// Modelos de solución armados a mano para probar las reglas sin depender de que existan módulos
/// reales. El modelo sano son dos módulos según la plantilla de docs/arquitectura-backend.md,
/// sección 4.3, donde Clientes usa el Contratos de Firmas. Cada caso de infracción parte de él y
/// cambia una sola cosa.
/// </summary>
internal static class ModelosSinteticos
{
    public static ModeloSolucion DosModulosBienFormados() => new(
        [
            P("Nitido.Compartido", usados: ["System.Runtime", "System.Collections"]),
            P(
                "Nitido.Compartido.Infraestructura",
                refs: ["Nitido.Compartido"],
                paquetes: ["Microsoft.EntityFrameworkCore", "Microsoft.Data.SqlClient"],
                usados: ["System.Runtime", "Nitido.Compartido", "Microsoft.EntityFrameworkCore"]),
            P(
                "Nitido.Compartido.Api",
                refs: ["Nitido.Compartido"],
                marcos: ["Microsoft.AspNetCore.App"],
                usados: ["System.Runtime", "Nitido.Compartido", "Microsoft.AspNetCore.Http.Abstractions"]),

            // Firmas: expone Contratos porque otro módulo lo necesita.
            P("Nitido.Firmas.Dominio", refs: ["Nitido.Compartido"], usados: ["System.Runtime", "Nitido.Compartido"]),
            P("Nitido.Firmas.Contratos", refs: ["Nitido.Compartido"], usados: ["System.Runtime", "Nitido.Compartido"]),
            P(
                "Nitido.Firmas.Aplicacion",
                refs: ["Nitido.Firmas.Dominio"],
                paquetes: ["FluentValidation"],
                usados:
                [
                    "System.Runtime",
                    "Nitido.Compartido",
                    "Nitido.Firmas.Dominio",
                    "FluentValidation",
                    "Microsoft.Extensions.Logging.Abstractions",
                ]),
            P(
                "Nitido.Firmas.Infraestructura",
                refs: ["Nitido.Firmas.Aplicacion", "Nitido.Compartido.Infraestructura"],
                usados:
                [
                    "System.Runtime",
                    "Nitido.Firmas.Aplicacion",
                    "Nitido.Firmas.Dominio",
                    "Nitido.Compartido.Infraestructura",
                    "Microsoft.EntityFrameworkCore",
                ]),
            P(
                "Nitido.Firmas.Api",
                refs: ["Nitido.Firmas.Aplicacion", "Nitido.Compartido.Api"],
                marcos: ["Microsoft.AspNetCore.App"],
                usados:
                [
                    "System.Runtime",
                    "Nitido.Firmas.Aplicacion",
                    "Nitido.Compartido.Api",
                    "Microsoft.AspNetCore.Http.Abstractions",
                ]),

            // Clientes: usa el Contratos de Firmas, nunca su Dominio ni su Aplicacion.
            P("Nitido.Clientes.Dominio", refs: ["Nitido.Compartido"], usados: ["System.Runtime", "Nitido.Compartido"]),
            P(
                "Nitido.Clientes.Aplicacion",
                refs: ["Nitido.Clientes.Dominio", "Nitido.Firmas.Contratos"],
                paquetes: ["FluentValidation"],
                usados:
                [
                    "System.Runtime",
                    "Nitido.Compartido",
                    "Nitido.Clientes.Dominio",
                    "Nitido.Firmas.Contratos",
                    "FluentValidation",
                ]),
            P(
                "Nitido.Clientes.Infraestructura",
                refs: ["Nitido.Clientes.Aplicacion", "Nitido.Firmas.Contratos", "Nitido.Compartido.Infraestructura"],
                usados:
                [
                    "System.Runtime",
                    "Nitido.Clientes.Aplicacion",
                    "Nitido.Clientes.Dominio",
                    "Nitido.Firmas.Contratos",
                    "Nitido.Compartido.Infraestructura",
                    "Microsoft.EntityFrameworkCore",
                ]),
            P(
                "Nitido.Clientes.Api",
                refs: ["Nitido.Clientes.Aplicacion", "Nitido.Compartido.Api"],
                marcos: ["Microsoft.AspNetCore.App"],
                usados:
                [
                    "System.Runtime",
                    "Nitido.Clientes.Aplicacion",
                    "Nitido.Compartido.Api",
                    "Microsoft.AspNetCore.Http.Abstractions",
                ]),

            // El host compone todo.
            P(
                "Nitido.Api",
                refs:
                [
                    "Nitido.Compartido",
                    "Nitido.Firmas.Infraestructura",
                    "Nitido.Firmas.Api",
                    "Nitido.Clientes.Infraestructura",
                    "Nitido.Clientes.Api",
                ],
                paquetes: ["Serilog.AspNetCore", "Microsoft.AspNetCore.OpenApi"],
                marcos: ["Microsoft.AspNetCore.App"],
                usados:
                [
                    "System.Runtime",
                    "Serilog.AspNetCore",
                    "Microsoft.AspNetCore",
                    "Nitido.Firmas.Infraestructura",
                    "Nitido.Firmas.Api",
                    "Nitido.Clientes.Infraestructura",
                    "Nitido.Clientes.Api",
                ]),

            // Las pruebas pueden referenciar lo que necesiten.
            P(
                "Nitido.Pruebas.Unidad",
                refs: ["Nitido.Api", "Nitido.Firmas.Dominio", "Nitido.Clientes.Dominio"],
                paquetes: ["xunit.v3"]),
        ],
        [new ArchivoComun("Directory.Build.props", [])]);

    public static ModeloSolucion Con(this ModeloSolucion modelo, string proyecto, Func<Proyecto, Proyecto> cambio)
    {
        var existente = modelo.Proyectos.SingleOrDefault(p => p.Nombre == proyecto)
            ?? throw new InvalidOperationException($"El modelo sintético no tiene el proyecto {proyecto}.");

        return modelo with
        {
            Proyectos = [.. modelo.Proyectos.Select(p => p.Nombre == proyecto ? cambio(existente) : p)],
        };
    }

    public static ModeloSolucion Sin(this ModeloSolucion modelo, string proyecto) =>
        modelo with { Proyectos = [.. modelo.Proyectos.Where(p => p.Nombre != proyecto)] };

    public static ModeloSolucion Agregando(this ModeloSolucion modelo, Proyecto proyecto) =>
        modelo with { Proyectos = [.. modelo.Proyectos, proyecto] };

    public static ModeloSolucion ConReferencia(this ModeloSolucion modelo, string proyecto, string referencia) =>
        modelo.Con(proyecto, p => p with { Referencias = [.. p.Referencias, referencia] });

    public static ModeloSolucion SinReferencia(this ModeloSolucion modelo, string proyecto, string referencia) =>
        modelo.Con(proyecto, p => p with
        {
            Referencias = [.. p.Referencias.Where(r => r != referencia)],
            RutasDeReferencias = p.RutasDeReferencias?.Where(r => r.Nombre != referencia).ToList(),
        });

    public static ModeloSolucion ConPaquete(this ModeloSolucion modelo, string proyecto, string paquete) =>
        modelo.Con(proyecto, p => p with { Paquetes = [.. p.Paquetes, paquete] });

    public static ModeloSolucion ConMarco(this ModeloSolucion modelo, string proyecto, string marco) =>
        modelo.Con(proyecto, p => p with { Marcos = [.. p.Marcos, marco] });

    public static ModeloSolucion ConUsado(this ModeloSolucion modelo, string proyecto, string ensamblado) =>
        modelo.Con(proyecto, p => p with { EnsambladosUsados = [.. p.Usados, ensamblado] });

    public static ModeloSolucion SinCargar(this ModeloSolucion modelo, string proyecto) =>
        modelo.Con(proyecto, p => p with { EnsambladosUsados = null });

    /// <summary>El ensamblado está en la salida pero lanzó una excepción al cargarse.</summary>
    public static ModeloSolucion ConErrorDeCarga(
        this ModeloSolucion modelo,
        string proyecto,
        string tipoDeExcepcion,
        string mensaje) =>
        modelo.Con(proyecto, p => p with
        {
            EnsambladosUsados = null,
            ErrorDeCarga = $"{tipoDeExcepcion}: {mensaje}",
        });

    /// <summary>
    /// Reemplaza lo declarado de un proyecto (referencias con sus rutas resueltas, paquetes, marcos y problemas de lectura)
    /// por lo que lee el lector real de un .csproj escrito en XML. Lo usado por reflexión no cambia.
    /// </summary>
    public static ModeloSolucion ConCsproj(this ModeloSolucion modelo, string proyecto, string xml)
    {
        var lectura = LectorDeMsBuild.LeerProyecto(XDocument.Parse(xml));
        return modelo.Con(proyecto, p => p with
        {
            Referencias = lectura.Referencias,
            Paquetes = lectura.Paquetes,
            Marcos = lectura.Marcos,
            ProblemasDeLectura = lectura.Problemas,
            RutasDeReferencias = LectorDeMsBuild.ResolverRutas(p.Ruta, lectura.IncludesDeReferencias),
        });
    }

    /// <summary>Agrega un archivo común de MSBuild escrito en XML, leído con el lector real.</summary>
    public static ModeloSolucion ConArchivoComunXml(this ModeloSolucion modelo, string ruta, string xml) =>
        modelo with { ArchivosComunes = [.. modelo.ArchivosComunes, LectorDeMsBuild.LeerArchivoComun(ruta, XDocument.Parse(xml))] };

    public static ModeloSolucion ConArchivoComun(this ModeloSolucion modelo, string ruta, string referencia) =>
        modelo with { ArchivosComunes = [.. modelo.ArchivosComunes, new ArchivoComun(ruta, [referencia])] };

    /// <summary>Un proyecto sintético. Si no se da la ruta, usa la que exige la convención.</summary>
    public static Proyecto P(
        string nombre,
        string[]? refs = null,
        string[]? paquetes = null,
        string[]? marcos = null,
        string[]? usados = null,
        string? ruta = null) =>
        new(
            nombre,
            ruta ?? RutaPorConvencion(nombre),
            refs ?? [],
            paquetes ?? [],
            marcos ?? [],
            usados ?? []);

    private static string RutaPorConvencion(string nombre)
    {
        var clasificacion = Clasificador.Clasificar(nombre);
        return clasificacion.Tipo == TipoProyecto.Pruebas
            ? $"tests/unidad/{nombre}/{nombre}.csproj"
            : Clasificador.RutaEsperada(nombre, clasificacion) ?? $"{nombre}/{nombre}.csproj";
    }
}
