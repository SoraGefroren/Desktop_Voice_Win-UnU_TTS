# Vn Emote Voice (TTS) 🎙️🎭
**Sistema de Síntesis de Voz Multiplataforma (TTS) y Análisis Emocional para Narradores Virtuales y Avatares VTuber**

[![Licencia: MIT](https://img.shields.io/badge/Licencia-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0%20(Cross--Platform)-512BD4.svg)](https://dotnet.microsoft.com/)
[![Sistemas](https://img.shields.io/badge/Sistemas-Windows%20%7C%20Linux%20%7C%20macOS-success.svg)](#-4-soporte-de-voces-nativas-por-sistema-operativo)
[![ONNX Runtime](https://img.shields.io/badge/ONNX%20Runtime-1.22.0-yellow.svg)](https://onnxruntime.ai/)

---

## 📌 1. Descripción y Propósito del Proyecto

**Vn Emote Voice** es un ecosistema de síntesis de voz y análisis afectivo desarrollado en **C# / .NET 10** concebido como el **motor de voz y gesticulación emocional** para la creación de **narradores virtuales, novelas visuales interactivas y avatares VTuber** (Live2D, VRoid, modelos 3D en Unity o Unreal Engine).

A diferencia de un sintetizador de voz tradicional que únicamente convierte texto plano en audio de forma monótona, **Vn Emote Voice**:
1. **Analiza el discurso oración por oración**, detectando la carga psicológica del texto mediante un motor léxico-contextual con 27 categorías emocionales.
2. **Interpreta los signos ortográficos** (`!`, `?`, `...`, MAYÚSCULAS) para modular el grado de intensidad (entonación *Exaltada*, *Gritando*, *Preguntando*, *Agitada*, *Quebrada*, etc.).
3. **Identifica transiciones y giros dramáticos** dentro de un mismo segmento (ejemplo: `Duda/Preguntando ➔ Furia/Gritando`).
4. **Arquitectura Multiplataforma con Voces Nativas:** Permite que cada sistema operativo use su propio motor local de voces nativas (Windows WinRT, macOS `say`, Linux `speech-dispatcher`), al tiempo que comparte motores neuronales de última generación (Kokoro v1.0, Piper ONNX y VoiceStudio API).
5. **Exporta paquetes de voz y metadatos JSON**, proporcionando a los sistemas de animación la información exacta para sincronizar expresiones faciales, cambios de pose y movimientos de cámara en tiempo real.

---

## ✨ 2. Características Principales

### 🧠 Análisis Emocional Contextual y Soporte Multilenguaje
- **27 categorías emocionales calibradas:** *Furia, Intriga, Miedo, Terror, Pánico, Burla, Lujuria, Ego, SobreConfianza, Soberbia, Confianza, Armonía, Exigencia, Duda, Cansancio, Plenitud, Irritación, Tristeza, Alegría, Sorpresa, Decepción, Esperanza, Ternura, Envidia, Vergüenza, Culpa y Gratitud*.
- **Modificadores de entonación por signos:** Calificación de emociones según la puntuación ortográfica.
- **Detección de puntos de pivote y transición:** Rastreo de conectores de contraste (`pero`, `sin embargo`, `de pronto`, etc.) para detectar saltos emocionales internos en oraciones compuestas.
- **Reglas de negación:** Anulación de falsos positivos léxicos (ej. *"no tengo miedo"* anula *Miedo* y promueve *Confianza*).
- **Modelos de lenguaje en JSON externos desacoplados:**
  - `emotions_es-mx.json`: Modelo calibrado para Español de México (cargado por defecto).
  - `emotions_es-es.json`: Modelo calibrado para Español de España (Castellano).
  - `emotions_en-gb.json`: Modelo calibrado para Inglés Británico.
  - Soporte para importar y validar en tiempo de ejecución cualquier archivo JSON con nuevos idiomas o personalizaciones.

### 🌐 Arquitectura Multi-Motor TTS Multiplataforma
- **Voces Nativas por Sistema Operativo (`ISystemVoiceEngine`):**
  - **🪟 Windows (`WindowsVoiceEngine`):** Acceso nativo a todas las voces locales y voces Online Natural instaladas en Windows 10/11 con soporte para SSML (WinRT `SpeechSynthesizer`).
  - **🍎 macOS (`MacVoiceEngine`):** Conexión nativa con el subsistema de voz `/usr/bin/say` de macOS, descubriendo voces de alta fidelidad instaladas (Siri, Mónica, Paulina, Samantha, etc.).
  - **🐧 Linux (`LinuxVoiceEngine`):** Conexión nativa con `speech-dispatcher` (`spd-say`) y `espeak-ng`.
- **Kokoro v1.0 (ONNX Local Multiplataforma):** Motor neuronal con audio a 24 kHz y catálogo multiidioma (Inglés, Español, Francés, Japonés, Chino, etc.).
- **Piper TTS (ONNX Local Multiplataforma):** Carga dinámica de modelos neuronales compactos mediante pares de archivos `.onnx` y `.json`.
- **VoiceStudio API (OmniVoice Remoto / Localhost):** Cliente de integración con servidores de difusión acústica VoiceStudio, permitiendo control total de refinamiento (`guidance_scale`, pasos de difusión `num_step`, `t_shift`, temperaturas de sonido, etc.).
- **Reproducción de Audio Agnóstica (`IAudioPlayer`):**
  - Windows: NAudio (`WaveOut` / `AudioFileReader`).
  - macOS: Reproductor nativo de alta precisión `/usr/bin/afplay`.
  - Linux: Reproductor de escritorio `paplay` (PulseAudio / PipeWire) o `aplay` (ALSA).

### 🎛️ Experiencia de Usuario y Controles Reactivos
- **Botón de Restauración Rápida (`🔄`):** Restaura en cualquier momento todos los controles del panel izquierdo a sus valores por defecto y re-selecciona el idioma y voz predeterminados del sistema operativo.
- **Editor enriquecido sincronizado:** El texto leído se resalta dinámicamente en tiempo real con scroll automático inteligente para que el segmento activo nunca salga de la vista.
- **Ajustes acústicos en caliente:** Modificación de Velocidad (Rate), Tono (Pitch) y Volumen durante la reproducción con adaptación suave sin necesidad de reiniciar el audio.
- **Interacción directa con el texto:** Clic en cualquier segmento del texto para saltar la reproducción y actuar el avatar de forma instantánea.

### 📦 Exportación Estructurada para Animación
- **Exportación de audio maestro (`.wav`):** Genera la pista continua completa.
- **Exportación segmentada con manifiesto JSON:** Guarda cada segmento de audio en un archivo `.wav` independiente y genera un archivo descriptivo `segments_info.json` con timestamps, emoción dominante, modificador, transición y emojis para alimentar motores de animación (VTuber / Unity / Blender / Unreal).

---

## 🏛️ 3. Arquitectura del Software

El proyecto sigue el **Principio de Inversión de Dependencias**, desacoplando la lógica de síntesis de las implementaciones específicas de cada plataforma:

```mermaid
graph TD
    UI[VnEmoteVoice UI / Presentación] -->|Orquesta Controles y Eventos| VM[VoiceManager]
    UI -->|Carga / Valida Modelos JSON| EA[EmotionAnalyzer]
    
    subgraph VnEmoteVoice.Core (Multiplataforma .NET 10)
        EA -->|Segmentación, Matices, Puntuación| TS[TextSegment / EmotionTransitionInfo]
        VM -->|ISystemVoiceEngine| SVE{SystemVoiceEngineFactory}
        VM -->|IAudioPlayer| AP[CrossPlatformAudioPlayer]
        VM -->|Inferencia ONNX| KOK[KokoroVoiceEngine]
        VM -->|Inferencia ONNX| PIP[PiperVoiceEngine]
        VM -->|Cliente HTTP REST| VSA[VoiceStudioApiEngine]
    end

    subgraph Motores Nativos por Plataforma
        SVE -->|Windows| WVE[WindowsVoiceEngine - WinRT]
        SVE -->|macOS| MVE[MacVoiceEngine - say CLI]
        SVE -->|Linux| LVE[LinuxVoiceEngine - spd-say / espeak-ng]
    end

    subgraph Reproducción de Sonido
        AP -->|Windows| NAU[NAudio WaveOut]
        AP -->|macOS| AFP[afplay Process]
        AP -->|Linux| PAP[paplay / aplay Process]
    end

    TS -->|Alimenta estado y colores| UI
    VM -->|Transfiere estado de reproducción| UI
```

### Componentes Principales:
- **`EmotionAnalyzer`:** Analizador de PLN 100% multiplataforma. Carga configuraciones JSON, aplica reglas léxicas, detección de pivotes y ponderación de emociones.
- **`VoiceManager`:** Fachada central de orquestación. Interactúa exclusivamente a través de interfaces (`ISystemVoiceEngine`, `IAudioPlayer`) haciéndolo compatible con cualquier sistema operativo.
- **`SystemVoiceEngineFactory`:** Fábrica que detecta el sistema operativo en tiempo de ejecución (`OperatingSystem.IsWindows()`, `OperatingSystem.IsMacOS()`, `OperatingSystem.IsLinux()`) y provee el motor nativo correspondiente.
- **`CrossPlatformAudioPlayer`:** Implementación de `IAudioPlayer` que conmuta entre `NAudio` (Windows), `afplay` (macOS) y `paplay`/`aplay` (Linux).
- **`KokoroVoiceEngine` & `PiperVoiceEngine`:** Motores neuronales ONNX compartidos idénticamente entre todos los sistemas operativos.
- **`VoiceStudioApiEngine`:** Cliente HTTP para la API de difusión de VoiceStudio.

---

## 💻 4. Soporte de Voces Nativas por Sistema Operativo

Cada sistema operativo cuenta con su propio ecosistema de síntesis y reproducción de sonido integrado:

### 🪟 Windows (`WindowsVoiceEngine`)
* **API Utilizada:** `Windows.Media.SpeechSynthesis.SpeechSynthesizer` (WinRT).
* **Características:**
  * Acceso automático a voces del sistema en español (*Microsoft Sabina*, *Microsoft Helena*) e inglés (*Microsoft David*, *Microsoft Zira*).
  * Soporte para voces *Natural Online* en Windows 11.
  * Modulación de prosodia mediante SSML (`<prosody rate="..." pitch="..." volume="...">`).
  * Reproducción de sonido gestionada por **NAudio** (`WaveOut`).

### 🍎 macOS (`MacVoiceEngine`)
* **API Utilizada:** Subsistema nativo `/usr/bin/say`.
* **Características:**
  * Detección dinámica de todas las voces instaladas en el sistema (`say -v "?"`), incluyendo voces de Siri y voces en español como *Paulina (es_MX)* o *Mónica (es_ES)*.
  * Generación directa a formato PCM WAV estándar mediante `--data-format=LEI16@22050`.
  * Detección de la voz predeterminada del usuario desde `com.apple.speech.voice.prefs`.
  * Reproducción nativa en terminal y segundo plano mediante `/usr/bin/afplay`.

### 🐧 Linux (`LinuxVoiceEngine`)
* **API Utilizada:** `speech-dispatcher` (`spd-say`) y `espeak-ng`.
* **Características:**
  * Compatible con el estándar de accesibilidad de entornos de escritorio Linux (GNOME, KDE, XFCE).
  * Detección automática de variantes lingüísticas mediante `espeak-ng --voices` o `spd-say -L`.
  * Generación directa de archivos WAV con control de palabras por minuto (`-s`) y tono (`-p`).
  * Reproducción de audio mediante `paplay` (PulseAudio / PipeWire) o `aplay` (ALSA).
  * Instalación de dependencias en distribuciones Debian/Ubuntu:
    ```bash
    sudo apt update && sudo apt install speech-dispatcher espeak-ng pulseaudio-utils
    ```

---

## 📖 5. Guía de Uso del Sistema

### 1. Selección de Voz y Motor
En el panel izquierdo, despliega la lista **Voz** o marca la casilla **Modelo externo**:
* **Voces del Sistema:** Lista las voces instaladas localmente en tu sistema operativo (Windows, macOS o Linux) y los modelos integrados de **Kokoro**.
* **Pipper (Externo):** Pulsa el botón de búsqueda para cargar un modelo `.onnx` y su archivo de configuración `.json`.
* **VoiceStudio Api (Externo):** Introduce la URL de tu instancia (por defecto `http://localhost:3900`), pulsa *Cargar*, selecciona el perfil deseado y despliega los *Ajustes de VoiceStudio* para controlar la influencia de indicación, pasos de difusión y filtros acústicos.

### 2. Selección del Modelo Emocional
En la sección **🧠 Análisis de Emociones**:
* **Usar modelo en Español (por defecto):** Mantiene activo el modelo calibrado `emotions_es-mx.json`.
* **Cargar archivo personalizado:** Desmarca la casilla para habilitar el selector de archivos y carga cualquier archivo JSON de emociones (por ejemplo, `__Assets/emotions_es-es.json` o `__Assets/emotions_en-gb.json`). El sistema validará su formato, identificador de idioma y número de emociones de forma inmediata.

### 3. Reproducción y Ajustes en Tiempo Real
* Pulsa **▶ Reproducir** para sintetizar y escuchar el texto.
* Los deslizadores de **Velocidad**, **Tono** y **Volumen** aplicarán cambios inmediatamente a la reproducción en curso.
* Puedes pausar con **⏸** o detener con **⏹**.
* Haz clic directamente en cualquier palabra o segmento del texto para saltar la reproducción a ese fragmento de forma instantánea.

### 4. Botón de Restauración Rápida (`🔄`)
* Ubicado en la barra inferior del panel de controles junto a los botones de descarga.
* Al pulsarlo en cualquier momento, restablece todos los sliders a sus valores estándar (Velocidad 1.0x, Tono 1.0, Volumen 100%), limpia modelos externos y **vuelve a seleccionar automáticamente el idioma y voz predeterminados de tu sistema operativo**.

### 5. Exportación para Animación y Avatares
* **📥 Descargar Audio Completo:** Genera un archivo `.wav` continuo con la narración de todo el texto.
* **🧩 Descargar por Segmentos (WAVs + JSON):** Solicita una carpeta de destino y genera:
  * `segment_001_Emocion.wav`, `segment_002_Emocion.wav`, etc.
  * `segments_info.json`: Lista detallada de cada fragmento con su texto, emoción dominante calificada, emoji, desglose de sub-emociones y metadatos de transición emocional para sincronizar expresiones faciales y cámaras en tu avatar (Live2D / VRoid / Unity / Unreal).

---

## 🎨 6. Estrategia de Interfaz Gráfica Multiplataforma (Avalonia UI)

El motor central **`VnEmoteVoice.Core`** es 100% multiplataforma (.NET 10). Para ejecutar la interfaz de escritorio de manera idéntica en Linux y macOS:

### Correspondencia de Componentes (WPF ➔ Avalonia UI)
| Elemento en WPF Actual | Equivalente en Avalonia UI | Comportamiento Multiplataforma |
|---|---|---|
| `<Window ...>` | `<Window ...>` | Misma sintaxis XAML con soporte para modo oscuro. |
| `<Border CornerRadius="8">` | `<Border CornerRadius="8">` | Idéntico. |
| `<RichTextBox x:Name="RtbInput">` | `<SelectableTextBlock>` o `<TextBox>` | Resaltado de texto mediante `Inlines` y `Run`. |
| `Run`, `LineBreak`, `Paragraph` | `Run`, `LineBreak` en `Inlines` | Mantiene el resaltado púrpura de segmentos activos. |
| `OpenFileDialog` / `SaveFileDialog` | `StorageProvider.OpenFilePickerAsync` | Despliega el explorador nativo (Explorer, Finder o portal GTK). |
| `Dispatcher.InvokeAsync(...)` | `Dispatcher.UIThread.InvokeAsync(...)` | Actualizaciones reactivas de UI sin bloquear el hilo de audio. |

---

## ⚖️ 7. Evaluación de la Licencia y Compatibilidad Legal

El proyecto está licenciado bajo la **Licencia MIT** ([LICENSE](LICENSE)).

| Componente / Dependencia | Licencia del Tercero | Compatibilidad con MIT | Justificación |
|---|---|---|---|
| **VnEmoteVoice (Código propio)** | **MIT License** | ✅ Nativa | Permisiva, permite uso comercial, modificación y distribución privada o pública. |
| **Microsoft.ML.OnnxRuntime** | MIT License | ✅ Total | Licencia idéntica y compatible en Windows, Linux y Mac. |
| **NAudio** | MIT License | ✅ Total | Utilizado en Windows. |
| **MisakiSharp** | MIT / Apache 2.0 | ✅ Total | Permisiva y compatible con código host bajo MIT. |
| **Voces Nativas (WinRT / say / spd-say)** | Nativas del Sistema | ✅ Total | Se invocan las herramientas y APIs oficiales de cada sistema sin redistribuir binarios protegidos. |
| **Kokoro TTS (v1.0 Weights)** | Apache 2.0 | ✅ Compatible | Permite el uso en proyectos MIT respetando la atribución original de los pesos. |
| **Piper TTS (Rhasspy)** | MIT License | ✅ Total | Código MIT; modelos externos bajo licencias abiertas (MIT, CC0, CC-BY). |
| **VoiceStudio (OmniVoice)** | Cliente de API independiente | ✅ Total | Consumo vía servicio HTTP externo sin contaminación de licencias. |

---

## 📋 8. Requisitos del Sistema

- **Sistemas Operativos Compatibles:**
  - **Windows:** Windows 10 (versión 2004 o superior) o Windows 11 (64 bits).
  - **macOS:** macOS 11 Big Sur o superior (Intel x64 y Apple Silicon ARM64 M1/M2/M3/M4).
  - **Linux:** Ubuntu 22.04+, Fedora 38+, Arch Linux o cualquier distribución con `speech-dispatcher` o `espeak-ng`.
- **Entorno de Ejecución:** [.NET 10.0 SDK / Runtime](https://dotnet.microsoft.com/).
- **Hardware Recomendado:**
  - CPU de 4 núcleos o superior.
  - 8 GB de memoria RAM (mínimo 4 GB).
  - Dispositivo de salida de audio estándar.

---

## 🚀 9. Compilación y Ejecución

### 1. Clonar el repositorio
```bash
git clone https://github.com/tu-usuario/Desktop_Voice_Win-UnU_TTS.git
cd Desktop_Voice_Win-UnU_TTS
```

### 2. Compilar el proyecto
```bash
dotnet build --configuration Release
```

### 3. Ejecutar la aplicación
```bash
dotnet run --project VnEmoteVoice.Console/VnEmoteVoice.Console.csproj
```

---

## 📄 10. Licencia y Créditos

Este proyecto se distribuye bajo la **Licencia MIT**. Consulta el archivo [LICENSE](LICENSE) para más detalles.

### Créditos y Agradecimientos:
- **Kokoro-82M:** Modelo TTS desarrollado por [hexgrad](https://huggingface.co/hexgrad/Kokoro-82M) (Licencia Apache 2.0).
- **Piper TTS:** Sistema neuronal desarrollado por [Rhasspy](https://github.com/rhasspy/piper) (Licencia MIT).
- **NAudio:** Biblioteca de procesamiento de audio en .NET creada por Mark Heath (Licencia MIT).
- **Microsoft ONNX Runtime:** Motor de inferencia de alto rendimiento de Microsoft (Licencia MIT).
- **MisakiSharp:** Motor de tokenización fonética G2P para C# (Licencia MIT).
