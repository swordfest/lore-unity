# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
