# F0-03b · Fase 0 · 3b · Doble factor con aplicación autenticadora y códigos de recuperación

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | F0-03a |
| Orden de construcción | 6 de 10 |
| Etiquetas | `fase-0`, `historia`, `seguridad`, `backend`, `modulo:firmas` |
| Estado | Terminada el 2026-10-25 (implementada, con pruebas; sin pull request: todavía no hay repositorios en GitHub). Diseño: [ADR 0004](../adr/0004-autenticacion-cookies-y-doble-factor.md). Desvío: TOTP propio (RFC 6238) en lugar del `AuthenticatorTokenProvider` de Identity; los códigos de recuperación van en una columna JSON de `T_USUARIOS`. Al confirmar la inscripción la persona queda con sesión completa. |

> **Ver ADR 0001** y `docs/arquitectura-backend.md` (sección 6.2). El almacén de códigos de recuperación y la marca TOTP van en `Firmas.Infraestructura`, y Data Protection en el host. Los criterios no cambian, salvo una aclaración del 2026-10-18: los errores `400` y `401` de esta historia usan ProblemDetails con el texto en `title`, igual que el `401` de F0-03a.

## Historia

Como contador de una firma quiero confirmar mi ingreso con un código de mi aplicación autenticadora para que nadie entre a la información de mis clientes aunque conozca mi contraseña.

## Criterios de aceptación

### Doble factor obligatorio

- [ ] El doble factor es obligatorio para todos los usuarios. La clave correcta no da sesión completa:
  - si el usuario no tiene doble factor, `/ingreso` responde `200 {"paso":"inscribir-segundo-factor"}`;
  - si ya lo tiene, responde `200 {"paso":"segundo-factor"}`.

  En ambos casos entrega una cookie parcial.
- [ ] La prueba `SesionParcial_NoDaAccesoAlResto` pasa: con la cookie parcial, todo endpoint que no sea de inscripción o verificación responde `401`.

### Inscripción

- [ ] `GET /api/autenticacion/segundo-factor/inscripcion` responde `200 {claveManual, uriOtpauth}`. La URI sigue el formato `otpauth://totp/...`: TOTP según RFC 6238, 6 dígitos, cada 30 segundos.
- [ ] `POST /api/autenticacion/segundo-factor/inscripcion` con `{codigo}`:
  - con un código válido, calculado en la prueba a partir de la clave, responde `200 {codigosRecuperacion: [...10 códigos]}`. Los códigos se entregan una sola vez.
  - con un código inválido responde `400` en formato ProblemDetails, con «El código no es válido. Revisa que la hora de tu teléfono esté bien y vuelve a intentarlo.» en `title`.

### Verificación

- [ ] `POST /api/autenticacion/segundo-factor` con `{codigo}`: un código válido da sesión completa (`200`); uno inválido responde `401` en formato ProblemDetails, con el mismo mensaje en `title`.
- [ ] La prueba `Totp_CodigoUsadoNoSirveOtraVez` pasa: un código ya usado se rechaza aunque siga dentro de su ventana.
- [ ] La prueba `Totp_ToleranciaDeReloj`, con reloj controlado, comprueba que se acepta un código de hace 30 segundos y se rechaza uno de hace 5 minutos.
- [ ] Los códigos fallidos cuentan para el bloqueo de F0-03a (5 intentos, 15 minutos). Lo comprueba la prueba `SegundoFactor_FallosCuentanParaBloqueo`.

### Códigos de recuperación

- [ ] `POST /api/autenticacion/segundo-factor/recuperacion` con `{codigo}`: un código válido da sesión completa y responde `200 {codigosRestantes}`. El mismo código, usado otra vez, responde `401`.

### Almacenamiento

- [ ] La prueba `SecretoTotp_GuardadoCifrado` pasa: la columna no contiene la clave en base32. El secreto se cifra con ASP.NET Core Data Protection, con el llavero en una carpeta local que está en `.gitignore`. No se usa SDK de nube (regla 7).
- [ ] La prueba `CodigosRecuperacion_GuardadosConHash` pasa: ningún código aparece en claro en la base.
- [ ] Todos los mensajes al usuario están en español de Colombia y en segunda persona.

## Pantalla

No aplica. Las pantallas de código e inscripción llegan en F0-05b.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables.

## Diseño técnico

Se completa en el paso 3. Ya están decididos estos puntos:

- **TOTP:** el `AuthenticatorTokenProvider` de Identity, más una marca del último paso de tiempo usado, para rechazar códigos repetidos. Identity no lo hace solo. Se descartó Otp.NET, que agrega una librería sin necesidad.
- **Códigos de recuperación:** un almacén propio que los guarda con hash. El almacén por defecto de Identity los guarda en texto plano.
- **Secreto TOTP:** cifrado con Data Protection. Dónde vive el llavero en la nube se decide con el despliegue, que no está en la fase 0. Esa configuración va en el host, nunca en el dominio.

## Fuera de alcance

- Que un administrador reinicie el doble factor de un usuario que perdió su teléfono y sus códigos (decisión tomada).
- Regenerar los códigos de recuperación.
- SMS o correo como segundo factor.
- Llaves de acceso (passkeys).
- Recordar el dispositivo.
