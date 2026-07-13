# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-07-13

### Added
- Pestañas en la ventana: Trabajo / Historial / Merge.
- Historial: timeline de revisiones (número, mensaje, fecha, autor); al
  seleccionar una entrada se ve el mensaje completo y la firma con botón Copiar.
- Merge: selección de branch origen con indicación clara de la dirección
  (origen → branch actual), previsualización con "Ver diferencias" y
  "Simular (dry-run)", y ejecución con mensaje personalizado.
- Resolución de conflictos: lista de archivos en conflicto con botones por
  archivo "Local (mío)" / "Remoto (suyo)", resolución masiva, finalizar
  merge (commit) y abortar merge, todo con diálogos de confirmación.

### Fixed
- El campo de mensaje de commit ahora se limpia de verdad tras
  Stage + Commit (+ Push) — antes el foco de IMGUI retenía el texto.

## [1.2.0] - 2026-07-12

### Added
- "Direcciones para compartir": lista de URLs `lore://` por cada interfaz de
  red activa cuando el servidor corre localmente, con botón Copiar.
- Botón "Limpiar" en el panel de salida.

### Changed
- El panel de salida (log) ahora se expande para ocupar todo el alto restante
  de la ventana (mínimo 150 px).

## [1.1.0] - 2026-07-12

### Added
- Módulo de servidor: indicador de salud del servidor del repo (health check
  HTTP cada 30 s, local o remoto) y botones Iniciar/Detener cuando el binario
  `loreserver` está instalado en la máquina.
- Ajustes para configurar la ruta de `loreserver` y su directorio de config.

## [1.0.0] - 2026-07-12

### Added
- Panel `Window → Lore` (Cmd/Ctrl+Shift+L) envolviendo el CLI de Lore.
- Estado del repo: branch, revisión, sync con remoto y lista de cambios A/M/D.
- Stage + Commit (+ Push), Sync (pull) y Push.
- Crear y cambiar branches, guardando escenas antes y refrescando assets después.
- Autodetección multiplataforma del CLI `lore` con ruta configurable.
