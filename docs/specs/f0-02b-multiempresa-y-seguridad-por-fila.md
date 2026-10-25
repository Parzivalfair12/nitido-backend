# F0-02b · Fase 0 · 2b · Multiempresa: filtro global y seguridad por fila

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | F0-02a |
| Orden de construcción | 4 de 10, después de F0-06 |
| Etiquetas | `fase-0`, `historia`, `base-de-datos`, `multiempresa`, `seguridad`, `backend`, `requiere-adr` |
| Estado | Especificación aprobada. Diseño técnico propuesto el 2026-10-24 (paso 3), con el [ADR 0003](../adr/0003-multiempresa-en-dos-barreras.md) en estado «Propuesto»: falta la aprobación del usuario (ver «Decisiones que aprueba el usuario»). Sin issue en GitHub. |

> **Ver ADR 0001** y `docs/arquitectura-backend.md` (sección 6.2). El filtro global y el interceptor de `SESSION_CONTEXT` van en `Nitido.Compartido.Infraestructura`. El filtro del cliente activo va en `Nitido.Compartido.Api`, que se crea en esta historia. `IContextoSesion` ya existe desde F0-02a con solo el usuario, y esta historia le agrega la firma y el cliente activo (decisión del 2026-10-18: no hay `IUsuarioActual`). Los demás criterios no cambian.

## Historia

Como firma de contadores quiero que ningún dato de un cliente se pueda ver desde otra firma, ni desde un cliente al que no tengo acceso, para cumplir con la confidencialidad que me exigen mis clientes.

## Criterios de aceptación

### Piezas del backend

- [ ] `Nitido.Compartido` expone tres piezas:
  - `IDeEmpresa`, con `UuidEmpresas`;
  - `IDeFirma`, con `UuidFirmas`;
  - `IContextoSesion`, con el usuario, la firma y el cliente activo de la petición. Es la misma interfaz que F0-02a creó solo con `UuidUsuarios` (`UuidUsuarios`, `UuidFirmas`, `UuidEmpresas?`); aquí se completa, sin crear otra. (Ver ADR 0001)
- [ ] Cada `DbContext` aplica el filtro global por convención a toda entidad que implemente `IDeEmpresa` o `IDeFirma`. La prueba `FiltroGlobal_CubreTodaEntidadDeEmpresaYFirma` recorre el modelo de EF y falla si alguna queda sin filtro.
- [ ] Un interceptor de conexión fija `SESSION_CONTEXT` en cada conexión que se abre, con `@read_only = 1`, para `UUID_USUARIOS`, `UUID_FIRMAS` y `UUID_EMPRESAS`.

### Seguridad por fila

- [ ] El script versionado en `db/seguridad-por-fila/`, aplicado por migración, crea dos cosas:
  - una función de predicado que valida la firma de la sesión y las empresas permitidas del usuario según `T_USUARIOS_EMPRESAS`;
  - una política de seguridad con predicados FILTER y BLOCK sobre `T_EMPRESAS`, `T_USUARIOS_EMPRESAS` y `T_USUARIOS`.
- [ ] La prueba `SeguridadPorFila_CubreTodaTablaDeEmpresaYFirma` consulta `sys.security_predicates` y `sys.security_policies`, y falla en dos casos:
  - una tabla con `UUID_EMPRESAS` o `UUID_FIRMAS` queda sin predicado;
  - la política no está activa (`is_enabled = 1`).

### Cliente activo

- [ ] Un filtro de endpoint valida que el `{uuidCliente}` de las rutas `/api/clientes/{uuidCliente}/...` esté en las empresas permitidas del usuario. Si no lo está, responde `404`. La prueba `ClienteEnRuta_SinAccesoResponde404` lo comprueba sobre un endpoint de prueba.

### Pruebas de aislamiento

Todas son de integración con Testcontainers y usan dos firmas sembradas.

- [ ] `Multiempresa_UsuarioDeOtraFirmaNoVeClientes`: una consulta por EF devuelve 0 filas de la otra firma.
- [ ] `SeguridadPorFila_SinFiltroDeEfNoDevuelveFilasAjenas`: con `IgnoreQueryFilters()` y con SQL directo, devuelve 0 filas ajenas.
- [ ] `SeguridadPorFila_BloqueaEscribirEnOtraFirma`: un `INSERT` o `UPDATE` con la firma de otro falla.
- [ ] `SeguridadPorFila_SinContextoNoDevuelveNada`.
- [ ] `SeguridadPorFila_UsuarioSinPermisoNoVeElCliente`: mismo usuario y misma firma, pero el cliente no está en `T_USUARIOS_EMPRESAS`.
- [ ] `Pool_ConexionReutilizadaNoConservaContexto`.

### Restricción de pruebas

- [ ] Hasta que exista el ingreso (F0-03a), las pruebas usan una implementación de `IContextoSesion` solo para pruebas. La prueba `ContextoDePrueba_NoSeRegistraEnLaApi` comprueba que el host no la registra.

## Pantalla

No aplica.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables.

## Diseño técnico

24 de octubre de 2026 · arquitecto. Repositorio: solo `nitido-backend`; no hay pull request del frontend. Decisión de base: [ADR 0003](../adr/0003-multiempresa-en-dos-barreras.md) (propuesto), sobre ADR 0001 y ADR 0002 (aceptados).

### Puntos que ya estaban decididos

Se conservan; el detalle de abajo los aplica.

- **El cliente activo viaja en la ruta** (`/api/clientes/{uuidCliente}/...`), no en la sesión. No guarda estado y aguanta varias pestañas abiertas con clientes distintos, que es lo normal en una firma con muchos clientes. Se descartaron el encabezado `X-Cliente` y el cliente guardado en la sesión.
- **Contexto en SQL:** un `DbConnectionInterceptor` llama a `sp_set_session_context` con `@read_only = 1` al abrir la conexión. La prueba del pool confirma que el contexto se limpia.
- **Predicado:** firma de la sesión más un `EXISTS` sobre `T_USUARIOS_EMPRESAS`, con índice. Así lo pide `convenciones-bd`, que exige la segunda barrera con las empresas permitidas. El ADR amplía el contexto de sesión con `UUID_FIRMAS` y `UUID_USUARIOS`.
- **Usuario de base de datos:** en local se acepta `sa`, el del contenedor de VS Code. Un usuario de aplicación sin permiso de `ALTER ANY SECURITY POLICY`, distinto del de migraciones, se exige cuando se defina el entorno de nube. No hay despliegue en la fase 0.

### Decisiones que aprueba el usuario

Las tomó el arquitecto como «decisión propuesta» porque la especificación no las fijaba. Todas están en el ADR 0003.

1. **`T_USUARIOS_EMPRESAS` gana la columna `UUID_FIRMAS`,** con una llave foránea compuesta hacia `T_EMPRESAS (UUID_EMPRESAS, UUID_FIRMAS)`. Sin ella, el predicado de `T_USUARIOS_EMPRESAS` tendría que consultar `T_EMPRESAS`, cuyo predicado consulta `T_USUARIOS_EMPRESAS` (un ciclo), y un usuario podría darse acceso a un cliente de otra firma. F0-02a ya dejaba abierta esta columna «si F0-02b la necesita» (2.2).
2. **`T_FIRMAS` también lleva política,** aunque el criterio nombra solo tres tablas: tiene `UUID_FIRMAS`, y la prueba `SeguridadPorFila_CubreTodaTablaDeEmpresaYFirma` la exige.
3. **Sin sesión, las tres llaves de `SESSION_CONTEXT` se fijan en `Guid.Empty` y de solo lectura,** en vez de dejarlas vacías, para que nadie pueda ponerles un valor después en esa conexión. Lo respalda un `CHECK` nuevo, `CK_T_FIRMAS_UUID_FIRMAS`, que impide una firma con llave vacía.
4. **Historial: solo FILTER, nunca BLOCK.** El versionado del sistema escribe en el historial y no se le pone nada que lo pueda frenar.
5. **Una política y sus funciones por módulo** (`seguridad.PS_FIRMAS`, `seguridad.PS_CLIENTES`), en el esquema `seguridad`, sin funciones compartidas entre módulos.
6. **El cliente activo solo llega a `IContextoSesion.UuidEmpresas` después de que el filtro de endpoint lo valida.** Un `{uuidCliente}` sin validar nunca llega al filtro de EF ni a `SESSION_CONTEXT`.
7. **Puerto `IAccesoAClientes` en `Nitido.Compartido/Multiempresa/`,** implementado por Clientes. Es la única forma de que `Nitido.Compartido.Api` pregunte por el acceso sin ver un módulo. Es una excepción razonada a la regla «lo usan dos módulos hoy».
8. **El interceptor de auditoría también falla si `UuidUsuarios` es `Guid.Empty`.** Hasta ahora fallaba solo si no había `IContextoSesion` registrado; desde esta historia el host siempre registra uno.
9. **Para F0-04 y F0-03a (no se construyen aquí):** registro de firma y semilla de Development con un alcance de contexto explícito, sin puertas en el predicado; ingreso con una llave `CORREO_INGRESO` que deja ver solo la fila del correo que ingresa.

### 1. Módulos y proyectos que cambian

| Proyecto | Cambio | Por qué |
| --- | --- | --- |
| `Nitido.Compartido` | `Multiempresa/`: `IDeEmpresa`, `IDeFirma`, `IContextoSesion` completo, `IAccesoAClientes`. | Contratos de multiempresa que usan los compartidos y los módulos (criterio; decisión 7). |
| `Nitido.Compartido.Infraestructura` | Filtro global en `DbContextNitido`, `InterceptorDeContextoSql`, regla nueva del interceptor de auditoría y `ScriptsSql` (lee los scripts incrustados). | Primera barrera y contexto de la segunda (criterio, ADR 0001). |
| `Nitido.Compartido.Api` (nuevo) | `ContextoSesionHttp`, `ReclamosDeSesion`, filtro del cliente activo, `MapearRutasDelCliente` y `AgregarApiNitido`. | El filtro de endpoint y la implementación del host de `IContextoSesion` (criterio, arquitectura 2). |
| `Nitido.Firmas.Dominio` | Sin cambios. | `Firma` no implementa `IDeFirma` (su llave es la firma); su filtro va en el `DbContext`. |
| `Nitido.Firmas.Infraestructura` | `Usuario : IDeFirma`; `FirmasDbContext` declara el filtro de `Firma`; `CHECK` en `FirmaConfiguracion`; migración `AgregarSeguridadPorFilaDeFirmas`; recurso incrustado `0001-firmas-politica.sql`. | Política de `T_FIRMAS` y `T_USUARIOS`. |
| `Nitido.Clientes.Dominio` | `Cliente : IDeFirma`; `AccesoCliente : IDeFirma` con `UuidFirmas`, que llena `AccesoCliente.Crear(uuidFirmas, uuidUsuarios, ahora)` desde `Cliente.Crear`. | Decisión 1. |
| `Nitido.Clientes.Infraestructura` | Llave alterna y llave foránea compuesta en las configuraciones; `Persistencia/AccesoAClientes.cs` (implementa `IAccesoAClientes`); registro en `AgregarClientes`; migración `AgregarSeguridadPorFilaDeClientes`; recurso incrustado `0002-clientes-politica.sql`. | Política de `T_EMPRESAS` y `T_USUARIOS_EMPRESAS`; puerto del filtro. |
| `Nitido.Firmas.Api`, `Nitido.Clientes.Api` | + `ProjectReference` a `Nitido.Compartido.Api` (plantilla 4.3). Sin rutas nuevas. | Lo pide la plantilla desde que existe el proyecto; los grupos de rutas de cliente llegan en F0-04. |
| `Nitido.Api` (host) | + `ProjectReference` a `Nitido.Compartido.Api`; `builder.Services.AgregarApiNitido()` antes de los módulos. | Registra la implementación real de `IContextoSesion`. |
| `Nitido.slnx` | `Nitido.Compartido.Api/Nitido.Compartido.Api.csproj` en `/src/`. | Convención de la solución. |

Ningún módulo habla con otro: Clientes no usa nada de Firmas en C#. La única relación nueva entre módulos es por nombre en SQL (la política de Clientes no usa funciones de Firmas; ver 5.2).

#### 1.1 `Nitido.Compartido/Multiempresa/`

Interfaces puras, sin paquetes (`Compartido_SoloDependeDelFramework`):

```csharp
public interface IDeFirma   { Guid UuidFirmas { get; } }
public interface IDeEmpresa { Guid UuidEmpresas { get; } }

public interface IContextoSesion
{
    /// Usuario que actúa. Guid.Empty si la petición no tiene sesión.
    Guid UuidUsuarios { get; }
    /// Firma del usuario. Guid.Empty si no hay sesión.
    Guid UuidFirmas { get; }
    /// Cliente activo: el {uuidCliente} de la ruta, ya validado por el filtro de endpoint.
    /// null si la ruta no lleva cliente o si todavía no se ha validado.
    Guid? UuidEmpresas { get; }
}

public interface IAccesoAClientes
{
    /// true si el cliente es de la firma de la sesión y el usuario de la sesión tiene acceso en T_USUARIOS_EMPRESAS.
    Task<bool> PuedeEntrarAsync(Guid uuidEmpresas, CancellationToken ct);
}
```

`Guid.Empty` significa «sin sesión» en todo el sistema. No se agrega una propiedad `TieneSesion`: quien la necesite compara con `Guid.Empty`.

#### 1.2 `Nitido.Compartido.Infraestructura`

| Pieza | Visibilidad | Qué hace |
| --- | --- | --- |
| `Persistencia/DbContextNitido` (cambia) | pública, abstracta | Gana la constante pública `FiltroMultiempresa = "Multiempresa"` y tres propiedades de instancia **no públicas**: `UsuarioDeLaSesion`, `FirmaDeLaSesion` (`Guid`) y `EmpresaDeLaSesion` (`Guid`, `Guid.Empty` si `UuidEmpresas` es `null`). Leen un `IContextoSesion` que se resuelve la primera vez con `this.GetService<IContextoSesion>()` y se guarda en la instancia; si no hay uno registrado, usan un contexto interno sin sesión (todo `Guid.Empty`). En `OnModelCreating`, después de aplicar las configuraciones, recorre las entidades (no propias, sin tipo base) y a cada una que implemente `IDeFirma` o `IDeEmpresa` le pone `HasQueryFilter(FiltroMultiempresa, filtro)` (ver 3). |
| `Persistencia/InterceptorDeContextoSql` (nuevo) | interna, singleton | `DbConnectionInterceptor` con `ConnectionOpened` y `ConnectionOpenedAsync` (ver 4). |
| `Persistencia/InterceptorDeAuditoria` (cambia) | interna | Si `UuidUsuarios` es `Guid.Empty`, falla con «No se puede guardar sin saber qué usuario actúa: el IContextoSesion no tiene usuario.» (conserva las dos frases que revisan las pruebas actuales). |
| `Persistencia/ScriptsSql` (nuevo) | pública, estática | `Leer(Assembly ensamblado, string nombreLogico)`: devuelve el texto de un recurso incrustado y falla con un mensaje claro si no existe. Lo usan las migraciones (5.3). |
| `PersistenciaNitido` (cambia) | pública | `AgregarPersistenciaNitido` registra también `InterceptorDeContextoSql` (singleton); `UsarSqlServerNitido` lo agrega junto al de auditoría. |

La regla del módulo no cambia: `Nitido.Compartido.Infraestructura` solo referencia `Nitido.Compartido`, con EF Core y SqlClient.

#### 1.3 `Nitido.Compartido.Api` (nuevo)

`Nitido.Compartido.Api/Nitido.Compartido.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Nitido.Compartido\Nitido.Compartido.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Nitido.Pruebas.Unidad" />
    <InternalsVisibleTo Include="Nitido.Pruebas.Integracion" />
  </ItemGroup>
</Project>
```

Sin paquetes nuevos en `Directory.Packages.props`.

| Pieza | Visibilidad | Qué hace |
| --- | --- | --- |
| `ApiNitido` | pública, estática | `AgregarApiNitido(this IServiceCollection)`: `AddHttpContextAccessor()` y `AddScoped<IContextoSesion, ContextoSesionHttp>()`. Se llama una vez, desde el host. |
| `Multiempresa/ReclamosDeSesion` | pública, estática | Nombres de los reclamos de la cookie: `UuidUsuarios = "uuid_usuarios"`, `UuidFirmas = "uuid_firmas"`. Decisión propuesta; F0-03a los emite al ingresar. Si los emite `Firmas.Infraestructura` (que no ve este proyecto), F0-03a los mueve a `Nitido.Compartido/Multiempresa/`. |
| `Multiempresa/ContextoSesionHttp` | interna | `IContextoSesion` con `IHttpContextAccessor`. `UuidUsuarios` y `UuidFirmas` salen de los reclamos de `HttpContext.User`; sin `HttpContext`, sin reclamo o con un valor que no es GUID, `Guid.Empty`. `UuidEmpresas` sale de `HttpContext.Items[ClienteActivo.ClaveValidada]`, que solo pone el filtro; nunca lee el valor de la ruta por su cuenta. Lee en cada acceso, sin guardar valores. Hasta F0-03a no hay ingreso, así que siempre da `Guid.Empty`. |
| `Multiempresa/FiltroClienteActivo` | interna | `IEndpointFilter` (ver 6). |
| `Multiempresa/RutasDelCliente` | pública, estática | `MapearRutasDelCliente(this IEndpointRouteBuilder rutas, string ruta = "")`: devuelve `rutas.MapGroup("/api/clientes/{uuidCliente:guid}" + ruta)` con `AddEndpointFilter<FiltroClienteActivo>()`, la marca `ClienteActivoRequerido` y `ProducesProblem(404)`. Es la única forma de armar rutas de cliente. |
| `Multiempresa/ClienteActivoRequerido` | pública, sellada | Marca de metadatos que revisa la prueba `RutasDelCliente_TodasValidanElClienteActivo`. |

`ARespuestaHttp` (de `Resultado` a ProblemDetails, arquitectura 2) **no** entra en esta historia: el primer endpoint que devuelve un `Resultado` es de F0-03a. El `404` del filtro se arma con `TypedResults.Problem`.

#### 1.4 Pruebas de arquitectura

Las reglas actuales **ya admiten** el proyecto nuevo, sin cambios:

- `Clasificador` reconoce `Nitido.Compartido.Api` como `TipoProyecto.CompartidoApi` y exige la ruta `Nitido.Compartido.Api/Nitido.Compartido.Api.csproj`.
- `InfraestructuraYApi_SoloLasReferenciaElHost` deja que lo referencien solo los `Api` de los módulos, el host y las pruebas, y que él solo dependa de `Nitido.Compartido`.
- `Api_NoReferenciaInfraestructura` acepta la referencia de un `Api` de módulo a `Nitido.Compartido.Api`, e `Infraestructura_NoReferenciaApi` prohíbe que una `Infraestructura` lo use.
- `Host_ComponeTodosLosModulos` exige que su ensamblado llegue a la salida de las pruebas: llega por el host.

**Ajuste propuesto (para el `tester`):** la tabla de la arquitectura (sección 1) prohíbe EF Core en `Nitido.Compartido.Api`, pero ninguna regla lo revisa. `Api_NoReferenciaInfraestructura` debe revisar también a `Nitido.Compartido.Api`: que no declare paquetes `Microsoft.EntityFrameworkCore*` y que su ensamblado no use EF Core ni `Nitido.Compartido.Infraestructura`. Lleva su caso sintético en `ReglasDeDependencia_DetectanCadaInfraccion` («Nitido.Compartido.Api declara EF Core»). No es una regla nueva: hace cumplir lo que ya dice la tabla.

### 2. Tablas y entidades

#### 2.1 Qué interfaz lleva cada entidad

| Entidad (proyecto) | Tabla | Interfaz | Filtro de EF (`Multiempresa`) | Predicados de SQL Server |
| --- | --- | --- | --- | --- |
| `Firma` (`Firmas.Dominio`) | `T_FIRMAS` | ninguna: su llave **es** la firma | Declarado a mano en `FirmasDbContext`: `Uuid == FirmaDeLaSesion` | `FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS)`: FILTER y los cuatro BLOCK |
| `Usuario` (`Firmas.Infraestructura`) | `T_USUARIOS` | `IDeFirma` | Por convención: `UuidFirmas == FirmaDeLaSesion` | `FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS)`: FILTER y los cuatro BLOCK |
| `Cliente` (`Clientes.Dominio`) | `T_EMPRESAS` | `IDeFirma` (no `IDeEmpresa`: la lista de clientes no tiene cliente activo) | Por convención: `UuidFirmas == FirmaDeLaSesion` | FILTER, BEFORE UPDATE y BEFORE DELETE: `FN_CLIENTES_ES_CLIENTE_PERMITIDO(UUID_FIRMAS, UUID_EMPRESAS)`; AFTER INSERT y AFTER UPDATE: `FN_CLIENTES_ES_DE_LA_FIRMA(UUID_FIRMAS)` |
| `AccesoCliente` (`Clientes.Dominio`) | `T_USUARIOS_EMPRESAS` | `IDeFirma` (nueva `UuidFirmas`) | Por convención: `UuidFirmas == FirmaDeLaSesion` | `FN_CLIENTES_ES_ACCESO_DEL_USUARIO(UUID_FIRMAS, UUID_USUARIOS)`: FILTER y los cuatro BLOCK |
| (historiales) | `T_<NOMBRE>_HISTORIAL` | — | — (EF no mapea el historial como entidad) | Solo FILTER, con la misma función que el FILTER de su tabla actual |

Ninguna entidad implementa `IDeEmpresa` todavía; la primera llega en la fase 1 (`T_TERCEROS` u otra). La rama de `IDeEmpresa` de la convención se prueba con un contexto solo de pruebas (8.1).

Por qué el filtro de EF de `Cliente` es solo por firma y no revisa el acceso: EF solo puede revisar el acceso con la navegación `Accesos`, y la consulta de la lista (F0-04) y el filtro de endpoint ya lo piden de forma explícita (6). La segunda barrera lo exige siempre. **Para F0-04:** `IConsultasClientes` filtra la lista por `Accesos.Any(a => a.UuidUsuarios == sesion.UuidUsuarios)`, para que las dos barreras digan lo mismo.

#### 2.2 Cambios de columnas, llaves e índices

Esquema `dbo`, salvo lo de seguridad por fila (esquema `seguridad`, 5).

```sql
-- Módulo Firmas · migración AgregarSeguridadPorFilaDeFirmas
ALTER TABLE T_FIRMAS ADD CONSTRAINT CK_T_FIRMAS_UUID_FIRMAS
  CHECK (UUID_FIRMAS <> '00000000-0000-0000-0000-000000000000');

-- Módulo Clientes · migración AgregarSeguridadPorFilaDeClientes
-- T_USUARIOS_EMPRESAS gana UUID_FIRMAS (decisión 1); su historial también la recibe.
ALTER TABLE T_USUARIOS_EMPRESAS ADD UUID_FIRMAS UNIQUEIDENTIFIER NOT NULL;      -- con relleno, ver 2.3

-- Llave alterna para la llave foránea compuesta.
ALTER TABLE T_EMPRESAS ADD CONSTRAINT UX_T_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS
  UNIQUE (UUID_EMPRESAS, UUID_FIRMAS);

-- La llave foránea de F0-02a pasa a ser compuesta y conserva su nombre.
ALTER TABLE T_USUARIOS_EMPRESAS DROP CONSTRAINT FK_T_USUARIOS_EMPRESAS_T_EMPRESAS;
ALTER TABLE T_USUARIOS_EMPRESAS ADD CONSTRAINT FK_T_USUARIOS_EMPRESAS_T_EMPRESAS
  FOREIGN KEY (UUID_EMPRESAS, UUID_FIRMAS) REFERENCES T_EMPRESAS (UUID_EMPRESAS, UUID_FIRMAS);   -- ON DELETE NO ACTION
-- EF crea además el índice de la llave foránea: IX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS.
```

En EF (las escribe el `dba`):

- `FirmaConfiguracion`: `HasCheckConstraint("CK_T_FIRMAS_UUID_FIRMAS", "UUID_FIRMAS <> '00000000-0000-0000-0000-000000000000'")`.
- `ClienteConfiguracion`: `HasAlternateKey(c => new { c.Uuid, c.UuidFirmas }).HasName("UX_T_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS")`; la relación `Accesos` pasa a `HasForeignKey("UuidEmpresas", nameof(AccesoCliente.UuidFirmas)).HasPrincipalKey(c => new { c.Uuid, c.UuidFirmas })`. `ConvencionDeNombres` no nombra llaves alternas, por eso el `HasName` explícito.
- `AccesoClienteConfiguracion`: `UuidFirmas` requerida. Los dos índices de F0-02a no cambian: `UX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_USUARIOS` es justo la búsqueda del `EXISTS` del predicado de `T_EMPRESAS`, y `IX_T_USUARIOS_EMPRESAS_UUID_USUARIOS` sirve al predicado de `T_USUARIOS_EMPRESAS`.

Índices que piden los predicados (todos ya existen salvo el de la llave foránea compuesta):

| Predicado | Búsqueda | Índice |
| --- | --- | --- |
| `T_EMPRESAS`: `EXISTS` en `T_USUARIOS_EMPRESAS` | `UUID_EMPRESAS` + `UUID_USUARIOS` | `UX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_USUARIOS` (F0-02a) |
| `T_USUARIOS_EMPRESAS`: firma y usuario | `UUID_USUARIOS` | `IX_T_USUARIOS_EMPRESAS_UUID_USUARIOS` (F0-02a) |
| `T_USUARIOS`: firma | `UUID_FIRMAS` | `IX_T_USUARIOS_UUID_FIRMAS` (F0-02a) |
| `T_EMPRESAS`: firma | `UUID_FIRMAS` | `UX_T_EMPRESAS_UUID_FIRMAS_TIPO_DOCUMENTO_NUMERO_DOCUMENTO` (F0-02a, empieza por la firma) |

#### 2.3 Datos existentes

No hay datos de producción (sin despliegue en la fase 0). La base de pruebas se crea en cada corrida. Una base local de un desarrollador puede tener filas en `T_USUARIOS_EMPRESAS` y en su historial, y `ADD ... NOT NULL` fallaría. Por eso la migración de Clientes, escrita a mano por el `dba` en esa parte (como las llaves foráneas de F0-02a):

1. `ALTER TABLE T_USUARIOS_EMPRESAS SET (SYSTEM_VERSIONING = OFF)`.
2. Agrega `UUID_FIRMAS` como `NULL` en `T_USUARIOS_EMPRESAS` y en `T_USUARIOS_EMPRESAS_HISTORIAL`.
3. Rellena las dos tablas desde `T_EMPRESAS` por `UUID_EMPRESAS` (la empresa de un acceso no cambia nunca).
4. Pasa la columna a `NOT NULL` en las dos.
5. `SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.T_USUARIOS_EMPRESAS_HISTORIAL))`.
6. Crea la llave alterna, cambia la llave foránea y aplica el script de la política (5).

Todo antes de crear la política, porque después la migración (sin sesión) no vería ninguna fila. La prueba `Migraciones_AccesosExistentesRecibenLaFirmaDeSuCliente` (8.1) lo comprueba con datos.

### 3. Filtro global de EF

**Convención** (en `DbContextNitido.OnModelCreating`, después de `ApplyConfigurationsFromAssembly`):

- Por cada entidad que implemente `IDeFirma`: `e => EF.Property<Guid>(e, "UuidFirmas") == this.FirmaDeLaSesion`.
- Por cada una que implemente `IDeEmpresa`: `e => EF.Property<Guid>(e, "UuidEmpresas") == this.EmpresaDeLaSesion`.
- Si implementa las dos, las dos condiciones con `&&` en el mismo filtro.
- Se registra con nombre: `modelBuilder.Entity(tipo).HasQueryFilter(DbContextNitido.FiltroMultiempresa, lambda)`. EF Core 10 permite varios filtros con nombre por entidad e ignorar solo uno (`IgnoreQueryFilters(["Multiempresa"])`); un filtro de borrado lógico futuro no choca con este.
- `EF.Property` por nombre sirve para propiedades de C# y sombra; la interfaz obliga a que la propiedad exista con ese nombre.

**Caché del modelo.** EF construye el modelo una sola vez por tipo de contexto, con la primera instancia. Por eso la expresión se arma con `Expression.Property(Expression.Constant(this), "FirmaDeLaSesion")`: EF reconoce el acceso a un miembro del propio `DbContext` y, en cada consulta, lo evalúa sobre la instancia actual (es el patrón documentado de multiinquilino). **Prohibido** capturar en la expresión el `IContextoSesion` o un valor leído: quedaría congelado con la sesión de la primera petición y mostraría a todas las firmas los datos de la primera. Lo vigila la prueba `FiltroGlobal_CadaDbContextUsaLaSesionDeSuAlcance` (8.1), que usa un contexto de sesión distinto por alcance. Por la misma razón se prohíbe `AddDbContextPool`.

**`Firma`:** `FirmasDbContext` sobrescribe `OnModelCreating`, llama a la base y agrega `modelBuilder.Entity<Firma>().HasQueryFilter(FiltroMultiempresa, f => f.Uuid == FirmaDeLaSesion)`.

**Sin sesión o en el arranque:** `FirmaDeLaSesion` y `EmpresaDeLaSesion` valen `Guid.Empty` y el filtro no devuelve nada, porque ninguna fila tiene esa firma (`CK_T_FIRMAS_UUID_FIRMAS` y las llaves foráneas) ni ese cliente. Construir el modelo (`dotnet ef`, `Tablas_PertenecenAUnSoloDbContext`) no evalúa las propiedades, así que no necesita sesión.

**Lo que el filtro no cubre:** `FromSql` sobre un `DbSet` sí lo aplica, igual que `ExecuteUpdate` y `ExecuteDelete`; `Database.SqlQuery` y el SQL a mano no. Para eso está la segunda barrera. Un `IgnoreQueryFilters()` sin nombre, o con `"Multiempresa"`, exige un comentario que diga por qué; el `revisor` lo rechaza si no.

### 4. Interceptor de conexión y pool

**`InterceptorDeContextoSql`** (interno, singleton, en `Nitido.Compartido.Infraestructura/Persistencia/`):

- Se engancha en **`ConnectionOpened`** y **`ConnectionOpenedAsync`**, no en `ConnectionOpening`: en `Opening` la conexión todavía no está abierta y no puede ejecutar nada. Se implementan las dos versiones, porque EF usa la síncrona en las operaciones síncronas.
- Resuelve `IContextoSesion` en cada apertura desde el contexto del evento (`eventData.Context?.GetService<IContextoSesion>()`), como el interceptor de auditoría. Sin contexto de EF o sin `IContextoSesion` registrado, usa todo en `Guid.Empty`: **falla cerrado**.
- Ejecuta un solo comando, con parámetros tipados como `uniqueidentifier`:

  ```sql
  EXEC sys.sp_set_session_context @key = N'UUID_USUARIOS', @value = @uuidUsuarios, @read_only = 1;
  EXEC sys.sp_set_session_context @key = N'UUID_FIRMAS',   @value = @uuidFirmas,   @read_only = 1;
  EXEC sys.sp_set_session_context @key = N'UUID_EMPRESAS', @value = @uuidEmpresas, @read_only = 1;
  ```

  `@uuidEmpresas` es `UuidEmpresas ?? Guid.Empty`. Las tres llaves se fijan siempre, aunque no haya sesión (decisión 3): una llave sin fijar se podría fijar después, en la misma conexión, con la firma de otro.
- No registra los valores en el log.

**Por qué funciona con el pool.** `@read_only = 1` impide cambiar la llave **en esa conexión lógica**. Cuando el pool entrega una conexión física ya usada, SqlClient la marca para restablecerse y el restablecimiento (`sp_reset_connection`) viaja con la primera orden que se envía, que es la del interceptor. El restablecimiento borra el `SESSION_CONTEXT`, incluidas las llaves de solo lectura (la documentación de SQL Server dice que `@read_only` dura «hasta que la conexión se cierra y vuelve al pool»). Así cada apertura empieza limpia y recibe el contexto de su petición. EF abre y cierra la conexión en cada consulta o guardado, salvo dentro de una transacción explícita, y cada apertura vuelve a pasar por el interceptor; la estrategia de reintentos también.

**Prueba `Pool_ConexionReutilizadaNoConservaContexto`** (8.1): cadena de la base migrada con `Pooling=true;Max Pool Size=1;Application Name=nitido-pool-<guid>` (el nombre único da un pool propio, y con una sola conexión la reutilización es segura):

1. Alcance 1, sesión de la firma A (usuario A1, cliente CA1): por `ClientesDbContext.Database.SqlQuery`, lee las tres llaves de `SESSION_CONTEXT`, `@@SPID` y `connect_time` de `sys.dm_exec_connections`. Espera A, A1 y CA1.
2. Alcance 2, sesión de la firma B: las mismas lecturas. Espera B, B1 y el cliente de B, **sin** el error 15664 («la llave es de solo lectura»), con el mismo `@@SPID` y el mismo `connect_time` (prueba que fue la misma conexión física).
3. Alcance 3, sin sesión: espera las tres llaves en `Guid.Empty`, la misma conexión física y 0 filas en `T_EMPRESAS`.
4. Repite el paso 2 con consultas síncronas (`ToList()`), para cubrir `ConnectionOpened`.

**Lo que se salta el interceptor:** una conexión que se abre por fuera de EF (`new SqlConnection`, o `GetDbConnection().Open()` directo sobre el objeto de conexión) no recibe contexto y no ve nada. Es el comportamiento buscado (falla cerrado). `/salud` abre su propia conexión y solo ejecuta `SELECT 1`, así que no le afecta. Hangfire (fase 2) usa sus propias tablas, que no tienen política.

**Migraciones.** `dotnet ef database update` y `Migrate()` abren la conexión por EF, con el interceptor, y sin sesión: las tres llaves quedan en `Guid.Empty`. Las órdenes de estructura no dependen de la seguridad por fila. Las de datos sobre tablas protegidas no verían nada, así que siguen la regla de 5.3.

**MARS.** `@read_only` no se puede fijar con varios lotes de MARS activos. Al abrir la conexión no hay ninguno, así que no afecta; de todos modos la cadena no activa `MultipleActiveResultSets`.

### 5. Seguridad por fila en SQL Server

#### 5.1 Nombres

- Esquema `seguridad` (buena práctica de SQL Server: separa los permisos de estos objetos de los de las tablas). Lo crea la migración de Firmas y lo quita su `Down`.
- Funciones: `seguridad.FN_<MODULO>_<QUE_VALIDA>`, funciones de tabla en línea con `SCHEMABINDING`, que devuelven `1 AS PERMITIDO`.
- Políticas: `seguridad.PS_<MODULO>`, con `STATE = ON, SCHEMABINDING = ON`.
- Scripts: `db/seguridad-por-fila/NNNN-<modulo>-<tema>.sql`.

#### 5.2 Scripts

`db/seguridad-por-fila/0001-firmas-politica.sql` (lo aplica `AgregarSeguridadPorFilaDeFirmas`):

```sql
CREATE SCHEMA seguridad AUTHORIZATION dbo;
GO
-- La fila es de la firma de la sesión.
CREATE FUNCTION seguridad.FN_FIRMAS_ES_DE_LA_FIRMA (@UUID_FIRMAS UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS PERMITIDO
    WHERE @UUID_FIRMAS = CAST(SESSION_CONTEXT(N'UUID_FIRMAS') AS UNIQUEIDENTIFIER);
GO
CREATE SECURITY POLICY seguridad.PS_FIRMAS
    ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS,
    ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS AFTER INSERT,
    ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS AFTER UPDATE,
    ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS BEFORE UPDATE,
    ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS BEFORE DELETE,
    ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_FIRMAS_HISTORIAL,
    ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS,
    ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS AFTER INSERT,
    ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS AFTER UPDATE,
    ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS BEFORE UPDATE,
    ADD BLOCK  PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS BEFORE DELETE,
    ADD FILTER PREDICATE seguridad.FN_FIRMAS_ES_DE_LA_FIRMA(UUID_FIRMAS) ON dbo.T_USUARIOS_HISTORIAL
WITH (STATE = ON, SCHEMABINDING = ON);
```

`db/seguridad-por-fila/0002-clientes-politica.sql` (lo aplica `AgregarSeguridadPorFilaDeClientes`, después de los pasos 1 a 5 de 2.3):

```sql
-- La fila es de la firma de la sesión. Copia propia del módulo: las funciones no se comparten entre módulos.
CREATE FUNCTION seguridad.FN_CLIENTES_ES_DE_LA_FIRMA (@UUID_FIRMAS UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS PERMITIDO
    WHERE @UUID_FIRMAS = CAST(SESSION_CONTEXT(N'UUID_FIRMAS') AS UNIQUEIDENTIFIER);
GO
-- El acceso es de la firma y del usuario de la sesión. Sin subconsultas: el predicado de T_EMPRESAS consulta esta tabla.
CREATE FUNCTION seguridad.FN_CLIENTES_ES_ACCESO_DEL_USUARIO (@UUID_FIRMAS UNIQUEIDENTIFIER, @UUID_USUARIOS UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS PERMITIDO
    WHERE @UUID_FIRMAS   = CAST(SESSION_CONTEXT(N'UUID_FIRMAS')   AS UNIQUEIDENTIFIER)
      AND @UUID_USUARIOS = CAST(SESSION_CONTEXT(N'UUID_USUARIOS') AS UNIQUEIDENTIFIER);
GO
-- El cliente es de la firma de la sesión y el usuario de la sesión tiene acceso a él.
CREATE FUNCTION seguridad.FN_CLIENTES_ES_CLIENTE_PERMITIDO (@UUID_FIRMAS UNIQUEIDENTIFIER, @UUID_EMPRESAS UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS PERMITIDO
    WHERE @UUID_FIRMAS = CAST(SESSION_CONTEXT(N'UUID_FIRMAS') AS UNIQUEIDENTIFIER)
      AND EXISTS (
          SELECT 1
          FROM dbo.T_USUARIOS_EMPRESAS AS ACCESO
          WHERE ACCESO.UUID_EMPRESAS = @UUID_EMPRESAS
            AND ACCESO.UUID_USUARIOS = CAST(SESSION_CONTEXT(N'UUID_USUARIOS') AS UNIQUEIDENTIFIER));
GO
CREATE SECURITY POLICY seguridad.PS_CLIENTES
    ADD FILTER PREDICATE seguridad.FN_CLIENTES_ES_CLIENTE_PERMITIDO(UUID_FIRMAS, UUID_EMPRESAS) ON dbo.T_EMPRESAS,
    ADD BLOCK  PREDICATE seguridad.FN_CLIENTES_ES_DE_LA_FIRMA(UUID_FIRMAS)                     ON dbo.T_EMPRESAS AFTER INSERT,
    ADD BLOCK  PREDICATE seguridad.FN_CLIENTES_ES_DE_LA_FIRMA(UUID_FIRMAS)                     ON dbo.T_EMPRESAS AFTER UPDATE,
    ADD BLOCK  PREDICATE seguridad.FN_CLIENTES_ES_CLIENTE_PERMITIDO(UUID_FIRMAS, UUID_EMPRESAS) ON dbo.T_EMPRESAS BEFORE UPDATE,
    ADD BLOCK  PREDICATE seguridad.FN_CLIENTES_ES_CLIENTE_PERMITIDO(UUID_FIRMAS, UUID_EMPRESAS) ON dbo.T_EMPRESAS BEFORE DELETE,
    ADD FILTER PREDICATE seguridad.FN_CLIENTES_ES_CLIENTE_PERMITIDO(UUID_FIRMAS, UUID_EMPRESAS) ON dbo.T_EMPRESAS_HISTORIAL,
    ADD FILTER PREDICATE seguridad.FN_CLIENTES_ES_ACCESO_DEL_USUARIO(UUID_FIRMAS, UUID_USUARIOS) ON dbo.T_USUARIOS_EMPRESAS,
    ADD BLOCK  PREDICATE seguridad.FN_CLIENTES_ES_ACCESO_DEL_USUARIO(UUID_FIRMAS, UUID_USUARIOS) ON dbo.T_USUARIOS_EMPRESAS AFTER INSERT,
    ADD BLOCK  PREDICATE seguridad.FN_CLIENTES_ES_ACCESO_DEL_USUARIO(UUID_FIRMAS, UUID_USUARIOS) ON dbo.T_USUARIOS_EMPRESAS AFTER UPDATE,
    ADD BLOCK  PREDICATE seguridad.FN_CLIENTES_ES_ACCESO_DEL_USUARIO(UUID_FIRMAS, UUID_USUARIOS) ON dbo.T_USUARIOS_EMPRESAS BEFORE UPDATE,
    ADD BLOCK  PREDICATE seguridad.FN_CLIENTES_ES_ACCESO_DEL_USUARIO(UUID_FIRMAS, UUID_USUARIOS) ON dbo.T_USUARIOS_EMPRESAS BEFORE DELETE,
    ADD FILTER PREDICATE seguridad.FN_CLIENTES_ES_ACCESO_DEL_USUARIO(UUID_FIRMAS, UUID_USUARIOS) ON dbo.T_USUARIOS_EMPRESAS_HISTORIAL
WITH (STATE = ON, SCHEMABINDING = ON);
```

Por qué cada decisión:

- **`T_EMPRESAS`, AFTER INSERT solo por firma:** EF inserta el cliente antes que su primer acceso, en el mismo `SaveChanges`. Un AFTER INSERT que exigiera el acceso rechazaría todo cliente nuevo. Crear clientes en la propia firma es lo que hace un administrador (F0-04).
- **`T_EMPRESAS`, AFTER UPDATE solo por firma:** impide pasar un cliente a otra firma. BEFORE UPDATE con acceso impide tocar un cliente sin acceso. El FILTER ya oculta esas filas al `UPDATE` y al `DELETE`; los BLOCK BEFORE quedan como defensa si un día alguien quita el FILTER, y porque la prueba de cobertura exige los cuatro.
- **`T_USUARIOS_EMPRESAS` por firma y usuario:** en la fase 0 un usuario solo ve y crea sus propios accesos (quien crea el cliente queda con acceso, F0-04). La llave foránea compuesta impide un acceso a un cliente de otra firma (las llaves foráneas no pasan por la seguridad por fila). Cuando lleguen los roles, un administrador verá los accesos de toda su firma (pendiente del ADR 0003).
- **Sin ciclos:** `FN_CLIENTES_ES_CLIENTE_PERMITIDO` consulta `T_USUARIOS_EMPRESAS`, cuyo predicado no consulta nada. SQL Server aplica también la política de `T_USUARIOS_EMPRESAS` dentro de esa subconsulta, y las condiciones coinciden.
- **`CAST` de `SESSION_CONTEXT`:** el valor llega como `sql_variant` con tipo `uniqueidentifier` (lo fija el interceptor con parámetros tipados), así que la conversión no depende de opciones de sesión como `DATEFORMAT`.

#### 5.3 Cómo se aplica por migración

- Cada `Infraestructura` incluye **sus** scripts como recurso incrustado, archivo por archivo, sin comodines:

  ```xml
  <!-- Nitido.Firmas.Infraestructura.csproj -->
  <ItemGroup>
    <EmbeddedResource Include="..\..\..\db\seguridad-por-fila\0001-firmas-politica.sql"
                      LogicalName="SeguridadPorFila.0001-firmas-politica.sql" />
  </ItemGroup>
  ```

  Las pruebas de arquitectura no revisan `EmbeddedResource` (5.5 solo mira referencias, `Import`, `Reference` y `Compile`), y un recurso no es código.
- La migración lo aplica en su `Up` con `migrationBuilder.Sql(ScriptsSql.Leer(typeof(<Migracion>).Assembly, "SeguridadPorFila.0001-firmas-politica.sql"))`. El generador de SQL Server de EF parte el texto en lotes por las líneas `GO` (lo necesitan `CREATE FUNCTION` y `CREATE SECURITY POLICY`, que deben ir solos en su lote). El `dba` lo confirma en `dotnet ef migrations script`; si EF no partiera los lotes, `ScriptsSql` devuelve los lotes y la migración llama a `Sql` una vez por lote.
- El `Down` va escrito en la migración, en orden inverso: `DROP SECURITY POLICY`, `DROP FUNCTION` (y en Firmas, `DROP SCHEMA seguridad` al final). El `Down` de Firmas falla si Clientes sigue aplicado (el esquema tiene objetos de Clientes y existen las llaves foráneas), igual que hoy; la prueba `Migraciones_Down_DeFirmasConClientesAplicado_FallaYDejaTodoIntacto` lo sigue cubriendo.
- **Un script aplicado no se edita.** Un cambio de política es un script nuevo (`0003-...`) y una migración nueva que la quita y la vuelve a crear, o que usa `ALTER SECURITY POLICY`.
- **Migraciones de datos sobre tablas protegidas** (regla nueva para `convenciones-bd`): apagan la política de su módulo (`ALTER SECURITY POLICY seguridad.PS_<MODULO> WITH (STATE = OFF)`) y la encienden antes de terminar, en la misma migración (EF la corre en una transacción; si falla, se deshace todo, también el apagado). La prueba de cobertura exige `is_enabled = 1` al final.
- **Cambios de estructura en tablas protegidas:** `SCHEMABINDING` impide cambiar una columna que usa un predicado o reconstruir la tabla. Esa migración quita los predicados de la tabla antes y los vuelve a poner después, con un script nuevo. Las columnas que no usa ningún predicado se cambian sin tocar la política. El `dba` revisa en cada PR el SQL de las tablas temporales protegidas.
- **Orden:** no cambia. `Firmas` → `Clientes`; dentro de cada módulo, sus migraciones en orden. `db/README.md` documenta la carpeta `seguridad-por-fila/`, las dos migraciones y la regla de migraciones de datos.

#### 5.4 Cómo se crean la primera firma, el primer usuario y el primer cliente

Con las políticas activas, ninguna conexión sin sesión puede insertar en estas tablas: los BLOCK AFTER INSERT comparan con `Guid.Empty`. **No se abre ninguna excepción en el predicado** (ni por rol, ni por usuario de base, ni por una llave «modo siembra»), porque con `sa` en local cualquier excepción dejaría la barrera apagada en desarrollo y en las pruebas. El mecanismo es declarar el contexto correcto, y las mismas barreras siguen activas:

- **Pruebas (esta historia):** la siembra fija en `ContextoSesionDePrueba` la firma y el usuario que crean cada fila (8.2). La firma se crea con `Firma.Crear`, que da la llave antes de guardar, y al usuario se le asigna la llave al crearlo.
- **Registro de firma (F0-04) y semilla de Development (F0-03a):** un **alcance con contexto explícito**. `Nitido.Compartido.Infraestructura` ofrece `CrearAlcanceConContexto(this IServiceScopeFactory, Guid uuidUsuarios, Guid uuidFirmas, Guid? uuidEmpresas)`, que crea un alcance cuyo `IContextoSesion` es el indicado (el host lo compone: el contexto explícito del alcance, si hay, y si no, `ContextoSesionHttp`). El caso de uso crea la firma con su fábrica, abre el alcance con `UuidFirmas` = la firma nueva y `UuidUsuarios` = el usuario «sistema» (`00000000-0000-0000-0000-000000000001`, F0-02a) y guarda firma y administrador en una transacción. Los AFTER INSERT pasan porque las filas son de la firma de la sesión; la auditoría registra al sistema como creador; el registro escribe un evento estructurado «Firma registrada» con el `UuidFirmas`. Una firma recién creada no tiene datos, así que ese contexto no puede leer nada ajeno. Se construye en F0-04, su primer uso.
- **Usos permitidos de `CrearAlcanceConContexto`:** registro de firma, semilla de Development y trabajos en segundo plano (con el contexto guardado del usuario que encoló el trabajo). El `revisor` rechaza cualquier otro.
- **Primer cliente (F0-04):** lo crea un usuario con sesión, en su firma. El AFTER INSERT de `T_EMPRESAS` solo pide la firma, y el del acceso pide firma y usuario de la sesión: los dos pasan en el mismo `SaveChanges`. Lo cubre `SeguridadPorFila_PermiteCrearClienteConSuAccesoEnUnGuardado` (8.1).
- **Correo repetido entre firmas (F0-04):** con la sesión de la firma nueva, la búsqueda de Identity por correo no ve usuarios de otras firmas, pero el índice único global sí los ve (los índices y las llaves foráneas no pasan por la seguridad por fila). El repositorio traduce esa violación del índice a `409`, como ya dice la arquitectura (6.2, F0-04).
- **Ingreso (F0-03a, decisión propuesta):** ver ADR 0003, «Ingreso».

### 6. Endpoints

No hay endpoints nuevos en producción. El documento OpenAPI no cambia (`OpenApiPruebas` sigue igual), y no hay pull request del frontend.

#### 6.1 Filtro del cliente activo

`FiltroClienteActivo` (`IEndpointFilter`, interno, en `Nitido.Compartido.Api/Multiempresa/`), que pone `MapearRutasDelCliente`:

1. Lee el valor de ruta `uuidCliente`. La restricción `:guid` ya responde `404` a un valor que no es GUID; si aun así no se puede leer, `404`.
2. Lee `IContextoSesion` de los servicios de la petición. Si `UuidUsuarios` es `Guid.Empty`, `404`. (Desde F0-03a la política de autorización responde `401` antes.)
3. Llama a `IAccesoAClientes.PuedeEntrarAsync(uuidCliente, ct)`. Si es `false`, `404`.
4. Si es `true`, guarda el cliente en `HttpContext.Items[ClienteActivo.ClaveValidada]` y sigue. Desde ahí, `ContextoSesionHttp.UuidEmpresas` lo entrega, y las conexiones que se abran después lo llevan en `SESSION_CONTEXT`.

`AccesoAClientes` (en `Clientes.Infraestructura/Persistencia/`, interno, `AddScoped<IAccesoAClientes, AccesoAClientes>()` en `AgregarClientes`):

```csharp
contexto.Clientes.AsNoTracking()
    .AnyAsync(c => c.Uuid == uuidEmpresas
                && c.Accesos.Any(a => a.UuidUsuarios == sesion.UuidUsuarios), ct);
```

Pasa por las dos barreras: el filtro de EF (firma) más la condición explícita de acceso, y la política de `T_EMPRESAS` (firma y acceso). La conexión de esta consulta se abre con `UUID_EMPRESAS = Guid.Empty` (todavía no está validado); EF la cierra al terminar, y la siguiente apertura ya lleva el cliente validado. Un cliente inactivo sigue siendo accesible; si F0-04 decide otra cosa, cambia esta consulta.

Contrato que hereda toda ruta de cliente, para OpenAPI:

```yaml
components:
  responses:
    ClienteNoEncontrado:
      description: El cliente no existe, es de otra firma o el usuario no tiene acceso a él.
      content:
        application/problem+json:
          schema: { $ref: "#/components/schemas/ProblemDetails" }
          example:
            type: "https://tools.ietf.org/html/rfc9110#section-15.5.5"
            title: "No encontramos ese cliente."
            status: 404
            codigo: "cliente_no_encontrado"
paths:
  /api/clientes/{uuidCliente}/...:        # toda ruta creada con MapearRutasDelCliente
    parameters:
      - { name: uuidCliente, in: path, required: true, schema: { type: string, format: uuid } }
    responses:
      "404": { $ref: "#/components/responses/ClienteNoEncontrado" }
```

El mismo `404`, con el mismo cuerpo, para los tres casos (no existe, otra firma, sin acceso): así no se revela que el cliente existe. No hay `403`.

#### 6.2 Endpoint de prueba

Solo en `tests/integracion`, nunca en el host: `EndpointsDePrueba`, un `IStartupFilter` (el mismo recurso que ya usa `ErroresPruebas`) que se agrega con `ConfigureTestServices` y, después de la canalización de la aplicación, monta `UseRouting` y `UseEndpoints` con:

```
GET /api/clientes/{uuidCliente:guid}/prueba-eco   (creado con MapearRutasDelCliente("/prueba-eco"))
200 → { "uuidEmpresas": "<IContextoSesion.UuidEmpresas>" }
404 → ProblemDetails «No encontramos ese cliente.»
```

Como usa `MapearRutasDelCliente`, prueba el filtro real con el `IAccesoAClientes` real, la base real y las dos políticas.

### 7. Implementación de `IContextoSesion` solo para pruebas

- `tests/integracion/Nitido.Pruebas.Integracion/Ayudantes/ContextoSesionDePrueba.cs` (existe) gana `UuidFirmas` y `UuidEmpresas` con `set`, y un método `Actuar(Guid uuidUsuarios, Guid uuidFirmas, Guid? uuidEmpresas = null)`. `UuidUsuarios` deja de ser aleatorio por defecto: arranca en `Guid.Empty`, como una petición sin sesión, y cada prueba dice quién actúa.
- `FabricaNitido`: el parámetro `registrarSesion` pasa a `usarSesionDePrueba` (por defecto `true`). Con `true`, reemplaza `IContextoSesion` por el `ContextoSesionDePrueba` (singleton; las pruebas corren en serie). Con `false`, **no toca** el registro del host: queda `ContextoSesionHttp`. Ya no existe la opción de quitar `IContextoSesion`, porque el host siempre lo registra.
- **`ContextoDePrueba_NoSeRegistraEnLaApi`** (en `Multiempresa/ContextoDeSesionPruebas.cs`): con `FabricaNitido(cadena sin tablas, usarSesionDePrueba: false)` y un `ConfigureTestServices` que solo copia la colección de servicios, comprueba que:
  1. hay **un solo** registro de `IContextoSesion`, con implementación `ContextoSesionHttp` y del ensamblado `Nitido.Compartido.Api`;
  2. ningún registro de `IContextoSesion` tiene implementación, instancia o fábrica declarada en un ensamblado `Nitido.Pruebas.*`;
  3. en un alcance, `GetRequiredService<IContextoSesion>()` no es `ContextoSesionDePrueba` y, sin `HttpContext`, da `Guid.Empty` y `null`.
  
  Las pruebas de arquitectura ya impiden que un proyecto de producción referencie uno de pruebas; esta prueba cubre lo que ellas no ven: el registro en la inyección de dependencias.

### 8. Pruebas, riesgos y plan

#### 8.1 Pruebas

Integración con Testcontainers, en `tests/integracion/Nitido.Pruebas.Integracion/Multiempresa/` (carpeta nueva), salvo donde se indica. Toda prueba negativa lleva su control positivo (la misma consulta, con la sesión dueña, sí ve la fila), para que no pase en vacío.

| Prueba | Archivo | Qué comprueba |
| --- | --- | --- |
| `FiltroGlobal_CubreTodaEntidadDeEmpresaYFirma` (criterio) | `FiltroGlobalPruebas.cs` | Teoría sobre los contextos de `AyudanteDeMigraciones.ContextosEnOrden` más `ContextoDeModeloDePrueba` (un `DbContextNitido` solo de pruebas con una entidad `IDeEmpresa` y otra con las dos interfaces). Construye el modelo sin conectarse (como `Tablas_PertenecenAUnSoloDbContext`) y falla si una entidad que implementa `IDeEmpresa` o `IDeFirma`, **o** que tiene una columna `UUID_FIRMAS` o `UUID_EMPRESAS` en su tabla, no tiene el filtro con nombre `Multiempresa`. Así atrapa también a `Firma` y a una entidad con la columna en sombra y sin interfaz. Revisa con `ToQueryString()` que la entidad `IDeEmpresa` filtra por `UUID_EMPRESAS` y la `IDeFirma` por `UUID_FIRMAS`. |
| `FiltroGlobal_CadaDbContextUsaLaSesionDeSuAlcance` | `FiltroGlobalPruebas.cs` | Con `IContextoSesion` registrado por alcance (un `ContextoSesionDePrueba` distinto en cada uno): alcance 1 con la firma A consulta primero (construye el modelo), alcance 2 con la firma B ve solo B, y el alcance 1 vuelve a ver solo A. Detecta un filtro que capture la sesión de la primera instancia. |
| `Multiempresa_UsuarioDeOtraFirmaNoVeClientes` (criterio) | `FiltroGlobalPruebas.cs` | En una base propia (`CrearBaseVaciaAsync` y migraciones), con las dos políticas apagadas solo en esa base, para medir la primera barrera sola: como A1, `Clientes.ToListAsync()` y `Set<Usuario>()` no traen filas de B, y sí las de A. |
| `SeguridadPorFila_CubreTodaTablaDeEmpresaYFirma` (criterio) | `SeguridadPorFilaPruebas.cs` | Consulta `sys.tables`, `sys.columns`, `sys.security_predicates` y `sys.security_policies`. Falla si una tabla (también las `_HISTORIAL`; se excluyen las `__EFMigrationsHistory_%`) con columna `UUID_EMPRESAS` o `UUID_FIRMAS` no tiene un FILTER en una política con `is_enabled = 1`; si una tabla actual no tiene los cuatro BLOCK (`AFTER INSERT`, `AFTER UPDATE`, `BEFORE UPDATE`, `BEFORE DELETE`); si una `_HISTORIAL` tiene un BLOCK; o si una política no tiene `is_schema_bound = 1`. También falla si no encuentra ninguna política, para no pasar en vacío. |
| `SeguridadPorFila_SinFiltroDeEfNoDevuelveFilasAjenas` (criterio) | `SeguridadPorFilaPruebas.cs` | Como A1: `IgnoreQueryFilters()` sobre `Cliente`, `AccesoCliente` y `Usuario`, `Database.SqlQuery` sobre las cuatro tablas, y `FOR SYSTEM_TIME ALL` sobre `T_EMPRESAS` y `T_USUARIOS`: 0 filas de B en todos. |
| `SeguridadPorFila_BloqueaEscribirEnOtraFirma` (criterio) | `SeguridadPorFilaPruebas.cs` | Como A1: `INSERT` en `T_EMPRESAS`, `T_USUARIOS` y `T_USUARIOS_EMPRESAS` con la firma de B falla con el error de predicado de bloqueo (33504; el `tester` confirma el número); `Cliente.Crear(B, ...)` por EF falla igual; `UPDATE ... SET UUID_FIRMAS = B` sobre un cliente de A falla; un `UPDATE` sobre un cliente de B afecta 0 filas y, leído con la sesión de B, no cambió; un acceso de A1 con la firma de A a un cliente de B falla por la llave foránea compuesta (547). |
| `SeguridadPorFila_SinContextoNoDevuelveNada` (criterio) | `SeguridadPorFilaPruebas.cs` | Con `SqlConnection` directa (sin llaves) y con EF sin sesión (llaves en `Guid.Empty`): 0 filas en las cuatro tablas y sus cuatro historiales, aunque haya filas sembradas de A y de B. |
| `SeguridadPorFila_UsuarioSinPermisoNoVeElCliente` (criterio) | `SeguridadPorFilaPruebas.cs` | Como A1 (misma firma que CA2): CA2, que solo tiene acceso de A2, no aparece con `IgnoreQueryFilters()` ni con SQL directo, ni en `T_EMPRESAS_HISTORIAL`. CA1 sí. |
| `SeguridadPorFila_PermiteCrearClienteConSuAccesoEnUnGuardado` | `SeguridadPorFilaPruebas.cs` | Como A1, `Cliente.Crear` y un solo `SaveChanges`: guarda el cliente y su acceso, y A1 lo ve después. Confirma que el orden de los INSERT de EF y la lectura de los periodos devueltos (`OUTPUT`) no chocan con los predicados (riesgo de 8.3). |
| `SesionSql_LlavesSonDeSoloLectura` | `SeguridadPorFilaPruebas.cs` | En una conexión abierta por EF, con y sin sesión, `EXEC sp_set_session_context N'UUID_FIRMAS', <B>` falla con el error 15664 y la sesión sigue igual. |
| `Pool_ConexionReutilizadaNoConservaContexto` (criterio) | `PoolDeConexionesPruebas.cs` | Ver 4. |
| `ClienteEnRuta_SinAccesoResponde404` (criterio) | `ClienteActivoPruebas.cs` | Con `EndpointsDePrueba`, como A1: CB1 (otra firma), CA2 (sin acceso), un GUID que no existe y un valor que no es GUID responden `404` con el mismo cuerpo ProblemDetails (`title` «No encontramos ese cliente.», `codigo` `cliente_no_encontrado`); sin sesión, CA1 responde `404`; con sesión, CA1 responde `200` y el eco devuelve CA1. |
| `RutasDelCliente_TodasValidanElClienteActivo` | `ClienteActivoPruebas.cs` | Recorre `EndpointDataSource` del host: toda ruta cuyo patrón tenga `{uuidCliente}` lleva `ClienteActivoRequerido`. Con `EndpointsDePrueba`, para no pasar en vacío mientras el host no tenga rutas de cliente. |
| `ContextoDePrueba_NoSeRegistraEnLaApi` (criterio) | `ContextoDeSesionPruebas.cs` | Ver 7. |
| `Migraciones_AccesosExistentesRecibenLaFirmaDeSuCliente` | `Persistencia/MigracionesPruebas.cs` | Base vacía: aplica Firmas completo y Clientes hasta `CrearClientes`, siembra con SQL directo una firma, un usuario, un cliente y un acceso, y modifica el acceso para que haya historial; aplica `AgregarSeguridadPorFilaDeClientes` y comprueba (con la sesión de esa firma) que el acceso y su historial tienen la `UUID_FIRMAS` del cliente. |
| `ContextoSesionHttp_SoloEntregaElClienteValidado` | `tests/unidad/.../Multiempresa/ContextoSesionHttpPruebas.cs` | Con un `DefaultHttpContext`: sin reclamos da `Guid.Empty`; con reclamos válidos da el usuario y la firma; un reclamo que no es GUID da `Guid.Empty`; un `uuidCliente` en la ruta sin la marca del filtro da `UuidEmpresas = null`; con la marca, el cliente. |
| `ReglasDeDependencia_DetectanCadaInfraccion` (amplía) | `tests/unidad/.../Arquitectura/` | Caso sintético nuevo: «Nitido.Compartido.Api declara EF Core» (ver 1.4). |

**Pruebas de F0-02a que hay que adaptar** (no se debilitan; cambia lo que siembran y cómo leen):

- `DatosDePrueba`: `GuardarFirmaYUsuarioAsync` asigna la llave al usuario al crearlo y fija `fabrica.Sesion.Actuar(usuario, firma)` antes de guardar; `GuardarAsync(cliente)` exige que la sesión sea de la firma del cliente y del usuario del primer acceso. `NuevoAccesoDeLaFabrica` usa la nueva firma `AccesoCliente.Crear(Guid, Guid, DateTimeOffset)`.
- `ConsultasSql`: gana una variante que recibe la sesión (`uuidUsuarios`, `uuidFirmas`, `uuidEmpresas`) y llama a `sp_set_session_context` en la misma conexión antes de la consulta. **Toda lectura de verificación con SQL directo usa esa variante.** Sin ella, una aserción como «no se guardó» (`AssertNoSeGuardoAsync` de `AuditoriaPruebas`) pasaría siempre, porque sin sesión no se ve nada.
- `Auditoria_SinContextoDeSesion_*`: pasan a `usarSesionDePrueba: false` (el host da `Guid.Empty`) y siguen esperando el mismo error, que ahora sale de la regla nueva (1.2).
- `RestriccionesPruebas`: sus `INSERT` con SQL directo fijan la sesión de la firma de la fila; si no, el predicado de bloqueo responde antes que el `CHECK` o la llave foránea que se quiere probar.
- `Migraciones_Down_EnElOrdenClientesLuegoFirmas_DejaSoloLosHistoriales`: también comprueba que no queda el esquema `seguridad` ni ninguna política.
- `Esquema_SigueConvenciones`: la llave foránea compuesta sigue llevando `UUID_EMPRESAS` hacia `T_EMPRESAS`; el `tester` confirma que `VerificadorDeEsquema` la reconoce.

#### 8.2 Siembra de las dos firmas

`Ayudantes/SiembraDeDosFirmas.cs`, con llaves y NIT únicos en cada llamada (la base compartida tiene filas de otras pruebas; cada prueba cuenta solo las suyas):

| Firma | Usuarios | Clientes y accesos |
| --- | --- | --- |
| A | A1 y A2, administradores | CA1 con acceso de A1; CA2 con acceso **solo** de A2 |
| B | B1, administrador | CB1 con acceso de B1 |

Cada fila se guarda con la sesión de quien la crea (`Actuar(A1, A)`, `Actuar(A2, A)`, `Actuar(B1, B)`). Después se cambia una vez la razón social de CA1 y de CB1, y el nombre de A1 y de B1, para que los historiales tengan filas y las pruebas sobre `_HISTORIAL` no pasen en vacío. Devuelve un registro con todas las llaves.

#### 8.3 Riesgos

| Riesgo | Mitigación |
| --- | --- |
| **Caché del modelo de EF:** un filtro que capture la sesión de la primera instancia muestra a todas las firmas los datos de la primera. Es el error más grave posible de esta historia. | El filtro solo lee propiedades de la instancia del `DbContext` (3). `FiltroGlobal_CadaDbContextUsaLaSesionDeSuAlcance` usa una sesión distinta por alcance. Se prohíbe `AddDbContextPool`. |
| **Pool de conexiones:** una conexión reutilizada con la sesión de otra firma. | Las llaves se fijan en cada apertura, y el restablecimiento del pool limpia las de solo lectura (4). Lo prueba `Pool_ConexionReutilizadaNoConservaContexto`, con la misma conexión física comprobada. Si SQL Server no las limpiara, la apertura fallaría con el error 15664: falla cerrado, nunca deja ver datos ajenos. |
| **Lectura de los valores generados al insertar:** EF lee con `OUTPUT` los periodos de las tablas temporales; si una lectura posterior pasara por el FILTER de `T_EMPRESAS` antes de existir el acceso, crear un cliente fallaría. | La documentación no lo dice expresamente. Lo cubre `SeguridadPorFila_PermiteCrearClienteConSuAccesoEnUnGuardado`. Si fallara, el `dba` y el `dev-backend` lo resuelven antes de F0-04 (por ejemplo, guardando el acceso en la misma orden), sin aflojar el predicado. |
| **Migraciones futuras sobre tablas protegidas:** `SCHEMABINDING` impide cambiar columnas de los predicados o reconstruir la tabla, y las migraciones de datos no ven filas. | Reglas de 5.3, que se agregan a `convenciones-bd`. El `dba` revisa el SQL de cada migración que toque estas tablas. |
| **`sa` en local:** puede apagar o quitar la política. | La seguridad por fila se aplica también a `sa` y a `dbo`, así que las pruebas con `sa` son reales; ninguna prueba apaga la política en la base compartida (solo en la base propia de `Multiempresa_UsuarioDeOtraFirmaNoVeClientes`). El usuario de aplicación sin permisos sobre la política queda para el ADR de despliegue. |
| **El ingreso (F0-03a) no podrá buscar el usuario por correo** con la política de `T_USUARIOS`. | Decisión propuesta en el ADR 0003 («Ingreso»). F0-03a la confirma en su diseño; no bloquea esta historia. |
| **Registro de firma y semilla (F0-04, F0-03a) sin sesión.** | Alcance con contexto explícito (5.4), sin puertas en el predicado. Se construye en F0-04. |
| **Las pruebas de F0-02a se rompen** al activar las políticas, y algunas aserciones con SQL directo podrían pasar en vacío. | Lista de adaptaciones de 8.1. El `tester` revisa cada aserción negativa con su control positivo. |
| **Rendimiento:** un comando más por apertura de conexión y una consulta más por petición a una ruta de cliente; un `EXISTS` por fila al leer `T_EMPRESAS`. | Un solo viaje para las tres llaves; búsquedas por índices que ya existen (2.2). En la fase 0 las tablas tienen pocas filas. Se mide cuando las tablas de la fase 1 crezcan. |
| **Vistas de toda la firma** con datos de varios clientes (tablero de pendientes) no caben en el predicado de datos de cliente de la fase 1. | Se resuelven en su historia sin aflojar el predicado (ADR 0003, «Lo que cuesta»). |
| **Fuga lateral por el índice único de correo.** | Aceptada: es el `409` de F0-04 (ADR 0003). |
| **Limitaciones conocidas de F0-02a.** | *Llave vacía duplicada:* no cambia; `Guid.Empty` como «sin sesión» no choca porque el interceptor de auditoría reemplaza las llaves vacías y `CK_T_FIRMAS_UUID_FIRMAS` lo respalda en la base. *`Execute*` y SQL a mano sin auditoría:* sí pasan por el interceptor de conexión (abren la conexión por EF), así que la seguridad por fila los cubre; la auditoría sigue a mano. *`Kind = Utc`:* sin relación. *Smart App Control:* el ensamblado nuevo `Nitido.Compartido.Api` puede quedar bloqueado la primera vez; si pasa, se reporta al usuario con el archivo, sin tocar la configuración de Windows. |
| **Migración de datos existentes.** | Solo `T_USUARIOS_EMPRESAS.UUID_FIRMAS`, con relleno desde `T_EMPRESAS` (2.3) y su prueba. No hay datos de producción. |

#### 8.4 Eventos y trabajos en segundo plano

Ninguno. Los trabajos de Hangfire (fase 2) usarán el alcance con contexto explícito (5.4).

#### 8.5 Pantallas

Ninguna. No hay pull request del frontend.

#### 8.6 Plan de implementación

Sin paquetes nuevos.

| Paso | Agente | Qué | Depende de | En paralelo con |
| --- | --- | --- | --- | --- |
| 0 | usuario | Aprueba el ADR 0003 y las decisiones de «Decisiones que aprueba el usuario». | — | — |
| 1 | `dev-backend` | Andamiaje: `Nitido.Compartido/Multiempresa/` (1.1); proyecto `Nitido.Compartido.Api` con su `.csproj`, en `Nitido.slnx` (`/src/`) y referenciado por el host y por los dos `Api`; `AgregarApiNitido` y `ContextoSesionHttp`; `IDeFirma` en `Usuario`, `Cliente` y `AccesoCliente` (con `UuidFirmas` y la nueva `AccesoCliente.Crear`); `ScriptsSql`. Salida: `dotnet build` sin advertencias y las pruebas de arquitectura en verde. Medio día. | 0 | — |
| 2 | `tester` | Pruebas primero: las de 8.1 con sus ayudantes (`SiembraDeDosFirmas`, `EndpointsDePrueba`, `ContextoDeModeloDePrueba`, `ConsultasSql` con sesión, `ContextoSesionDePrueba` y `FabricaNitido` ajustados), las adaptaciones de F0-02a y el caso sintético de 1.4. Deben fallar por la razón correcta. | 1 | 3, 4a |
| 3 | `dev-backend` | Filtro global en `DbContextNitido` y el de `Firma` en `FirmasDbContext` (3); `InterceptorDeContextoSql` y su registro (4); regla nueva de auditoría (1.2); `FiltroClienteActivo`, `MapearRutasDelCliente` y `ClienteActivoRequerido` (6.1); `AccesoAClientes` y su registro; `Program.cs`. | 1 | 2, 4a |
| 4a | `dba` | Configuraciones de 2.2 (`CHECK` de `T_FIRMAS`, llave alterna, llave foránea compuesta); scripts `0001-firmas-politica.sql` y `0002-clientes-politica.sql` en `db/seguridad-por-fila/` (5.2); `EmbeddedResource` en los dos `.csproj` de `Infraestructura`. | 1 | 2, 3 |
| 4b | `dba` | Migraciones `AgregarSeguridadPorFilaDeFirmas` y `AgregarSeguridadPorFilaDeClientes`: lo que genera EF más lo escrito a mano (relleno de 2.3 con el versionado apagado, `Sql` con el script, `Down` en orden inverso). Revisa el SQL (lotes `GO`, sin cascadas, nombres), regenera `db/scripts/` y actualiza `db/README.md` (carpeta `seguridad-por-fila/`, migraciones, regla de migraciones de datos). Aplica las cuatro migraciones en orden sobre una base vacía del contenedor local. | 4a, 3 (para ver la convención del filtro en el modelo) | — |
| 5 | `tester` | Verificación: `dotnet build` sin advertencias, `dotnet format Nitido.slnx --verify-no-changes` y `dotnet test` completo. Anota el tiempo total para F0-06. | 2, 3, 4b | — |
| 6 | `dev-backend` | Verificación manual en local: `dotnet ef database update` en orden sobre una base vacía; con `sa` y sin sesión, `SELECT COUNT(*) FROM T_EMPRESAS` da 0 aunque haya filas; `/salud` responde `200`. | 5 | — |
| 7 | `revisor` | Revisión: que el filtro solo use miembros de la instancia; que no haya `AddDbContextPool` ni `IgnoreQueryFilters` sin justificar; que ningún predicado tenga excepciones por rol o por usuario; que los `_HISTORIAL` tengan solo FILTER; que las aserciones negativas tengan control positivo; que el `404` sea igual en los tres casos; capas y referencias de `Nitido.Compartido.Api`. | 6 | — |

`dev-frontend` y `devops` no participan. El `contador` no participa: no hay reglas contables.

#### 8.7 Cambios propuestos a archivos que el arquitecto no edita

Los aplica quien corresponda cuando el usuario apruebe el ADR 0003.

1. **Skill `convenciones-bd`, sección «Multiempresa»,** agregar:
   - «Los objetos de seguridad por fila van en el esquema `seguridad`: funciones `seguridad.FN_<MODULO>_<QUE_VALIDA>` con `SCHEMABINDING` y una política por módulo, `seguridad.PS_<MODULO>`, con `STATE = ON, SCHEMABINDING = ON`. Las funciones no se comparten entre módulos. El SQL vive en `db/seguridad-por-fila/NNNN-<modulo>-<tema>.sql` y lo aplica la migración del módulo; un script aplicado no se edita.»
   - «Toda tabla con `UUID_EMPRESAS` o `UUID_FIRMAS` lleva FILTER y los cuatro BLOCK; su `_HISTORIAL`, solo FILTER.»
   - «Ningún predicado tiene excepciones por rol ni por usuario de la base.»
   - «Una migración que mueve datos en tablas protegidas apaga la política de su módulo y la vuelve a encender en la misma migración. Una que cambia una columna usada por un predicado quita el predicado antes y lo pone después.»
   - Cambiar «filtra por `SESSION_CONTEXT(N'UUID_EMPRESAS')` y las empresas permitidas del usuario» por «filtra por la firma de la sesión y, en las tablas de datos de un cliente, por el cliente activo (`SESSION_CONTEXT(N'UUID_EMPRESAS')`), y siempre por las empresas permitidas del usuario en `T_USUARIOS_EMPRESAS`».
2. **`docs/arquitectura-backend.md`, sección 2:** en `Nitido.Compartido`, agregar `IAccesoAClientes` (con la razón de la decisión 7); en `Nitido.Compartido.Api`, agregar `MapearRutasDelCliente` y anotar que `ARespuestaHttp` llega en F0-03a; en `Nitido.Compartido.Infraestructura`, agregar `ScriptsSql` y, desde F0-04, `CrearAlcanceConContexto`. En 5.3, `Api_NoReferenciaInfraestructura` cubre también `Nitido.Compartido.Api` (1.4).
3. **Agente `revisor`, «Convenciones»:** agregar «multiempresa: el filtro global solo lee miembros de la instancia del `DbContext`; nada de `AddDbContextPool`; `IgnoreQueryFilters` solo con nombre y justificado; `CrearAlcanceConContexto` solo en registro de firma, semilla de Development y trabajos en segundo plano; las rutas de cliente solo con `MapearRutasDelCliente`».
4. **F0-03a, diseño técnico:** retomar la propuesta de ingreso del ADR 0003 y los nombres de `ReclamosDeSesion`.
5. **F0-04, diseño técnico:** registro con `CrearAlcanceConContexto` y el usuario «sistema»; `409` por el índice único de correo; la lista de clientes filtra por acceso también en EF (2.1); las rutas de detalle con `MapearRutasDelCliente`.

## Fuera de alcance

- Asignar usuarios a clientes desde una pantalla. En la fase 0, quien crea el cliente queda con acceso (F0-04).
- Roles de contador titular, auxiliar y solo lectura con acceso por cliente (plan, «Entornos y seguridad»): historia posterior.
- Endurecer el usuario de base de datos en la nube.
