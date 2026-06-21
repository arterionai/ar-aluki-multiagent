# BRD - Aluki Starter Baseline

Version: 1.1
Status: Draft for new clean repository
Date: 2026-06-21

## 1. Objetivo del producto

Aluki es una plataforma de sesiones inteligentes omnicanal que captura, organiza y recupera conocimiento con grounding y trazabilidad.

Este BRD define el baseline completo de features ya acordados y asume como arquitectura obligatoria la establecida en [ARCHITECTURE_BASELINE.md](../ARCHITECTURE_BASELINE.md).

## 2. Alcance de este BRD

Incluye:
- Features funcionales del starter baseline y su expansión ya definida en specs.
- Requisitos de seguridad, tenancy, grounding, gobernanza y costo.
- Criterios de aceptación de nivel producto para implementación incremental.

No incluye:
- Migración del código legado.
- Features no formalizados en specs del repositorio.

## 3. Usuarios objetivo

1. Usuario individual (tenant tipo INDIVIDUAL)
- Captura recuerdos, tareas, links, sugerencias y notas personales.

2. Equipo o empresa (tenant tipo ORGANIZATION)
- Comparte contexto conversacional por grupos/canales.
- Gestiona conocimiento común con control de acceso.

3. Equipo interno Arterion (admin)
- Opera y clasifica sugerencias de producto en panel privado.

## 4. Problemas que resolvemos

1. Información dispersa entre canales y mensajes.
2. Dificultad para recordar hechos con evidencia.
3. Baja trazabilidad de decisiones, tareas y contexto.
4. Riesgo de fuga de datos entre organizaciones o usuarios.
5. Costos variables de IA sin control operativo fino.
6. Falta de captura estructurada de feedback y cierre del loop producto-usuario.

## 5. Propuesta de valor

- Captura omnicanal en un modelo unificado (UMO).
- Memoria recuperable con citas y provenance.
- Conexión semántica entre entidades y relaciones.
- Acciones útiles (recordatorios, agenda, links) desde conversación.
- Seguridad por tenant/contexto con RLS y políticas.
- Control de costo por ruteo de modelos y presupuestos por tenant.
- Canal de sugerencias con incentivos y operación admin.

## 6. Requisitos funcionales (inventario completo)

### F1. Ingesta omnicanal y normalización
Descripción:
- Recibir eventos de WhatsApp (MVP) y diseño channel-agnostic para Teams, Email, Telegram y Slack.
- Normalizar todo a Unified Message Object.
- Manejar adjuntos para procesamiento posterior.

Criterios de aceptación:
- Todo mensaje válido entra como UMO con `tenant_id`, `context_id`, `created_by_user_id`, `source_channel`.
- Contenido no soportado responde con fallback controlado, sin romper sesión.

### F2. Captura de memoria y extracción estructurada
Descripción:
- Convertir conversaciones en hechos persistentes.
- Extraer hechos, tareas, decisiones, montos, entidades y fechas con provenance.

Criterios de aceptación:
- Cada hecho persistido se traza a mensaje origen.
- Idempotencia evita duplicados por reintentos o redelivery.

### F3. Recall grounded con citas
Descripción:
- Responder preguntas solo con memoria recuperada.
- Incluir citas explícitas y explicación de origen.

Criterios de aceptación:
- Toda respuesta de recall incluye evidencia.
- Si no hay evidencia suficiente, el sistema no inventa.

### F4. Grafo semántico de entidades y relaciones
Descripción:
- Resolver entidades, enlazar menciones y mantener relaciones.
- Permitir consultas y explicaciones de relación entre hechos.

Criterios de aceptación:
- Entidades/relaciones aisladas por tenant.
- El sistema explica por qué dos hechos están conectados.

### F5. Integración de calendario (Outlook y Google)
Descripción:
- Crear eventos por lenguaje natural.
- Flujo de conexión OAuth con almacenamiento seguro de tokens.
- Clarificación multi-turno para campos ambiguos.

Criterios de aceptación:
- Soporte de Office 365/Outlook y Google Calendar.
- Si no hay conexión previa, se inicia link de conexión one-time firmado.
- Confirmación al usuario con fecha/hora final normalizada a su timezone.

### F6. Reminders personales y recurrentes
Descripción:
- Crear recordatorios one-shot y recurrentes desde conversación.
- Enviar notificaciones con opciones de done/snooze.

Criterios de aceptación:
- Recordatorios disparan en tiempo programado.
- Soporte de snooze, done y overdue follow-up.
- Recurring cadence diaria/semanal/mensual.

### F7. Delegated reminders (recordar a otra persona)
Descripción:
- Detectar intención delegada sin caer en flujo incorrecto.
- Capturar destinatario y consentimiento.
- Enviar recordatorio al tercero cuando aplique.

Criterios de aceptación:
- No se enruta a onboarding/contact flow equivocado.
- Se distingue gestión de reminders personales vs delegados.
- Fallos de entrega al tercero informan al solicitante.

### F8. Link capture y enriquecimiento seguro
Descripción:
- Capturar URLs con contexto limpio.
- Confirmación sí/no sin bucles.
- Enriquecer metadata de forma segura y mostrar URL completa en recall.

Criterios de aceptación:
- Nunca responde "no puedo acceder a enlaces" como fallback primario.
- Sí/no resuelve una sola vez sin loop.
- Recall devuelve URL completa y descripción (metadata o etiqueta usuario).

### F9. YouTube save & classify
Descripción:
- Detectar enlaces YouTube, extraer metadata, clasificar por categoría/tags/resumen.
- Persistir como objeto de link estructurado con idempotencia.

Criterios de aceptación:
- Soporte `youtu.be`, `watch`, `shorts`, `embed`, `live`.
- Enriquecimiento por Data API con fallback oEmbed.
- Sin metadata disponible, se guarda link sin romper flujo.

### F10. AI extraction avanzada (audio, texto, recibos)
Descripción:
- Transcripción de audio (es-MX/en-US).
- Resumen y extracción de accionables/decisiones en texto.
- OCR de recibos con RFC y montos.

Criterios de aceptación:
- Voice note produce transcripción + acción estructurada.
- Texto largo produce resumen + decisiones + pendientes.
- Recibos devuelven proveedor, fecha, total, impuestos y RFC cuando exista.

### F11. Sugerencias de producto (captura y seguimiento)
Descripción:
- Capturar sugerencias separadas de memoria normal.
- Aceptar texto, audio y foto como contexto adicional.

Criterios de aceptación:
- Sugerencia no se mezcla en recall normal de notas.
- Follow-ups se asocian a sugerencia activa por ventana temporal.

### F12. Admin de sugerencias e incentivos
Descripción:
- Dashboard interno Arterion con login Entra ID.
- Clasificación por estado/prioridad/categoría y auditoría.
- Incentivos al usuario por sugerencia nueva y progresión.

Criterios de aceptación:
- Solo staff autorizado puede ver/editar sugerencias.
- Base reward y quality bonus se aplican una sola vez por regla.
- Notificación de recompensa respeta ventana/plantilla de WhatsApp.

### F13. UX conversacional y enrutamiento por dominio
Descripción:
- Clarificación, follow-up, small talk, out-of-scope.
- Arquitectura de agentes de dominio para evitar god-object y aislar estado.

Criterios de aceptación:
- Mensajes ambiguos generan aclaración controlada.
- Fallback a captura cuando ningún agente resuelve intención.
- Incorporar nuevos dominios sin editar core monolítico de procesamiento.

### F14. Seguridad, gobierno y cumplimiento transversal
Descripción:
- Controles de acceso, auditoría, consentimiento, idempotencia y presupuesto.

Criterios de aceptación:
- No hay operación sin `PrincipalContext`.
- No hay query sin scope de tenant/contexto.
- Todo side effect queda auditado.
- STOP/ALTO desactiva procesamiento según política.

## 7. Requisitos de arquitectura (obligatorios)

1. Runtime
- Orleans para sesiones vivas (estado corto).
- Durable Functions para procesos largos (timers, waits, retries, resume).

2. Modelo de ejecución
- Skill Registry como unidad atómica.
- Agentes orquestan; skills ejecutan lógica de negocio y side effects.

3. Datos
- PostgreSQL como almacén principal.
- pgvector para recuperación semántica.
- Capa de grafo semántico dentro de PostgreSQL.

4. Aislamiento
- RLS habilitado y aplicado por tenant.
- Contexto y membresía obligatorios para autorización.

## 8. Modelo de identidad y acceso

Entidades requeridas:
- Tenants (INDIVIDUAL u ORGANIZATION).
- Users.
- Memberships.
- Contexts.
- ContextAccess.

Campos obligatorios para artifacts:
- `tenant_id`
- `context_id`
- `created_by_user_id`
- `source_channel`
- `provenance_message_id` cuando aplique

Reglas de oro:
- No operation without PrincipalContext.
- No query without tenant scope.
- No recall without provenance.

## 9. Requisitos no funcionales

### NFR1. Rendimiento
- P95 de respuesta síncrona objetivo <= 2 segundos en flujos no bloqueantes.
- Objetivos específicos de extracción: texto < 5 segundos, OCR recibos < 10 segundos, audio corto <= 15 segundos.

### NFR2. Escalabilidad
- Escala horizontal en runtime de sesiones y workers de workflows.

### NFR3. Confiabilidad
- Reintentos con backoff en procesos largos.
- Reanudación segura con contexto de tenant/user.

### NFR4. Observabilidad
- Trazas y métricas por `request_id`, `session_id`, `tenant_id`, `context_id`, `skill_name`.

### NFR5. Cost governance
- Presupuesto por tenant.
- Circuit breaker por costo y latencia.
- Ruteo de modelo con de-escalación automática.

## 10. Estrategia de modelos para los features

Política:
- Default con modelos de valor.
- Escalación por baja confianza, alta ambigüedad o criticidad.
- De-escalación por estabilidad y presupuesto.

Aplicación:
- F1/F13: routing y clasificación ligera con modelos de valor.
- F2/F3/F4/F10: extracción, síntesis y recall con escalación selectiva.
- F5/F6/F7/F8/F9: mezcla de validación determinística + LLM mínimo necesario.
- F14: enforcement de política y presupuesto siempre activo.

## 11. Feature Matrix (trazabilidad a specs)

| Feature BRD | Spec fuente |
|---|---|
| F1 | `specs/001-whatsapp-capture/spec.md` |
| F2, F3 | `specs/002-personal-memory/spec.md` |
| F5 | `specs/003-calendar-integration/spec.md` |
| F10 | `specs/004-ai-extraction/spec.md` |
| F6 | `specs/005-reminders/spec.md` |
| F7 | `specs/006-delegated-reminders/spec.md` |
| F11 | `specs/007-feedback-suggestions/spec.md` |
| F12 | `specs/008-suggestions-admin/spec.md` |
| F9 | `specs/008-youtube-links/spec.md` |
| F8 | `specs/009-link-capture/spec.md` |
| F13 | `specs/009-domain-agents/spec.md` |

## 12. Fuera de alcance (por ahora)

1. Migración completa de implementaciones legacy.
2. Integraciones de canales no formalizadas en specs.
3. Features no contempladas en este BRD.

## 13. Métricas de éxito inicial

1. Calidad funcional
- Recall con cita en al menos 95% de respuestas de memoria que afirman hechos.

2. Seguridad y aislamiento
- Cero lecturas cruzadas entre tenants.

3. Operación
- 100% de side effects auditados.

4. Coste
- Cumplimiento de presupuesto diario por tenant con alertas y control automático.

5. Producto
- Captura estable de links y sugerencias con confirmación útil al usuario.

## 14. Roadmap de entrega sugerido

Fase A
- F1, F14 base, modelo de identidad y RLS.

Fase B
- F2, F3, F10 (captura + extracción + recall grounded).

Fase C
- F5, F6, F7 (acciones operativas de agenda/reminders).

Fase D
- F8, F9, F11, F12, F13 (links + sugerencias + madurez de dominio).

## 15. Dependencias

1. Infraestructura
- PostgreSQL con extensión vector.
- Orleans host.
- Durable Functions host.

2. Plataforma
- Canal inicial WhatsApp para validación end-to-end.

3. Gobernanza
- Definición de roles/políticas tenant-context.
- Acceso Entra ID para panel admin de sugerencias.

## 16. Decisión de producto

Esta versión 1.1 consolida el baseline arquitectónico y el inventario completo de features ya definidos, para habilitar un arranque limpio con alcance explícito y trazable.
