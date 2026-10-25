namespace Nitido.Pruebas.Unidad.Arquitectura;

/// <summary>
/// Demuestran que cada regla de arquitectura detecta su infracción. Mientras la solución no tenga
/// módulos, las pruebas sobre la solución real no tienen proyectos por capa que revisar; estas
/// parten de un modelo sintético sano, le cambian una sola cosa y esperan que la regla lo marque
/// con un mensaje que nombra el proyecto y la referencia.
/// </summary>
public class ReglasDeDependenciaPruebas
{
    private sealed record Caso(
        string Regla,
        Func<ModeloSolucion, ModeloSolucion> Infraccion,
        params string[] Fragmentos);

    private static readonly Dictionary<string, Caso> Casos = new()
    {
        // Convención de nombres y carpetas
        ["Convencion: dominio fuera de su carpeta"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Con(
                "Nitido.Clientes.Dominio",
                p => p with { Ruta = "Modulos/Clientes/Domain/Nitido.Clientes.Dominio.csproj" }),
            "Nitido.Clientes.Dominio",
            "Modulos/Clientes/Domain"),
        ["Convencion: capa que no existe"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Agregando(ModelosSinteticos.P(
                "Nitido.Clientes.Servicios",
                ruta: "Modulos/Clientes/Servicios/Nitido.Clientes.Servicios.csproj")),
            "Nitido.Clientes.Servicios"),
        ["Convencion: proyecto suelto en la raíz"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Agregando(ModelosSinteticos.P(
                "Nitido.Utilidades",
                ruta: "Nitido.Utilidades/Nitido.Utilidades.csproj")),
            "Nitido.Utilidades"),
        ["Convencion: pruebas fuera de tests"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Agregando(ModelosSinteticos.P(
                "Nitido.Pruebas.Extra",
                ruta: "Extra/Nitido.Pruebas.Extra.csproj")),
            "Nitido.Pruebas.Extra"),
        ["Convencion: falta Nitido.Api"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Sin("Nitido.Api"),
            "Nitido.Api"),
        ["Convencion: falta Nitido.Compartido"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Sin("Nitido.Compartido"),
            "Nitido.Compartido"),
        ["Convencion: producción referencia un proyecto de pruebas"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConReferencia("Nitido.Clientes.Dominio", "Nitido.Pruebas.Unidad"),
            "Nitido.Clientes.Dominio",
            "Nitido.Pruebas.Unidad"),
        ["Convencion: proyecto de apoyo bajo tests/ sin el nombre Nitido.Pruebas"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Agregando(ModelosSinteticos.P(
                "Nitido.ContextoDePrueba",
                ruta: "tests/apoyo/Nitido.ContextoDePrueba/Nitido.ContextoDePrueba.csproj")),
            "Nitido.ContextoDePrueba",
            "solo la ruta"),
        ["Convencion: el host referencia un proyecto de apoyo bajo tests/"] = new(
            ReglasDeArquitectura.Convencion,
            m => m
                .Agregando(ModelosSinteticos.P(
                    "Nitido.ContextoDePrueba",
                    ruta: "tests/apoyo/Nitido.ContextoDePrueba/Nitido.ContextoDePrueba.csproj"))
                .ConReferencia("Nitido.Api", "Nitido.ContextoDePrueba"),
            "Nitido.Api",
            "Nitido.ContextoDePrueba",
            "ningún proyecto de producción referencia pruebas"),
        ["Convencion: proyecto de módulo bajo tests/"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Agregando(ModelosSinteticos.P(
                "Nitido.Pagos.Dominio",
                paquetes: ["Serilog"],
                ruta: "tests/unidad/Nitido.Pagos.Dominio/Nitido.Pagos.Dominio.csproj")),
            "Nitido.Pagos.Dominio",
            "solo la ruta"),
        ["Convencion: Nitido.Pruebas fuera de tests cumple solo el nombre"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.Agregando(ModelosSinteticos.P(
                "Nitido.Pruebas.Extra",
                ruta: "Extra/Nitido.Pruebas.Extra.csproj")),
            "Nitido.Pruebas.Extra",
            "solo el nombre"),

        // Convención: proyectos que no están en Nitido.slnx
        ["Convencion: el host referencia un proyecto de apoyo que no está en Nitido.slnx"] = new(
            // Escenario: tests/apoyo/Nitido.ContextoDePrueba, fuera del slnx, referenciado por el host
            // y que a su vez referencia Nitido.Firmas.Infraestructura. Las reglas no lo ven; la
            // convención tiene que marcar la referencia.
            ReglasDeArquitectura.Convencion,
            m => m.ConReferencia("Nitido.Api", "Nitido.ContextoDePrueba"),
            "Nitido.Api",
            "ProjectReference Nitido.ContextoDePrueba",
            "no está en Nitido.slnx; las reglas no pueden revisarlo"),
        ["Convencion: el host referencia un módulo olvidado en Nitido.slnx"] = new(
            // Escenario: Modulos/Pagos/... no está en el slnx (su Dominio usaría Serilog y referenciaría
            // Nitido.Firmas.Infraestructura) pero el host referencia su Infraestructura.
            ReglasDeArquitectura.Convencion,
            m => m.ConReferencia("Nitido.Api", "Nitido.Pagos.Infraestructura"),
            "Nitido.Api",
            "ProjectReference Nitido.Pagos.Infraestructura",
            "no está en Nitido.slnx; las reglas no pueden revisarlo"),
        ["Convencion: una referencia entre módulos a un proyecto que no está en Nitido.slnx"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConReferencia("Nitido.Clientes.Aplicacion", "Nitido.Pagos.Contratos"),
            "Nitido.Clientes.Aplicacion",
            "ProjectReference Nitido.Pagos.Contratos",
            "no está en Nitido.slnx"),
        ["Convencion: un proyecto de pruebas referencia uno que no está en Nitido.slnx"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConReferencia("Nitido.Pruebas.Unidad", "Nitido.Pagos.Infraestructura"),
            "Nitido.Pruebas.Unidad",
            "ProjectReference Nitido.Pagos.Infraestructura",
            "no está en Nitido.slnx"),
        ["Convencion: la ruta apunta a un archivo distinto con el mismo nombre que uno de la solución"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Aplicacion",
                Csproj("""<ProjectReference Include="..\..\..\Apoyo\Nitido.Firmas.Contratos\Nitido.Firmas.Contratos.csproj" />""")),
            "Nitido.Clientes.Aplicacion",
            "ProjectReference Nitido.Firmas.Contratos",
            "no está en Nitido.slnx"),
        ["Convencion: la ruta sale de la raíz del repositorio"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Aplicacion",
                Csproj("""<ProjectReference Include="..\..\..\..\Otro\Nitido.Firmas.Contratos.csproj" />""")),
            "Nitido.Clientes.Aplicacion",
            "ProjectReference Nitido.Firmas.Contratos",
            "no está en Nitido.slnx"),
        ["Convencion: un proyecto de apoyo bajo tests/ referenciado por la ruta aunque no esté en Nitido.slnx"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<ProjectReference Include="..\..\..\tests\apoyo\Nitido.Apoyo\Nitido.Apoyo.csproj" />""")),
            "Nitido.Clientes.Dominio",
            "ProjectReference Nitido.Apoyo",
            "ningún proyecto de producción referencia pruebas"),

        // Convención: lo que la lectura estática del .csproj no puede seguir
        ["Modulos: ProjectReference con dos rutas, la primera la Infraestructura de otro módulo"] = new(
            ReglasDeArquitectura.Modulos,
            m => m.ConCsproj(
                "Nitido.Clientes.Aplicacion",
                Csproj(
                    """<ProjectReference Include="..\..\Firmas\Infraestructura\Nitido.Firmas.Infraestructura.csproj;..\Dominio\Nitido.Clientes.Dominio.csproj" />""")),
            "Nitido.Clientes.Aplicacion",
            "Nitido.Firmas.Infraestructura"),
        ["Modulos: projectreference escrito en minúsculas a la Infraestructura de otro módulo"] = new(
            ReglasDeArquitectura.Modulos,
            m => m.ConCsproj(
                "Nitido.Clientes.Aplicacion",
                """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><projectreference Include="..\..\Firmas\Infraestructura\Nitido.Firmas.Infraestructura.csproj" /></ItemGroup></Project>"""),
            "Nitido.Clientes.Aplicacion",
            "Nitido.Firmas.Infraestructura"),
        ["Dominio: packagereference escrito en minúsculas"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><packagereference Include="Serilog" /></ItemGroup></Project>"""),
            "Nitido.Clientes.Dominio",
            "PackageReference Serilog"),
        ["Dominio: FRAMEWORKREFERENCE con el atributo include en minúsculas"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><FRAMEWORKREFERENCE include="Microsoft.AspNetCore.App" /></ItemGroup></Project>"""),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Microsoft.AspNetCore.App"),
        ["Dominio: sdk web escrito como elemento sdk en minúsculas"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                """<Project sdk="Microsoft.NET.Sdk"><sdk name="Microsoft.NET.Sdk.Web" /></Project>"""),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Microsoft.AspNetCore.App"),
        ["Convencion: ProjectReference con una propiedad en el Include"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Aplicacion",
                Csproj("""<ProjectReference Include="$(Algo)\Nitido.Firmas.Infraestructura.csproj" />""")),
            "Nitido.Clientes.Aplicacion",
            "ProjectReference Include=\"$(Algo)",
            "no se puede leer sin evaluarlo"),
        ["Convencion: ProjectReference con un comodín en el Include"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Aplicacion",
                Csproj("""<ProjectReference Include="..\..\*\*\*.csproj" />""")),
            "Nitido.Clientes.Aplicacion",
            "ProjectReference Include="),
        ["Convencion: PackageReference con comodín"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj("Nitido.Clientes.Dominio", Csproj("""<PackageReference Include="Serilog.*" />""")),
            "Nitido.Clientes.Dominio",
            "PackageReference Include=\"Serilog.*\""),
        ["Convencion: FrameworkReference con propiedad"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj("Nitido.Clientes.Dominio", Csproj("""<FrameworkReference Include="$(Marco)" />""")),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Include=\"$(Marco)\""),
        ["Convencion: .csproj con Import"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                """<Project Sdk="Microsoft.NET.Sdk"><Import Project="..\..\comun.props" /></Project>"""),
            "Nitido.Clientes.Dominio",
            "Import Project=\"..\\..\\comun.props\""),
        ["Convencion: .csproj con Reference a un ensamblado suelto"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<Reference Include="Serilog"><HintPath>..\lib\Serilog.dll</HintPath></Reference>""")),
            "Nitido.Clientes.Dominio",
            "Reference Include=\"Serilog\""),
        ["Convencion: Compile que apunta al código de otro módulo"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<Compile Include="..\..\OtroModulo\**\*.cs" />""")),
            "Nitido.Clientes.Dominio",
            "Compile Include=\"..\\..\\OtroModulo\\**\\*.cs\"",
            "solo compila archivos de su propia carpeta"),
        ["Convencion: Compile con una ruta absoluta"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj("Nitido.Clientes.Dominio", Csproj("""<Compile Include="C:\Otro\Codigo.cs" />""")),
            "Nitido.Clientes.Dominio",
            "Compile Include=\"C:\\Otro\\Codigo.cs\""),
        ["Convencion: Compile con una propiedad que sale de la carpeta"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<Compile Include="$(SolutionDir)Modulos\Firmas\Dominio\X.cs" />""")),
            "Nitido.Clientes.Dominio",
            "Compile Include=\"$(SolutionDir)Modulos\\Firmas\\Dominio\\X.cs\"",
            "solo compila archivos de su propia carpeta"),
        ["Convencion: Compile con una lista de elementos"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj("Nitido.Clientes.Dominio", Csproj("""<Compile Include="@(Otros)" />""")),
            "Nitido.Clientes.Dominio",
            "Compile Include=\"@(Otros)\""),
        ["Convencion: Compile con metadatos de elemento"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj("Nitido.Clientes.Dominio", Csproj("""<Compile Include="%(Ruta)\X.cs" />""")),
            "Nitido.Clientes.Dominio",
            "Compile Include=\"%(Ruta)"),
        ["Convencion: Compile con MSBuildProjectDirectory que sube con .."] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<Compile Include="$(MSBuildProjectDirectory)\..\Otro\X.cs" />""")),
            "Nitido.Clientes.Dominio",
            "Compile Include=\"$(MSBuildProjectDirectory)"),
        ["Convencion: elementos import y reference escritos en minúsculas"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                """<Project Sdk="Microsoft.NET.Sdk"><import project="..\..\comun.props" /><reference include="Serilog" /></Project>"""),
            "Nitido.Clientes.Dominio",
            "Import Project=\"..\\..\\comun.props\""),
        ["Convencion: elemento reference en minúsculas"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<reference include="Serilog" />""")),
            "Nitido.Clientes.Dominio",
            "Reference Include=\"Serilog\""),
        ["Convencion: elemento compile en minúsculas que sale de la carpeta"] = new(
            ReglasDeArquitectura.Convencion,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<compile include="..\Otro\X.cs" />""")),
            "Nitido.Clientes.Dominio",
            "Compile Include=\"..\\Otro\\X.cs\""),

        // Compartido
        ["Compartido con PackageReference a Serilog"] = new(
            ReglasDeArquitectura.Compartido,
            m => m.ConPaquete("Nitido.Compartido", "Serilog"),
            "Nitido.Compartido",
            "Serilog"),
        ["Compartido con FrameworkReference a ASP.NET Core"] = new(
            ReglasDeArquitectura.Compartido,
            m => m.ConMarco("Nitido.Compartido", "Microsoft.AspNetCore.App"),
            "Nitido.Compartido",
            "Microsoft.AspNetCore.App"),
        ["Compartido con ProjectReference a un módulo"] = new(
            ReglasDeArquitectura.Compartido,
            m => m.ConReferencia("Nitido.Compartido", "Nitido.Firmas.Dominio"),
            "Nitido.Compartido",
            "Nitido.Firmas.Dominio"),
        ["Compartido que usa Microsoft.Extensions por reflexión"] = new(
            ReglasDeArquitectura.Compartido,
            m => m.ConUsado("Nitido.Compartido", "Microsoft.Extensions.Logging.Abstractions"),
            "Nitido.Compartido",
            "Microsoft.Extensions.Logging.Abstractions"),

        // Dominio
        ["Dominio con PackageReference a EF Core"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConPaquete("Nitido.Clientes.Dominio", "Microsoft.EntityFrameworkCore"),
            "Nitido.Clientes.Dominio",
            "Microsoft.EntityFrameworkCore"),
        ["Dominio con ProjectReference a su Aplicacion"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConReferencia("Nitido.Clientes.Dominio", "Nitido.Clientes.Aplicacion"),
            "Nitido.Clientes.Dominio",
            "Nitido.Clientes.Aplicacion"),
        ["Dominio con FrameworkReference a ASP.NET Core"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConMarco("Nitido.Clientes.Dominio", "Microsoft.AspNetCore.App"),
            "Nitido.Clientes.Dominio",
            "Microsoft.AspNetCore.App"),
        ["Dominio que usa Serilog por reflexión"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConUsado("Nitido.Clientes.Dominio", "Serilog"),
            "Nitido.Clientes.Dominio",
            "Serilog"),
        ["Dominio de un módulo bajo tests/ con Serilog"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.Agregando(ModelosSinteticos.P(
                "Nitido.Pagos.Dominio",
                paquetes: ["Serilog"],
                ruta: "tests/unidad/Nitido.Pagos.Dominio/Nitido.Pagos.Dominio.csproj")),
            "Nitido.Pagos.Dominio",
            "PackageReference Serilog"),
        ["Dominio con dos PackageReference separados por punto y coma"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<PackageReference Include="Newtonsoft.Json;Microsoft.EntityFrameworkCore" />""")),
            "Nitido.Clientes.Dominio",
            "PackageReference Microsoft.EntityFrameworkCore"),
        ["Dominio con dos FrameworkReference separados por punto y coma"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                Csproj("""<FrameworkReference Include="Microsoft.NETCore.App; Microsoft.AspNetCore.App" />""")),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Microsoft.AspNetCore.App"),
        ["Dominio con Sdk Web en el atributo"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj("Nitido.Clientes.Dominio", """<Project Sdk="Microsoft.NET.Sdk.Web" />"""),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Microsoft.AspNetCore.App"),
        ["Dominio con Sdk Web con versión"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj("Nitido.Clientes.Dominio", """<Project Sdk="Microsoft.NET.Sdk.Web/10.0.0" />"""),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Microsoft.AspNetCore.App"),
        ["Dominio con varios Sdk separados por punto y coma"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                """<Project Sdk="Microsoft.NET.Sdk;Microsoft.NET.Sdk.Web/10.0.0" />"""),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Microsoft.AspNetCore.App"),
        ["Dominio con Sdk Web como elemento Sdk"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                """<Project Sdk="Microsoft.NET.Sdk"><Sdk Name="Microsoft.NET.Sdk.Web" Version="10.0.0" /></Project>"""),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Microsoft.AspNetCore.App"),
        ["Dominio con Sdk Web en un Import con Sdk"] = new(
            ReglasDeArquitectura.Dominio,
            m => m.ConCsproj(
                "Nitido.Clientes.Dominio",
                """<Project><Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk.Web" /></Project>"""),
            "Nitido.Clientes.Dominio",
            "FrameworkReference Microsoft.AspNetCore.App"),

        // Contratos
        ["Contratos con ProjectReference a un Dominio"] = new(
            ReglasDeArquitectura.Contratos,
            m => m.ConReferencia("Nitido.Firmas.Contratos", "Nitido.Firmas.Dominio"),
            "Nitido.Firmas.Contratos",
            "Nitido.Firmas.Dominio"),
        ["Contratos con PackageReference a FluentValidation"] = new(
            ReglasDeArquitectura.Contratos,
            m => m.ConPaquete("Nitido.Firmas.Contratos", "FluentValidation"),
            "Nitido.Firmas.Contratos",
            "FluentValidation"),
        ["Contratos que usa EF Core por reflexión"] = new(
            ReglasDeArquitectura.Contratos,
            m => m.ConUsado("Nitido.Firmas.Contratos", "Microsoft.EntityFrameworkCore"),
            "Nitido.Firmas.Contratos",
            "Microsoft.EntityFrameworkCore"),

        // Aplicacion
        ["Aplicacion con FrameworkReference a ASP.NET Core"] = new(
            ReglasDeArquitectura.Aplicacion,
            m => m.ConMarco("Nitido.Clientes.Aplicacion", "Microsoft.AspNetCore.App"),
            "Nitido.Clientes.Aplicacion",
            "Microsoft.AspNetCore.App"),
        ["Aplicacion con ProjectReference a su Infraestructura"] = new(
            ReglasDeArquitectura.Aplicacion,
            m => m.ConReferencia("Nitido.Clientes.Aplicacion", "Nitido.Clientes.Infraestructura"),
            "Nitido.Clientes.Aplicacion",
            "Nitido.Clientes.Infraestructura"),
        ["Aplicacion con ProjectReference al Dominio de otro módulo"] = new(
            ReglasDeArquitectura.Aplicacion,
            m => m.ConReferencia("Nitido.Clientes.Aplicacion", "Nitido.Firmas.Dominio"),
            "Nitido.Clientes.Aplicacion",
            "Nitido.Firmas.Dominio"),
        ["Aplicacion con PackageReference a EF Core"] = new(
            ReglasDeArquitectura.Aplicacion,
            m => m.ConPaquete("Nitido.Clientes.Aplicacion", "Microsoft.EntityFrameworkCore.SqlServer"),
            "Nitido.Clientes.Aplicacion",
            "Microsoft.EntityFrameworkCore.SqlServer"),
        ["Aplicacion que usa Serilog por reflexión"] = new(
            ReglasDeArquitectura.Aplicacion,
            m => m.ConUsado("Nitido.Clientes.Aplicacion", "Serilog"),
            "Nitido.Clientes.Aplicacion",
            "Serilog"),
        ["Aplicacion que usa EF Core por reflexión"] = new(
            ReglasDeArquitectura.Aplicacion,
            m => m.ConUsado("Nitido.Clientes.Aplicacion", "Microsoft.EntityFrameworkCore"),
            "Nitido.Clientes.Aplicacion",
            "Microsoft.EntityFrameworkCore"),

        // Api
        ["Api con ProjectReference a su Infraestructura"] = new(
            ReglasDeArquitectura.Api,
            m => m.ConReferencia("Nitido.Clientes.Api", "Nitido.Clientes.Infraestructura"),
            "Nitido.Clientes.Api",
            "Nitido.Clientes.Infraestructura"),
        ["Api con PackageReference a EF Core"] = new(
            ReglasDeArquitectura.Api,
            m => m.ConPaquete("Nitido.Clientes.Api", "Microsoft.EntityFrameworkCore"),
            "Nitido.Clientes.Api",
            "Microsoft.EntityFrameworkCore"),
        ["Api con ProjectReference al Api de otro módulo"] = new(
            ReglasDeArquitectura.Api,
            m => m.ConReferencia("Nitido.Clientes.Api", "Nitido.Firmas.Api"),
            "Nitido.Clientes.Api",
            "Nitido.Firmas.Api"),
        ["Api que usa su Infraestructura por reflexión"] = new(
            ReglasDeArquitectura.Api,
            m => m.ConUsado("Nitido.Clientes.Api", "Nitido.Clientes.Infraestructura"),
            "Nitido.Clientes.Api",
            "Nitido.Clientes.Infraestructura"),

        // Api: también Nitido.Compartido.Api (F0-02b, diseño 1.4). La tabla de la arquitectura (sección 1) le
        // prohíbe EF Core y la Infraestructura; antes ninguna regla lo revisaba.
        ["Nitido.Compartido.Api declara EF Core"] = new(
            ReglasDeArquitectura.Api,
            m => m.ConPaquete("Nitido.Compartido.Api", "Microsoft.EntityFrameworkCore.SqlServer"),
            "Nitido.Compartido.Api",
            "PackageReference Microsoft.EntityFrameworkCore.SqlServer"),
        ["Nitido.Compartido.Api usa EF Core por reflexión"] = new(
            ReglasDeArquitectura.Api,
            m => m.ConUsado("Nitido.Compartido.Api", "Microsoft.EntityFrameworkCore"),
            "Nitido.Compartido.Api",
            "ensamblado Microsoft.EntityFrameworkCore"),
        ["Nitido.Compartido.Api usa Nitido.Compartido.Infraestructura por reflexión"] = new(
            ReglasDeArquitectura.Api,
            m => m.ConUsado("Nitido.Compartido.Api", "Nitido.Compartido.Infraestructura"),
            "Nitido.Compartido.Api",
            "ensamblado Nitido.Compartido.Infraestructura"),
        ["Nitido.Compartido.Api usa la Infraestructura de un módulo por reflexión"] = new(
            ReglasDeArquitectura.Api,
            m => m.ConUsado("Nitido.Compartido.Api", "Nitido.Clientes.Infraestructura"),
            "Nitido.Compartido.Api",
            "ensamblado Nitido.Clientes.Infraestructura"),

        // Infraestructura
        ["Infraestructura con ProjectReference a su Api"] = new(
            ReglasDeArquitectura.Infraestructura,
            m => m.ConReferencia("Nitido.Clientes.Infraestructura", "Nitido.Clientes.Api"),
            "Nitido.Clientes.Infraestructura",
            "Nitido.Clientes.Api"),
        ["Infraestructura con ProjectReference a Nitido.Compartido.Api"] = new(
            ReglasDeArquitectura.Infraestructura,
            m => m.ConReferencia("Nitido.Clientes.Infraestructura", "Nitido.Compartido.Api"),
            "Nitido.Clientes.Infraestructura",
            "Nitido.Compartido.Api"),
        ["Infraestructura con ProjectReference al Dominio de otro módulo"] = new(
            ReglasDeArquitectura.Infraestructura,
            m => m.ConReferencia("Nitido.Clientes.Infraestructura", "Nitido.Firmas.Dominio"),
            "Nitido.Clientes.Infraestructura",
            "Nitido.Firmas.Dominio"),
        ["Infraestructura que usa su Api por reflexión"] = new(
            ReglasDeArquitectura.Infraestructura,
            m => m.ConUsado("Nitido.Clientes.Infraestructura", "Nitido.Clientes.Api"),
            "Nitido.Clientes.Infraestructura",
            "Nitido.Clientes.Api"),

        // Módulos solo por Contratos
        ["Modulo que referencia la Infraestructura de otro"] = new(
            ReglasDeArquitectura.Modulos,
            m => m.ConReferencia("Nitido.Firmas.Aplicacion", "Nitido.Clientes.Infraestructura"),
            "Nitido.Firmas.Aplicacion",
            "Nitido.Clientes.Infraestructura"),
        ["Modulo que referencia la Aplicacion de otro"] = new(
            ReglasDeArquitectura.Modulos,
            m => m.ConReferencia("Nitido.Clientes.Infraestructura", "Nitido.Firmas.Aplicacion"),
            "Nitido.Clientes.Infraestructura",
            "Nitido.Firmas.Aplicacion"),
        ["Modulo que usa el Dominio de otro por reflexión"] = new(
            ReglasDeArquitectura.Modulos,
            m => m.ConUsado("Nitido.Firmas.Dominio", "Nitido.Clientes.Dominio"),
            "Nitido.Firmas.Dominio",
            "Nitido.Clientes.Dominio"),

        // Solo el host referencia Infraestructura y Api
        ["Proyecto que no es el host referencia un Api"] = new(
            ReglasDeArquitectura.SoloElHost,
            m => m.ConReferencia("Nitido.Firmas.Contratos", "Nitido.Clientes.Api"),
            "Nitido.Firmas.Contratos",
            "Nitido.Clientes.Api"),
        ["Infraestructura referencia el Api de otro módulo"] = new(
            ReglasDeArquitectura.SoloElHost,
            m => m.ConReferencia("Nitido.Firmas.Infraestructura", "Nitido.Clientes.Api"),
            "Nitido.Firmas.Infraestructura",
            "Nitido.Clientes.Api"),
        ["Aplicacion referencia una Infraestructura"] = new(
            ReglasDeArquitectura.SoloElHost,
            m => m.ConReferencia("Nitido.Firmas.Aplicacion", "Nitido.Firmas.Infraestructura"),
            "Nitido.Firmas.Aplicacion",
            "Nitido.Firmas.Infraestructura"),
        ["Aplicacion referencia Nitido.Compartido.Infraestructura"] = new(
            ReglasDeArquitectura.SoloElHost,
            m => m.ConReferencia("Nitido.Clientes.Aplicacion", "Nitido.Compartido.Infraestructura"),
            "Nitido.Clientes.Aplicacion",
            "Nitido.Compartido.Infraestructura"),
        ["Api referencia Nitido.Compartido.Infraestructura"] = new(
            ReglasDeArquitectura.SoloElHost,
            m => m.ConReferencia("Nitido.Clientes.Api", "Nitido.Compartido.Infraestructura"),
            "Nitido.Clientes.Api",
            "Nitido.Compartido.Infraestructura"),
        ["Infraestructura referencia Nitido.Compartido.Api"] = new(
            ReglasDeArquitectura.SoloElHost,
            m => m.ConReferencia("Nitido.Clientes.Infraestructura", "Nitido.Compartido.Api"),
            "Nitido.Clientes.Infraestructura",
            "Nitido.Compartido.Api"),
        ["Nitido.Compartido.Infraestructura referencia un módulo"] = new(
            ReglasDeArquitectura.SoloElHost,
            m => m.ConReferencia("Nitido.Compartido.Infraestructura", "Nitido.Clientes.Dominio"),
            "Nitido.Compartido.Infraestructura",
            "Nitido.Clientes.Dominio"),
        ["Nitido.Compartido.Api referencia Nitido.Compartido.Infraestructura"] = new(
            ReglasDeArquitectura.SoloElHost,
            m => m.ConReferencia("Nitido.Compartido.Api", "Nitido.Compartido.Infraestructura"),
            "Nitido.Compartido.Api",
            "Nitido.Compartido.Infraestructura"),

        // El host compone todos los módulos
        ["Host que no compone el Api de un módulo"] = new(
            ReglasDeArquitectura.Host,
            m => m.SinReferencia("Nitido.Api", "Nitido.Firmas.Api"),
            "Nitido.Api",
            "Nitido.Firmas.Api"),
        ["Host que no compone la Infraestructura de un módulo"] = new(
            ReglasDeArquitectura.Host,
            m => m.SinReferencia("Nitido.Api", "Nitido.Clientes.Infraestructura"),
            "Nitido.Api",
            "Nitido.Clientes.Infraestructura"),
        ["Host que no compone un módulo entero"] = new(
            ReglasDeArquitectura.Host,
            m => m
                .SinReferencia("Nitido.Api", "Nitido.Firmas.Infraestructura")
                .SinReferencia("Nitido.Api", "Nitido.Firmas.Api"),
            "Nitido.Api",
            "Nitido.Firmas.Infraestructura"),
        ["Modulo sin Api en la solución"] = new(
            ReglasDeArquitectura.Host,
            m => m.Sin("Nitido.Clientes.Api").SinReferencia("Nitido.Api", "Nitido.Clientes.Api"),
            "Nitido.Api",
            "Nitido.Clientes.Api"),
        ["Ensamblado de un módulo que no se puede cargar"] = new(
            ReglasDeArquitectura.Host,
            m => m.SinCargar("Nitido.Firmas.Dominio"),
            "Nitido.Firmas.Dominio",
            "no se pudo cargar"),
        ["Ensamblado de un módulo que lanza FileLoadException al cargar"] = new(
            ReglasDeArquitectura.Host,
            m => m.ConErrorDeCarga(
                "Nitido.Firmas.Dominio",
                "FileLoadException",
                "Could not load file or assembly 'Nitido.Firmas.Dominio'."),
            "Nitido.Firmas.Dominio",
            "no se pudo cargar Nitido.Firmas.Dominio: FileLoadException: Could not load file"),
        ["Ensamblado de un módulo con formato inválido"] = new(
            ReglasDeArquitectura.Host,
            m => m.ConErrorDeCarga("Nitido.Firmas.Dominio", "BadImageFormatException", "Formato incorrecto."),
            "Nitido.Firmas.Dominio",
            "no se pudo cargar Nitido.Firmas.Dominio: BadImageFormatException: Formato incorrecto"),

        // Archivos comunes de MSBuild
        ["Directory.Build.props con PackageReference"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComun("Directory.Build.props", "PackageReference Serilog"),
            "Directory.Build.props",
            "PackageReference Serilog"),
        ["Directory.Build.targets con ProjectReference"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComun("Directory.Build.targets", "ProjectReference Nitido.Compartido"),
            "Directory.Build.targets",
            "ProjectReference Nitido.Compartido"),
        ["Directory.Packages.props con GlobalPackageReference"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComun("Directory.Packages.props", "GlobalPackageReference Serilog"),
            "Directory.Packages.props",
            "GlobalPackageReference Serilog"),
        ["Directory.Build.props anidado con Import de otro props"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComunXml(
                "Modulos/Clientes/Directory.Build.props",
                """<Project><Import Project="..\..\comun.props" /></Project>"""),
            "Modulos/Clientes/Directory.Build.props",
            "Import ..\\..\\comun.props"),
        ["Directory.Build.targets con Reference a un ensamblado suelto"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComunXml(
                "Directory.Build.targets",
                """<Project><ItemGroup><Reference Include="Serilog" /></ItemGroup></Project>"""),
            "Directory.Build.targets",
            "Reference Serilog"),
        ["Directory.Packages.props con Import, aunque sea la cadena estándar"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComunXml(
                "Directory.Packages.props",
                """<Project><Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Packages.props', '$(MSBuildThisFileDirectory)../'))" /></Project>"""),
            "Directory.Packages.props",
            "Import"),
        ["Directory.Build.props que importa la cadena estándar de otro archivo"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComunXml(
                "Modulos/Clientes/Directory.Build.props",
                """<Project><Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))" /></Project>"""),
            "Modulos/Clientes/Directory.Build.props",
            "Import"),
        ["Directory.Build.props con ProjectReference de dos rutas"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComunXml(
                "Directory.Build.props",
                """<Project><ItemGroup><ProjectReference Include="a.csproj;b.csproj" /></ItemGroup></Project>"""),
            "Directory.Build.props",
            "ProjectReference a.csproj;b.csproj"),
        ["Directory.Build.props de la raíz con la cadena estándar de GetPathOfFileAbove"] = new(
            // En la raíz del repositorio la cadena importaría nitido/Directory.Build.props, que está
            // fuera del repositorio.
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComunXml(
                "Directory.Build.props",
                """<Project><Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" /></Project>"""),
            "Directory.Build.props",
            "Import"),
        ["Directory.Build.targets de la raíz con la cadena estándar de GetPathOfFileAbove"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComunXml(
                "Directory.Build.targets",
                """<Project><Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildThisFileDirectory)../'))" /></Project>"""),
            "Directory.Build.targets",
            "Import"),
        ["Directory.Build.props con elementos en minúsculas"] = new(
            ReglasDeArquitectura.ArchivosComunes,
            m => m.ConArchivoComunXml(
                "Directory.Build.props",
                """<Project><itemgroup><packagereference include="Serilog" /></itemgroup></Project>"""),
            "Directory.Build.props",
            "PackageReference Serilog"),

        // Regla 7: SDK de nube
        ["Infraestructura con PackageReference a Azure.Storage.Blobs"] = new(
            ReglasDeArquitectura.SdkDeNube,
            m => m.ConPaquete("Nitido.Clientes.Infraestructura", "Azure.Storage.Blobs"),
            "Nitido.Clientes.Infraestructura",
            "Azure.Storage.Blobs"),
        ["Dominio con PackageReference a AWSSDK.S3"] = new(
            ReglasDeArquitectura.SdkDeNube,
            m => m.ConPaquete("Nitido.Clientes.Dominio", "AWSSDK.S3"),
            "Nitido.Clientes.Dominio",
            "AWSSDK.S3"),
        ["Host que usa Google.Cloud.Storage.V1 por reflexión"] = new(
            ReglasDeArquitectura.SdkDeNube,
            m => m.ConUsado("Nitido.Api", "Google.Cloud.Storage.V1"),
            "Nitido.Api",
            "Google.Cloud.Storage.V1"),
        ["Pruebas con PackageReference a Amazon.S3"] = new(
            ReglasDeArquitectura.SdkDeNube,
            m => m.ConPaquete("Nitido.Pruebas.Unidad", "Amazon.S3"),
            "Nitido.Pruebas.Unidad",
            "Amazon.S3"),
    };

    /// <summary>Un .csproj mínimo con los elementos dados dentro de un ItemGroup.</summary>
    private static string Csproj(string elementos) =>
        $"""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{elementos}</ItemGroup></Project>""";

    public static TheoryData<string> NombresDeCasos => new(Casos.Keys);

    [Theory]
    [MemberData(nameof(NombresDeCasos))]
    public void ReglasDeDependencia_DetectanCadaInfraccion(string nombreDelCaso)
    {
        var caso = Casos[nombreDelCaso];
        var regla = ReglasDeArquitectura.Todas[caso.Regla];
        var sano = ModelosSinteticos.DosModulosBienFormados();

        Assert.True(
            regla(sano).Count == 0,
            $"El modelo sano ya infringe {caso.Regla}: {string.Join(" | ", regla(sano))}");

        var infracciones = regla(caso.Infraccion(sano));

        Assert.True(
            infracciones.Any(i => caso.Fragmentos.All(f => i.Contains(f, StringComparison.Ordinal))),
            $"{caso.Regla} no marcó «{nombreDelCaso}» con un mensaje que nombre "
            + $"{string.Join(" y ", caso.Fragmentos)}. Infracciones: "
            + (infracciones.Count == 0 ? "ninguna" : string.Join(" | ", infracciones)));
    }

    [Fact]
    public void ReglasDeDependencia_AceptanUnModuloBienFormado()
    {
        var modelo = ModelosSinteticos.DosModulosBienFormados();

        // El caso que se prueba: un módulo usa el Contratos del otro.
        var clientes = Assert.Single(modelo.Proyectos, p => p.Nombre == "Nitido.Clientes.Aplicacion");
        Assert.Contains("Nitido.Firmas.Contratos", clientes.Referencias);
        Assert.Contains(modelo.Proyectos, p => p.Nombre == "Nitido.Firmas.Contratos");

        foreach (var (nombre, regla) in ReglasDeArquitectura.Todas)
        {
            var infracciones = regla(modelo);
            Assert.True(
                infracciones.Count == 0,
                $"{nombre} marcó un módulo bien formado: {string.Join(" | ", infracciones)}");
        }
    }

    [Fact]
    public void ReglasDeDependencia_CadaReglaTieneAlMenosUnCasoSintetico()
    {
        var cubiertas = Casos.Values.Select(c => c.Regla).ToHashSet(StringComparer.Ordinal);

        var sinCaso = ReglasDeArquitectura.Todas.Keys.Where(r => !cubiertas.Contains(r)).ToList();

        Assert.True(
            sinCaso.Count == 0,
            $"Reglas sin un caso sintético que las infrinja: {string.Join(", ", sinCaso)}");
    }

    [Fact]
    public void ReglasDeDependencia_CadaReglaTieneUnaPruebaSobreLaSolucionReal()
    {
        var sinPrueba = ReglasDeArquitectura.Todas.Keys
            .Where(r => typeof(ArquitecturaPruebas).GetMethod(r) is null)
            .ToList();

        Assert.True(
            sinPrueba.Count == 0,
            $"Reglas sin una prueba sobre la solución real: {string.Join(", ", sinPrueba)}");
    }

    [Fact]
    public void SdkDeNube_AceptaSoloLosProyectosDeProveedorAutorizados()
    {
        var modelo = ModelosSinteticos.DosModulosBienFormados()
            .ConPaquete("Nitido.Clientes.Infraestructura", "Azure.Storage.Blobs")
            .ConPaquete("Nitido.Clientes.Dominio", "Azure.Core");

        var sinAutorizados = ReglasDeArquitectura.SdkDeNubeSoloEnProyectosDeProveedor(modelo, []);
        var conUnAutorizado = ReglasDeArquitectura.SdkDeNubeSoloEnProyectosDeProveedor(
            modelo,
            ["Nitido.Clientes.Infraestructura"]);

        Assert.Equal(2, sinAutorizados.Count);
        var infraccion = Assert.Single(conUnAutorizado);
        Assert.Contains("Nitido.Clientes.Dominio", infraccion, StringComparison.Ordinal);
        Assert.Contains("Azure.Core", infraccion, StringComparison.Ordinal);
    }
}
