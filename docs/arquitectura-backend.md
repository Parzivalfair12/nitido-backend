# Arquitectura del backend: Clean Architecture por módulo

18 de octubre de 2026 · arquitecto · Estado: aceptado el 18 de octubre de 2026 por el usuario ([ADR 0001](adr/0001-clean-architecture.md))

Este documento dice cómo se organiza el código de `nitido-backend`: qué va en cada proyecto, qué puede referenciar a qué, cómo se hace cumplir y qué cambia en las historias de la fase 0. La base se construye en `F0-01b` (`docs/specs/f0-01b-clean-architecture.md`).

**Decisión en una línea:** se aplica Clean Architecture **dentro de cada módulo** del monolito, con un proyecto `.csproj` por capa. Hay un núcleo compartido mínimo. El host `Nitido.Api` es la única raíz de composición. Las reglas las vigilan el compilador y unas pruebas de arquitectura, sin librerías nuevas.

Los ejemplos de código son ilustrativos: no son archivos reales, y quien implemente puede ajustar los detalles mientras respete las reglas.

---

## 1. Regla de dependencias

```
                         ┌────────────────────────────┐
                         │   Nitido.Api (host)        │  raíz de composición:
                         │   Program.cs               │  ve todo, nadie lo ve
                         └──┬──────────────────────┬──┘
                            │                      │
            ┌───────────────▼───────┐      ┌───────▼───────────────┐
            │ Nitido.<M>.Api        │      │ Nitido.<M>.           │
            │ endpoints, HTTP       │      │ Infraestructura       │
            │                       │      │ EF, Identity, Hangfire│
            └───────────┬───────────┘      └───────────┬───────────┘
                        │      (no se ven entre sí)    │
                        └──────────────┬───────────────┘
                                       ▼
                         ┌────────────────────────────┐
                         │ Nitido.<M>.Aplicacion      │──────► Nitido.<Otro>.Contratos
                         │ casos de uso, puertos      │        (interfaz pública de otro módulo)
                         └─────────────┬──────────────┘
                                       ▼
                         ┌────────────────────────────┐
                         │ Nitido.<M>.Dominio         │
                         │ entidades, reglas          │
                         └─────────────┬──────────────┘
                                       ▼
                         ┌────────────────────────────┐
                         │ Nitido.Compartido          │  solo la librería base de .NET
                         └────────────────────────────┘

  Nitido.Compartido.Infraestructura ◄── solo *.Infraestructura y el host
  Nitido.Compartido.Api             ◄── solo *.Api y el host
```

Las flechas apuntan hacia adentro. El dominio no conoce la base de datos, la web, el registro ni la nube.

### Referencias permitidas

| Proyecto | Puede referenciar (proyectos) | Paquetes y marcos permitidos | Prohibido |
| --- | --- | --- | --- |
| `Nitido.Compartido` | ninguno | ninguno | todo proyecto, paquete o `FrameworkReference` |
| `Nitido.Compartido.Infraestructura` | `Nitido.Compartido` | EF Core, `Microsoft.Data.SqlClient` | módulos, `Nitido.Compartido.Api` |
| `Nitido.Compartido.Api` | `Nitido.Compartido` | `Microsoft.AspNetCore.App` | módulos, `Nitido.Compartido.Infraestructura`, EF Core |
| `Nitido.<M>.Dominio` | `Nitido.Compartido` | ninguno | todo lo demás |
| `Nitido.<M>.Contratos` | `Nitido.Compartido` | ninguno | todo lo demás |
| `Nitido.<M>.Aplicacion` | `<M>.Dominio`, `<M>.Contratos`, `<Otro>.Contratos`, `Nitido.Compartido` | `FluentValidation`, `Microsoft.Extensions.Logging.Abstractions` | `Infraestructura` y `Api` de cualquier módulo; `Aplicacion` y `Dominio` de otro módulo; EF Core, ASP.NET Core, SqlClient, Serilog, Hangfire, SDK de nube |
| `Nitido.<M>.Infraestructura` | `<M>.Aplicacion`, `<Otro>.Contratos`, `Nitido.Compartido.Infraestructura` | EF Core, Identity (stores), Hangfire, `Microsoft.AspNetCore.App` cuando lo exija Identity o Data Protection | `<M>.Api`, cualquier proyecto de otro módulo que no sea `Contratos`, `Nitido.Compartido.Api`, SDK de nube (salvo proyectos de proveedor autorizados, sección 5) |
| `Nitido.<M>.Api` | `<M>.Aplicacion`, `Nitido.Compartido.Api` | `Microsoft.AspNetCore.App` | `<M>.Infraestructura`, cualquier proyecto de otro módulo, EF Core |
| `Nitido.Api` (host) | todos | los del host (Serilog, OpenAPI, Hangfire, `Microsoft.EntityFrameworkCore.Design` con `PrivateAssets=all`) | ninguno lo referencia, salvo los proyectos de pruebas |
| `tests/*` | lo que necesiten | los de pruebas | ningún proyecto de producción referencia pruebas |

Las referencias transitivas se aceptan: por ejemplo, `Api` usa tipos de `Dominio` a través de `Aplicacion`. Lo que se vigila es lo que cada `.csproj` declara y lo que cada ensamblado usa de verdad (sección 5).

---

## 2. Qué vive en cada capa

### `Nitido.Compartido`: núcleo compartido puro

- **Tipos que usan varios módulos y no tienen reglas de un módulo en particular:**
  - `Resultado`, `Resultado<T>`, `Error` y `TipoError` (desde F0-01b).
  - `IDeEmpresa`, `IDeFirma` e `IContextoSesion` (desde F0-02b; F0-02a puede crear `IContextoSesion` solo con el usuario, ver 6.3).
  - `IAccesoAClientes` (F0-02b): puerto con el que el filtro del cliente activo de `Nitido.Compartido.Api` pregunta si el usuario puede entrar a un cliente, sin ver ningún módulo. Lo implementa Clientes. Es una excepción razonada a la regla «lo usan dos módulos hoy»: es parte del contrato de multiempresa, igual que `IContextoSesion` (ADR 0003, decisión 7).
- **Puertos transversales,** cuando lleguen y si los usan dos módulos o más: `IAlmacenArchivos` (Documentos y Conservacion, fase 2); `ISecretos`, si se cumple esa misma regla.
- **Prohibido:**
  - reglas de negocio de un módulo;
  - constantes tributarias (regla 5);
  - EF Core, ASP.NET Core, Serilog o cualquier paquete;
  - clases «Utilidades», «Helpers» o «Comunes» sin dueño.

**Regla para entrar a Compartido** (la vigila el `revisor`). Un tipo entra solo si cumple las cuatro condiciones:
1. Hoy lo usan dos módulos o más; «quizás después» no cuenta.
2. No tiene reglas de un módulo.
3. Solo depende de la librería base de .NET.
4. Es estable.

Ejemplo: el objeto de valor `Nit` nace en `Clientes.Dominio` (F0-04) y pasa a Compartido cuando Terceros lo necesite (fase 1).

### `Nitido.Compartido.Infraestructura` (se crea en F0-02a)

Piezas de EF Core que usan todos los `DbContext` de los módulos:
- convención de nombres (de PascalCase a `T_` y `UUID_`, mayúsculas con guion bajo);
- columnas de auditoría como propiedades sombra, para que el dominio no las cargue;
- interceptor de auditoría de `SaveChanges`, que usa `TimeProvider` e `IContextoSesion`. También asigna las llaves: las llaves de un solo `Guid` son `ValueGeneratedNever`, y el interceptor pone `Guid.CreateVersion7(ahora)` solo a las entradas `Added` cuya llave llega vacía (`Guid.Empty`). No se marcan `OnAdd` porque EF trataba como `Modified` a un hijo nuevo de un agregado ya cargado (hallazgo M1 de la revisión de F0-02a, 2026-10-18). `ExecuteUpdate`, `ExecuteDelete` y el SQL escrito a mano no pasan por el interceptor: quien los use llena la auditoría a mano; las fechas leídas de la base vuelven como `DateTimeKind.Unspecified` (UTC por convención);
- filtro global por convención para `IDeEmpresa` e `IDeFirma` (F0-02b);
- interceptor de conexión que fija `SESSION_CONTEXT` (F0-02b);
- mapeo de `DECIMAL(19,2)` como valor por defecto para `decimal`;
- `ScriptsSql` (F0-02b): lee los scripts SQL incrustados que aplican las migraciones (`db/seguridad-por-fila/`);
- `CrearAlcanceConContexto` y `CrearAlcanceParaIngreso` (F0-03a y F0-04): alcances de inyección de dependencias con un contexto de sesión explícito (`ContextoDeAlcance`, que solo ellos pueden fijar y una sola vez), solo para el registro de firma, la semilla de Development, los trabajos en segundo plano y el ingreso (la búsqueda del usuario por correo con la llave `CORREO_INGRESO` y la escritura de sus contadores de intentos). Ver ADR 0004.

### `Nitido.Compartido.Api` (se crea en F0-02b)

- Conversión de `Resultado` a ProblemDetails (`ARespuestaHttp`); llega en F0-03a, con el primer endpoint que devuelve un `Resultado`.
- Filtro de endpoint del cliente activo en la ruta, que responde `404` si el usuario no tiene acceso (F0-02b), y `MapearRutasDelCliente`, la única forma de armar rutas `/api/clientes/{uuidCliente}/...`.
- Implementación de `IContextoSesion` a partir de `HttpContext` (F0-02b y F0-03a).

### `Nitido.<M>.Dominio`

- Entidades y agregados con sus invariantes. Ejemplos: `Firma`, `Cliente`, y más adelante `Asiento`, que nunca queda descuadrado (regla 1).
- Objetos de valor (`Nit`, `Periodo`) y enumeraciones.
- Eventos de dominio, como `record`s, si un módulo los necesita.
- Sin atributos de EF, sin `async` de infraestructura y sin registro. Dinero en `decimal` y fechas de negocio en `DateOnly`.
- Las fábricas devuelven `Resultado<T>` cuando la regla puede fallar por datos del usuario. Lanzan `ArgumentException` solo ante errores de programación.

### `Nitido.<M>.Aplicacion`

- **Casos de uso:** una clase por caso, en una carpeta por caso (corte vertical dentro de la capa). Ejemplo: `CrearCliente/CrearCliente.cs`, `CrearClienteComando.cs` y `CrearClienteValidador.cs`.
- **Puertos del módulo,** en `Puertos/`:
  - `IRepositorioClientes`, uno por agregado y solo con los métodos que se usan;
  - `IUnidadDeTrabajo`, propio del módulo;
  - `IConsultasClientes`, para las lecturas.
- **Validación:** validadores de FluentValidation con mensajes en español de Colombia. Los ejecuta el propio caso de uso, para que también apliquen cuando lo llama un trabajo de Hangfire o un evento, no solo HTTP.
- **Implementación de los `Contratos` del módulo** cuando involucran reglas. Las que son solo lectura pueden vivir en Infraestructura.
- **Registro** con `ILogger<T>` (abstracciones de Microsoft), nunca Serilog directo.

### `Nitido.<M>.Contratos` (solo si otro módulo lo necesita)

- Interfaces públicas del módulo, como `IConsultaFirmas`, y sus DTO como `record`s.
- Eventos de integración que el módulo publica (desde la fase 2).
- Sin entidades del dominio: se exponen copias planas.

### `Nitido.<M>.Infraestructura`

- `<M>DbContext`:
  - con su propio historial (`__EFMigrationsHistory_<M>`);
  - implementa el `IUnidadDeTrabajo` del módulo;
  - solo mapea tablas del módulo.
- Configuraciones `IEntityTypeConfiguration<T>`, migraciones y semillas aplicadas por migración (`convenciones-bd`).
- Repositorios e implementaciones de `IConsultas<M>`:
  - proyecciones con `AsNoTracking().Select(...)`;
  - SQL a mano con `Database.SqlQuery<T>` cuando la consulta lo pida, sin Dapper.
- Adaptadores de puertos: Identity en Firmas (F0-03a), trabajos de Hangfire y, cuando lleguen, implementaciones locales de puertos propios.
- `Modulo<M>.cs` con el método público `Agregar<M>(IServiceCollection, IConfiguration)`. Registra todo lo del módulo, incluidos los casos de uso de Aplicacion, con registros explícitos y sin escaneo de ensamblados.

### `Nitido.<M>.Api`

- Minimal APIs: `Endpoints<M>.cs` con el método público `Mapear<M>(IEndpointRouteBuilder)`, que crea `MapGroup("/api/<ruta>")`.
- Contratos HTTP propios (`...Solicitud`, `...Respuesta`) y su mapeo a mano. El contrato con el frontend es el documento OpenAPI, y un cambio interno de Aplicacion no debe cambiarlo sin que alguien lo note.
- Los endpoints son delgados: traducen la solicitud, llaman al caso de uso o a la consulta y convierten el `Resultado` en respuesta. No tienen lógica.

### `Nitido.Api` (host)

- `Program.cs`:
  - Serilog, ProblemDetails, OpenAPI y `/salud`;
  - autenticación, antifalsificación y limitador (F0-03a);
  - Data Protection (F0-03b);
  - `TimeProvider.System`;
  - `Agregar<M>()` y `Mapear<M>()` de cada módulo.
- Orden de las migraciones entre módulos (ver 6.2).
- Si `Program.cs` crece de más, lo transversal se mueve a métodos de extensión en `Nitido.Api/Configuracion/`. No se crea un proyecto para eso.

---

## 3. Decisiones concretas

### 3.1 Casos de uso propios, sin MediatR

**Decisión:** clases simples, inyectadas directamente en el endpoint. No hay interfaz genérica `ICasoDeUso<,>`, mediador, tubería ni decoradores.

**Por qué:**
- MediatR pasó a licencia doble desde la versión 13: RPL-1.5 o comercial de Lucky Penny Software, y la 12.5 ya no recibe correcciones.
- Además, lo que aporta (tuberías para validación, transacciones y registro) en nítido se resuelve con tres líneas explícitas dentro de cada caso de uso.
- Una clase concreta se lee, se prueba y se navega con «ir a la definición» sin indirección.

**Alternativa:** si más adelante hace falta una tubería transversal real, por ejemplo métricas por caso de uso, se evalúa `Mediator` (MIT, generador de código) en un ADR. Hoy no hace falta.

```csharp
public sealed class CrearCliente(
    IRepositorioClientes clientes, IUnidadDeTrabajo unidad,
    IValidator<CrearClienteComando> validador, IContextoSesion sesion, TimeProvider tiempo)
{
    public async Task<Resultado<ClienteCreado>> EjecutarAsync(CrearClienteComando comando, CancellationToken ct)
    {
        var validacion = await validador.ValidateAsync(comando, ct);
        if (!validacion.IsValid) return Error.Validacion(validacion.ToDictionary());

        var nit = Nit.Crear(comando.NumeroDocumento);
        if (nit.EsError) return nit.Error;
        if (await clientes.ExisteAsync(nit.Valor, ct)) return ErroresClientes.NitRepetido;

        var cliente = Cliente.Crear(sesion.UuidFirmas, nit.Valor, comando.RazonSocial, tiempo.GetUtcNow());
        clientes.Agregar(cliente);
        await unidad.GuardarAsync(ct);
        return new ClienteCreado(cliente.Uuid, cliente.Nit.Numero, cliente.Nit.DigitoVerificacion, cliente.RazonSocial);
    }
}
```

### 3.2 Errores de negocio: `Resultado`, no excepciones

**Decisión:**
- Un error esperado se devuelve como `Resultado` con un `Error` tipado. Ejemplos: correo repetido, NIT con dígito errado, cliente inexistente, más adelante periodo cerrado o asiento descuadrado.
- Las excepciones quedan para fallas inesperadas (errores de programación, base caída). Esas terminan en el `500` sin detalle que ya existe.

**Por qué en nítido:**
- Aprobar en lote (fase 3) produce errores por documento que se acumulan y se muestran juntos. Con excepciones, eso obliga a capturarlas una por una.
- El tipo de error decide el código HTTP en un solo lugar.

**Alternativa descartada:** excepciones de negocio traducidas por un `IExceptionHandler`. Son menos líneas al principio, pero esconden el flujo y vuelven costosos los lotes.

**Sin librería** (ErrorOr, FluentResults): el tipo propio son unas 60 líneas.

```csharp
public enum TipoError { Validacion, NoEncontrado, Conflicto, NoAutorizado }

public sealed record Error(TipoError Tipo, string Codigo, string Mensaje,
    IReadOnlyDictionary<string, string[]>? Campos = null)
{
    public static Error Conflicto(string codigo, string mensaje) => new(TipoError.Conflicto, codigo, mensaje);
    public static Error NoEncontrado(string codigo, string mensaje) => new(TipoError.NoEncontrado, codigo, mensaje);
    public static Error NoAutorizado(string codigo, string mensaje) => new(TipoError.NoAutorizado, codigo, mensaje);
    public static Error Validacion(IDictionary<string, string[]> campos) =>
        new(TipoError.Validacion, "validacion", "Revisa los datos marcados.",
            campos.ToFrozenDictionary(c => c.Key, c => c.Value.ToArray()));
}
// Resultado<T>: EsExito, EsError, Valor (lanza si es error), Error (lanza si es éxito),
// conversiones implícitas desde T y desde Error. Resultado (sin T) para comandos sin valor de retorno.
```

Precisiones que el diseño daba por supuestas:
- Los éxitos se crean con `Resultado.Exito()` (sin valor) y `Resultado<T>.Exito(T)`. Esta última es la única forma cuando `T` es una interfaz, porque C# no aplica conversiones implícitas definidas por el usuario desde una interfaz.
- Leer `Valor` de un error o `Error` de un éxito lanza `InvalidOperationException`. `Error.Validacion` copia los campos (claves y arreglos), así que cambiar el diccionario de origen después no altera el error.
- Con `Resultado<object>`, `Resultado<object> r = (object)error;` queda como éxito con el error como valor, no como error. Use `Resultado<object>.Exito(...)` o convierta desde un `Error` tipado.

**De `Resultado` a HTTP** (en `Nitido.Compartido.Api`, un solo lugar):

| Tipo | Respuesta |
| --- | --- |
| `Validacion` | `400`, `ValidationProblem` con los campos |
| `NoEncontrado` | `404` |
| `Conflicto` | `409` |
| `NoAutorizado` | `401` |

En todos los casos, el `title` lleva el mensaje en español y la extensión `codigo` lleva el código estable para el frontend. No hay `403`: un recurso ajeno responde `404`, como deciden F0-02b y F0-04.

### 3.3 Repositorios para escribir, consultas directas para leer (CQRS ligero)

- **Comandos:**
  - un repositorio por agregado, con interfaz en Aplicacion e implementación en Infraestructura;
  - un `IUnidadDeTrabajo` por módulo, en su propio espacio de nombres para que los 14 no choquen en la inyección de dependencias;
  - un caso de uso llama a `GuardarAsync` **una vez**, y ese único `SaveChanges` es la transacción.
- **Consultas:**
  - `IConsultas<M>` en Aplicacion devuelve DTO planos;
  - Infraestructura la implementa con proyecciones directas, sin cargar agregados, y siempre sobre el `DbContext` del módulo, que aplica el filtro de multiempresa.
  - El endpoint puede llamar a la consulta sin un caso de uso intermedio si no hay reglas.
- **No se usan:**
  - repositorio genérico `IRepositorio<T>`;
  - patrón Specification;
  - `IQueryable` expuesto fuera de Infraestructura;
  - una base de lectura aparte.

**Alternativa descartada:** inyectar el `DbContext` directamente en los casos de uso. Ahorra archivos, pero obliga a que Aplicacion referencie EF Core y rompe la frontera que justifica esta arquitectura.

### 3.4 Cómo habla un módulo con otro

1. **Llamada síncrona:** `<Otro>.Contratos` define la interfaz y el módulo dueño la implementa y la registra en su `Agregar<M>()`. Quien la usa referencia solo el proyecto `Contratos`.
2. **Evento interno** (desde la fase 2, por ejemplo «documento leído»): el evento se declara en el `Contratos` de quien lo publica. El despacho en proceso y la bandeja de salida (outbox) se deciden en un ADR cuando llegue la primera necesidad. En la fase 0 no hay eventos.
3. **Nunca:**
   - referenciar `Dominio`, `Aplicacion`, `Infraestructura` o `Api` de otro módulo;
   - mapear en un `DbContext` una tabla de otro módulo.
4. **Llaves foráneas en SQL entre módulos:** se permiten (F0-02a), y se crean en la migración del módulo que referencia, por nombre de tabla y sin entidad.

```csharp
// Nitido.Firmas.Contratos
public interface IConsultaFirmas
{
    Task<FirmaResumen?> ObtenerAsync(Guid uuidFirmas, CancellationToken ct);
}
public sealed record FirmaResumen(Guid UuidFirmas, string Nombre);
```

### 3.5 Transacciones

- **Regla general:** una operación de escritura toca un solo `DbContext` y hace un solo `SaveChanges`.
- **Si una herramienta guarda por su cuenta** (por ejemplo `UserManager.CreateAsync` de Identity en el registro de firma, F0-04), el caso de uso abre una transacción explícita con un método de la unidad de trabajo del módulo (`EjecutarEnTransaccionAsync`), compatible con la estrategia de reintentos de SQL Server.
- **Entre dos módulos** no hay transacción compartida: se diseña con eventos y consistencia eventual. Si un caso futuro la exige, se decide en un ADR (conexión y transacción compartidas entre contextos).

### 3.6 Tiempo e identificadores

- `TimeProvider` (de la librería base) se inyecta en Aplicacion e Infraestructura. El host registra `TimeProvider.System` y las pruebas usan un reloj controlado (F0-03a lo necesita para el bloqueo y la sesión). No se crea `IReloj`.
- Las llaves son GUID versión 7. La fábrica del dominio recibe el momento (`DateTimeOffset ahora`, que sale de `TimeProvider`) y crea la llave con `Guid.CreateVersion7(ahora)`. Así el dominio no depende de servicios y las pruebas son deterministas. No se crea `IGeneradorDeIdentificadores`. Las clases de persistencia sin fábrica de dominio reciben su llave del interceptor de auditoría, que asigna `Guid.CreateVersion7(ahora)` solo a las entradas `Added` con `Guid.Empty` (llaves `ValueGeneratedNever`; ver la sección 2, `Nitido.Compartido.Infraestructura`). Limitación: dos entidades nuevas con llave vacía en el mismo `DbContext` chocan en el mapa de identidad de EF antes de que corra el interceptor; si hay que agregar varias a la vez, asígnales la llave al crearlas.

### 3.7 Usuario y contexto de la petición

Se decidió **una sola interfaz** (aprobado el 2026-10-18), `IContextoSesion` (`UuidUsuarios`, `UuidFirmas`, `UuidEmpresas?`), en lugar de `IUsuarioActual` (F0-02a) más `IContextoSesion` (F0-02b). Las dos describen lo mismo y las usan las mismas piezas: el interceptor de auditoría y el de `SESSION_CONTEXT`. Ver 6.3.

### 3.8 Nombres y visibilidad

- **Nombres:**
  - Los tipos propios van en español, también los técnicos: `IRepositorioClientes`, `IUnidadDeTrabajo`, `CrearCliente`, `ModuloClientes`. Así está escrito el código actual y así lo usan las especificaciones (`IContextoSesion`, `IAlmacenArchivos`).
  - Los tipos del marco conservan su nombre (`DbContext`, `IEndpointRouteBuilder`).
  - El plan dice «inglés para lo técnico (`Repository`, `Handler`)». Ver la propuesta en la sección 9.
- **Visibilidad:**
  - `Dominio` y `Aplicacion`: tipos `public`. Los protegen las referencias de proyecto, porque solo los ven su propia `Infraestructura` y su propia `Api`.
  - `Infraestructura` y `Api`: todo `internal`, salvo `Agregar<M>` y `Mapear<M>`. Si la herramienta `dotnet ef` exige un `DbContext` público, se deja público y se anota.
  - Las pruebas de integración que necesiten tipos internos usan `<InternalsVisibleTo Include="Nitido.Pruebas.Integracion" />`.

---

## 4. Estructura de proyectos y plantilla de módulo

### 4.1 Carpetas

```
nitido-backend/
  Nitido.slnx
  Nitido.Api/                              host
  Nitido.Compartido/                       núcleo puro
    Resultados/                            Resultado, Error, TipoError (F0-01b)
    Multiempresa/                          IDeEmpresa, IDeFirma, IContextoSesion, IAccesoAClientes (F0-02b)
  Nitido.Compartido.Infraestructura/       EF común (F0-02a)
  Nitido.Compartido.Api/                   web común (F0-02b)
  Modulos/
    Clientes/
      Dominio/Nitido.Clientes.Dominio.csproj
      Aplicacion/Nitido.Clientes.Aplicacion.csproj
      Contratos/Nitido.Clientes.Contratos.csproj          (solo si otro módulo lo usa)
      Infraestructura/Nitido.Clientes.Infraestructura.csproj
      Api/Nitido.Clientes.Api.csproj
  tests/unidad | integracion | contables                  sin cambios: tres proyectos para toda la solución
```

- **Carpetas cortas, ensamblados con nombre completo.** Las carpetas se llaman `Dominio`, `Aplicacion`, etc., así que el comando de `CLAUDE.md` (`dotnet ef migrations add <Nombre> --context <Modulo>DbContext -p Modulos/<Modulo>/Infraestructura -s Nitido.Api -o Migraciones`) sigue sirviendo. Con más de un `DbContext` en el host, `--context` es obligatorio. El ensamblado y el espacio de nombres salen del nombre del `.csproj` (`Nitido.Clientes.Infraestructura`).
- **No se crea un proyecto de pruebas por módulo.** `Nitido.Pruebas.Unidad` referencia los `Dominio` y `Aplicacion` que necesite, y `Nitido.Pruebas.Integracion` sigue entrando por el host.

**Dentro de cada proyecto** (guía, no regla):

| Proyecto | Contenido |
| --- | --- |
| `Dominio` | una carpeta por agregado (`Clientes/Cliente.cs`, `Clientes/Nit.cs`) |
| `Aplicacion` | una carpeta por caso de uso, más `Puertos/` y `Consultas/` |
| `Infraestructura` | `Persistencia/` (`DbContext`, `Configuraciones/`, `Repositorios/`, `Consultas/`), `Migraciones/` y `Modulo<M>.cs` |
| `Api` | `Endpoints<M>.cs` y `Http/` (solicitudes y respuestas) |

### 4.2 ¿Cuatro proyectos por módulo o carpetas?

Son cuatro proyectos, más `Contratos` cuando haga falta.

**Lo que cuesta:**
- Con los 9 módulos del MVP: unos 36 a 40 proyectos. Con los 14: entre 56 y 64.
- Cada `.csproj` tiene de 3 a 10 líneas, porque `Directory.Build.props` y `Directory.Packages.props` ya ponen el resto.
- La compilación completa crece unos segundos por proyecto; la incremental casi no cambia.

**Lo que se gana:**
- El compilador impide que el dominio use EF o que un módulo use clases de otro.
- Con carpetas en un solo proyecto, eso solo se detecta inspeccionando el cuerpo de los métodos, y para eso haría falta una librería (NetArchTest no publica versión desde 2021) o leer IL a mano.

Para un equipo de un desarrollador con agentes, que un error no compile vale más que unos `.csproj` de más.

**Para no inflar el costo:**
- `Contratos` se crea solo cuando otro módulo lo necesita de verdad.
- Un módulo sin reglas propias puede no tener `Dominio`, si su especificación lo justifica.
- No hay proyectos de pruebas por módulo.
- No hay plantilla de `dotnet new`: basta la de abajo.

### 4.3 Plantilla de «módulo vacío»

Se usa para crear Firmas y Clientes en F0-02a. Reemplaza `<M>` por el nombre del módulo. Las rutas son relativas a la carpeta de cada proyecto.

```xml
<!-- Modulos/<M>/Dominio/Nitido.<M>.Dominio.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\Nitido.Compartido\Nitido.Compartido.csproj" />
  </ItemGroup>
</Project>

<!-- Modulos/<M>/Aplicacion/Nitido.<M>.Aplicacion.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="FluentValidation" />          <!-- cuando el módulo valide (F0-04) -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Dominio\Nitido.<M>.Dominio.csproj" />
  </ItemGroup>
</Project>

<!-- Modulos/<M>/Infraestructura/Nitido.<M>.Infraestructura.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Aplicacion\Nitido.<M>.Aplicacion.csproj" />
    <ProjectReference Include="..\..\..\Nitido.Compartido.Infraestructura\Nitido.Compartido.Infraestructura.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Nitido.Pruebas.Integracion" />
  </ItemGroup>
</Project>

<!-- Modulos/<M>/Api/Nitido.<M>.Api.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Aplicacion\Nitido.<M>.Aplicacion.csproj" />
    <ProjectReference Include="..\..\..\Nitido.Compartido.Api\Nitido.Compartido.Api.csproj" />
  </ItemGroup>
</Project>

<!-- Modulos/<M>/Contratos/Nitido.<M>.Contratos.csproj (solo si hace falta) -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\..\Nitido.Compartido\Nitido.Compartido.csproj" />
  </ItemGroup>
</Project>
```

**Notas de la plantilla:**
- EF Core llega a Infraestructura por `Nitido.Compartido.Infraestructura`. Si el módulo usa un paquete propio (Identity, Hangfire), lo declara en su `.csproj`.
- Desde F0-02b existe `Nitido.Compartido.Api` y los proyectos `Api` de los módulos lo referencian (antes llevaban solo la `FrameworkReference`).

**Archivos mínimos de cada proyecto nuevo:**
- `Infraestructura/Modulo<M>.cs` con `Agregar<M>()`;
- `Api/Endpoints<M>.cs` con `Mapear<M>()`, aunque el grupo no tenga rutas todavía.

**Pasos para agregar un módulo:**
1. Crear las carpetas y los `.csproj` de la plantilla.
2. Agregarlos a la solución, en su carpeta: `dotnet sln Nitido.slnx add Modulos/<M>/Dominio/Nitido.<M>.Dominio.csproj --solution-folder src/Modulos/<M>` (igual para cada proyecto).
   Si se olvida, la prueba de convención falla: todo proyecto referenciado debe estar en `Nitido.slnx`.
3. En `Nitido.Api.csproj`, referenciar `Nitido.<M>.Infraestructura` y `Nitido.<M>.Api`.
4. En `Program.cs`, llamar a `Agregar<M>()` y `Mapear<M>()`.
5. Correr `dotnet test`: las pruebas de arquitectura confirman las referencias y que el host compone el módulo.

**Resultado esperado en la solución:**

```xml
<Folder Name="/src/Modulos/Clientes/">
  <Project Path="Modulos/Clientes/Dominio/Nitido.Clientes.Dominio.csproj" />
  <Project Path="Modulos/Clientes/Aplicacion/Nitido.Clientes.Aplicacion.csproj" />
  <Project Path="Modulos/Clientes/Infraestructura/Nitido.Clientes.Infraestructura.csproj" />
  <Project Path="Modulos/Clientes/Api/Nitido.Clientes.Api.csproj" />
</Folder>
```

### 4.4 `Program.cs` cuando haya módulos

Se aplica desde F0-02a. F0-01b no cambia `Program.cs`.

```csharp
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AgregarFirmas(builder.Configuration)
    .AgregarClientes(builder.Configuration);
// ...
var app = builder.Build();
// ... canalización actual (errores, registro, /salud, OpenAPI) sin cambios
app.MapearFirmas();
app.MapearClientes();
```

```csharp
// Modulos/Clientes/Infraestructura/ModuloClientes.cs
public static class ModuloClientes
{
    public static IServiceCollection AgregarClientes(this IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddDbContext<ClientesDbContext>(/* cadena Nitido, historial __EFMigrationsHistory_Clientes, interceptores comunes */);
        servicios.AddScoped<IUnidadDeTrabajo>(sp => sp.GetRequiredService<ClientesDbContext>());
        servicios.AddScoped<IRepositorioClientes, RepositorioClientes>();
        servicios.AddScoped<IConsultasClientes, ConsultasClientes>();
        servicios.AddScoped<CrearCliente>();
        servicios.AddScoped<IValidator<CrearClienteComando>, CrearClienteValidador>();
        return servicios;
    }
}
```

---

## 5. Cómo se hace cumplir

### 5.1 Tres barreras

1. **El compilador:** un proyecto no puede usar lo que no referencia.
2. **Pruebas de arquitectura en `Nitido.Pruebas.Unidad`,** sin paquetes nuevos. Las escribe el `tester` en F0-01b y corren en cada `dotnet test` y en CI.
3. **El `revisor`:** la regla de entrada a Compartido, la lógica en los endpoints y el SQL a mano fuera de Infraestructura.

### 5.2 Cómo funcionan las pruebas

Cada prueba combina dos fuentes, porque cada una ve lo que la otra no:

- **Lectura estática** de `Nitido.slnx` y de cada `.csproj` con `System.Xml.Linq`:
  - se leen `ProjectReference`, `PackageReference` y `FrameworkReference`, y se anota como problema de lectura todo lo que no se puede seguir sin evaluar MSBuild (5.5);
  - cada proyecto se clasifica por su nombre (host, compartido, `Nitido.<M>.<Capa>`) y, para pruebas, por nombre y ruta a la vez; la convención exige además que la ruta coincida con el nombre;
  - se leen también los archivos comunes de MSBuild (`Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`) de la raíz y de cualquier carpeta, salvo `bin`, `obj`, `node_modules`, `.git` y `.vs`;
  - detecta referencias declaradas aunque no se usen todavía.
- **Reflexión** (`Assembly.Load` por nombre de cada proyecto de la solución y `GetReferencedAssemblies()`), como ya hace `Compartido_NoDependeDeSdkDeNube`:
  - detecta el uso real de un ensamblado que llegó de forma transitiva o desde un `Directory.Build.props`;
  - si un proyecto de la solución no está en la salida de las pruebas, la prueba falla, porque eso indica que el host no lo compone;
  - a los proyectos de pruebas no se les hace reflexión.

**Las reglas son funciones puras sobre un modelo** (`ModeloSolucion(Proyectos, ArchivosComunes)`, con `Proyecto(Nombre, Ruta, Referencias, Paquetes, Marcos, EnsambladosUsados, ErrorDeCarga, ProblemasDeLectura)` y `ArchivoComun(Ruta, ReferenciasAgregadas)`). El tipo, el módulo y la capa no se guardan: los calcula `Clasificador` a partir del nombre y la ruta. Así se prueban dos veces:
- con modelos sintéticos, para demostrar que cada regla detecta su infracción (al estilo de `DeteccionDeSdkDeNube_MarcaSoloLosPrefijosProhibidos`);
- sobre la solución real.

Con esto no hace falta un módulo de ejemplo para que las pruebas digan algo.

La raíz del repositorio se busca subiendo hasta encontrar `Nitido.slnx`, como ya hace `CasosDorados_CarpetaExiste`. Las pruebas no dependen del directorio de trabajo.

**Prefijos de ensamblados de tecnología** (lista única en la prueba): `Microsoft.EntityFrameworkCore`, `Microsoft.Data.SqlClient`, `Microsoft.AspNetCore`, `Serilog`, `Hangfire`, `FluentValidation`, `Microsoft.Extensions.`, y los de nube `Azure.`, `Amazon`, `AWSSDK` y `Google.Cloud`.

### 5.3 Pruebas

| Prueba | Qué falla |
| --- | --- |
| `Solucion_ProyectosSiguenConvencionDeNombresYCarpetas` | Un proyecto de `Nitido.slnx` que no sea el host, uno de los tres compartidos, uno de `tests/` o `Modulos/<M>/<Capa>/Nitido.<M>.<Capa>.csproj` con `Capa` ∈ {Dominio, Aplicacion, Contratos, Infraestructura, Api}. También falla si no encuentra `Nitido.Api` y `Nitido.Compartido`, para no pasar en vacío. |
| `Compartido_SoloDependeDelFramework` | `Nitido.Compartido` declara un proyecto, paquete o marco, o su ensamblado usa algo distinto de `System.*`, `netstandard` o `mscorlib`. |
| `Dominio_SoloDependeDeCompartido` | Un `*.Dominio` referencia algo distinto de `Nitido.Compartido`, declara paquetes o marcos, o su ensamblado usa algo fuera de la librería base y `Nitido.Compartido`. Cubre EF, ASP.NET, Serilog, FluentValidation y nube. |
| `Contratos_SoloDependenDeCompartido` | Lo mismo para `*.Contratos`. |
| `Aplicacion_NoDependeDeInfraestructuraNiWeb` | Un `*.Aplicacion` referencia proyectos fuera de su `Dominio`, los `Contratos` y `Nitido.Compartido`; declara un paquete fuera de `FluentValidation` y `Microsoft.Extensions.Logging.Abstractions`; tiene `FrameworkReference`; o su ensamblado usa EF, SqlClient, ASP.NET, Serilog, Hangfire o nube. |
| `Api_NoReferenciaInfraestructura` | Un `*.Api` referencia algo distinto de su `Aplicacion` y `Nitido.Compartido.Api`, o declara EF Core. Revisa también a `Nitido.Compartido.Api`: que no declare paquetes `Microsoft.EntityFrameworkCore*` y que su ensamblado no use EF Core ni `Nitido.Compartido.Infraestructura`. |
| `Infraestructura_NoReferenciaApi` | Un `*.Infraestructura` referencia su `Api`, `Nitido.Compartido.Api`, o proyectos de otro módulo que no sean `Contratos`. |
| `Modulos_SoloSeReferencianPorContratos` | Un proyecto del módulo A referencia un proyecto del módulo B que no sea `B.Contratos`. |
| `InfraestructuraYApi_SoloLasReferenciaElHost` | Un proyecto distinto del host o de las pruebas referencia un `*.Infraestructura` o un `*.Api`; `Nitido.Compartido.Infraestructura` lo referencia algo que no sea `*.Infraestructura` o el host; o `Nitido.Compartido.Api` lo referencia algo que no sea `*.Api` o el host. |
| `Host_ComponeTodosLosModulos` | `Nitido.Api.csproj` no referencia el `Infraestructura` y el `Api` de cada módulo de la solución. |
| `ArchivosComunesDeMsBuild_NoAgreganReferencias` | `Directory.Build.props`, `.targets` o `Directory.Packages.props` (también anidados) agrega referencias o importa otros archivos. Eso se saltaría la lectura estática. Detalle y excepción en 5.5. |
| `SdkDeNube_SoloEnProyectosDeProveedor` | **Reemplaza a `Compartido_NoDependeDeSdkDeNube`** (ver 5.4). |
| `ReglasDeDependencia_DetectanCadaInfraccion` | Teoría con modelos sintéticos: al menos un caso que infringe cada regla anterior, y la regla lo marca. |
| `ReglasDeDependencia_AceptanUnModuloBienFormado` | Un modelo sintético de módulo según la plantilla (con `Contratos` usado por otro módulo) no produce infracciones. |

Las dos pruebas de detección que ya existen (`DeteccionDeSdkDeNube_MarcaSoloLosPrefijosProhibidos` y `DeteccionDeSdkDeNube_DevuelveSoloLosMarcadosDeUnaLista`) se conservan sin cambios.

### 5.4 La regla 7 existente

`Compartido_NoDependeDeSdkDeNube` se **renombra** a `SdkDeNube_SoloEnProyectosDeProveedor` y se **amplía**:
- además de la reflexión que ya hace, revisa los `PackageReference` de cada `.csproj`;
- carga todos los proyectos de la solución por nombre, no solo los alcanzables desde el host.

La prueba tiene una lista explícita de proyectos de proveedor autorizados, que **arranca vacía**. Así su efecto hoy es el mismo que antes: ningún ensamblado `Nitido.*` puede usar un SDK de nube.

Cuando se decida el despliegue, el ADR correspondiente agrega a la lista el proyecto de cada proveedor (por ejemplo una implementación de `IAlmacenArchivos` para Azure Blob), y solo ese. Es lo que pide la regla 7: «una implementación local y una por proveedor», nunca en el dominio ni en los casos de uso.

El nombre anterior quedaba corto: no era solo Compartido y ya recorría todos los ensamblados.

### 5.5 Reglas adicionales

Las pruebas de 5.3 hacen cumplir además lo siguiente (documentado el 2026-10-18, tras la revisión de F0-01b):

**Clasificación y referencias**
- Un proyecto es de pruebas solo si está bajo `tests/` **y** se llama `Nitido.Pruebas.*`. Si cumple solo una de las dos, es infracción, y además se le aplican las reglas de producción (reflexión incluida). Su ruta debe ser `tests/<tipo>/<Nombre>/<Nombre>.csproj`.
- Ningún proyecto de producción referencia un proyecto que parezca de pruebas (por ruta o por nombre).
- Las pruebas pueden referenciar cualquier `Infraestructura` y `Api`, de los módulos y de los compartidos.
- `Nitido.Compartido.Infraestructura` y `Nitido.Compartido.Api` solo dependen de `Nitido.Compartido`.
- Todo módulo que aparezca en la solución exige su `Infraestructura` y su `Api` en `Nitido.slnx`, referenciados por el host. `Dominio` y `Contratos` son opcionales (4.2 y 4.3).
- Todo `ProjectReference` debe apuntar a un proyecto de `Nitido.slnx`. Uno que apunte fuera de la solución es infracción, porque las reglas no lo verían.

**Lectura de cada `.csproj`** (lo que no se puede seguir sin evaluar MSBuild es infracción)
- Los nombres de los elementos de MSBuild se comparan sin distinguir mayúsculas (`<projectreference>` cuenta igual que `<ProjectReference>`).
- Un `Include` con varios valores separados por `;` se lee valor por valor.
- En `ProjectReference`, `PackageReference` y `FrameworkReference`, un valor con comodín o propiedad (`*`, `?`, `$(`, `@(`, `%(`) es infracción.
- Prohibidos: `Import`, `Reference` (ensamblados sueltos) y `Compile` que salga de la carpeta del proyecto (ruta absoluta, con unidad o con `..`).
- `Compile Include` con `$(`, `@(` o `%(` es infracción, salvo `$(MSBuildThisFileDirectory)` o `$(MSBuildProjectDirectory)` sin `..`.
- El Sdk Web cuenta como `FrameworkReference` a `Microsoft.AspNetCore.App`, se declare como se declare: en el atributo `Sdk`, con versión (`Microsoft.NET.Sdk.Web/1.0`), entre varios separados por `;`, con el elemento `<Sdk>` o con `<Import Sdk>`. `Microsoft.NET.Sdk.Razor` se trata igual, por precaución.

**Archivos comunes de MSBuild**
- `Directory.Build.props`, `Directory.Build.targets` y `Directory.Packages.props`, en la raíz y anidados, no agregan `PackageReference`, `ProjectReference`, `FrameworkReference`, `GlobalPackageReference` ni `Reference`, ni tienen `Import`.
- Única excepción: un `Directory.Build.props` o `.targets` **anidado** puede importar el del mismo nombre de la carpeta de arriba con la cadena estándar `$([MSBuild]::GetPathOfFileAbove('<mismo archivo>', '$(MSBuildThisFileDirectory)../'))`. En los de la raíz esa cadena es infracción, porque saldría del repositorio.

**Carga de ensamblados**
- Si el ensamblado de un proyecto no está en la salida de las pruebas, el mensaje es «el host no lo compone».
- Si está pero no se puede cargar (`FileLoadException`, `BadImageFormatException`), el mensaje es distinto y lleva el tipo y el texto de la excepción.

---

## 6. Impacto en lo construido y en las historias

### 6.1 Código y pruebas actuales

| Pieza | Cambio |
| --- | --- |
| `Nitido.Compartido` | Gana `Resultados/` (`Resultado`, `Resultado<T>`, `Error`, `TipoError`) en F0-01b. `MarcadorDeEnsamblado` se queda, porque lo usan las pruebas. El `.csproj` sigue sin referencias. |
| `Nitido.Api` | Sin cambios en F0-01b. Desde F0-02a: referencias a `Infraestructura` y `Api` de cada módulo, `TimeProvider.System`, `Agregar<M>()` y `Mapear<M>()` (4.4), y `Microsoft.EntityFrameworkCore.Design` con `PrivateAssets=all`, para que funcione `dotnet ef`. |
| `Nitido.slnx` | Sin cambios en F0-01b. Desde F0-02a: carpetas `/src/Modulos/<M>/` y los compartidos nuevos en `/src/`. |
| `Modulos/.gitkeep` | Se queda hasta F0-02a. |
| Pruebas (23) | Siguen en verde: 22 sin cambios y 1 renombrada y ampliada (5.4). Se agregan las de 5.3 y las de `Resultado`. `ArquitecturaPruebas.cs` puede moverse a `tests/unidad/Nitido.Pruebas.Unidad/Arquitectura/`. |
| `Directory.Packages.props` | Sin paquetes nuevos en F0-01b. |

### 6.2 Por especificación

Cada especificación afectada lleva una nota «Ver ADR 0001» al inicio. Lo que sigue son ajustes de diseño técnico (se aplican en el paso 3 de cada historia) y, aparte, **conflictos con criterios**. Los conflictos se propusieron al usuario y quedaron decididos y aplicados a las especificaciones el 2026-10-18 (los siete puntos recomendados, aceptados).

**F0-01 (terminada).**
- Ajuste: la prueba `Compartido_NoDependeDeSdkDeNube` se renombra en F0-01b. El criterio queda como registro histórico.

**F0-02a · Conexión y tablas.**
- Ajustes de diseño:
  1. Firmas y Clientes se crean con la plantilla 4.3. Se crea `Nitido.Compartido.Infraestructura` con la convención de nombres, la auditoría como propiedades sombra y el interceptor de auditoría.
  2. **Dueño de cada tabla:**
     - Firmas: `T_FIRMAS` y `T_USUARIOS`.
     - Clientes: `T_EMPRESAS` y `T_USUARIOS_EMPRESAS`. Así, «crear cliente y dar acceso a quien lo crea» (F0-04) es un solo `SaveChanges`.
     - La llave foránea `T_USUARIOS_EMPRESAS.UUID_USUARIOS → T_USUARIOS` se crea en la migración de Clientes, por nombre de tabla.
  3. **Orden de migraciones:** Firmas antes que Clientes, porque Clientes depende de las tablas de Firmas. El orden se fija en un solo lugar del host o del ayudante de pruebas, y lo usa `Migraciones_SeAplicanSobreBaseLimpia`.
  4. **La clase que mapea `T_USUARIOS` vive en `Firmas.Infraestructura`, no en el dominio.** En F0-03a se convierte en el usuario de Identity sin mover nada (6.3). `Firmas.Dominio` tiene `Firma` y los objetos de valor que hagan falta.
  5. **Prueba nueva propuesta:** `Tablas_PertenecenAUnSoloDbContext`. Construye el modelo de cada `DbContext`, sin conectarse, y falla si dos contextos mapean la misma tabla. Las pruebas de arquitectura no pueden ver eso.
- **Conflicto 1:** el criterio pide `Dominio_NoReferenciaEntityFramework`, y F0-01b trae `Dominio_SoloDependeDeCompartido`, que es más estricta. Propuesta: cambiar el criterio para que pida que `Dominio_SoloDependeDeCompartido` pase con los módulos nuevos (o conservar el nombre viejo como un caso más de la teoría, si se prefiere la trazabilidad).
- **Conflicto 2:** el criterio de auditoría nombra `IUsuarioActual`. Propuesta: usar `IContextoSesion` con solo `UuidUsuarios` en F0-02a, y que F0-02b le agregue la firma y el cliente (3.7).

**F0-02b · Multiempresa.**
- Ajustes de diseño:
  - `IDeEmpresa`, `IDeFirma` e `IContextoSesion` van en `Nitido.Compartido/Multiempresa/`. Esto coincide con el criterio.
  - El filtro por convención y el interceptor de `SESSION_CONTEXT` van en `Nitido.Compartido.Infraestructura`.
  - Se crea `Nitido.Compartido.Api` con el filtro de endpoint del cliente activo.
  - La implementación de `IContextoSesion` solo para pruebas vive en `tests/`. La prueba `ContextoDePrueba_NoSeRegistraEnLaApi` ya cubre que el host no la registre.
- Sin conflictos.

**F0-03a · Ingreso con contraseña.**
- Ajustes de diseño:
  - Identity (`AddIdentityCore`, stores y `UserManager`) vive en `Firmas.Infraestructura`, nunca en `Dominio` ni en `Aplicacion`. La clase de usuario es la de 6.2-4, que pasa a heredar de `IdentityUser<Guid>`.
  - Los casos de uso de autenticación (`Ingresar`, `Salir`, `ObtenerSesion`) van en `Firmas.Aplicacion`, detrás de un puerto (por ejemplo `IIdentidad`) que Infraestructura implementa con `UserManager` y `SignInManager`. Ese puerto conserva la respuesta genérica.
  - La cookie, la antifalsificación, el limitador y la política de autorización por defecto van en el host.
  - La semilla de Development va en `Firmas.Infraestructura` y el host la invoca solo en Development.
  - El ADR de autenticación confirma estos puntos.
- **Observación:** el criterio de `401` pide el cuerpo `{"mensaje": ...}`, que no es ProblemDetails, mientras F0-01 decidió que toda respuesta de error usa ProblemDetails. No es un conflicto con este ADR, pero la conversión de 3.2 produciría ProblemDetails. Propuesta: `401` en ProblemDetails, con el texto en `title` (el frontend lee `title`). Decidido el 2026-10-18: sí, ProblemDetails con el texto en `title`.

**F0-03b · Doble factor.**
- Ajustes de diseño: el almacén de códigos de recuperación con hash y la marca del último paso TOTP van en `Firmas.Infraestructura`, y la configuración de Data Protection en el host (como ya dice la especificación).
- Sin conflictos.

**F0-04 · Firmas y clientes.**
- Ajustes de diseño:
  - Casos de uso:
    - `RegistrarFirma` va en `Firmas.Aplicacion`, con una transacción explícita, porque `UserManager` guarda por su cuenta (3.5).
    - `CrearCliente` va en `Clientes.Aplicacion`.
    - La lista y el detalle son `IConsultasClientes`, con proyección.
  - `Nit`, con el cálculo del dígito de verificación, va en `Clientes.Dominio`, y `DigitoVerificacion_CalculaComoLaDian` es una prueba de unidad del dominio.
  - Los `409` y `400` salen de `Resultado` (3.2). La carrera del índice único (`UX_T_EMPRESAS_...`) se traduce a `Conflicto` en el repositorio.
  - `Firmas.Contratos` se crea solo si Clientes de verdad necesita algo de Firmas. La firma sale de `IContextoSesion`, así que es probable que no haga falta.
- **Conflicto:** el criterio pide `Clientes_NoReferenciaInfraestructuraDeFirmas`, y F0-01b trae `Modulos_SoloSeReferencianPorContratos`, que lo cubre para todos los módulos. Propuesta: cambiar el criterio para que pida esa prueba, con Firmas y Clientes ya en la solución.

**F0-06 · Integración continua.**
- Sin cambios en los criterios.
- Las pruebas de arquitectura corren dentro de `dotnet test -c Release` y leen archivos del repositorio, así que el trabajo debe correr desde la raíz del checkout.
- Hay que vigilar que la meta de 15 minutos se mantenga con más proyectos.

**F0-05a, F0-05b y F0-05c (frontend):** sin impacto. El contrato OpenAPI no cambia por esta decisión.

### 6.3 Puntos que conectan varias historias

- **`IContextoSesion` único** (3.7): F0-02a lo crea con el usuario y F0-02b lo completa.
- **Usuario e Identity:** la clase de persistencia de `T_USUARIOS` vive en `Firmas.Infraestructura` desde F0-02a. Así, el dominio nunca depende de `Microsoft.Extensions.Identity.Stores`, que exige heredar de `IdentityUser<TKey>`.
  - La alternativa es un `Usuario` en el dominio con un store propio de Identity: unas 150 líneas más y credenciales dentro del dominio.
  - La decisión final es del ADR de autenticación.
- **Números de ADR:** este es el 0001. Los pendientes de la fase 0 (DbContext por módulo, multiempresa y autenticación) toman 0002 en adelante, en el orden en que se escriban.

---

## 7. Plan de implementación

La base es la historia **F0-01b · Base de Clean Architecture** (1 día), con su especificación completa en `docs/specs/f0-01b-clean-architecture.md`. Va después de F0-01 y antes de F0-02a.

**Alcance:**
- `Resultado` en Compartido;
- las pruebas de 5.3;
- la plantilla documentada en 4.3.

**No incluye un módulo de ejemplo:** sería código descartable, y las reglas ya se prueban con modelos sintéticos. Los módulos reales se crean con la plantilla en F0-02a.

| Paso | Agente | Qué | En paralelo con |
| --- | --- | --- | --- |
| 0 | usuario | Acepta el ADR 0001 y decide los puntos abiertos (sección 9 y spec F0-01b). | ninguno |
| 1 | `tester` | Pruebas de `Resultado`, que fallan porque el tipo no existe. Modelo de solución, reglas, casos sintéticos y pruebas sobre la solución real (5.3). Renombra y amplía la prueba de la regla 7 (5.4). | paso 2, cuando las pruebas de `Resultado` estén escritas |
| 2 | `dev-backend` | `Nitido.Compartido/Resultados/`. Nada más en producción. | paso 1 (reglas) |
| 3 | `tester` | Verificación: `dotnet build` con 0 advertencias y 0 errores, `dotnet format --verify-no-changes` y `dotnet test` con las 23 anteriores más las nuevas. | ninguno |
| 4 | `revisor` | Revisión: que las reglas no pasen en vacío, que la lista de proveedores de nube esté vacía y que no haya paquetes nuevos. | ninguno |

`dba` y `devops` no participan.

---

## 8. Riesgos y lo que no se hace

### Riesgos

| Riesgo | Mitigación |
| --- | --- |
| **Ceremonia:** muchos archivos para casos simples (entidad, comando, validador, caso de uso, puerto, repositorio, configuración, endpoint, solicitud, respuesta). | Las consultas sin reglas van del endpoint a `IConsultas<M>` sin caso de uso intermedio. `Contratos` y `Dominio` se crean solo cuando hacen falta. No hay interfaces para los casos de uso. |
| **Compilación más lenta** con 40 a 60 proyectos. | Se mide en F0-06. Para el trabajo diario se compila el proyecto afectado. Si llega a molestar, se evalúa en un ADR, sin juntar capas. |
| **Multiempresa:** el filtro de EF vive en Infraestructura, y una consulta con SQL a mano lo salta. | La seguridad por fila sigue como segunda barrera (F0-02b). El SQL a mano solo va en Infraestructura y lo revisa el `revisor`. La firma siempre sale de `IContextoSesion`, nunca del cuerpo de la petición. |
| **Transacciones entre módulos** imposibles por diseño. | Eventos con bandeja de salida (ADR en la fase 2). Si un caso lo exige, transacción compartida con un ADR. |
| **Migraciones entre módulos** en desorden, por llaves foráneas cruzadas. | Orden fijo en un solo lugar (6.2, F0-02a), probado sobre una base limpia. |
| **Identity se cuela en el dominio.** | La clase de usuario vive en Infraestructura (6.3), y `Dominio_SoloDependeDeCompartido` lo detecta. |
| **Compartido se vuelve un cajón de sastre.** | Regla de entrada de cuatro condiciones (2). La prueba impide paquetes y el `revisor` impide reglas de negocio. |
| **Reportes pesados** de la fase 4 que leen tablas de varios módulos. | Se decide en un ADR en la fase 4 (vistas o SQL de solo lectura en Reportes.Infraestructura), sin romper la regla en el código. |
| **Datos existentes:** no hay. | Nada que migrar. |

### Lo que no se hace

- MediatR, AutoMapper, ErrorOr, FluentResults, Scrutor, NetArchTest o ArchUnitNET.
- Repositorio genérico, unidad de trabajo global, patrón Specification o `IQueryable` fuera de Infraestructura.
- Interfaces o decoradores para los casos de uso, buses de comandos o tuberías.
- Bus de eventos o bandeja de salida antes de que un módulo publique un evento.
- Un proyecto por caso de uso, proyectos de pruebas por módulo o plantilla de `dotnet new`.
- Un módulo de ejemplo.
- Lógica de negocio en Compartido.
- Base de lectura separada.

---

## 9. Cambios propuestos a archivos que el arquitecto no edita

Los decidió el usuario el 2026-10-18 («Sí, implementa lo que indica el arquitecto») y quedaron aplicados ese mismo día. El texto de abajo se conserva como registro.

1. **`backend/CLAUDE.md`, sección «Estructura»,** reemplazar las tres primeras líneas del bloque por:
   ```
   Nitido.Api/                         host de ASP.NET Core: raíz de composición
   Nitido.Compartido/                  núcleo puro: Resultado, multiempresa, puertos comunes (sin paquetes)
   Nitido.Compartido.Infraestructura/  EF Core común: convenciones, auditoría, filtro y SESSION_CONTEXT
   Nitido.Compartido.Api/              web común: Resultado a ProblemDetails, filtro de cliente activo
   Modulos/<Modulo>/                   Dominio, Aplicacion, Contratos (si otro módulo lo usa), Infraestructura, Api
   ```
   Y agregar en «Cómo se trabaja»: «Antes de crear un módulo o mover código entre capas, lee `docs/arquitectura-backend.md`. Las pruebas de arquitectura deben seguir en verde.»
2. **`backend/CLAUDE.md`, «Reglas que nunca se rompen»:** no se agrega una regla nueva. La regla de dependencias se hace cumplir con pruebas, y no hace falta repetirla allí.
3. **`CLAUDE.md` de la carpeta raíz `nitido/`:** sigue describiendo el monorepo (`apps/web/`, `src/Modulos/...`). Conviene alinearlo con `backend/CLAUDE.md` o reducirlo a un índice de los dos repositorios.
4. **Skill `convenciones-bd`, sección «Migraciones»:** agregar:
   - «Un `DbContext` por módulo, con historial `__EFMigrationsHistory_<Modulo>`.»
   - «Ningún `DbContext` mapea tablas de otro módulo; las llaves foráneas entre módulos se crean por nombre de tabla en la migración del módulo que referencia.»
   - «Las columnas de auditoría son propiedades sombra configuradas por convención en `Nitido.Compartido.Infraestructura`.»
   - Sigue pendiente, del índice de la fase 0: «una migración por módulo afectado en cada historia».
5. **Agente `dev-backend`, «Cómo escribes»:**
   - Reemplazar «El dominio no depende de EF Core ni de ASP.NET» por «Sigue `docs/arquitectura-backend.md`: Dominio solo depende de `Nitido.Compartido`; Aplicacion no depende de EF Core, ASP.NET Core ni Serilog; un módulo solo ve los `Contratos` de otro.»
   - Agregar: «Errores de negocio con `Resultado`, no con excepciones. Sin MediatR ni AutoMapper.»
6. **Agente `revisor`, punto 4 «Convenciones»:** agregar «capa correcta (lógica fuera de los endpoints, SQL a mano solo en Infraestructura) y regla de entrada a `Nitido.Compartido`».
7. **Agente `tester`:** agregar en «Invariantes»: «Las pruebas de arquitectura (`tests/unidad/.../Arquitectura/`) nunca se debilitan; una excepción a una regla exige un ADR.»
8. **`docs/plan-de-desarrollo.md`, «Repositorio y convenciones»:**
   - Cambiar «en inglés para lo técnico (`Repository`, `Handler`)» por «también en español para lo técnico propio (`IRepositorioClientes`, `CrearCliente`); los tipos del marco conservan su nombre».
   - En «Arquitectura», enlazar este documento.
   - El plan es copia de un documento del usuario; por eso no se editó aquí.
