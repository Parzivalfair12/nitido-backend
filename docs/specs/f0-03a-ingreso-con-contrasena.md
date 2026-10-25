# F0-03a · Fase 0 · 3a · Ingreso con correo y contraseña

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | F0-02b |
| Orden de construcción | 5 de 10 |
| Etiquetas | `fase-0`, `historia`, `seguridad`, `backend`, `modulo:firmas`, `requiere-adr` |
| Estado | Terminada el 2026-10-25 (implementada, con pruebas; sin pull request: todavía no hay repositorios en GitHub). Diseño: [ADR 0004](../adr/0004-autenticacion-cookies-y-doble-factor.md). Desvíos de la especificación: sin `UserManager` de Identity (solo `PasswordHasher`), y antifalsificación por doble envío firmado. |

> **Ver ADR 0001** y `docs/arquitectura-backend.md` (secciones 6.2 y 6.3). Identity vive en `Firmas.Infraestructura`, nunca en el dominio. Los casos de uso de autenticación van detrás de un puerto en `Firmas.Aplicacion`, y la cookie y el limitador en el host. El cuerpo del `401` se decidió el 2026-10-18: ProblemDetails con el texto en `title`, no `{"mensaje":...}`.

Esta historia sale de partir la historia 3 original en dos; la otra parte es F0-03b, el doble factor. Juntas pasaban de 2 días.

## Historia

Como contador de una firma quiero ingresar a nítido con mi correo y mi contraseña para trabajar en los clientes de mi firma sin que otra persona pueda entrar con mi cuenta.

## Criterios de aceptación

### Contraseña

- [ ] La migración `AgregarCredencialesAUsuarios`, del módulo Firmas, agrega a `T_USUARIOS` las columnas de clave, sello de seguridad, intentos fallidos y bloqueo, con nombres en mayúsculas según la convención.
- [ ] La prueba `Clave_SeGuardaConHash` pasa: la columna no contiene la clave en claro, y la clave se verifica con el `PasswordHasher` de Identity.
- [ ] La política de contraseña exige mínimo 12 caracteres, sin reglas de composición. El valor sale de `Seguridad:Clave:LargoMinimo`, y la prueba `Clave_LargoMinimoSaleDeConfiguracion` lo comprueba cambiando la configuración.

### Ingreso

- [ ] `POST /api/autenticacion/ingreso` con `{correo, clave}` correctos responde `200` y entrega la cookie `nitido.sesion` con `HttpOnly`, `Secure` y `SameSite=Strict`.
- [ ] Con clave errada, correo inexistente, usuario inactivo o cuenta bloqueada responde `401` en formato ProblemDetails (`application/problem+json`) con `title` igual a «El correo o la contraseña no coinciden.» y `status` 401 (Ver ADR 0001, 2026-10-18; el frontend lee `title`). La prueba `Ingreso_RespuestaIgualParaCorreoInexistenteYClaveErrada` compara las respuestas completas (estado, `title` y demás campos) y comprueba que son idénticas en los cuatro casos, sin un campo `mensaje`.
- [ ] Tras 5 intentos fallidos seguidos, la cuenta queda bloqueada 15 minutos. Los valores salen de `Seguridad:Bloqueo:IntentosMaximos` y `Seguridad:Bloqueo:Minutos`. La prueba `Ingreso_BloqueaTrasIntentosFallidos`, con reloj controlado, comprueba que la clave correcta no sirve durante el bloqueo y vuelve a servir al terminar.
- [ ] La petición número 11 en un minuto, desde la misma IP, a `/api/autenticacion/*` responde `429`. El límite sale de `Seguridad:LimitePeticiones:PorMinuto` y usa el limitador de ASP.NET Core.

### Sesión

- [ ] `GET /api/autenticacion/yo` responde `200` con `{nombre, correo, rol, firma}` si hay sesión. Sin sesión responde `401` en formato ProblemDetails, nunca un `302` (Ver ADR 0001).
- [ ] `POST /api/autenticacion/salida` responde `204`, y la siguiente llamada a `/yo` responde `401`.
- [ ] La sesión vence tras 30 minutos sin actividad (`Seguridad:Sesion:MinutosInactividad`). Lo comprueba la prueba `Sesion_VencePorInactividad`, con reloj controlado.
- [ ] Al ingresar, la sesión lleva el usuario y la firma, y `IContextoSesion` (creada en F0-02a y completada en F0-02b) se llena desde ella a partir de `HttpContext`, en `Nitido.Compartido.Api`.

### Protecciones

- [ ] Todo `POST`, `PUT` o `DELETE` sin el encabezado de antifalsificación responde `400`. Los nombres son `XSRF-TOKEN` para la cookie y `X-XSRF-TOKEN` para el encabezado, los que espera `HttpClient` de Angular.
- [ ] La prueba `Endpoints_ExigenSesionSalvoListaPermitida` recorre los endpoints registrados. Falla si alguno fuera de `/salud`, `/api/autenticacion/ingreso` y `/api/firmas/registro` (F0-04) responde sin exigir sesión, porque la política por defecto exige usuario autenticado.
- [ ] La prueba `Ingreso_NoEscribeLaClaveEnRegistros` captura Serilog en memoria y no encuentra la clave enviada.

### Datos para probar a mano

- [ ] Solo en Development, una semilla crea una firma y un usuario administrador. El correo y la clave salen de user-secrets: `Desarrollo:UsuarioInicial:Correo` y `Desarrollo:UsuarioInicial:Clave`. Con `ASPNETCORE_ENVIRONMENT=Production` no se crea nada; lo comprueba la prueba `SemillaDesarrollo_NoCorreFueraDeDevelopment`.
- [ ] `Nitido.Api/Nitido.Api.http` trae las peticiones de ingreso, `/yo` y salida.

## Pantalla

No aplica. La pantalla de ingreso llega en F0-05b.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables.

## Diseño técnico

Se completa en el paso 3, junto con el ADR «Autenticación: Identity, cookie de sesión y doble factor obligatorio». Ya están decididos estos puntos:

- **ASP.NET Core Identity** (`AddIdentityCore`), con las entidades mapeadas a `T_USUARIOS` y demás tablas `T_` por `IEntityTypeConfiguration`. La clase de usuario ya vive en `Firmas.Infraestructura` desde F0-02a y aquí pasa a heredar de `IdentityUser<Guid>` sin moverse (Ver ADR 0001). El plan lo nombra en «Stack tecnológico». Se descartó una implementación propia.
- **Cookie `HttpOnly`** en el mismo origen, gracias al proxy de Angular, con antifalsificación. Sin tokens en el navegador, como dice el plan. Se descartó JWT.
- **Límite de peticiones** con el limitador incluido en ASP.NET Core, sin librerías nuevas.
- **Respuesta genérica** también para la cuenta bloqueada, para no revelar qué correos existen.
- **Referencia:** OWASP ASVS nivel 2 (plan, «Entornos y seguridad»).
- **Heredado de F0-02b ([ADR 0003](../adr/0003-multiempresa-en-dos-barreras.md), «Ingreso»):** la política de `T_USUARIOS` solo deja ver las filas de la firma de la sesión, así que el ingreso (que busca al usuario por correo antes de saber su firma) usa una cuarta llave de solo lectura, `CORREO_INGRESO`, y un predicado FILTER en `T_USUARIOS` que, con la firma de la sesión en `Guid.Empty`, deja ver solo la fila de ese correo. Este diseño técnico lo confirma o lo cambia. La cookie lleva los reclamos `ReclamosDeSesion.UuidUsuarios` (`uuid_usuarios`) y `ReclamosDeSesion.UuidFirmas` (`uuid_firmas`), de `Nitido.Compartido.Api`; si los emite `Firmas.Infraestructura`, que no ve ese proyecto, se mueven a `Nitido.Compartido/Multiempresa/`. La semilla de Development usa `CrearAlcanceConContexto` (se construye en F0-04), sin puertas en el predicado.

## Fuera de alcance

- Doble factor (F0-03b).
- «¿La olvidaste?» y cambio de contraseña: requieren envío de correos y no se dibujan en la fase 0.
- «Mantener la sesión iniciada».
- Ingreso con Google o Microsoft.
- Administrar usuarios e invitarlos.
- Roles distintos de Administrador.

**Nota:** entre F0-03a y F0-03b, la contraseña sola da acceso completo. Se acepta porque no hay despliegue en la fase 0, pero F0-03b debe unirse a `main` antes de cualquier ambiente compartido.
