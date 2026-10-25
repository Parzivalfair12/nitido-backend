> Documento histórico. Describe el kit de arranque original, cuando el proyecto era un solo repositorio. Desde el 18 de octubre de 2026 nítido tiene dos repositorios (`nitido-backend` y `nitido-frontend`); la estructura vigente está en `README.md` y `CLAUDE.md`. Los agentes y las skills viven ahora en `.claude/` de la carpeta raíz `nitido/`, común a los dos repositorios y sin versionar con ellos.

# Kit de arranque de nítido para Claude Code

Contiene la configuración del equipo de agentes y las skills del proyecto.

```
CLAUDE.md                         instrucciones generales (en la carpeta raíz `nitido/`)
.claude/agents/                   (en la carpeta raíz `nitido/`) 8 agentes: arquitecto, contador, dba, dev-backend,
                                  dev-frontend, tester, revisor, devops
.claude/skills/                   7 skills: nueva-funcionalidad, convenciones-bd, lector-dian,
                                  motor-asientos, normativa-colombia, diseno-cristal,
                                  pruebas-contables
```

## Cómo usarlo
1. Crea los repositorios `nitido-backend` y `nitido-frontend` en GitHub y clónalos como `backend/` y `frontend/` dentro de la carpeta raíz `nitido/`.
2. Deja `CLAUDE.md` y la carpeta `.claude/` en la carpeta raíz `nitido/` (no se versionan con los repositorios).
3. Abre Claude Code en la carpeta raíz `nitido/`. Los agentes aparecen con `/agents` y las skills se cargan solas cuando aplican.
4. Para construir algo: crea la historia en GitHub y pide «usa nueva-funcionalidad con la historia #1».

La fase 0 del plan (repositorio, CI, ingreso, firmas y clientes) es la primera historia recomendada.
