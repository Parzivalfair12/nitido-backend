# ADR 0004 · Autenticación: cookies de sesión, doble factor obligatorio e ingreso por correo

| | |
| --- | --- |
| Estado | Aceptado (2026-10-25). Las decisiones del diseño técnico de F0-03a, F0-03b y F0-04 las delegó el usuario al coordinador. |
| Fecha | 2026-10-25 |
| Historias | F0-03a, F0-03b, F0-04 |
| Relacionados | [ADR 0001](0001-clean-architecture.md), [ADR 0003](0003-multiempresa-en-dos-barreras.md) |

## Contexto

El ingreso con correo y contraseña y el doble factor TOTP son obligatorios (plan, «Entornos y seguridad», OWASP ASVS nivel 2). Las tablas de usuarios son temporales y están protegidas por seguridad por fila (ADR 0003), lo que condiciona cómo se busca un usuario por su correo antes de saber su firma.

## Decisiones

### 1. Dos cookies: sesión parcial y sesión completa

Una cookie `nitido.parcial` (10 minutos, sin renovación) dice «la clave es correcta, falta el segundo factor»; una cookie `nitido.sesion` (30 minutos de inactividad, con renovación) dice «sesión completa». Las dos son `HttpOnly`, `Secure`, `SameSite=Strict` y solo llevan los reclamos `uuid_usuarios` y `uuid_firmas`. Son dos esquemas de autenticación de ASP.NET Core: la política por defecto exige el de sesión completa, y la política `SesionParcial` (inscripción y verificación del doble factor) exige el parcial. Sin sesión se responde 401 con ProblemDetails, nunca 302.

- **Alternativa descartada:** una sola cookie con un reclamo «segundo factor verificado». Obligaba a que cada endpoint recordara revisarlo; con dos esquemas, un endpoint olvidado queda protegido por la política por defecto.
- **Consecuencia:** la renovación de la cookie es deslizante, y ASP.NET la renueva solo pasada la mitad de su vida. Un periodo de inactividad de entre 15 y 30 minutos puede vencer la sesión un poco antes de lo esperado. Se acepta.
- **Vida máxima:** la sesión completa vence a las 12 horas de haberse abierto (`Seguridad:Sesion:HorasMaximas`), aunque se use sin parar (ASVS 3.3.2). La hora de apertura va en las propiedades de la cookie, porque la renovación deslizante reinicia `IssuedUtc`.
- **Salir no revoca en el servidor:** `POST /salida` borra la cookie del navegador, pero una copia de la cookie sigue sirviendo hasta su vencimiento. Cerrarlo del todo exige un almacén de sesiones o validar el sello en cada petición (una lectura a la base por petición); se decide con el cambio de contraseña. Mismo caso: un usuario desactivado conserva su sesión hasta que venza; solo `/yo` revisa `Activo`.
- **Pendiente:** el sello de seguridad (`SELLO_SEGURIDAD`) no se valida en cada petición; hoy solo es la ficha de concurrencia de la fila. Validarlo (para invalidar sesiones al cambiar la contraseña) llega con el cambio de contraseña.

### 2. Sin `UserManager` de Identity

Se usa `PasswordHasher<TUser>` de `Microsoft.Extensions.Identity.Core` para las contraseñas, no `AddIdentityCore` con `UserManager` y `SignInManager`. Sus almacenes (`UserStore`) asumen un modelo propio de tablas, no encajan con las tablas temporales con historial ni con la seguridad por fila, y el bloqueo, el TOTP y los códigos de recuperación se implementan aquí con las reglas de la especificación. **Esto se aparta de lo que decía la especificación de F0-03a** («`AddIdentityCore`»). Lo único de Identity que se usa es el hash.

### 3. TOTP propio (RFC 6238), sin librería

Seis dígitos, paso de 30 segundos, HMAC-SHA1 (lo que esperan las aplicaciones autenticadoras), tolerancia de un paso a cada lado y rechazo de cualquier paso que no sea posterior al último usado (`TOTP_ULTIMO_PASO`). Son unas 40 líneas, con los vectores de la RFC 6238 como prueba. **Se aparta de lo que decía F0-03b** («`AuthenticatorTokenProvider` de Identity»), que necesita `UserManager`. El secreto va cifrado con Data Protection (`T_USUARIOS.SECRETO_TOTP`); el llavero está en una carpeta local (`.llavero/`, en `.gitignore`), sin cifrar en reposo, hasta que se decida el despliegue.

### 4. Códigos de recuperación en una columna JSON

Diez códigos de diez caracteres (alfabeto sin caracteres ambiguos), guardados como SHA-256 en un arreglo JSON de `T_USUARIOS.CODIGOS_RECUPERACION_HASH`, que se muestran una sola vez. Una tabla propia habría exigido otra tabla temporal con su propia política de seguridad por fila (y su historial) por diez valores; la columna usa la ficha de concurrencia de la fila para que dos usos simultáneos del mismo código no pasen los dos. SHA-256 sin sal es aceptable aquí porque cada código tiene unos 50 bits de entropía aleatoria.

### 5. Búsqueda del usuario por correo: llave `CORREO_INGRESO`

El ingreso busca al usuario por correo antes de saber su firma. Se confirma la propuesta del ADR 0003: una cuarta llave de solo lectura, `CORREO_INGRESO` (siempre fijada; vacía salvo en esa búsqueda), y el predicado FILTER de `T_USUARIOS` pasa a `seguridad.FN_USUARIOS_ES_VISIBLE`, que deja ver las filas de la firma de la sesión **o**, solo con la firma y el usuario de la sesión en `Guid.Empty` y la llave con valor, la fila de ese correo. Los predicados BLOCK no cambian: la fila encontrada se puede leer pero no escribir. Los contadores de intentos se escriben en un segundo alcance con el usuario y la firma ya conocidos (`CrearAlcanceConContexto`). Todo lo anterior vive en `Nitido.Compartido.Infraestructura` (`ContextoDeAlcance` en `Nitido.Compartido`) y no tiene puertas traseras: la llave solo la fija ese código.

### 6. Antifalsificación por doble envío firmado

`XSRF-TOKEN` (legible por el navegador) lleva un valor aleatorio firmado con Data Protection y todo POST, PUT, PATCH o DELETE de `/api` debe traerlo igual en `X-XSRF-TOKEN` (lo que hace `HttpClient` de Angular solo). Se descartó el servicio `IAntiforgery` de ASP.NET Core porque ata el token a la identidad: el token cambia al ingresar, al pasar de sesión parcial a completa y al salir, y las peticiones entre medias fallarían con 400. Con `SameSite=Strict` y `Secure`, el doble envío firmado cubre el mismo riesgo.

### 7. Límite de peticiones y rutas sin sesión

El ingreso, la inscripción y verificación del segundo factor y el registro de firma limitan a 10 peticiones por minuto y por IP (`Seguridad:LimitePeticiones:PorMinuto`); `/yo` y `/salida` no lo llevan, porque una firma entera detrás de una sola IP recarga la aplicación varias veces por minuto con el limitador de ASP.NET Core; en 429 se responde ProblemDetails. Detrás de un proxy, la IP debe venir de `ForwardedHeaders` (pendiente con el despliegue).

No se usa una política de respaldo (`FallbackPolicy`): con ella, una ruta inexistente respondería 401 en vez de 404. En su lugar, cada grupo de rutas llama a `RequireAuthorization()` y la prueba `Endpoints_ExigenSesionSalvoListaPermitida` recorre todos los endpoints registrados y falla si alguno fuera de la lista (`/salud`, ingreso, registro y el contrato OpenAPI) no exige sesión.

### 8. Registro de firmas y siembra de Development

`POST /api/firmas/registro` solo existe con `Registro:Habilitado=true` (`true` en Development, `false` por defecto, 404 si no). Crea la firma y su administrador en un solo `SaveChanges`, en un alcance con la firma nueva y el usuario «sistema». En Development, `SemillaDeDesarrollo` crea una firma y un administrador con `Desarrollo:UsuarioInicial:Correo` y `Desarrollo:UsuarioInicial:Clave` (user-secrets); fuera de Development no hace nada.

### 9. Contador de intentos compartido

Los fallos de la contraseña, del código TOTP y de los códigos de recuperación suman en el mismo contador (`INTENTOS_FALLIDOS`). **La clave correcta no lo reinicia**: solo lo reinicia completar el segundo factor. Si lo reiniciara, quien ya tiene la clave podría probar códigos sin límite (ingresar, cuatro códigos, ingresar…); la prueba `SegundoFactor_VolverAIngresarConLaClaveNoReiniciaElBloqueo` lo cubre. Consecuencia aceptada: cinco errores de clave repartidos en varios ingresos que no completan el segundo factor bloquean la cuenta.

## Límites conocidos (aceptados para la fase 0)

- Dos intentos fallidos simultáneos pueden contarse como uno (la ficha de concurrencia hace que uno de los dos no guarde).
- Un correo que no existe tarda unos milisegundos menos que uno que existe (no abre el segundo alcance): lo atenúa el límite de peticiones y el hash que se verifica igual.
- Faltan encabezados de seguridad (`X-Content-Type-Options`, HSTS): se definen con el despliegue.
- Detrás de un proxy, la IP del límite debe venir de `ForwardedHeaders`.

## Consecuencias

- Mensajes de error y bloqueo idénticos para clave errada, correo inexistente, usuario inactivo y cuenta bloqueada (se verifica un hash ficticio cuando el correo no existe, para igualar el tiempo).
- Un usuario con la contraseña correcta pero sin segundo factor no puede hacer nada salvo inscribirlo. Entre F0-03a y F0-03b, la contraseña sola daba acceso; esa nota ya no aplica: F0-03b está en el mismo cambio.
- Quien pierde su teléfono y sus códigos de recuperación no puede entrar: el restablecimiento por un administrador quedó fuera de alcance (decisión de F0-03b).
