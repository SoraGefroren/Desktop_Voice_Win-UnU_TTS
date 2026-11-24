# Plan Maestro de Migración Multiplataforma (Windows, Linux y macOS)
## Proyecto: VnEmoteVoice (TTS con Narrador Virtual Emocional)

---

## 1. Diagnóstico del Estado Actual

El proyecto **VnEmoteVoice** cuenta con una base sólida y modular. Como se destacó en el requerimiento, la gran ventaja es que **el núcleo neuronal y de procesamiento lingüístico ya es 100% multiplataforma**:

| Componente | Archivo | Estado Multiplataforma | Tecnología Subyacente |
|---|---|---|---|
| **Kokoro TTS** | `KokoroVoiceEngine.cs` |  **Multiplataforma** | ONNX Runtime (`Microsoft.ML.OnnxRuntime`), MisakiSharp, G2P puro en memoria. |
| **Piper TTS** | `PiperVoiceEngine.cs` |  **Multiplataforma** | ONNX Runtime, MisakiSharp / eSpeakReplay, tensores densos. |
| **VoiceStudio API** | `VoiceStudioApiEngine.cs` |  **Multiplataforma** | `HttpClient`, `System.Text.Json`, llamadas REST estándar. |
| **Analizador Emocional** | `EmotionAnalyzer.cs` |  **Multiplataforma** | Expresiones regulares, LINQ, diccionarios JSON BCP-47 externos. |

### Componentes Actuales Dependientes de Plataforma (A Migrar)

1. **Interfaz Gráfica (UI):**
   * `VnEmoteVoice.Console` utiliza **WPF** (`UseWPF=true`, `net10.0-windows10.0.19041.0`). WPF es exclusivo de Windows y no compila ni ejecuta en Linux ni en macOS.
   * Dependencias directas de WPF: `System.Windows`, `System.Windows.Controls`, `System.Windows.Documents.RichTextBox`, `FlowDocument`, `Run`, `LineBreak`, `DispatcherTimer`, `OpenFileDialog`, `SaveFileDialog`.
2. **Voces Nativas del Sistema Operativo:**
   * `VoiceManager.cs` referencia directamente `Windows.Media.SpeechSynthesis.SpeechSynthesizer` (WinRT), una API nativa de Windows 10/11 que no existe en Linux ni en macOS.
3. **Reproducción de Audio:**
   * Se utiliza `NAudio.Wave.WaveOut`. `WaveOut` utiliza la API Win32 MME de Windows (`winmm.dll`). En macOS y Linux, la instanciación de `WaveOut` arroja `PlatformNotSupportedException` o `DllNotFoundException`.
4. **Moniker del Proyecto (.csproj):**
   * Los proyectos tienen como Target Framework `net10.0-windows10.0.19041.0`. Debe migrarse a `net10.0` para que el código común sea agnóstico del sistema operativo.

---

## 2. Arquitectura Objetivo: Principio de Inversión de Dependencias

Para lograr que cada sistema operativo mantenga sus propias voces y controladores de sonido sin alterar los motores neuronales compartidos, se estructurará la solución bajo interfaces de abstracción:

```
┌────────────────────────────────────────────────────────────────────────┐
│                   VnEmoteVoice.UI (Avalonia UI)                        │
│            (Multiplataforma: Windows, Linux y macOS en XAML)           │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│                       VnEmoteVoice.Core (.NET 10)                      │
│                                                                        │
│   ┌────────────────────┐ ┌────────────────────┐ ┌──────────────────┐   │
│   │ KokoroVoiceEngine  │ │  PiperVoiceEngine  │ │VoiceStudioEngine │   │
│   └────────────────────┘ └────────────────────┘ └──────────────────┘   │
│   ┌────────────────────────────────────────────────────────────────┐   │
│   │                      EmotionAnalyzer                           │   │
│   └────────────────────────────────────────────────────────────────┘   │
│                                   │                                    │
│   ┌───────────────────────────────┴────────────────────────────────┐   │
│   │                         VoiceManager                           │   │
│   └──────┬────────────────────────┬─────────────────────────┬──────┘   │
└──────────┼────────────────────────┼─────────────────────────┼──────────┘
           │                        │                         │
┌──────────▼───────────┐ ┌──────────▼──────────┐  ┌───────────▼──────────┐
│  WindowsVoiceEngine  │ │   MacVoiceEngine    │  │   LinuxVoiceEngine   │
│ (WinRT / SAPI nativo)│ │(say CLI / AVFoundation)│ (speech-dispatcher / │
│                      │ │                     │  │     espeak-ng)       │
└──────────────────────┘ └─────────────────────┘  └──────────────────────┘
```

---

## 3. Estrategia para Voces Nativas por Sistema Operativo

Se definirá una interfaz común en `VnEmoteVoice.Core`:

```csharp
public interface ISystemVoiceEngine : IDisposable
{
    bool IsAvailable { get; }
    List<string> GetInstalledLanguages();
    List<string> GetVoicesForLanguage(string language);
    Task SynthesizeToWavAsync(string text, string outputPath, string voiceName, string language, float speed, float pitch, float volume);
    (string Language, string VoiceName) GetSystemDefaultLanguageAndVoice();
}
```

### 3.1. Windows: `WindowsVoiceEngine`
* **Implementación:** Mantiene la lógica actual basada en `Windows.Media.SpeechSynthesis.SpeechSynthesizer` (WinRT) o respaldo con `System.Speech.Synthesis`.
* **Características:**
  * Soporta voces locales instaladas (David, Zira, Helena, Sabina, etc.) y voces *Natural Online* de Windows 11.
  * SSML nativo con control de `rate`, `pitch` y `volume`.

### 3.2. macOS: `MacVoiceEngine`
* **Implementación Primaria:** Integración con la utilidad nativa `/usr/bin/say` y el framework `AVSpeechSynthesizer` / `NSSpeechSynthesizer`.
* **Descubrimiento de voces:**
  * Comando: `say -v "?"`
  * Devuelve la lista completa de voces instaladas en macOS con su idioma y género (ej. *Mónica [es_ES]*, *Paulina [es_MX]*, *Samantha [en_US]*, voces de alta calidad Siri).
* **Síntesis a archivo WAV:**
  * Comando: `say -v "NombreVoz" -r <velocidad_ppm> -o "salida.wav" --data-format=LEI16@22050 "Texto"`
* **Detección de voz por defecto:**
  * Lectura de las preferencias de macOS mediante `defaults read com.apple.speech.voice.prefs SelectedVoiceName` o el idioma activo con `NSLocale.CurrentLocale`.

### 3.3. Linux: `LinuxVoiceEngine`
* **Implementación Primaria:** `speech-dispatcher` (estándar en GNOME, KDE, Ubuntu, Fedora, Debian) con fallback a `espeak-ng` o `rhvoice`.
* **Descubrimiento de voces:**
  * `spd-say -L` / `spd-conf --test-speech-dispatcher` o enumeración de `espeak-ng --voices`.
* **Síntesis a archivo WAV:**
  * Opción A (`spd-say`): Genera la locución a través del demonio del sistema.
  * Opción B (`espeak-ng`): `espeak-ng -v <idioma/voz> -s <velocidad> -p <tono> -w "salida.wav" "Texto"`.
  * Opción C (`piper` / `festival`): Motores TTS ligeros ampliamente distribuidos en repositorios Linux (`apt install speech-dispatcher`).
* **Detección de voz por defecto:**
  * Lectura de las variables de entorno `$LANG`, `$LC_MESSAGES` y la configuración de `~/.config/speech-dispatcher/speechd.conf`.

---

## 4. Estrategia de Reproducción de Audio Multiplataforma

`NAudio.Wave.WaveOut` será sustituido por una abstracción `IAudioPlayer` para evitar bloqueos en Unix:

```csharp
public interface IAudioPlayer : IDisposable
{
    bool IsPlaying { get; }
    void Play(string filePath, float volume);
    void Pause();
    void Resume();
    void Stop();
    void SetVolume(float volume);
}
```

### Opciones de Librerías de Audio Multiplataforma para .NET 10:
1. **Opción Recomendada:** `Silk.NET.OpenAL` o `ManagedBass` / `MiniAudioEx`:
   * **MiniAudioEx / ManagedBass:** Una sola biblioteca compilada estáticamente para Windows (`.dll`), Linux (`.so`) y macOS (`.dylib` tanto x64 como Apple Silicon ARM64).
   * Cero configuración del usuario: reproduce WAV, MP3 y flujos PCM directamente con control de volumen, pausa y eventos de fin de pista.
2. **Opción Alternativa:** Abstracción condicional:
   * En Windows: Continúa usando `NAudio`.
   * En Linux: Reproducción vía `libasound` (ALSA) / PulseAudio / PipeWire.
   * En macOS: Reproducción vía `CoreAudio` / `AudioQueue`.

---

## 5. Estrategia de Interfaz Gráfica: Migración a Avalonia UI

Para que la interfaz mantenga el diseño oscuro moderno, el resaltado emocional del texto, los tooltips y los botones de control en los 3 sistemas operativos sin tener que reescribir la UI desde cero:

### ¿Por qué Avalonia UI?
* **Mismo lenguaje visual (XAML):** Más del 85% del XAML de `MainWindow.xaml` se conserva directamente (Grid, StackPanel, Border, Button, ComboBox, Slider, ScrollViewer, ToolTip).
* **Compatibilidad idéntica con C# y MVVM:** Soporta `async/await`, `Dispatcher`, eventos de clic y enlaces de datos.
* **Componente de Texto:** Dispone de `SelectableTextBlock` o `RichTextBox` de Avalonia con soporte de formateo de fragmentos (`Inlines`, `Run`, color de fondo y fuente negrita para resaltar emociones).
* **Selector de Archivos:** Reemplaza `Microsoft.Win32.OpenFileDialog` por `TopLevel.GetTopLevel(this).StorageProvider.OpenFilePickerAsync(...)`, el cual despliega el explorador nativo en Windows (Explorer), macOS (Finder) y Linux (portal de archivos de escritorio XDG/GTK).

### Mapeo de Componentes XAML WPF ➔ Avalonia UI

| Elemento en WPF Actual | Equivalente en Avalonia UI | Notas |
|---|---|---|
| `<Window ...>` | `<Window ...>` | Misma sintaxis, soporta `ExtendClientAreaToDecorations`. |
| `<Border CornerRadius="8">` | `<Border CornerRadius="8">` | Idéntico. |
| `<RichTextBox x:Name="RtbInput">` | `<TextBox TextWrapping="Wrap" ...>` o `TextPresenter` | Soporta formateo enriquecido y selección. |
| `Run`, `LineBreak`, `Paragraph` | `Run`, `LineBreak` en `FormattedText` / `Inlines` | Mantiene el resaltado púrpura de segmentos. |
| `OpenFileDialog` / `SaveFileDialog` | `StorageProvider.OpenFilePickerAsync` | Nativo por sistema operativo. |
| `Dispatcher.InvokeAsync(...)` | `Dispatcher.UIThread.InvokeAsync(...)` | Actualización de interfaz en segundo plano. |

---

## 6. Plan de Ejecución Paso a Paso

### Fase 1: Desacoplamiento del Núcleo (`VnEmoteVoice.Core`)
1. Cambiar el Target Framework de `VnEmoteVoice.Core.csproj` de `net10.0-windows10.0.19041.0` a **`net10.0`** (multiplataforma puro).
2. Crear la interfaz `ISystemVoiceEngine` y extraer la implementación actual de Windows a `WindowsVoiceEngine.cs` (usando compilación condicional `#if WINDOWS` o carga dinámica por plataforma).
3. Implementar `MacVoiceEngine.cs` con el cliente de voz del comando `say`.
4. Implementar `LinuxVoiceEngine.cs` con el cliente `speech-dispatcher` / `espeak-ng`.
5. Crear la factoría `SystemVoiceEngineFactory.Create()` que instancie automáticamente el motor adecuado con `OperatingSystem.IsWindows()`, `OperatingSystem.IsMacOS()` o `OperatingSystem.IsLinux()`.
6. Reemplazar `NAudio.Wave.WaveOut` en `VoiceManager.cs` por una interfaz `IAudioPlayer` compatible con los 3 sistemas operativos.

### Fase 2: Creación del Proyecto de UI Multiplataforma (`VnEmoteVoice.Desktop`)
1. Crear el proyecto `VnEmoteVoice.Desktop` con la plantilla de Avalonia UI para .NET 10.
2. Portar los estilos y recursos del tema oscuro desde `MainWindow.xaml` a los estilos globales de Avalonia (`FluentTheme` con acento morado `#6d28d9`).
3. Portar los paneles de control:
   * Selector de Idioma y Voz del Sistema.
   * Panel de Carga Piper (archivos `.onnx` y `.json`).
   * Panel de Conexión a VoiceStudio API.
   * Sliders de Velocidad, Tono y Volumen.
   * Panel de Análisis Emocional (carga de diccionarios BCP-47 externos).
   * Botón de restauración `🔄` a valores por defecto del sistema.
4. Adaptar el visor de texto con resaltado activo de segmentos emocionales durante la reproducción.

### Fase 3: Pruebas y Validación por Plataforma
* **Windows (x64):**
  * Verificar voces locales de Windows y voces online.
  * Verificar Kokoro y Piper con directivas ONNX.
* **macOS (Intel x64 y Apple Silicon ARM64):**
  * Probar síntesis con `say` en español e inglés.
  * Probar ejecución nativa en procesadores M1/M2/M3/M4.
  * Verificar permisos de audio y sandbox de macOS.
* **Linux (Ubuntu / Fedora / Arch x64):**
  * Probar detección de `speech-dispatcher` y `espeak-ng`.
  * Verificar salida de sonido mediante ALSA / PulseAudio / PipeWire.

### Fase 4: Empaquetado y Distribución
* **Windows:** Ejecutable autónomo `.exe` o instalador MSIX.
* **macOS:** Paquete `.app` empaquetado en imagen de disco `.dmg`.
* **Linux:** Formato binario portable **AppImage** y paquete Flatpak / `.deb`.

---

## 7. Ventajas Clave del Plan

1. **Reutilización Total:** No se pierde ninguna de las mejoras recientes (análisis de 27 emociones, transiciones de color, diccionarios BCP-47 en JSON, selector de Piper externo o VoiceStudio).
2. **Voces Autóctonas de Alta Calidad:** Cada usuario disfrutará de las mejores voces que su sistema operativo ofrece de fábrica (las voces naturales de Windows en PC, las voces de Siri/macOS en Mac y la flexibilidad de speech-dispatcher en Linux).
3. **Mantenibilidad:** El código compartido reside en un único proyecto `VnEmoteVoice.Core` agnóstico, lo que facilita futuras actualizaciones y pruebas unitarias.
