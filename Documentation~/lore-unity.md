# Lore VCS para Unity

Integración del sistema de control de versiones [Lore](https://github.com/EpicGames/lore)
(Epic Games, open source) en el editor de Unity, envolviendo el CLI `lore`.

## Requisitos

- CLI de `lore` instalado ([releases oficiales](https://github.com/EpicGames/lore/releases)):
  - macOS/Linux: `curl -fsSL https://raw.githubusercontent.com/EpicGames/lore/main/scripts/install.sh | bash`
  - Windows: instalador MSI o `irm https://raw.githubusercontent.com/EpicGames/lore/main/scripts/install.ps1 | iex`
- El proyecto Unity debe ser un working tree de Lore (carpeta `.lore/` en la raíz
  del proyecto, junto a `Assets/`). Se crea con `lore repository create` o `lore clone`.

## Instalación

- **Git URL**: Package Manager → `+` → *Install package from git URL*.
- **Tarball**: Package Manager → `+` → *Install package from tarball*.
- **Embebido**: copia la carpeta del paquete a `Packages/` de tu proyecto.

## La ventana de Lore

`Window → Lore` (atajo `Cmd/Ctrl+Shift+L`).

### Header
Branch actual, revisión y estado de sincronización con el servidor (`✓ en sync`).
El botón `↻` refresca todo.

### Branches
- Dropdown para cambiar de branch. Antes de cambiar, el plugin ofrece guardar
  las escenas modificadas; después del switch refresca la base de assets.
- Campo + botón "Crear y cambiar" para abrir una branch nueva desde la revisión actual.

### Cambios
Lista de archivos añadidos (A), modificados (M) y borrados (D) respecto a la
revisión actual. Respeta el `.loreignore` del repositorio.

### Commit
Mensaje + "Stage + Commit" o "Stage + Commit + Push". El stage ejecuta
`lore stage --scan .`, así que el `.loreignore` decide qué entra.

### Sync / Push
- **Sync (pull)**: trae la última revisión del servidor (ofrece guardar escenas antes).
- **Push**: sube tus commits a la branch remota.

### Servidor
- Indicador de salud del servidor del repositorio (● online / ○ sin respuesta),
  re-chequeado cada 30 s. El host se lee del `remote_url` en `.lore/config.toml`.
- Si el binario `loreserver` está instalado en la máquina: botones
  **Iniciar servidor** (proceso desacoplado del editor) y **Detener servidor**
  (con confirmación).
- Con el servidor corriendo, se listan las **direcciones para compartir**
  (`lore://IP:41337/repo`) de cada interfaz de red, con botón Copiar.

### ⚙ Ajustes
- Ruta del CLI `lore` (autodetecta `~/.local/bin/lore`, `%USERPROFILE%\bin\lore.exe`
  y ubicaciones típicas).
- Ruta del binario `loreserver` y de su directorio de configuración (`--config`).

## Recomendaciones para proyectos Unity con Lore

- Mantén en el `.loreignore`: `Library`, `Temp`, `Logs`, `UserSettings`, `obj`,
  `Build(s)`, `*.csproj`, `*.sln`.
- Activa *Asset Serialization: Force Text* y *Visible Meta Files*
  (`Edit → Project Settings → Editor`) para que escenas y prefabs sean diffeables.
- Para assets binarios compartidos, usa `lore lock <ruta>` antes de editar.
