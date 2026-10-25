# 0003. Multiempresa en dos barreras: filtro global de EF y seguridad por fila de SQL Server

| | |
| --- | --- |
| Estado | Aceptado (2026-10-24, por el usuario con sus nueve decisiones) |
| Fecha | 2026-10-24 |

## Contexto

La regla 3 de `CLAUDE.md` dice que ningún dato de un cliente se ve desde otro. nítido guarda en una sola base (ADR 0002) los datos de muchas firmas, y cada firma lleva muchos clientes. Una sola consulta mal escrita (un `WHERE` olvidado, un SQL a mano, un `IgnoreQueryFilters()`) basta para mostrarle a una firma los clientes de otra.

Lo que ya está decidido y pesa sobre esta decisión:

- **F0-02b (especificación aprobada):** el cliente activo viaja en la ruta (`/api/clientes/{uuidCliente}/...`); un `DbConnectionInterceptor` fija `SESSION_CONTEXT` con `@read_only = 1`; el predicado valida la firma de la sesión más un `EXISTS` sobre `T_USUARIOS_EMPRESAS`; en local se acepta `sa`.
- **ADR 0001:** el filtro y el interceptor viven en `Nitido.Compartido.Infraestructura`; el filtro del cliente activo, en `Nitido.Compartido.Api`; `IContextoSesion` es la única interfaz del contexto de la petición.
- **ADR 0002:** un `DbContext` y un historial de migraciones por módulo; cada tabla tiene un solo dueño; las llaves foráneas entre módulos se escriben por nombre de tabla.
- **F0-02a:** las cuatro tablas son temporales (`T_<NOMBRE>_HISTORIAL`), y la documentación de SQL Server dice que los predicados de la tabla actual **no** se copian solos a la de historial.
- **Pool de conexiones:** `@read_only = 1` impide cambiar la llave en la misma conexión lógica; el pool reutiliza conexiones físicas entre peticiones de firmas distintas.
- Un desarrollador con agentes: las barreras tienen que comprobarse solas, con pruebas.

## Opciones

1. **Solo el filtro global de EF.** Simple y sin SQL a mano. Pero cualquier SQL escrito a mano, `IgnoreQueryFilters()` o una herramienta que se conecte directo a la base lo salta. Una sola barrera, en el mismo código que puede fallar.
2. **Solo la seguridad por fila de SQL Server.** Protege toda conexión, incluso el SQL a mano. Pero los errores aparecen tarde (el código «funciona» y devuelve vacío), no ayuda a escribir consultas correctas en EF y depende de que cada conexión lleve su contexto.
3. **Las dos barreras, independientes:** el filtro global de EF por convención (primera barrera, la que usa el código) y la seguridad por fila con `SESSION_CONTEXT` (segunda barrera, la que protege aunque la primera falle). Cada una se prueba por separado.
4. **Una base o un esquema por firma.** Aislamiento físico. Pero multiplica migraciones, respaldos y cadenas de conexión, y contradice el ADR 0002. Desproporcionado para la fase 0.

## Decisión

Se adopta la **opción 3**. Los detalles que siguen son parte de la decisión.

### Contexto de la petición

- `IContextoSesion` (en `Nitido.Compartido/Multiempresa/`) entrega `UuidUsuarios`, `UuidFirmas` y `UuidEmpresas?`. Sin sesión, `UuidUsuarios` y `UuidFirmas` valen `Guid.Empty`. `UuidEmpresas` es el cliente de la ruta **solo después** de que el filtro de endpoint lo valida; antes, y en rutas sin cliente, es `null`.
- La implementación del host vive en `Nitido.Compartido.Api` y lee la sesión de `HttpContext`. La implementación para pruebas vive en `tests/`.

### Primera barrera: filtro global de EF

- `DbContextNitido` aplica, por convención, un filtro con nombre `Multiempresa` (filtros con nombre de EF Core 10) a toda entidad que implemente `IDeFirma` (`UUID_FIRMAS` = firma de la sesión) o `IDeEmpresa` (`UUID_EMPRESAS` = cliente activo). Una entidad con las dos interfaces recibe las dos condiciones.
- El filtro lee la sesión a través de propiedades de la **instancia** del `DbContext`, nunca de una variable capturada, porque EF guarda el modelo en caché por tipo y solo reemplaza por la instancia actual lo que cuelga del propio contexto.
- Una entidad cuya llave primaria es la firma (`Firma`, en `T_FIRMAS`) recibe el mismo filtro declarado a mano en su `DbContext`.
- Una prueba recorre el modelo de cada `DbContext` y falla si una entidad con interfaz, o con una columna `UUID_FIRMAS` o `UUID_EMPRESAS`, queda sin el filtro `Multiempresa`.
- Se prohíbe `AddDbContextPool`: un contexto reutilizado conservaría la sesión de otra petición.

### Segunda barrera: seguridad por fila

- **Contexto en SQL:** un `DbConnectionInterceptor` (en `ConnectionOpened` y `ConnectionOpenedAsync`) ejecuta, en un solo viaje a la base, `sp_set_session_context` con `@read_only = 1` para `UUID_USUARIOS`, `UUID_FIRMAS` y `UUID_EMPRESAS`. Las tres llaves se fijan siempre. Si falta un valor, se fija `Guid.Empty`: así la conexión queda bloqueada y nadie puede ponerle después, en esa misma conexión, la firma de otro (por ejemplo con una inyección de SQL). Ninguna firma puede tener llave vacía (`CK_T_FIRMAS_UUID_FIRMAS`), así que `Guid.Empty` no coincide con ninguna fila.
- **Pool:** SQL Server limpia el `SESSION_CONTEXT`, y con él las llaves de solo lectura, al restablecer una conexión del pool (`sp_reset_connection`, que viaja con la primera orden de la conexión reutilizada, que es justo la del interceptor). La prueba `Pool_ConexionReutilizadaNoConservaContexto` lo confirma con un pool de una sola conexión.
- **Esquema y nombres:** los objetos van en el esquema `seguridad`. Funciones `seguridad.FN_<MODULO>_<QUE_VALIDA>`, con `SCHEMABINDING`; políticas `seguridad.PS_<MODULO>`, con `SCHEMABINDING = ON` y `STATE = ON`.
- **Una política y sus funciones por módulo,** creadas y quitadas por la migración del módulo dueño de las tablas (ADR 0002). Las funciones no se comparten entre módulos: así cambiar una no obliga a tocar la política de otro. El esquema `seguridad` lo crea y lo quita Firmas, que es el primero y el último en el orden.
- **Predicados:**
  - Tablas de la firma (`T_FIRMAS`, `T_USUARIOS`): la fila es de la firma de la sesión.
  - `T_EMPRESAS`: para leer, actualizar (antes) y borrar, la fila es de la firma de la sesión **y** el usuario de la sesión tiene acceso en `T_USUARIOS_EMPRESAS`. Para insertar y actualizar (después), solo la firma: crear un cliente no puede exigir un acceso que todavía no existe.
  - `T_USUARIOS_EMPRESAS`: la fila es de la firma **y** del usuario de la sesión. Lleva desde esta historia la columna `UUID_FIRMAS`, con una llave foránea compuesta hacia `T_EMPRESAS (UUID_EMPRESAS, UUID_FIRMAS)` que impide un acceso a un cliente de otra firma, y que evita que su predicado consulte `T_EMPRESAS` (eso haría un ciclo entre los dos predicados).
  - Tablas de datos de un cliente (desde la fase 1): la fila es del cliente activo de la sesión **y** el usuario tiene acceso a ese cliente. La función nace con la primera de esas tablas.
- **Historial:** cada tabla `_HISTORIAL` lleva el mismo predicado FILTER que su tabla actual, y ninguno BLOCK. El versionado del sistema escribe en el historial, y un predicado FILTER no afecta escrituras; sin predicados BLOCK no hay nada que pueda frenar el versionado. Los usuarios no pueden escribir en el historial mientras `SYSTEM_VERSIONING` esté encendido. **Nota (2026-10-25):** `T_USUARIOS_HISTORIAL` quedó con `FN_FIRMAS_ES_DE_LA_FIRMA`, no con la función de ingreso por correo de `T_USUARIOS` (ADR 0004): el historial nunca se ve por correo, y así es más estricto.
- **Sin excepciones por rol ni por usuario de la base en el predicado.** Con `sa` en local, una excepción para `db_owner` dejaría la barrera apagada en desarrollo y en las pruebas, que pasarían en vacío. La seguridad por fila de SQL Server se aplica también a `dbo` y a `sa`.
- **Migraciones:** corren sin sesión (las tres llaves en `Guid.Empty`), así que las órdenes de estructura funcionan y las de datos no ven nada. Una migración que mueve datos en tablas protegidas apaga la política de su módulo (`ALTER SECURITY POLICY seguridad.PS_<MODULO> WITH (STATE = OFF)`) y la vuelve a encender en la misma migración, que corre en una sola transacción. La prueba de cobertura exige `is_enabled = 1` al final.
- **Scripts:** el SQL de cada política vive en `db/seguridad-por-fila/NNNN-<modulo>-<tema>.sql`. Cada `Infraestructura` lo incluye como recurso incrustado y su migración lo aplica con `migrationBuilder.Sql`. Un script aplicado no se edita; un cambio es un script y una migración nuevos.

### Cliente activo en la ruta

- `Nitido.Compartido.Api` expone `MapearRutasDelCliente(ruta)`, que crea el grupo `/api/clientes/{uuidCliente:guid}{ruta}` con el filtro de endpoint ya puesto. Ningún módulo arma esas rutas a mano; una prueba recorre los endpoints y falla si una ruta con `{uuidCliente}` no tiene el filtro.
- El filtro pregunta por el puerto `IAccesoAClientes` (en `Nitido.Compartido/Multiempresa/`, implementado por Clientes) si el usuario de la sesión puede entrar al cliente. Si no puede, si no hay sesión o si el cliente no existe, responde `404` (nunca `403`, para no revelar que existe).
- `IAccesoAClientes` entra a `Nitido.Compartido` aunque hoy lo implemente un solo módulo, porque es parte del contrato de multiempresa, igual que `IContextoSesion`: `Nitido.Compartido.Api` no puede ver los `Contratos` de Clientes.

### Escrituras sin sesión de usuario: contexto explícito

Registrar una firma (F0-04), la semilla de Development (F0-03a) y, desde la fase 2, los trabajos de Hangfire escriben sin una petición con sesión. No se abre ninguna puerta en el predicado. En cambio:

- **Alcance con contexto explícito** (se construye en F0-04, su primer uso): `Nitido.Compartido.Infraestructura` ofrece crear un alcance de inyección de dependencias cuyo `IContextoSesion` es uno dado. Las mismas barreras aplican: el alcance no apaga nada, solo declara quién actúa.
- **Registro de firma:** el caso de uso crea la firma con su fábrica (llave nueva) y abre un alcance con `UuidFirmas` = esa firma nueva y `UuidUsuarios` = el usuario «sistema» (`00000000-0000-0000-0000-000000000001`, F0-02a). Los predicados AFTER INSERT de `T_FIRMAS` y `T_USUARIOS` pasan porque las filas son de la firma de la sesión, y las columnas de auditoría registran que las creó el sistema. Una firma recién creada no tiene datos, así que ese contexto no ve nada ajeno.
- **Usos permitidos:** solo el registro de firma, la semilla de Development y los trabajos en segundo plano (con el contexto del usuario que los encoló). El `revisor` rechaza cualquier otro.

### Ingreso (decisión propuesta para F0-03a; no se construye aquí)

El ingreso busca el usuario por correo antes de saber su firma, y el predicado de `T_USUARIOS` lo impide. Propuesta: una cuarta llave de solo lectura, `CORREO_INGRESO`, que solo fija el caso de uso de ingreso, y un predicado FILTER en `T_USUARIOS` que, cuando la firma de la sesión es `Guid.Empty`, deja ver **solo** la fila cuyo `CORREO_NORMALIZADO` es ese correo. Después de encontrarlo, la verificación de la clave y los contadores de intentos corren en un alcance explícito con la firma y el usuario encontrados. F0-03a lo confirma o lo cambia en su propio diseño.

## Consecuencias

**Lo bueno:**

- Un error en una consulta de EF lo para SQL Server, y un SQL a mano sin filtro tampoco ve filas ajenas. Las dos barreras se prueban por separado.
- Una conexión sin contexto, o abierta por fuera de EF, no ve nada: el sistema falla cerrado.
- El cliente activo en la ruta aguanta varias pestañas con clientes distintos.
- Cada módulo sigue siendo dueño de sus tablas, de su política y de sus funciones.

**Lo que cuesta:**

- Un viaje más a la base cada vez que se abre una conexión (tres llamadas en un solo comando), y una consulta indexada por cada petición a una ruta de cliente.
- Las migraciones que mueven datos en tablas protegidas tienen que apagar y encender la política. Las que cambian una columna usada por un predicado, o que reconstruyen una tabla protegida, tienen que quitar el predicado antes y volver a ponerlo después (`SCHEMABINDING` lo exige).
- Una función de otro módulo que consulte `T_USUARIOS_EMPRESAS` (las tablas de datos de cliente, desde la fase 1) ata esas columnas: Clientes no podrá cambiarlas sin coordinar.
- `T_USUARIOS_EMPRESAS.UUID_FIRMAS` repite un dato que ya está en `T_EMPRESAS`. La llave foránea compuesta impide que se desalinee.
- Las vistas de toda la firma con datos de varios clientes (por ejemplo, un tablero con pendientes por cliente) no caben en el predicado de datos de cliente, que exige un cliente activo. Se resuelven en su historia (consulta por cliente o resumen propio), no aflojando el predicado.
- El índice único global de correo (`UX_T_USUARIOS_CORREO_NORMALIZADO`) revela, al registrar, que un correo ya existe en otra firma. Se acepta: es el `409` que pide F0-04.

**Pendiente:**

- Un usuario de base de datos para la aplicación sin `ALTER ANY SECURITY POLICY`, sin `ALTER` sobre el esquema `seguridad`, sin `IMPERSONATE` y sin `db_owner`, distinto del de migraciones. Se decide con el ADR de despliegue. En local sigue `sa`.
- El ingreso de F0-03a (propuesta arriba) y el alcance con contexto explícito de F0-04.
- Los roles por cliente (titular, auxiliar, solo lectura) cambiarán los predicados de `T_USUARIOS_EMPRESAS`, por ejemplo para que un administrador vea los accesos de toda su firma.
