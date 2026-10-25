# Base de datos

Aquí van los scripts y los datos de apoyo de la base de datos SQL Server. Las tablas y los cambios de estructura (migraciones) no viven aquí: los maneja EF Core desde `Modulos/` (cada módulo, en su capa de Infraestructura). Antes de tocar la base de datos, lee la skill `convenciones-bd`.

## Migraciones: dónde viven y en qué orden se aplican

Cada módulo con tablas tiene su propio `DbContext`, sus migraciones en `Modulos/<Modulo>/Infraestructura/Migraciones/` y su propia tabla de historial (`__EFMigrationsHistory_<Modulo>`), todo en la misma base y el esquema `dbo` (ADR 0002). Las migraciones generadas no se editan a mano, con tres excepciones: las llaves foráneas hacia tablas de otro módulo se escriben a mano en la migración del módulo que referencia; el SQL de seguridad por fila (`migrationBuilder.Sql` con el script incrustado) y el relleno de datos de `AgregarSeguridadPorFilaDeClientes` también son escritos a mano. Ojo: `.editorconfig` marca `[**/Migraciones/*.cs] generated_code = true`, y eso también excluye de los analizadores el código escrito a mano de esas migraciones; por eso esa parte se revisa a mano en el pull request.

Orden vigente (el módulo referenciado va antes que el que lo referencia):

| Orden | Módulo | `DbContext` | Migración | Tablas |
| --- | --- | --- | --- | --- |
| 1 | Firmas | `FirmasDbContext` | `CrearFirmasYUsuarios` | `T_FIRMAS`, `T_USUARIOS` |
| 2 | Clientes | `ClientesDbContext` | `CrearClientes` | `T_EMPRESAS`, `T_USUARIOS_EMPRESAS` y las llaves foráneas `FK_T_EMPRESAS_T_FIRMAS` y `FK_T_USUARIOS_EMPRESAS_T_USUARIOS` (hacia Firmas, escritas a mano) |
| 3 | Firmas | `FirmasDbContext` | `AgregarSeguridadPorFilaDeFirmas` | `CK_T_FIRMAS_UUID_FIRMAS`; esquema `seguridad`, función `FN_FIRMAS_ES_DE_LA_FIRMA` y política `PS_FIRMAS` (`T_FIRMAS`, `T_USUARIOS` y sus historiales) |
| 4 | Clientes | `ClientesDbContext` | `AgregarSeguridadPorFilaDeClientes` | `T_USUARIOS_EMPRESAS.UUID_FIRMAS` (con relleno), llave alterna `UX_T_EMPRESAS_UUID_EMPRESAS_UUID_FIRMAS`, llave foránea compuesta `FK_T_USUARIOS_EMPRESAS_T_EMPRESAS`, tres funciones `FN_CLIENTES_*` y política `PS_CLIENTES` (`T_EMPRESAS`, `T_USUARIOS_EMPRESAS` y sus historiales) |
| 5 | Firmas | `FirmasDbContext` | `AgregarCredencialesAUsuarios` | Columnas de clave, sello de seguridad, intentos, bloqueo y segundo factor en `T_USUARIOS` (F0-03a y F0-03b). El `Down` quita los predicados de `T_USUARIOS` antes de quitar las columnas (la política está enlazada al esquema) y los vuelve a poner |
| 6 | Firmas | `FirmasDbContext` | `AgregarIngresoPorCorreoEnSeguridadPorFila` | Función `FN_USUARIOS_ES_VISIBLE` y el cambio del FILTER de `T_USUARIOS` para el ingreso por correo ([ADR 0004](../docs/adr/0004-autenticacion-cookies-y-doble-factor.md)) |

Cada módulo aplica sus migraciones en su propio orden, y Firmas va antes que Clientes: `database update` de Firmas (migraciones 1, 3, 5 y 6) y después el de Clientes (2 y 4). `AgregarSeguridadPorFilaDeClientes` necesita el esquema `seguridad` que crea la de Firmas.

Comandos, desde la raíz del repositorio y con `ConnectionStrings:Nitido` configurada (ver `README.md`):

```bash
dotnet tool restore
dotnet ef database update --context FirmasDbContext -p Modulos/Firmas/Infraestructura -s Nitido.Api
dotnet ef database update --context ClientesDbContext -p Modulos/Clientes/Infraestructura -s Nitido.Api
```

Para crear una migración nueva (el nombre va en español y describe el cambio):

```bash
dotnet ef migrations add <Nombre> --context <Modulo>DbContext -p Modulos/<Modulo>/Infraestructura -s Nitido.Api -o Migraciones
```

Para deshacer, en orden inverso: primero `database update 0` de Clientes y después el de Firmas (la migración de Clientes quita sus llaves foráneas hacia Firmas en el `Down`). El `Down` de `AgregarSeguridadPorFilaDeFirmas` falla a propósito si Clientes sigue aplicado: `DROP SCHEMA seguridad` encuentra las funciones de Clientes y EF deshace la migración entera.

Cada módulo nuevo agrega su fila a la tabla de arriba y a la lista del ayudante de migraciones de las pruebas de integración.

### Tablas temporales

Las cuatro tablas son temporales de SQL Server (`SYSTEM_VERSIONING = ON`) con historial en `T_<NOMBRE>_HISTORIAL`. Los periodos `PERIODO_INICIO` y `PERIODO_FIN` son `DATETIME2(3)` en UTC y quedan ocultos (`HIDDEN`), como los crea EF Core 10 cuando son propiedades sombra (decisión aceptada el 2026-10-18): un `SELECT *` no los devuelve, pero se piden por nombre y `INFORMATION_SCHEMA.COLUMNS` los lista. Cuando EF 11 permita mapearlos a propiedades visibles de C#, pasarán a visibles. Si una migración futura cambia columnas de una tabla temporal, EF apaga y vuelve a prender `SYSTEM_VERSIONING` dentro de la migración; revisa ese SQL en el pull request.

## `scripts/`

SQL generado con `dotnet ef migrations script` para adjuntar a los pull requests y revisar. No se ejecutan a mano como camino normal: la fuente de verdad son las migraciones. Cada archivo crea el historial del módulo y aplica todas sus migraciones desde una base vacía, y hay que aplicarlos en el orden de la tabla de arriba.

- `scripts/firmas-crear-firmas-y-usuarios.sql`: `FirmasDbContext`, migraciones `CrearFirmasYUsuarios` y `AgregarSeguridadPorFilaDeFirmas`.
- `scripts/clientes-crear-clientes.sql`: `ClientesDbContext`, migraciones `CrearClientes` y `AgregarSeguridadPorFilaDeClientes`.

Limitación: en estos archivos EF no escribe `GO` entre los lotes de un `migrationBuilder.Sql`, así que sirven para revisar el SQL, no para ejecutarlos con `sqlcmd` (un `CREATE FUNCTION` tiene que ir solo en su lote). Al aplicar la migración con EF, cada lote sí se ejecuta aparte: el generador de SQL Server parte el texto del script por las líneas `GO`.

Se regeneran con:

```bash
dotnet ef migrations script --context FirmasDbContext -p Modulos/Firmas/Infraestructura -s Nitido.Api -o db/scripts/firmas-crear-firmas-y-usuarios.sql
dotnet ef migrations script --context ClientesDbContext -p Modulos/Clientes/Infraestructura -s Nitido.Api -o db/scripts/clientes-crear-clientes.sql
```

## `semillas/`

Datos iniciales que la base necesita para funcionar, como el plan único de cuentas (PUC) base y los catálogos fijos. Todavía no existe: la carpeta se crea cuando una historia traiga datos semilla (F0-02a no trae ninguno).

## `seguridad-por-fila/`

Scripts de SQL Server que impiden que los datos de una firma o de un cliente se vean desde otra firma o desde un cliente sin acceso (segunda barrera; la primera es el filtro global de EF). Nacen con la historia F0-02b (ADR 0003). Cada script lo incrusta como recurso la `Infraestructura` de su módulo (`EmbeddedResource` con `LogicalName="SeguridadPorFila.<archivo>"` en el `.csproj`) y lo aplica su migración con `ScriptsSql.Leer`.

| Script | Lo aplica | Crea |
| --- | --- | --- |
| `0001-firmas-politica.sql` | `AgregarSeguridadPorFilaDeFirmas` | esquema `seguridad`, `FN_FIRMAS_ES_DE_LA_FIRMA`, política `PS_FIRMAS` sobre `T_FIRMAS`, `T_USUARIOS` y sus historiales |
| `0003-firmas-ingreso-por-correo.sql` | `AgregarIngresoPorCorreoEnSeguridadPorFila` | `FN_USUARIOS_ES_VISIBLE` y `ALTER SECURITY POLICY` del FILTER de `T_USUARIOS`: con la llave `CORREO_INGRESO` el ingreso ve solo la fila de su correo |
| `0002-clientes-politica.sql` | `AgregarSeguridadPorFilaDeClientes` | `FN_CLIENTES_ES_DE_LA_FIRMA`, `FN_CLIENTES_ES_ACCESO_DEL_USUARIO`, `FN_CLIENTES_ES_CLIENTE_PERMITIDO`, política `PS_CLIENTES` sobre `T_EMPRESAS`, `T_USUARIOS_EMPRESAS` y sus historiales |

Reglas:

- Objetos en el esquema `seguridad`: funciones `seguridad.FN_<MODULO>_<QUE_VALIDA>` con `SCHEMABINDING` y una política por módulo `seguridad.PS_<MODULO>` con `STATE = ON, SCHEMABINDING = ON`. Las funciones no se comparten entre módulos.
- Toda tabla con `UUID_FIRMAS` o `UUID_EMPRESAS` lleva FILTER y los cuatro BLOCK (`AFTER INSERT`, `AFTER UPDATE`, `BEFORE UPDATE`, `BEFORE DELETE`); su `_HISTORIAL`, solo FILTER (el versionado del sistema escribe en el historial y no se le pone nada que lo pueda frenar).
- Ningún predicado tiene excepciones por rol ni por usuario de la base: se aplica también a `sa` y a `dbo`. Sin sesión (llaves de `SESSION_CONTEXT` en `Guid.Empty`) no se ve ninguna fila.
- Un script aplicado no se edita. Un cambio de política es un script nuevo (`0003-...`) y una migración nueva que quita la política y la vuelve a crear, o que usa `ALTER SECURITY POLICY`.
- Migraciones de datos sobre tablas protegidas: la migración corre sin sesión y no ve filas. O el cambio de datos va antes de crear la política (como el relleno de `UUID_FIRMAS`), o la migración apaga la política de su módulo (`ALTER SECURITY POLICY seguridad.PS_<MODULO> WITH (STATE = OFF)`) y la enciende antes de terminar, en la misma migración (EF la corre en una transacción). La prueba de cobertura exige `is_enabled = 1` al final.
- Cambios de estructura en tablas protegidas: `SCHEMABINDING` impide cambiar una columna que usa un predicado o reconstruir la tabla. Esa migración quita los predicados de la tabla antes y los vuelve a poner después, con un script nuevo. Las columnas que ningún predicado usa se cambian sin tocar la política.
- El `dba` revisa en cada pull request el SQL de las tablas temporales protegidas.

### Relleno de `T_USUARIOS_EMPRESAS.UUID_FIRMAS` (`AgregarSeguridadPorFilaDeClientes`)

La columna nueva se agrega como nula en la tabla y en su historial, con `SYSTEM_VERSIONING = OFF`; se rellena desde la firma del cliente (`T_EMPRESAS`, por `UUID_EMPRESAS`; la firma de un cliente no cambia nunca) y, para accesos del historial cuyo cliente ya no está en `T_EMPRESAS`, desde `T_EMPRESAS_HISTORIAL`; pasa a `NOT NULL` en las dos; se vuelve a encender el versionado y recién entonces se crean la llave alterna, la llave foránea compuesta y la política. Si alguna fila quedara sin firma, el paso a `NOT NULL` falla y la transacción deshace todo (nunca se adivina una firma).

Cómo revertirlo: el `Down` quita la política y las funciones, la llave foránea compuesta, el índice, la llave alterna y la columna (SQL Server la quita también del historial), y recrea la llave foránea de F0-02a sobre `UUID_EMPRESAS`. Los accesos no se pierden; solo desaparece la columna repetida.
