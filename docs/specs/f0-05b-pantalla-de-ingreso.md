# F0-05b · Fase 0 · 5b · Pantalla de ingreso con doble factor

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | F0-05a, F0-03b |
| Orden de construcción | 9 de 10 |
| Repositorio | Frontend (`nitido-frontend`). Consume la API de F0-03b, que vive en `nitido-backend`: el pull request del backend va primero y el cliente se genera desde su OpenAPI. |
| Etiquetas | `fase-0`, `historia`, `frontend`, `diseno`, `seguridad` |
| Estado | Terminada el 2026-10-25 (implementada, con pruebas; sin pull request: todavía no hay repositorios en GitHub). El cliente de la API no está generado desde el OpenAPI: las llamadas viven a mano en `src/app/nucleo/api/` con tipos que reflejan el contrato. El QR se genera en el navegador con `qrcode` (PNG). Capturas generadas; **falta que el usuario las apruebe frente al mockup**. |

**Referencia:** mockup `docs/mockup-nitido.dc.html` (repositorio del frontend), líneas 218 a 246 (ingreso) y 1081 a 1099 (mensajes), más la skill `diseno-cristal`.

## Historia

Como contador quiero una pantalla de ingreso clara, que me pida mi contraseña y mi código, para entrar rápido y con seguridad.

## Criterios de aceptación

### Ingreso

- [ ] En `/ingreso` está la pantalla del mockup:
  - logo de 30 px y la marca «nítido»;
  - el título «Ingresa a tu cuenta» y el texto «Usa el correo con el que tu firma te dio acceso.»;
  - una tarjeta de vidrio de máximo 380 px de ancho con dos campos: «Correo» (placeholder «nombre@tufirma.com», `type="email"`, `autocomplete="email"`) y «Contraseña» (`autocomplete="current-password"`);
  - el botón principal «Ingresar», de 42 px de alto.
- [ ] No aparecen «¿La olvidaste?» ni «Mantener la sesión iniciada en este equipo».
- [ ] Debajo del botón está el texto «Al ingresar aceptas los términos de uso y la política de tratamiento de datos.». Cada enlace abre y cierra un aviso (`.nota`, `role="region"`) con el resumen del mockup (líneas 1090 a 1093).
- [ ] Debajo de la tarjeta está el texto «¿Tu firma aún no usa Nítido? Solicita acceso». En la fase 0, «Solicita acceso» es texto, sin acción.
- [ ] La validación en pantalla muestra los mensajes del mockup en un `role="alert"`:
  - «Escribe un correo válido para ingresar.»
  - «Escribe tu contraseña para ingresar.»
- [ ] Un `401` de la API muestra «El correo o la contraseña no coinciden.». El texto llega en el campo `title` de la respuesta ProblemDetails (no en un campo `mensaje`), y la pantalla lo lee de ahí (Ver ADR 0001, 2026-10-18). Un `429` muestra «Hiciste muchos intentos seguidos. Espera un minuto y vuelve a intentarlo.»

### Segundo factor

- [ ] El paso de código tiene:
  - el título «Confirma que eres tú» y el texto «Escribe el código de 6 dígitos de tu aplicación autenticadora.»;
  - el campo «Código», con `inputmode="numeric"` y `autocomplete="one-time-code"`;
  - el botón «Confirmar»;
  - el enlace «Usar un código de recuperación», que cambia el campo por «Código de recuperación».
- [ ] La inscripción tiene:
  - el título «Protege tu cuenta»;
  - un código QR;
  - el texto «¿No puedes escanearlo? Escribe esta clave:» seguido de la clave en grupos de 4;
  - el campo «Código» y el botón «Activar».
- [ ] Después de activar se muestran los 10 códigos de recuperación, bajo el título «Guarda tus códigos de recuperación» y el texto «Cada código sirve una sola vez si pierdes tu teléfono. Guárdalos en un lugar seguro: no los volverás a ver.»
  - Hay dos botones secundarios: «Copiar» y «Descargar». «Descargar» genera el archivo `codigos-recuperacion-nitido.txt` en el navegador.
  - El botón «Continuar» queda inactivo hasta marcar «Ya guardé mis códigos».
- [ ] El QR se genera en el navegador. `inscripcion.spec.ts` comprueba que ninguna petición sale de `localhost` durante la inscripción: un servicio externo de QR expondría el secreto.
- [ ] Un código inválido muestra «El código no es válido. Revisa que la hora de tu teléfono esté bien y vuelve a intentarlo.»

### Sesión

- [ ] Un usuario sin sesión que abre `/clientes` termina en `/ingreso` y, al completar el ingreso, vuelve a `/clientes`. La sesión se valida con `GET /api/autenticacion/yo`.
- [ ] El bloque de usuario del menú (F0-05a) muestra el nombre, las iniciales y el rol que entrega `/yo`. «Salir» llama a `POST /api/autenticacion/salida` y lleva a `/ingreso`.
- [ ] Las peticiones `POST` llevan el encabezado `X-XSRF-TOKEN` que espera la API. El flujo completo no recibe ningún `400` por antifalsificación.

### Pruebas y comparación

- [ ] `ingreso.spec.ts` pasa con el flujo completo: inscripción, códigos, salida, ingreso con código e ingreso con código de recuperación. El TOTP se calcula en la prueba a partir de la clave que muestra la pantalla.
- [ ] Hay capturas a 1440×900 y 390×844 de cada paso, aprobadas por el usuario frente al mockup. Axe no reporta violaciones graves ni críticas.

## Pantalla

Mockup `docs/mockup-nitido.dc.html`, líneas 218 a 246.

**Diferencias aprobadas:**
- Sin «¿La olvidaste?» ni «Mantener la sesión iniciada en este equipo».
- «Solicita acceso» es texto, sin acción: no hay registro de firma desde la pantalla en la fase 0.
- **Pasos de código, inscripción y códigos de recuperación:** no están en el mockup. Se diseñan con la misma tarjeta y la misma tipografía del ingreso, y sus textos, propuestos arriba, los aprueba el usuario con las capturas.
- **Opacidad de la tarjeta:** 0,55, como el mockup; la skill dice 0,5.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables.

## Diseño técnico

Se completa en el paso 3.
- **QR:** recomendación, una librería pequeña que genere SVG en el navegador; no amerita ADR.
- **TOTP en la prueba punta a punta:** recomendación, una librería de desarrollo en `e2e/`.

## Fuera de alcance

- «¿La olvidaste?» y recuperación de contraseña.
- «Mantener la sesión iniciada».
- Pantalla de registro de firma.
- Recordar el dispositivo.
- Revisión legal de los textos de términos y política, que está en la fase 5 del plan.
