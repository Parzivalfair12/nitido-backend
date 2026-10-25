# F0-02a · Fase 0 · 2a · Conexión a SQL Server local y tablas de firmas, usuarios y clientes

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | F0-01b (Ver ADR 0001) |
| Orden de construcción | 2 de 10 |
| Etiquetas | `fase-0`, `historia`, `base-de-datos`, `backend`, `modulo:firmas`, `modulo:clientes`, `requiere-adr` |
| Estado | Terminada el 2026-10-18: revisada (1 hallazgo medio, M1, corregido y con prueba de regresión); 298 pruebas en verde (245 de unidad, 52 de integración, 1 contable). ADR 0002 aceptado. Sin issue en GitHub. |

> **Ver ADR 0001** y `docs/arquitectura-backend.md` (secciones 4.3 y 6.2). Ahora depende de F0-01b. Firmas y Clientes se crean con la plantilla de módulo, y nace `Nitido.Compartido.Infraestructura`. Decisiones del usuario del 2026-10-18, ya aplicadas en esta historia: el criterio `Dominio_NoReferenciaEntityFramework` pasó a `Dominio_SoloDependeDeCompartido`; `IUsuarioActual` desapareció a favor de una sola `IContextoSesion`; y la clase que mapea `T_USUARIOS` vive en `Firmas.Infraestructura`.

Esta historia sale de partir la historia 2 original en dos; la otra parte es F0-02b. Las dos juntas pasaban de 2 días, y la multiempresa merece su propio PR y su propia revisión.

## Historia

Como desarrollador de nítido quiero conectar la API al SQL Server local con los secretos fuera del repositorio y crear las tablas de firmas, usuarios y clientes según las convenciones para que el ingreso y los clientes tengan dónde guardar sus datos.

## Criterios de aceptación

### Conexión

- [ ] La cadena de conexión se lee solo de `ConnectionStrings:Nitido`. En local se carga con `dotnet user-secrets set "ConnectionStrings:Nitido" "<cadena>" -p Nitido.Api`, y ningún archivo versionado la contiene.
- [ ] Si falta la cadena, la API no arranca y registra: «Falta la cadena de conexión ConnectionStrings:Nitido. Configúrala con dotnet user-secrets (ver README).»
- [ ] `GET /salud` responde `200` con el contenedor SQL encendido y `503` con el contenedor apagado. Lo comprueba la prueba `Salud_Responde503SinBaseDeDatos`.

### Módulos

- [ ] Existen `Modulos/Firmas/` y `Modulos/Clientes/`, cada uno con los proyectos `Dominio`, `Aplicacion`, `Infraestructura` y `Api`, agregados a `Nitido.slnx`.
- [ ] La prueba `Dominio_SoloDependeDeCompartido` (F0-01b) pasa con `Firmas.Dominio` y `Clientes.Dominio` ya en la solución. (Reemplaza a `Dominio_NoReferenciaEntityFramework`; Ver ADR 0001, 2026-10-18.)
- [ ] La clase que mapea `T_USUARIOS` está en `Firmas.Infraestructura`, no en `Firmas.Dominio`. (Ver ADR 0001, 2026-10-18.)
- [ ] `Nitido.Compartido/Multiempresa/` expone `IContextoSesion`, la única interfaz del contexto de la petición. En esta historia solo trae `UuidUsuarios`; F0-02b le agrega la firma y el cliente activo. No existe `IUsuarioActual`. (Ver ADR 0001, 2026-10-18.)
- [ ] La configuración de EF vive en `IEntityTypeConfiguration<T>` dentro de `Infraestructura`, sin atributos en el dominio.

### Tablas

Las columnas exactas las fija el `dba` en el diseño técnico.

- [ ] `T_FIRMAS` tiene `UUID_FIRMAS` y `NOMBRE NVARCHAR(200)`.
- [ ] `T_USUARIOS` tiene:
  - `UUID_USUARIOS` y `UUID_FIRMAS`;
  - `NOMBRE`, `CORREO` y `CORREO_NORMALIZADO`, con el índice `UX_T_USUARIOS_CORREO_NORMALIZADO`, porque un correo pertenece a una sola firma;
  - `ROL VARCHAR(20)`, con una restricción que en la fase 0 solo admite `ADMINISTRADOR`;
  - `ACTIVO BIT`.
- [ ] `T_EMPRESAS`, el cliente de la firma, tiene:
  - `UUID_EMPRESAS` y `UUID_FIRMAS`;
  - `TIPO_DOCUMENTO VARCHAR(4)`, que guarda el código de la DIAN (31 = NIT);
  - `NUMERO_DOCUMENTO VARCHAR(20)` y `DIGITO_VERIFICACION CHAR(1)`;
  - `RAZON_SOCIAL NVARCHAR(200)` y `ACTIVO BIT`;
  - el índice `UX_T_EMPRESAS_UUID_FIRMAS_TIPO_DOCUMENTO_NUMERO_DOCUMENTO`.
- [ ] `T_USUARIOS_EMPRESAS` guarda los clientes a los que puede entrar cada usuario. Tiene `UUID_USUARIOS_EMPRESAS`, `UUID_USUARIOS` y `UUID_EMPRESAS`, y un índice que empieza por `UUID_EMPRESAS`.
- [ ] `T_USUARIOS` y `T_USUARIOS_EMPRESAS` son tablas temporales de SQL Server (`SYSTEM_VERSIONING = ON`) con historial en `T_USUARIOS_HISTORIAL` y `T_USUARIOS_EMPRESAS_HISTORIAL`. Lo comprueba la prueba `Esquema_TablasTemporalesTienenHistorial`, que verifica que las columnas `PERIODO_INICIO` y `PERIODO_FIN` existen con tipo `datetime2` y precisión 3, que son `GENERATED ALWAYS AS ROW START` y `ROW END`, que están ocultas (`is_hidden = 1`) y que el historial `T_<NOMBRE>_HISTORIAL` existe. (Agregado en el paso 3 por la decisión del usuario del 2026-10-18 sobre la pregunta abierta 3. El usuario confirmó el mismo día que `T_FIRMAS` y `T_EMPRESAS` también lo son, con historial en `T_FIRMAS_HISTORIAL` y `T_EMPRESAS_HISTORIAL`.)
- [ ] Hay una migración por módulo afectado: `CrearFirmasYUsuarios` en Firmas y `CrearClientes` en Clientes. `dotnet ef database update` las aplica sin errores sobre una base vacía, y el SQL de `dotnet ef migrations script` va adjunto al PR.

### Pruebas

Todas son de integración y usan Testcontainers con SQL Server 2025, la misma versión del contenedor local.

- [ ] `Migraciones_SeAplicanSobreBaseLimpia`.
- [ ] `Esquema_SigueConvenciones` consulta `INFORMATION_SCHEMA` y `sys.*`, y falla si:
  - una tabla de nítido no empieza por `T_`;
  - una llave primaria no se llama `UUID_<TABLA sin T_>`;
  - falta alguna de las columnas `FECHA_CREACION`, `UUID_USUARIOS_CREACION`, `FECHA_MODIFICACION` o `UUID_USUARIOS_MODIFICACION`;
  - una columna `UUID_EMPRESAS` no tiene llave foránea a `T_EMPRESAS` o no tiene un índice que empiece por ella.
- [ ] `Esquema_NoUsaFloatRealNiMoney` falla si alguna columna es `float`, `real` o `money`. La fase 0 no maneja dinero, pero la regla 4 queda protegida desde ya.
- [ ] `Auditoria_LlenaFechasYUsuario` comprueba lo que hace el interceptor de `SaveChanges`:
  - al crear, llena `FECHA_CREACION` en UTC, con `DATETIME2(3)` y tomada de `TimeProvider`, y `UUID_USUARIOS_CREACION`, tomado de `IContextoSesion.UuidUsuarios` (Ver ADR 0001);
  - al modificar, llena `FECHA_MODIFICACION` y `UUID_USUARIOS_MODIFICACION`.
- [ ] `Identificadores_SonGuidVersion7`: las llaves nuevas de las cuatro tablas son GUID versión 7 creados con `Guid.CreateVersion7(ahora)`, con `ahora` tomado de `TimeProvider` (`docs/arquitectura-backend.md`, 3.6). Con un reloj controlado, la prueba comprueba que `Version` es 7 y que la marca de tiempo del GUID (sus primeros 48 bits) coincide con el reloj. (Ajustado en el paso 3, 2026-10-18: antes decía `Guid.CreateVersion7()` sin el momento, que toma la hora del sistema y no se puede probar de forma determinista.)

## Pantalla

No aplica.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables, y estas tablas no son contables. La pregunta abierta 3 del índice (tablas temporales en `T_USUARIOS` y `T_USUARIOS_EMPRESAS`) quedó decidida el 2026-10-18: sí.

## Diseño técnico

18 de octubre de 2026 · arquitecto. Repositorio: solo `nitido-backend`. Decisiones de base: ADR 0001 (aceptado) y [ADR 0002](../adr/0002-dbcontext-y-migraciones-por-modulo.md) (aceptado el 2026-10-18: un `DbContext` y un historial de migraciones por módulo, en una sola base).

### Puntos que ya estaban decididos

Se conservan; el detalle de abajo los aplica.

- **Un `DbContext` por módulo,** cada uno con su propia tabla de historial de migraciones (`__EFMigrationsHistory_Firmas`, `__EFMigrationsHistory_Clientes`). Cada historia lleva una migración por módulo afectado. Ver ADR 0002.
- **Contexto de la petición:** una sola interfaz, `IContextoSesion`, en `Nitido.Compartido/Multiempresa/`. La usa el interceptor de auditoría de `Nitido.Compartido.Infraestructura`. Las pruebas de auditoría usan una implementación solo para pruebas (Ver ADR 0001, 2026-10-18).
- **Llaves foráneas entre módulos:** se permiten en SQL, porque es una sola base. En C#, Clientes no usa nada de Firmas en esta historia (ver 1.4).
- **Usuario «sistema»:** un `UUID` fijo para los registros que crea el sistema, por ejemplo durante el registro de una firma. Las columnas de auditoría no llevan llave foránea, igual que en el ejemplo de `convenciones-bd`. Valor: `00000000-0000-0000-0000-000000000001`. No tiene fila en `T_USUARIOS`. La constante en código nace con su primer uso (semilla de F0-03a o registro de F0-04), en `Nitido.Compartido/Multiempresa/` si la usan dos módulos.
- **Configuración de EF:** se traduce de PascalCase en C# a las convenciones `T_` y `UUID_` de la base (plan, «Repositorio y convenciones»). Ver 1.2.

### 1. Proyectos y módulos

#### 1.1 Proyectos que nacen o cambian

| Proyecto | Referencias de proyecto | Paquetes y marcos | Qué trae en F0-02a |
| --- | --- | --- | --- |
| `Nitido.Compartido` (existe) | ninguna | ninguno | `Multiempresa/IContextoSesion.cs`: `Guid UuidUsuarios { get; }`. Nada más. F0-02b le agrega `UuidFirmas` y `UuidEmpresas?`. |
| `Nitido.Compartido.Infraestructura` (nuevo, carpeta `Nitido.Compartido.Infraestructura/`) | `Nitido.Compartido` | `Microsoft.EntityFrameworkCore.SqlServer` | Ver 1.2. |
| `Nitido.Firmas.Dominio` | `Nitido.Compartido` | ninguno | `Firmas/Firma.cs`. |
| `Nitido.Firmas.Aplicacion` | `Nitido.Firmas.Dominio` | ninguno | El `.csproj` y un `MarcadorDeEnsamblado` (ver 7b). Los casos de uso llegan en F0-03a y F0-04. |
| `Nitido.Firmas.Infraestructura` | `Nitido.Firmas.Aplicacion`, `Nitido.Compartido.Infraestructura` | ninguno propio | `Persistencia/FirmasDbContext.cs`, `Persistencia/Usuario.cs`, `Persistencia/Configuraciones/` (dos clases), `Migraciones/`, `ModuloFirmas.cs`. `InternalsVisibleTo Nitido.Pruebas.Integracion`. |
| `Nitido.Firmas.Api` | `Nitido.Firmas.Aplicacion` | `FrameworkReference Microsoft.AspNetCore.App` | `EndpointsFirmas.cs` y un `MarcadorDeEnsamblado` (ver 7b). |
| `Nitido.Clientes.Dominio` | `Nitido.Compartido` | ninguno | `Clientes/Cliente.cs`, `Clientes/AccesoCliente.cs`. |
| `Nitido.Clientes.Aplicacion` | `Nitido.Clientes.Dominio` | ninguno | El `.csproj` y un `MarcadorDeEnsamblado` (ver 7b). `CrearCliente` llega en F0-04. |
| `Nitido.Clientes.Infraestructura` | `Nitido.Clientes.Aplicacion`, `Nitido.Compartido.Infraestructura` | ninguno propio | `Persistencia/ClientesDbContext.cs`, `Persistencia/Configuraciones/` (dos clases), `Migraciones/`, `ModuloClientes.cs`. `InternalsVisibleTo Nitido.Pruebas.Integracion`. |
| `Nitido.Clientes.Api` | `Nitido.Clientes.Aplicacion` | `FrameworkReference Microsoft.AspNetCore.App` | `EndpointsClientes.cs` y un `MarcadorDeEnsamblado` (ver 7b). |
| `Nitido.Api` (host, existe) | + `Nitido.Firmas.Infraestructura`, `Nitido.Firmas.Api`, `Nitido.Clientes.Infraestructura`, `Nitido.Clientes.Api` | + `Microsoft.EntityFrameworkCore.Design` con `PrivateAssets="all"` | `Program.cs` (1.3), `Configuracion/ComprobacionSqlServer.cs` (3.2) y `<UserSecretsId>` en el `.csproj` (lo agrega `dotnet user-secrets init -p Nitido.Api`). |
| `Nitido.Pruebas.Integracion` (existe) | sin cambios (entra por el host) | + `Testcontainers.MsSql` | Ver 8. |

Los `.csproj` siguen la plantilla 4.3 de `docs/arquitectura-backend.md`. Los proyectos `Api` llevan solo la `FrameworkReference` porque `Nitido.Compartido.Api` nace en F0-02b. `Modulos/.gitkeep` se borra.

Entidades mínimas (las completa F0-04; aquí solo existen para mapear, migrar y probar):

- **`Firma`** (`Firmas.Dominio`): `Uuid`, `Nombre`. Fábrica `Firma.Crear(string nombre, DateTimeOffset ahora)`, que asigna `Uuid = Guid.CreateVersion7(ahora)`. Sin validaciones de negocio todavía; F0-04 puede cambiar la fábrica a `Resultado<Firma>`.
- **`Usuario`** (`Firmas.Infraestructura`, `internal`): `Uuid`, `UuidFirmas`, `Nombre`, `Correo`, `CorreoNormalizado`, `Rol` (texto), `Activo`. Es la clase de persistencia de `T_USUARIOS` (Ver ADR 0001). No tiene fábrica de dominio: su llave sale del generador de 1.2. En F0-03a pasa a heredar de `IdentityUser<Guid>`; sus propiedades pueden cambiar de nombre (`Email`, `NormalizedEmail`) porque el contrato son los nombres de columna, que no cambian.
- **`Cliente`** (`Clientes.Dominio`): `Uuid`, `UuidFirmas`, `TipoDocumento`, `NumeroDocumento`, `DigitoVerificacion`, `RazonSocial`, `Activo` y la colección `Accesos`. Fábrica `Cliente.Crear(Guid uuidFirmas, string tipoDocumento, string numeroDocumento, string digitoVerificacion, string razonSocial, Guid uuidUsuariosConAcceso, DateTimeOffset ahora)`, que crea el cliente y su primer `AccesoCliente`. F0-04 reemplaza los tres textos del documento por el objeto de valor `Nit`.
- **`AccesoCliente`** (`Clientes.Dominio`): `Uuid`, `UuidUsuarios`. Entidad hija de `Cliente`; la llave hacia el cliente (`UUID_EMPRESAS`) es una propiedad sombra de la relación, así que el dominio no la carga.

Ninguna clase de dominio tiene atributos de EF ni referencia a la base.

#### 1.2 `Nitido.Compartido.Infraestructura`

Todo `internal`, salvo lo marcado como público.

| Pieza | Visibilidad | Qué hace |
| --- | --- | --- |
| `Persistencia/DbContextNitido` | pública, abstracta | Clase base de todos los `DbContext` de los módulos. Constructor `(DbContextOptions opciones)`. En `ConfigureConventions`: `decimal` como `DECIMAL(19,2)`, `DateTime` y `DateTime?` con precisión 3, y agrega `ConvencionDeNombres`. En `OnModelCreating`: aplica las `IEntityTypeConfiguration<T>` del ensamblado del contexto derivado (`GetType().Assembly`), y después, a cada entidad que no sea de tipo propio, le agrega las cuatro columnas de auditoría como propiedades sombra y el generador de llaves de abajo si su llave es un solo `Guid`. Todas las llaves foráneas quedan con `DeleteBehavior.Restrict`. |
| `Persistencia/ConvencionDeNombres` | interna | Convención de EF (`IModelFinalizingConvention`) que solo pone nombres que nadie configuró a mano (un `HasColumnName` o `HasDatabaseName` explícito gana): columna = la propiedad en mayúsculas con guion bajo (`RazonSocial` → `RAZON_SOCIAL`, `UuidFirmas` → `UUID_FIRMAS`); llave primaria de una sola columna = `UUID_<TABLA sin T_>`, sin importar el nombre de la propiedad (así sirve para el `Id` de Identity en F0-03a); restricción de llave primaria `PK_<TABLA>`; llave foránea `FK_<TABLA>_<TABLA_PRINCIPAL>` (o `FK_<TABLA>_<COLUMNA>` si la tabla referencia dos veces la misma); índice `IX_<TABLA>_<COLUMNAS>` y, si es único, `UX_<TABLA>_<COLUMNAS>`. El nombre de la tabla no se deduce: cada configuración lo declara (`Cliente` se guarda en `T_EMPRESAS`, y el plural en español no se puede calcular). |
| `Persistencia/ColumnasDeAuditoria` | pública, estática | Los nombres de las cuatro propiedades sombra (`FechaCreacion`, `UuidUsuariosCreacion`, `FechaModificacion`, `UuidUsuariosModificacion`), para que las pruebas y el interceptor no repitan textos. |
| `Persistencia/InterceptorDeAuditoria` | interna, singleton | `SaveChangesInterceptor` (versión síncrona y asíncrona). Recibe `TimeProvider` por constructor. En cada `SaveChanges` resuelve `IContextoSesion` desde los servicios de la aplicación del contexto (`eventData.Context.GetService<IContextoSesion>()`) y: a las filas nuevas les pone `FECHA_CREACION = tiempo.GetUtcNow().UtcDateTime` y `UUID_USUARIOS_CREACION = sesion.UuidUsuarios`; a las modificadas, `FECHA_MODIFICACION` y `UUID_USUARIOS_MODIFICACION`, y marca las de creación como no modificadas. Si no hay `IContextoSesion` registrado, falla (excepción): nunca guarda sin usuario. Se resuelve en cada guardado, y no por constructor, para que el host no necesite una implementación en F0-02a (ver 7, «Contexto de sesión»). |
| Asignación de llaves (en `InterceptorDeAuditoria`) | interno | Pone `Guid.CreateVersion7(tiempo.GetUtcNow())`, con el `TimeProvider` de los servicios, solo a las entradas `Added` cuya llave llega vacía (`Guid.Empty`). Las entidades de dominio traen su llave desde la fábrica (3.6); esto cubre las clases de persistencia sin fábrica (`Usuario` aquí, y el usuario de Identity en F0-03a). **Corregido el 2026-10-18 (hallazgo M1):** ya no existe la clase `GeneradorGuidVersion7` ni un generador de valores de EF; ver la nota debajo de esta tabla. **Limitación conocida:** como no hay generador de EF, dos entidades nuevas con llave vacía agregadas al mismo `DbContext` antes de guardar chocan en el mapa de identidad de EF (ambas son `Guid.Empty`) y EF lanza una excepción antes de que corra el interceptor. Quien agregue varias entidades de persistencia sin fábrica a la vez debe asignarles la llave al crearlas (`Guid.CreateVersion7(ahora)`). En F0-03a, `UserManager` asigna el `Id` por su cuenta. |
| `Persistencia/TablaTemporal` | pública, estática | Método de extensión `MapearATablaTemporal(this EntityTypeBuilder b, string tabla)`: llama a `ToTable(tabla, t => t.IsTemporal(...))` con historial `<tabla>_HISTORIAL` y periodos `PERIODO_INICIO` y `PERIODO_FIN`. Así los nombres de 2.2 no se repiten en cada configuración. |
| `PersistenciaNitido` | pública, estática | `AgregarPersistenciaNitido(this IServiceCollection)`: registra `OpcionesBaseDeDatos` con `ConnectionStrings:Nitido`, su validación y `ValidateOnStart()` (ver 3.1), y el `InterceptorDeAuditoria`. Se llama una vez, desde el host. `UsarSqlServerNitido(this DbContextOptionsBuilder, IServiceProvider, string modulo)`: `UseSqlServer(cadena, sql => { sql.MigrationsHistoryTable($"__EFMigrationsHistory_{modulo}"); sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: 5 s, null); })` y agrega el interceptor. La usa cada `Agregar<M>()`. |
| `OpcionesBaseDeDatos` | pública | `string CadenaDeConexion`. La lee también el host para la comprobación de salud. |

> **Nota del 2026-10-18 · hallazgo M1 de la revisión.** El diseño original marcaba las llaves `Guid` como `ValueGenerated.OnAdd` con un `ValueGenerator`. Con eso, EF trataba como `Modified` (y no como `Added`) a un hijo nuevo con llave ya asignada que se agregaba a un agregado cargado, y el guardado fallaba o actualizaba una fila que no existe. Corrección: las llaves de un solo `Guid` son `ValueGeneratedNever`, y el `InterceptorDeAuditoria` asigna `Guid.CreateVersion7(ahora)` (con `ahora` de `TimeProvider`) solo a las entradas `Added` cuya llave es `Guid.Empty`. La regla «el generador se usa solo cuando la llave llega vacía» se mantiene, y las llaves que trae la fábrica del dominio no se tocan. Además, `ExecuteUpdate`, `ExecuteDelete` y el SQL escrito a mano no pasan por el interceptor: quien los use llena la auditoría a mano.

`TimeProvider.System` lo registra el host (arquitectura 4.4), no este proyecto.

#### 1.3 Composición en el host

`Program.cs`, sobre la canalización actual, que no cambia:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AgregarPersistenciaNitido();          // una vez, antes de los módulos
builder.Services
    .AgregarFirmas(builder.Configuration)              // AddDbContext<FirmasDbContext>((sp, o) => o.UsarSqlServerNitido(sp, "Firmas"))
    .AgregarClientes(builder.Configuration);           // AddDbContext<ClientesDbContext>((sp, o) => o.UsarSqlServerNitido(sp, "Clientes"))
builder.Services.AddHealthChecks()
    .AddCheck<ComprobacionSqlServer>("sqlserver", timeout: TimeSpan.FromSeconds(3));
// ...
app.MapearFirmas();                                    // MapGroup("/api/firmas") sin rutas
app.MapearClientes();                                  // MapGroup("/api/clientes") sin rutas
```

Además, un `catch (OptionsValidationException ex)` antes del `catch` general, que registra `ex.Message` como evento `Fatal` sin la pila (es un error de configuración, no del programa) y devuelve 1. Ojo: «sin la pila» vale solo para ese registro `Fatal`. El framework escribe además, por su cuenta, un registro `Hosting failed to start` en nivel `Error` con la pila de la excepción; no es código nuestro y no se suprime.

- `Agregar<M>(IServiceCollection, IConfiguration)` conserva la firma de la arquitectura (4.4) aunque hoy no lea la configuración; F0-03a la usa.
- **Api en esta historia:** solo `Mapear<M>()`, que crea el grupo sin rutas. Un grupo vacío no publica nada: el documento OpenAPI no cambia. Es lo mínimo que piden la plantilla y `Host_ComponeTodosLosModulos`.
- **Los `DbContext` son `internal`.** `dotnet ef` los encuentra por el registro de `AddDbContext` en el host. Si la herramienta exigiera un tipo público, se deja público y se anota (arquitectura 3.8).

#### 1.4 ¿Hace falta `Firmas.Contratos`?

No. En esta historia Clientes no llama a Firmas desde C#: las dos llaves foráneas hacia tablas de Firmas (`T_EMPRESAS.UUID_FIRMAS → T_FIRMAS` y `T_USUARIOS_EMPRESAS.UUID_USUARIOS → T_USUARIOS`) se crean por nombre en la migración de Clientes. La firma del usuario vendrá de `IContextoSesion` (F0-02b), así que es probable que F0-04 tampoco lo necesite.

#### 1.5 Pruebas de arquitectura que deben seguir en verde

Las 12 reglas de `ArquitecturaPruebas` y las de modelo no se tocan. Lo que exige cada una a esta historia:

| Prueba | Qué cuidar |
| --- | --- |
| `Solucion_ProyectosSiguenConvencionDeNombresYCarpetas` | Rutas `Modulos/<M>/<Capa>/Nitido.<M>.<Capa>.csproj` y `Nitido.Compartido.Infraestructura/Nitido.Compartido.Infraestructura.csproj`. Los 9 proyectos nuevos en `Nitido.slnx`: `Nitido.Compartido.Infraestructura` en `/src/`, los de cada módulo en `/src/Modulos/<M>/`. Toda `ProjectReference` debe apuntar a un proyecto de la solución. |
| `Dominio_SoloDependeDeCompartido` | `Firma`, `Cliente` y `AccesoCliente` solo usan la librería base y `Nitido.Compartido`. Sin paquetes. |
| `Aplicacion_NoDependeDeInfraestructuraNiWeb` | Los `Aplicacion` solo referencian su `Dominio`; todavía sin `FluentValidation`. |
| `Api_NoReferenciaInfraestructura` | Los `Api` solo referencian su `Aplicacion`, con la `FrameworkReference`. |
| `Infraestructura_NoReferenciaApi` y `Modulos_SoloSeReferencianPorContratos` | `Clientes.Infraestructura` no referencia nada de Firmas. |
| `InfraestructuraYApi_SoloLasReferenciaElHost` | `Nitido.Compartido.Infraestructura` solo lo referencian los dos `Infraestructura`, el host (si lo necesita) y las pruebas. |
| `Host_ComponeTodosLosModulos` | El host referencia `Infraestructura` y `Api` de Firmas y de Clientes. Cada ensamblado nuevo debe quedar en la salida de las pruebas (llega por el host). |
| `ArchivosComunesDeMsBuild_NoAgreganReferencias` | `Directory.Packages.props` solo gana `PackageVersion`. Nada de `GlobalPackageReference`. |
| `SdkDeNube_SoloEnProyectosDeProveedor` | Ningún `.csproj` declara un paquete `Azure.*` y ningún ensamblado `Nitido.*` usa uno. Ver 7, «Regla 7». |
| `Compartido_SoloDependeDelFramework` | `IContextoSesion` es una interfaz pura. |

Prueba nueva propuesta en la arquitectura (6.2-5), que esta historia agrega: `Tablas_PertenecenAUnSoloDbContext`. Vive en `Nitido.Pruebas.Integracion` (necesita ver los `DbContext` internos) y no se conecta a la base: construye el modelo de cada contexto con una cadena ficticia y falla si dos contextos nombran la misma tabla o la misma tabla de historial.

### 2. Tablas

#### 2.1 Dueño de cada tabla

| Tabla | Módulo y `DbContext` | Temporal | Historial |
| --- | --- | --- | --- |
| `T_FIRMAS` | Firmas · `FirmasDbContext` | Sí (decidido, ver 2.3) | `T_FIRMAS_HISTORIAL` |
| `T_USUARIOS` | Firmas · `FirmasDbContext` | Sí (decidido) | `T_USUARIOS_HISTORIAL` |
| `T_EMPRESAS` | Clientes · `ClientesDbContext` | Sí (decidido, ver 2.3) | `T_EMPRESAS_HISTORIAL` |
| `T_USUARIOS_EMPRESAS` | Clientes · `ClientesDbContext` | Sí (decidido) | `T_USUARIOS_EMPRESAS_HISTORIAL` |
| `__EFMigrationsHistory_Firmas` | Firmas (la crea EF) | No | — |
| `__EFMigrationsHistory_Clientes` | Clientes (la crea EF) | No | — |

Cómo se evita que un `DbContext` mapee tablas de otro:
1. Cada contexto aplica solo las configuraciones de su propio ensamblado (`DbContextNitido`, 1.2), y un módulo no puede ver las clases de otro (compilador y pruebas de arquitectura).
2. Las llaves foráneas entre módulos van por nombre de tabla en la migración, sin entidad ni navegación (2.4).
3. Si alguien crea en Clientes una clase propia mapeada a `T_USUARIOS`, la detecta `Tablas_PertenecenAUnSoloDbContext` (1.5).

#### 2.2 Columnas exactas

Esquema `dbo`. El SQL de abajo fija nombres, tipos, nulabilidad, llaves e índices de lo que genera `dotnet ef migrations script`. El **orden de las columnas es cosmético y la spec no lo fija**: EF las ordena a su manera (por ejemplo, `FECHA_MODIFICACION` antes de `UUID_USUARIOS_CREACION`, `PERIODO_FIN` antes de `PERIODO_INICIO` y `UUID_EMPRESAS` después de los periodos). Los nombres de restricciones e índices salen de `ConvencionDeNombres` y de las configuraciones. Ninguna columna tiene valor por defecto, salvo los periodos, que genera SQL Server. Sin borrado en cascada.

```sql
-- Módulo Firmas · migración CrearFirmasYUsuarios
CREATE TABLE T_FIRMAS (
  UUID_FIRMAS                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_T_FIRMAS PRIMARY KEY,
  NOMBRE                     NVARCHAR(200)    NOT NULL,
  FECHA_CREACION             DATETIME2(3)     NOT NULL,
  UUID_USUARIOS_CREACION     UNIQUEIDENTIFIER NOT NULL,
  FECHA_MODIFICACION         DATETIME2(3)     NULL,
  UUID_USUARIOS_MODIFICACION UNIQUEIDENTIFIER NULL,
  PERIODO_INICIO             DATETIME2(3) GENERATED ALWAYS AS ROW START NOT NULL,
  PERIODO_FIN                DATETIME2(3) GENERATED ALWAYS AS ROW END   NOT NULL,
  PERIOD FOR SYSTEM_TIME (PERIODO_INICIO, PERIODO_FIN)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.T_FIRMAS_HISTORIAL));

CREATE TABLE T_USUARIOS (
  UUID_USUARIOS              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_T_USUARIOS PRIMARY KEY,
  UUID_FIRMAS                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_T_USUARIOS_T_FIRMAS REFERENCES T_FIRMAS(UUID_FIRMAS),
  NOMBRE                     NVARCHAR(200)    NOT NULL,
  CORREO                     NVARCHAR(256)    NOT NULL,
  CORREO_NORMALIZADO         NVARCHAR(256)    NOT NULL,
  ROL                        VARCHAR(20)      NOT NULL CONSTRAINT CK_T_USUARIOS_ROL CHECK (ROL IN ('ADMINISTRADOR')),
  ACTIVO                     BIT              NOT NULL,
  FECHA_CREACION             DATETIME2(3)     NOT NULL,
  UUID_USUARIOS_CREACION     UNIQUEIDENTIFIER NOT NULL,
  FECHA_MODIFICACION         DATETIME2(3)     NULL,
  UUID_USUARIOS_MODIFICACION UNIQUEIDENTIFIER NULL,
  PERIODO_INICIO             DATETIME2(3) GENERATED ALWAYS AS ROW START NOT NULL,
  PERIODO_FIN                DATETIME2(3) GENERATED ALWAYS AS ROW END   NOT NULL,
  PERIOD FOR SYSTEM_TIME (PERIODO_INICIO, PERIODO_FIN)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.T_USUARIOS_HISTORIAL));
CREATE UNIQUE INDEX UX_T_USUARIOS_CORREO_NORMALIZADO ON T_USUARIOS (CORREO_NORMALIZADO);
CREATE INDEX IX_T_USUARIOS_UUID_FIRMAS ON T_USUARIOS (UUID_FIRMAS);

-- Módulo Clientes · migración CrearClientes (se aplica después de la de Firmas)
CREATE TABLE T_EMPRESAS (
  UUID_EMPRESAS              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_T_EMPRESAS PRIMARY KEY,
  UUID_FIRMAS                UNIQUEIDENTIFIER NOT NULL,   -- FK_T_EMPRESAS_T_FIRMAS, entre módulos (2.4)
  TIPO_DOCUMENTO             VARCHAR(4)       NOT NULL,   -- código DIAN; en la fase 0 solo '31' (NIT), lo valida F0-04
  NUMERO_DOCUMENTO           VARCHAR(20)      NOT NULL,
  DIGITO_VERIFICACION        CHAR(1)          NOT NULL,
  RAZON_SOCIAL               NVARCHAR(200)    NOT NULL,
  ACTIVO                     BIT              NOT NULL,
  FECHA_CREACION             DATETIME2(3)     NOT NULL,
  UUID_USUARIOS_CREACION     UNIQUEIDENTIFIER NOT NULL,
  FECHA_MODIFICACION         DATETIME2(3)     NULL,
  UUID_USUARIOS_MODIFICACION UNIQUEIDENTIFIER NULL,
  PERIODO_INICIO             DATETIME2(3) GENERATED ALWAYS AS ROW START NOT NULL,
  PERIODO_FIN                DATETIME2(3) GENERATED ALWAYS AS ROW END   NOT NULL,
  PERIOD FOR SYSTEM_TIME (PERIODO_INICIO, PERIODO_FIN)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.T_EMPRESAS_HISTORIAL));
CREATE UNIQUE INDEX UX_T_EMPRESAS_UUID_FIRMAS_TIPO_DOCUMENTO_NUMERO_DOCUMENTO
  ON T_EMPRESAS (UUID_FIRMAS, TIPO_DOCUMENTO, NUMERO_DOCUMENTO);

CREATE TABLE T_USUARIOS_EMPRESAS (
  UUID_USUARIOS_EMPRESAS     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_T_USUARIOS_EMPRESAS PRIMARY KEY,
  UUID_USUARIOS              UNIQUEIDENTIFIER NOT NULL,   -- FK_T_USUARIOS_EMPRESAS_T_USUARIOS, entre módulos (2.4)
  UUID_EMPRESAS              UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_T_USUARIOS_EMPRESAS_T_EMPRESAS REFERENCES T_EMPRESAS(UUID_EMPRESAS),
  FECHA_CREACION             DATETIME2(3)     NOT NULL,
  UUID_USUARIOS_CREACION     UNIQUEIDENTIFIER NOT NULL,
  FECHA_MODIFICACION         DATETIME2(3)     NULL,
  UUID_USUARIOS_MODIFICACION UNIQUEIDENTIFIER NULL,
  PERIODO_INICIO             DATETIME2(3) GENERATED ALWAYS AS ROW START NOT NULL,
  PERIODO_FIN                DATETIME2(3) GENERATED ALWAYS AS ROW END   NOT NULL,
  PERIOD FOR SYSTEM_TIME (PERIODO_INICIO, PERIODO_FIN)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = dbo.T_USUARIOS_EMPRESAS_HISTORIAL));
CREATE UNIQUE INDEX UX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_USUARIOS
  ON T_USUARIOS_EMPRESAS (UUID_EMPRESAS, UUID_USUARIOS);
CREATE INDEX IX_T_USUARIOS_EMPRESAS_UUID_USUARIOS ON T_USUARIOS_EMPRESAS (UUID_USUARIOS);
```

Los periodos del SQL de arriba se crean con `HIDDEN` (`PERIODO_INICIO DATETIME2(3) GENERATED ALWAYS AS ROW START HIDDEN NOT NULL`, y lo mismo con `ROW END`), porque EF Core 10 los crea así cuando son propiedades sombra. Decisión aceptada por el usuario el 2026-10-18 (ver 2.3).

Por qué cada decisión que no venía en los criterios:

- **`CORREO` y `CORREO_NORMALIZADO` en `NVARCHAR(256)`:** es el largo que usa Identity para el correo; F0-03a mapea `Email` y `NormalizedEmail` a estas columnas sin migrarlas.
- **`UX_T_USUARIOS_CORREO_NORMALIZADO` es global, no por firma:** un correo pertenece a una sola firma (criterio), y el ingreso (F0-03a) busca por correo antes de saber la firma.
- **`DIGITO_VERIFICACION` es `NOT NULL`:** la fase 0 solo admite NIT y todo NIT tiene dígito. Si más adelante se admite cédula sin dígito, pasar a `NULL` no es un cambio destructivo.
- **Sin `CHECK` en `TIPO_DOCUMENTO`:** el código lo valida el dominio en F0-04; ampliar a otros tipos no debe exigir una migración.
- **`UX_T_USUARIOS_EMPRESAS_UUID_EMPRESAS_UUID_USUARIOS` es única y empieza por `UUID_EMPRESAS`:** cumple la regla de multiempresa de `convenciones-bd`, impide dar dos veces el mismo acceso y sirve tal cual al predicado de seguridad por fila de F0-02b (`EXISTS` por empresa y usuario).
- **`IX_T_USUARIOS_EMPRESAS_UUID_USUARIOS`:** respalda la llave foránea hacia `T_USUARIOS` y la consulta «clientes a los que puede entrar este usuario».
- **`T_USUARIOS_EMPRESAS` no lleva `UUID_FIRMAS`:** los criterios no la piden y la firma se deduce de la empresa. Si F0-02b la necesita para el predicado, la agrega su migración.
- **Sin fila del usuario «sistema» y sin semillas:** no hay datos iniciales en esta historia.

#### 2.3 Tablas temporales

- **Decidido por el usuario (2026-10-18):** `T_USUARIOS` y `T_USUARIOS_EMPRESAS` son temporales.
- **`T_EMPRESAS`: sí, decidido por el usuario el 2026-10-18.** Es el dato maestro del cliente (NIT, razón social, activo), del mismo tipo que `T_TERCEROS`, que `convenciones-bd` ya exige temporal, y saber quién desactivó un cliente o le cambió la razón social es auditoría que pide el plan.
- **`T_FIRMAS`: sí, decidido por el usuario el 2026-10-18.** La firma es el inquilino y con una fila por firma el costo es nulo; así la regla queda simple: «toda tabla de la fase 0 es temporal».

Nombres y forma:
- Historial: `T_<NOMBRE>_HISTORIAL`, en `dbo`, como dice `convenciones-bd`. Lo crea SQL Server con las mismas columnas, sin llave primaria, sin llaves foráneas, sin `CHECK` y con su propio índice agrupado por los periodos (nombre que pone SQL Server).
- Periodos: `PERIODO_INICIO` y `PERIODO_FIN`, `DATETIME2(3)`, en UTC, **ocultos** (`HIDDEN`), como propiedades sombra de EF. Es como los crea EF Core 10 cuando los periodos son propiedades sombra; mapearlos a propiedades visibles de C# llega con EF 11. Aceptado por el usuario el 2026-10-18. Efecto: `SELECT *` no los devuelve, pero se piden por nombre; `INFORMATION_SCHEMA.COLUMNS` los lista; el tipo `datetime2(3)` y `SYSTEM_VERSIONING` no cambian. Cuando EF 11 permita mapearlos, pasarán a visibles. `convenciones-bd` fija estos nombres y esta condición (ajuste aplicado a la skill el 2026-10-18, ver el final).
- Precisión: la convención global de `DateTime` (precisión 3) alcanza a los periodos. Si el SQL generado los deja en `DATETIME2` sin precisión (7), el `dba` lo anota en el PR y se acepta: no son momentos de negocio.
- Retención del historial: sin límite (valor por defecto). La conservación se decide en la fase 5 del plan.
- La columna de auditoría y el periodo no se reemplazan: `FECHA_MODIFICACION`/`UUID_USUARIOS_MODIFICACION` dicen quién; el historial dice qué cambió.

#### 2.4 Llaves foráneas entre módulos

Van en la migración `CrearClientes`, escritas a mano después de generarla, con `ON DELETE NO ACTION`, y se quitan primero en su `Down`:

| Nombre | Tabla y columna | Referencia |
| --- | --- | --- |
| `FK_T_EMPRESAS_T_FIRMAS` | `T_EMPRESAS.UUID_FIRMAS` | `T_FIRMAS.UUID_FIRMAS` |
| `FK_T_USUARIOS_EMPRESAS_T_USUARIOS` | `T_USUARIOS_EMPRESAS.UUID_USUARIOS` | `T_USUARIOS.UUID_USUARIOS` |

La instantánea del modelo de Clientes no las conoce, y está bien: EF no las va a intentar quitar ni crear de nuevo.

#### 2.5 Qué hace el `dba`

1. Escribe las cuatro `IEntityTypeConfiguration<T>` (2.2) en `Persistencia/Configuraciones/` de cada módulo: tabla con `MapearATablaTemporal`, largos (`HasMaxLength`), `IsUnicode(false)` en las columnas `VARCHAR`, `IsFixedLength()` en el `CHAR(1)`, índices únicos, el `CHECK` de `ROL` (`HasCheckConstraint("CK_T_USUARIOS_ROL", "ROL IN ('ADMINISTRADOR')")`) y la relación `Cliente.Accesos` con la llave sombra `UuidEmpresas`.
2. Genera las migraciones cuando `ConvencionDeNombres` esté lista:
   `dotnet ef migrations add CrearFirmasYUsuarios -p Modulos/Firmas/Infraestructura -s Nitido.Api --context FirmasDbContext -o Migraciones`
   y lo mismo con `CrearClientes`, `Modulos/Clientes/Infraestructura` y `ClientesDbContext`.
3. Agrega a mano las dos llaves foráneas de 2.4 en `CrearClientes`.
4. Revisa el SQL de `dotnet ef migrations script --context <M>DbContext` contra 2.2 (nombres, tipos, nulabilidad, periodos, sin cascadas, sin `nvarchar(max)`) y lo adjunta al PR.
5. Aplica las dos migraciones en orden sobre una base vacía del contenedor local y documenta el orden y los comandos en `db/README.md`.

### 3. Endpoints y configuración

No hay endpoints nuevos ni cambios en el documento OpenAPI. `GET /salud` existe desde F0-01 y no aparece en OpenAPI; solo cambia su comportamiento.

#### 3.1 Cadena de conexión

- **Clave única:** `ConnectionStrings:Nitido`. En local, user-secrets (`dotnet user-secrets init -p Nitido.Api` una vez, que agrega `UserSecretsId` al `.csproj`). En CI (F0-06) y en la nube, la variable de entorno `ConnectionStrings__Nitido`. Nunca en `appsettings*.json`, `launchSettings.json` ni otro archivo versionado.
- **Ejemplo para el README** (contenedor de VS Code): `Server=localhost,1433;Database=nitido;User Id=sa;Password=<la tuya>;Encrypt=True;TrustServerCertificate=True`. `TrustServerCertificate=True` solo en local, porque el contenedor usa un certificado autofirmado.
- **Dónde se valida:** en `Nitido.Compartido.Infraestructura` (`AgregarPersistenciaNitido`), porque todos los módulos leen la misma cadena por `OpcionesBaseDeDatos`. La lectura es perezosa (al resolver las opciones), no en `Program.cs` antes de `Build()`; así las pruebas pueden poner su propia cadena con la configuración de `WebApplicationFactory` (salvo la de arranque sin cadena, que invoca el punto de entrada; ver 3.3).
- **Validación:** `Validate(o => !string.IsNullOrWhiteSpace(o.CadenaDeConexion), "Falta la cadena de conexión ConnectionStrings:Nitido. Configúrala con dotnet user-secrets (ver README).")` con `ValidateOnStart()`. Una cadena vacía o con solo espacios cuenta como faltante.
- **Comportamiento:** el host falla al arrancar con `OptionsValidationException`, cuyo `Message` es exactamente el texto del criterio. El `catch` de `Program.cs` (1.3) lo registra como `Fatal` con ese texto y la API termina con código 1. `dotnet ef` también falla con el mismo mensaje si falta el secreto, porque construye los contextos.
- **Lo que no se registra nunca:** la cadena ni partes de ella.

#### 3.2 `GET /salud`

```yaml
/salud:
  get:
    summary: Estado de la API y de su base de datos
    security: []          # anónimo, como hoy
    responses:
      "200": { description: "La API y SQL Server responden", content: { text/plain: { example: "Healthy" } } }
      "503": { description: "SQL Server no responde a tiempo", content: { text/plain: { example: "Unhealthy" } } }
```

- **Comprobación:** `Nitido.Api/Configuracion/ComprobacionSqlServer.cs` (`IHealthCheck`). Abre una `SqlConnection` con `OpcionesBaseDeDatos.CadenaDeConexion` y ejecuta `SELECT 1`, con el token de cancelación de la comprobación (tiempo límite de 3 s en el registro). No usa los `DbContext`, porque su estrategia de reintentos tardaría decenas de segundos en declarar la base caída.
- **Por qué vive en el host y no en `Compartido.Infraestructura`:** `/salud` es del host, que ya trae las comprobaciones de salud en `Microsoft.AspNetCore.App`. En `Compartido.Infraestructura` haría falta un paquete más (`Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`) y ampliar su fila en la tabla de referencias de la arquitectura. `Microsoft.Data.SqlClient` llega al host de forma transitiva por los módulos.
- **Cuerpo:** el texto por defecto (`Healthy`/`Unhealthy`). No se devuelve el detalle de la excepción. El `503` queda en el registro de peticiones como `Error`, por la regla actual de `Program.cs` para códigos 5xx; es lo que se quiere cuando la base se cae.
- **Sin paquete nuevo:** ni `AspNetCore.HealthChecks.SqlServer` ni `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (esta última comprobaría cada contexto por separado y con reintentos).

#### 3.3 Pruebas de la conexión

| Prueba | Cómo, sin volver lentas las pruebas |
| --- | --- |
| `Salud_RespondeOk` (existe; se adapta) | Con la fábrica que apunta al contenedor compartido. Sigue esperando `200`, `Healthy` y `text/plain`. |
| `Salud_Responde503SinBaseDeDatos` | No usa el contenedor. La fábrica pone `ConnectionStrings:Nitido = Server=tcp:127.0.0.1,1;Database=nitido;User Id=sa;Password=x;Encrypt=False;Connect Timeout=2` (puerto cerrado, solo TCP). La conexión se rechaza de inmediato en Linux y en unos 2 s en Windows; el tiempo límite de 3 s acota el peor caso. Espera `503`. |
| `Arranque_SinCadenaDeConexionNoArranca` (nueva) | **No se puede probar con `WebApplicationFactory`** (corrección del 2026-10-18; el diseño original decía que sí porque `Program.cs` no pasaba por el `catch`, y no es cierto): el `catch (OptionsValidationException)` de `Program.cs` captura la excepción dentro del punto de entrada, y `CreateClient()` termina en `ObjectDisposedException`, no en la excepción de validación. La prueba real invoca el punto de entrada de `Program.cs` en el mismo proceso, con el argumento `--ConnectionStrings:Nitido=` vacío o con solo espacios (los argumentos de la línea de comandos ganan a los user-secrets del equipo), y comprueba el código de salida 1 y un único registro `Fatal` con el texto exacto del criterio. Aparte, prueba la validación directamente: resolver `OpcionesBaseDeDatos` con la cadena vacía o con espacios lanza `OptionsValidationException` con `Message` igual al texto del criterio. El registro `Hosting failed to start` (nivel `Error`, con la pila) que escribe el framework no es del código nuestro y la prueba no lo cuenta como segundo `Fatal`. La verificación a mano del paso 6 del plan se conserva. |

### 4. Eventos internos

Ninguno.

### 5. Trabajos en segundo plano

Ninguno.

### 6. Pantallas

Ninguna. Esta historia no toca `nitido-frontend`: no hay pull request del frontend.

### 7. Riesgos

| Riesgo | Mitigación |
| --- | --- |
| **Arranque del contenedor en las pruebas.** SQL Server tarda entre 10 y 30 s en arrancar, y la primera descarga de la imagen (unos 1,5 GB) varios minutos. | Un solo contenedor para toda la corrida: *assembly fixture* de xUnit v3 (`[assembly: AssemblyFixture(typeof(ContenedorSqlServer))]`). Una base `nitido_pruebas` migrada una sola vez y compartida; `Migraciones_SeAplicanSobreBaseLimpia` crea su propia base vacía en el mismo contenedor (`CREATE DATABASE` con nombre único, un segundo). Las pruebas ya corren en serie (`ConfiguracionDePruebas.cs`), así que no hay carreras sobre los datos; cada prueba usa llaves nuevas y no depende de que las tablas estén vacías. |
| **Imagen de SQL Server 2025.** `2025-latest` cambia sin aviso. | Etiqueta fija en el ayudante de pruebas: `mcr.microsoft.com/mssql/server:2025-CU9-GDR1-ubuntu-24.04`, la más reciente de 2025 en MCR el 2026-10-18. Se pasa al constructor de `MsSqlBuilder` (con `TreatWarningsAsErrors`, el constructor sin imagen, marcado obsoleto, rompería la compilación). Si la espera del módulo no detecta que la imagen está lista, se agrega una espera por el mensaje «SQL Server is now ready for client connections». |
| **Las pruebas de integración actuales dejan de arrancar,** porque la API ahora exige la cadena. Peor aún: `WebApplicationFactory` arranca en Development por defecto y carga los user-secrets del equipo, así que pasarían en local y fallarían en CI. | El `tester` crea `FabricaNitido` (deriva de `WebApplicationFactory<Program>`) que siempre pone la cadena del contenedor con `ConfigureAppConfiguration` (fuente de último, gana a los user-secrets), y pasa las 5 clases existentes a usarla. Ninguna prueba vuelve a usar `new WebApplicationFactory<Program>()` directo. |
| **Dos `DbContext` sobre la misma base y su orden.** `CrearClientes` falla si se aplica antes que `CrearFirmasYUsuarios`. | Orden fijo `Firmas` → `Clientes` en una sola lista del ayudante de pruebas, ejecutada por `Migraciones_SeAplicanSobreBaseLimpia` sobre una base vacía en cada corrida; copiado en `db/README.md` para el comando manual. La API no migra al arrancar (ADR 0002). Desde EF Core 9, `Migrate()` falla si el modelo tiene cambios sin migración, así que la misma prueba detecta una migración olvidada. |
| **Tablas temporales y migraciones de EF.** | EF Core soporta tablas temporales desde la versión 6 (`ToTable(..., t => t.IsTemporal(...))`, con `HasPeriodStart`, `HasPeriodEnd` y `UseHistoryTable`), y sigue en EF Core 10; verificado en la documentación de Microsoft. Los periodos son propiedades sombra y por eso quedan ocultos (`HIDDEN`); mapearlos a propiedades visibles de C# llega en EF Core 11 y no hace falta (aceptado por el usuario el 2026-10-18, ver 2.3). En cambios futuros de columnas, EF apaga y prende `SYSTEM_VERSIONING` dentro de la migración: el `dba` revisa ese SQL en cada PR. |
| **Las tablas de historial confunden las pruebas de esquema.** No tienen llave primaria y tienen `UUID_EMPRESAS` sin llave foránea; las de EF (`__EFMigrationsHistory_*`) no empiezan por `T_`. | `Esquema_SigueConvenciones` excluye las tablas de historial (`sys.tables.temporal_type = 1`) y las `__EFMigrationsHistory_%`, y no exige llave foránea a la llave primaria de la propia `T_EMPRESAS`. Las columnas de auditoría sí se revisan también en el historial. |
| **Seguridad por fila y tablas de historial (aviso para F0-02b).** La documentación de SQL Server no dice que los predicados de una política se apliquen solos a la tabla de historial. | F0-02b agrega predicados FILTER también a las tablas `_HISTORIAL` (BLOCK no aplica: el historial no se escribe), o le niega `SELECT` sobre ellas al usuario de la aplicación. Su prueba `SeguridadPorFila_CubreTodaTablaDeEmpresaYFirma` las va a marcar, porque tienen `UUID_FIRMAS` o `UUID_EMPRESAS`. |
| **Regla 3 (multiempresa) llega en F0-02b.** Entre las dos historias no hay filtro global ni seguridad por fila. | No hay endpoints que lean ni escriban datos de clientes, ni despliegue. F0-02b va antes de F0-03a y F0-04 en el orden del índice; no se cambia ese orden. |
| **Contexto de sesión sin implementación en el host.** Con `ValidateOnBuild` (activo en Development), un interceptor que pidiera `IContextoSesion` por constructor rompería el arranque. | El interceptor lo resuelve al guardar (1.2) y falla si no existe: nada se guarda sin usuario. En F0-02a solo guardan las pruebas, que registran su `ContextoSesionDePrueba` desde `tests/`. F0-02b y F0-03a registran la implementación real. |
| **`UserSecretsId` falta en `Nitido.Api.csproj`.** `dotnet user-secrets set` responde un error. | Primer paso del plan: `dotnet user-secrets init -p Nitido.Api`. La propiedad no afecta las pruebas de arquitectura (no es una referencia). El README deja de decir «no lo ejecutes». |
| **Smart App Control de Windows** puede bloquear DLL recién compiladas o descargadas (error «una directiva de Control de aplicaciones bloqueó este archivo»), sobre todo las de paquetes nuevos (Testcontainers, EF). | Si aparece, se reporta al usuario con el archivo bloqueado. Apagar Smart App Control es decisión del usuario y no se puede volver a encender sin reinstalar Windows; ningún agente lo cambia. |
| **Regla 7: paquetes `Azure.*` transitivos.** `Microsoft.EntityFrameworkCore.SqlServer` 10.0.13 depende de `Microsoft.Data.SqlClient` 6.1.7, que trae `Azure.Core` 1.50.0, `Azure.Identity` 1.17.1 y `Microsoft.Identity.Client` (para autenticación con Entra ID). Quedan en la salida del host y de las `Infraestructura`. | **No hay conflicto con la regla ni con la prueba.** La regla 7 prohíbe SDK de nube en el dominio y en los casos de uso: `Dominio` y `Aplicacion` no ven SqlClient (sus reglas lo impiden), y ningún código nuestro usa tipos `Azure.*`. `SdkDeNube_SoloEnProyectosDeProveedor` revisa los `PackageReference` declarados y lo que cada ensamblado `Nitido.*` usa de verdad: los dos quedan limpios. La salida que quitaría los transitivos (forzar `Microsoft.Data.SqlClient` 7.x, que movió Azure a un paquete aparte) no está soportada con EF Core 10, que se compiló contra la 6.x; no se recomienda. Se quitan solos al pasar a EF Core 11 (requiere SqlClient 7). Decidido por el usuario el 2026-10-18: aceptar los transitivos hasta EF Core 11. |
| **Auditoría de vulnerabilidades de NuGet.** El SDK 10 audita también los paquetes transitivos y `TreatWarningsAsErrors` convierte un aviso (`NU1901` a `NU1904`) en error de compilación, por ejemplo si se publica una falla en `Azure.Identity`. | Se corrige subiendo el parche de EF Core o fijando la versión corregida del transitivo en `Directory.Packages.props`. Silenciar el aviso exige decisión del usuario. |
| **GUID v7 y el orden de SQL Server.** SQL Server ordena `UNIQUEIDENTIFIER` empezando por los últimos 6 bytes, que en un GUID v7 son aleatorios. Una llave primaria agrupada con GUID v7 se inserta en desorden y se fragmenta. | Sin efecto en la fase 0 (tablas de pocas filas). Antes de las tablas grandes de la fase 1 (`T_ASIENTOS`, `T_LINEAS_ASIENTO`, documentos) se decide en un ADR, por ejemplo llave primaria no agrupada con índice agrupado por `UUID_EMPRESAS` y fecha. No cambia el tipo ni la convención de nombres. |
| **El historial no dice quién borró.** Un `DELETE` mueve la fila al historial con el último usuario que la modificó. | En la fase 0 nada borra: usuarios y clientes se desactivan con `ACTIVO`. Quitar accesos (`T_USUARIOS_EMPRESAS`) llega en una historia posterior, que debe resolverlo (por ejemplo, actualizar el usuario de modificación antes de borrar). |
| **El historial de `T_USUARIOS` crece con cada intento de ingreso (F0-03a).** Identity actualiza contadores de intentos y bloqueo en la misma fila. | Es útil para auditoría de seguridad y el limitador (10 por minuto por IP) acota el volumen. Se revisa con la política de conservación (fase 5). |
| **Seguridad de la cadena.** | Solo user-secrets o variables de entorno; `sa` solo en local (F0-02b ya pide un usuario de aplicación para la nube). El `revisor` comprueba que ningún archivo versionado tenga `ConnectionStrings`. |
| **Migración de datos existentes.** | No hay datos: es la primera migración de cada módulo. |

### 7b. Desviaciones de las pruebas respecto del diseño (reportadas por el tester, 2026-10-18)

- **Contenedor perezoso con dos bases compartidas.** El diseño del riesgo «Arranque del contenedor» hablaba de una sola base `nitido_pruebas`. En la práctica el contenedor arranca perezosamente (solo cuando una prueba lo pide) y hay dos bases compartidas en él: `nitido_sin_tablas`, sin migrar, para las pruebas antiguas (las 5 clases existentes de F0-01, que no necesitan tablas), y `nitido_pruebas`, migrada una sola vez, para las pruebas de esquema, auditoría e identificadores. `Migraciones_SeAplicanSobreBaseLimpia` sigue creando su propia base vacía.
- **`MarcadorDeEnsamblado` en `Aplicacion` y en `Api` de cada módulo.** El texto de 1.1 decía «solo el `.csproj`», pero los proyectos `Aplicacion` y `Api` llevan una clase `MarcadorDeEnsamblado`. Existe para esquivar el bloqueo de Smart App Control de Windows sobre DLL vacías (ver 7, «Smart App Control») y para que otros proyectos localicen el ensamblado. Se puede borrar cuando el módulo tenga contenido real en esa capa.
- **`AccesoCliente.Crear` es `internal`.** La fábrica de `AccesoCliente` no es pública, así que no se prueba directamente: queda cubierta a través de `Cliente.Crear`, que crea el cliente y su primer acceso.

### 8. Plan de implementación

Paquetes nuevos para `Directory.Packages.props` (verificados en nuget.org el 2026-10-18; última versión estable de cada uno):

```xml
<!-- Base de datos -->
<PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.13" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.13" />
<!-- Pruebas -->
<PackageVersion Include="Testcontainers.MsSql" Version="4.16.0" />
```

- Herramienta local `dotnet-ef` 10.0.13 en `.config/dotnet-tools.json` (`dotnet new tool-manifest` y `dotnet tool install dotnet-ef --version 10.0.13`), para que todos usen la misma versión.
- No se agregan: `Microsoft.EntityFrameworkCore.Tools` (es para la consola de Visual Studio), paquetes de comprobaciones de salud (3.2), `Microsoft.Data.SqlClient` explícito (llega con EF), `Microsoft.Extensions.TimeProvider.Testing` (el reloj de prueba es una clase de cinco líneas en `tests/`) ni Respawn.
- Transitivos de nube: ver 7, «Regla 7».

| Paso | Agente | Qué | Depende de | En paralelo con |
| --- | --- | --- | --- | --- |
| 0 | usuario | Hecho. Decidido el 2026-10-18: sí a las cuatro. `T_FIRMAS` y `T_EMPRESAS` temporales (2.3), `Azure.*` transitivos aceptados (7), ADR 0002 aceptado y ajustes a `convenciones-bd` aprobados y aplicados (abajo). | — | — |
| 1 | `dev-backend` | Andamiaje: los 9 proyectos con la plantilla 4.3, `Nitido.slnx`, referencias del host, paquetes en `Directory.Packages.props`, `dotnet user-secrets init -p Nitido.Api`, manifiesto de `dotnet-ef`, `IContextoSesion`, `Agregar<M>()` y `Mapear<M>()` vacíos, entidades de 1.1 y `DbContext` sin configuraciones. Se borra `Modulos/.gitkeep`. Salida: `dotnet build` sin advertencias y las pruebas de arquitectura en verde. Medio día. | 0 | 2 |
| 2 | `tester` | Pruebas primero, con los nombres de este diseño: `ContenedorSqlServer` (*assembly fixture*, imagen fija), `FabricaNitido`, ayudante de migraciones con la lista `Firmas` → `Clientes`, `ContextoSesionDePrueba` y `RelojDePrueba` en `tests/`; pasar las 5 clases existentes a `FabricaNitido`; escribir `Salud_Responde503SinBaseDeDatos`, `Arranque_SinCadenaDeConexionNoArranca`, `Migraciones_SeAplicanSobreBaseLimpia` (también revisa que existan `__EFMigrationsHistory_Firmas` y `__EFMigrationsHistory_Clientes`), `Esquema_SigueConvenciones` (con las exclusiones de 7), `Esquema_NoUsaFloatRealNiMoney`, `Esquema_TablasTemporalesTienenHistorial`, `Auditoria_LlenaFechasYUsuario` (reloj con milisegundos exactos, para que el redondeo de `DATETIME2(3)` no dé falsos fallos; revisa también `DATETIME_PRECISION = 3`), `Identificadores_SonGuidVersion7` (por fábrica de dominio y por el generador, con `Usuario`) y `Tablas_PertenecenAUnSoloDbContext`. Deben fallar por la razón correcta. | 1 (nombres de tipos) | 3, 4a |
| 3 | `dev-backend` | `Nitido.Compartido.Infraestructura` completo (1.2), `Program.cs` (1.3), `ComprobacionSqlServer` (3.2) y README (base local, user-secrets, ejemplo de cadena, orden de migraciones, qué hacer con el error de cadena faltante). | 1 | 2, 4a |
| 4a | `dba` | Las cuatro configuraciones (2.5, punto 1). | 1 | 2, 3 |
| 4b | `dba` | Migraciones `CrearFirmasYUsuarios` y `CrearClientes`, llaves foráneas entre módulos a mano, revisión del SQL contra 2.2, script para el PR y `db/README.md` (2.5, puntos 2 a 5). | 3 (convención de nombres), 4a | — |
| 5 | `tester` | Verificación: `dotnet build` sin advertencias, `dotnet format Nitido.slnx --verify-no-changes` y `dotnet test` completo (unidad, integración con Testcontainers, contables). Anota el tiempo total de la corrida para F0-06. | 2, 3, 4b | — |
| 6 | `dev-backend` | Verificación manual en local: sin el secreto, `dotnet run --project Nitido.Api` registra el texto exacto y termina; con el contenedor encendido, `/salud` da `200`; apagado, `503`; `dotnet ef database update` en orden sobre una base vacía. | 5 | — |
| 7 | `revisor` | Revisión: capas y referencias, ningún `ConnectionStrings` en archivos versionados, SQL adjunto contra 2.2, temporales e historial, que las pruebas de esquema no pasen en vacío, lista de proveedores de nube vacía. | 6 | — |

`devops` no participa (la integración continua es F0-06, que sigue después de esta historia). El `contador` no participa: no hay reglas contables.

### Ajustes aplicados a la skill `convenciones-bd` (2026-10-18)

El usuario aprobó estos tres ajustes el 2026-10-18 y ya están en la skill. Se conserva el texto aplicado como registro:

1. En «Columnas obligatorias», reemplazar la tercera viñeta por:
   «- Tablas contables (asientos, líneas, documentos, terceros, plan de cuentas, parámetros), de seguridad (usuarios y accesos a clientes) y maestras de la firma (firmas, empresas): tabla temporal de SQL Server (`SYSTEM_VERSIONING = ON`) con historial en `T_<NOMBRE>_HISTORIAL`, en el mismo esquema. Periodos `PERIODO_INICIO` y `PERIODO_FIN`, `DATETIME2(3)` en UTC, no ocultos. En EF se configuran con `MapearATablaTemporal` de `Nitido.Compartido.Infraestructura`.»
   (Corregido el mismo día por decisión del usuario: los periodos quedan ocultos. El texto vigente en la skill dice: «... `DATETIME2(3)` en UTC, ocultos (`HIDDEN`, como los crea EF Core 10: `SELECT *` no los devuelve, se piden por nombre; cuando EF 11 permita mapearlos pasarán a visibles). En EF se configuran con `MapearATablaTemporal` ...». El texto de arriba se conserva solo como registro de lo propuesto.)
2. En «Nombres», agregar:
   «- Restricciones: llave primaria `PK_<TABLA>`; llave foránea `FK_<TABLA>_<TABLA_PRINCIPAL>` (o `FK_<TABLA>_<COLUMNA>` si la tabla referencia dos veces la misma); `CHECK` `CK_<TABLA>_<COLUMNA>`.»
3. En «Migraciones», agregar:
   «- Las tablas de historial, las de historial de migraciones (`__EFMigrationsHistory_<Modulo>`) y sus índices automáticos no siguen las reglas de llave primaria ni de `UUID_EMPRESAS`; las pruebas de esquema las excluyen.
   - Ninguna llave foránea borra en cascada (`ON DELETE NO ACTION`).»

## Pendientes (hallazgos aplazados de la revisión del 2026-10-18)

El `revisor` los encontró y se aplazan a propósito; ninguno bloquea F0-02a.

- **`/salud` anónimo abre una conexión por petición.** Cualquiera puede forzar conexiones a SQL Server. Se resuelve con un limitador de peticiones o dejando `/salud` en red interna al definir el despliegue (F0-06 o la historia de despliegue).
- **Fechas leídas con `Kind` sin marcar.** Las fechas que vienen de la base llegan como `DateTimeKind.Unspecified` (UTC por convención). Falta un convertidor de valores que les ponga `Kind = Utc` al leer.
- **`ConvencionDeNombres` y tipos propios (owned).** Dos tipos propios del mismo tipo dentro de una misma tabla chocarían en el nombre de columna. Hoy no hay tipos propios; se resuelve cuando aparezca el primero.

## Fuera de alcance

- Filtro de multiempresa y seguridad por fila (F0-02b).
- Columnas de contraseña y doble factor (F0-03a y F0-03b).
- Endpoints.
- Datos fiscales del cliente (régimen, responsabilidades del RUT, CIIU).
- Roles distintos de Administrador. Los de titular, auxiliar y solo lectura del plan («Entornos y seguridad») quedan para una historia posterior.
