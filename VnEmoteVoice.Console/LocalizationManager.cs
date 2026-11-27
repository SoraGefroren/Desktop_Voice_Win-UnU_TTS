using System;
using System.Collections.Generic;
using System.Globalization;

namespace VnEmoteVoice.Console
{
    /// <summary>
    /// Administrador central de localización e internacionalización (i18n) para la interfaz de usuario.
    /// Soporta cambio dinámico de idioma en tiempo de ejecución entre Español (es), Inglés (en) y Chino (zh).
    /// Separa completamente el idioma de la interfaz del idioma de síntesis de voz del motor TTS.
    /// </summary>
    public static class LocalizationManager
    {
        public static event Action? LanguageChanged;

        private static string _currentLanguage = "es";

        public static string CurrentLanguage
        {
            get => _currentLanguage;
            private set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    LanguageChanged?.Invoke();
                }
            }
        }

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase)
        {
            // ==========================================
            // ESPAÑOL (es)
            // ==========================================
            ["es"] = new Dictionary<string, string>
            {
                // Menú Principal
                ["Menu_Language"] = "_Idioma",
                ["Menu_Language_Es"] = "🇪🇸 Español",
                ["Menu_Language_En"] = "🇬🇧 English (Inglés)",
                ["Menu_Language_Zh"] = "🇨🇳 中文 (Chino)",
                ["Menu_Language_Ru"] = "🇷🇺 Русский (Ruso)",

                ["Menu_Edit"] = "_Edición",
                ["Menu_Edit_Find"] = "_Buscar...",
                ["Menu_Edit_FindNext"] = "Buscar _siguiente",
                ["Menu_Edit_FindPrev"] = "Buscar _anterior",
                ["Menu_Edit_Replace"] = "_Reemplazar...",

                ["Menu_Help"] = "A_yuda",
                ["Menu_Help_About"] = "_Acerca de Vn Emote Voice...",

                // Barra Superior (Header)
                ["Header_AppTitle"] = "Vn Emote Voice (TTS)",
                ["Header_AvatarTag"] = "AVATAR:",
                ["Header_TransitionBadge"] = "TRANSICIÓN",
                ["Header_TransitionBadge_Tooltip"] = "Transición emocional detectada en este segmento de diálogo",
                ["Header_Emotion_Neutral"] = "Neutral",
                ["Header_BtnPlay_Tooltip"] = "Reproducir síntesis de voz (F5)",
                ["Header_BtnPause_Tooltip"] = "Pausar reproducción",
                ["Header_BtnStop_Tooltip"] = "Detener reproducción y reiniciar posición",

                // Panel Izquierdo: Selectores
                ["Panel_LanguageTitle"] = "🌐 Idioma",
                ["Panel_ModelSelectorTitle"] = "🧠 Selector de Modelos",
                ["Panel_ExternalModel"] = "Modelo externo",
                ["Panel_VoiceTitle"] = "🎙 Voz",

                // Panel Piper Externo
                ["Piper_PanelTitle"] = "📂 Modelo Piper (Externo)",
                ["Piper_OnnxTitle"] = "Archivo .onnx",
                ["Piper_JsonTitle"] = "Archivo .json",
                ["Piper_Browse"] = "Buscar",
                ["Piper_NoFile"] = "Sin archivo seleccionado",

                // Panel VoiceStudio API Externo
                ["VoiceStudio_PanelTitle"] = "🌐 VoiceStudio API (Externo)",
                ["VoiceStudio_UrlTitle"] = "Enlace del API",
                ["VoiceStudio_Connect"] = "Cargar",
                ["VoiceStudio_ProfileTitle"] = "Perfil disponible",
                ["VoiceStudio_Status_Disconnected"] = "No conectado",
                ["VoiceStudio_Status_Connecting"] = "Conectando al API...",
                ["VoiceStudio_Status_Connected"] = "Conectado — {0} perfiles disponibles",
                ["VoiceStudio_Status_Error"] = "Error al conectar con VoiceStudio",
                ["VoiceStudio_Settings_Expander"] = "⚙ Ajustes de VoiceStudio",
                ["VoiceStudio_GuidanceScale"] = "Influencia de la indicación",
                ["VoiceStudio_TShift"] = "Momento del refinamiento",
                ["VoiceStudio_PosTemp"] = "Variación del orden de muestreo",
                ["VoiceStudio_ClassTemp"] = "Variación del sonido",
                ["VoiceStudio_LayerPenalty"] = "Equilibrio entre grueso y fino",
                ["VoiceStudio_NumStep"] = "Refinamiento de voz (pasos)",
                ["VoiceStudio_BalanceVolume"] = "Equilibrar volumen",
                ["VoiceStudio_Denoise"] = "Limpiar ruido de referencia",
                ["VoiceStudio_Postprocess"] = "Pulir el audio generado",

                // Sliders de Voz
                ["Slider_SpeedTitle"] = "⚡ Velocidad",
                ["Slider_PitchTitle"] = "🎵 Tono",
                ["Slider_VolumeTitle"] = "🔊 Volumen",

                // Análisis de Emociones
                ["Emotion_PanelTitle"] = "🧠 Análisis de Emociones",
                ["Emotion_UseDefaultModel"] = "Usar modelo en Español (por defecto)",
                ["Emotion_CustomJsonTitle"] = "📂 Archivo JSON de emociones:",
                ["Emotion_NoFile"] = "Ningún archivo seleccionado",
                ["Emotion_Load"] = "Cargar",

                // Botones Inferiores y Tooltips
                ["Btn_Reset_Tooltip"] = "Restaurar controles y configuración por defecto del sistema",
                ["Btn_DownloadAudio_Tooltip"] = "Descargar audio completo en archivo WAV",
                ["Btn_SegmentedDownload_Tooltip"] = "Descarga segmentada por frases con metadatos JSON para Narrador Virtual",

                // Editor de Texto
                ["Editor_CharCount"] = "{0} caracteres",
                ["Editor_WelcomeText"] = "Hola. ¡Bienvenido al sistema de conversión de texto a voz! Aquí puedes escribir cualquier texto y convertirlo en audio usando diferentes voces e idiomas disponibles. También, puedes ajustar la velocidad, el tono y el volumen de la voz para personalizar tu experiencia.\n\n¡Escribe algo y pruébalo!",

                // Diálogos y Alertas (MessageBox)
                ["Msg_InputEmpty"] = "Por favor, escribe algún texto para generar el audio.",
                ["Msg_WarningTitle"] = "Atención",
                ["Msg_ErrorTitle"] = "Error",
                ["Msg_SuccessTitle"] = "Éxito",
                ["Msg_DownloadSuccess"] = "Audio exportado exitosamente:\n{0}",
                ["Msg_DownloadError"] = "Error al exportar audio: {0}",
                ["Msg_SegmentedExportSuccess"] = "Exportación completada:\n• {0} archivos WAV\n• 1 archivo JSON con emociones\n\nCarpeta: {1}",
                ["Msg_SegmentedExportTitle"] = "Descarga segmentada exitosa",
                ["Msg_SaveAudioFilter"] = "Archivo de audio WAV (*.wav)|*.wav",
                ["Msg_SelectFolderTitle"] = "Selecciona la carpeta donde guardar los segmentos y el archivo JSON",

                // Ventana Buscar y Reemplazar
                ["FindReplace_Title_Find"] = "Buscar",
                ["FindReplace_Title_Replace"] = "Reemplazar",
                ["FindReplace_LblFind"] = "Buscar:",
                ["FindReplace_LblReplace"] = "Reemplazar por:",
                ["FindReplace_ChkMatchCase"] = "Coincidir mayúsculas y minúsculas",
                ["FindReplace_ChkWholeWord"] = "Solo palabras completas",
                ["FindReplace_LblDirection"] = "Dirección:",
                ["FindReplace_RbDown"] = "Abajo",
                ["FindReplace_RbUp"] = "Arriba",
                ["FindReplace_BtnFindNext"] = "Buscar siguiente",
                ["FindReplace_BtnFindPrev"] = "Buscar anterior",
                ["FindReplace_BtnReplace"] = "Reemplazar",
                ["FindReplace_BtnReplaceAll"] = "Reemplazar todo",
                ["FindReplace_BtnClose"] = "Cerrar",
                ["FindReplace_Status_EnterText"] = "Ingresa el texto a buscar.",
                ["FindReplace_Status_NotFound"] = "No se encuentra \"{0}\".",
                ["FindReplace_Status_Found"] = "Coincidencia encontrada.",
                ["FindReplace_Status_Replaced"] = "Texto reemplazado.",
                ["FindReplace_Status_ReplacedAll"] = "Se reemplazaron {0} coincidencia(s).",
                ["FindReplace_Status_NoMatchesToReplace"] = "No se encontraron coincidencias para \"{0}\".",

                // Ventana Acerca de
                ["About_Title"] = "Acerca de Vn Emote Voice",
                ["About_AppTitle"] = "Vn Emote Voice (TTS)",
                ["About_Version"] = "Versión 1.0 — Multiplataforma (Windows | Linux | macOS)",
                ["About_AuthorLabel"] = "Autor:",
                ["About_DescTitle"] = "Descripción del Sistema:",
                ["About_DescParagraph1"] = "Vn Emote Voice es un sistema avanzado de Síntesis de Voz (TTS) y Análisis Emocional diseñado para la creación de narradores virtuales, novelas visuales interactivas y avatares VTuber (Live2D, VRoid, 3D).",
                ["About_DescParagraph2"] = "Integra motores de voz locales nativos por sistema operativo (Windows WinRT, macOS say, Linux speech-dispatcher), modelos neuronales locales ONNX de última generación (Kokoro v1.0 y Piper), VoiceStudio API, y un motor lingüístico con 27 emociones calibradas y detección de giros dramáticos en tiempo real.",
                ["About_License"] = "Licencia MIT — Código Abierto",
                ["About_BtnClose"] = "Aceptar"
            },

            // ==========================================
            // INGLÉS (en)
            // ==========================================
            ["en"] = new Dictionary<string, string>
            {
                // Main Menu
                ["Menu_Language"] = "_Language",
                ["Menu_Language_Es"] = "🇪🇸 Spanish (Español)",
                ["Menu_Language_En"] = "🇬🇧 English",
                ["Menu_Language_Zh"] = "🇨🇳 Chinese (中文)",
                ["Menu_Language_Ru"] = "🇷🇺 Russian (Русский)",

                ["Menu_Edit"] = "_Edit",
                ["Menu_Edit_Find"] = "_Find...",
                ["Menu_Edit_FindNext"] = "Find _Next",
                ["Menu_Edit_FindPrev"] = "Find _Previous",
                ["Menu_Edit_Replace"] = "_Replace...",

                ["Menu_Help"] = "_Help",
                ["Menu_Help_About"] = "_About Vn Emote Voice...",

                // Header Bar
                ["Header_AppTitle"] = "Vn Emote Voice (TTS)",
                ["Header_AvatarTag"] = "AVATAR:",
                ["Header_TransitionBadge"] = "TRANSITION",
                ["Header_TransitionBadge_Tooltip"] = "Emotional transition detected in this dialogue segment",
                ["Header_Emotion_Neutral"] = "Neutral",
                ["Header_BtnPlay_Tooltip"] = "Play speech synthesis (F5)",
                ["Header_BtnPause_Tooltip"] = "Pause playback",
                ["Header_BtnStop_Tooltip"] = "Stop playback and reset position",

                // Left Panel: Selectors
                ["Panel_LanguageTitle"] = "🌐 Language",
                ["Panel_ModelSelectorTitle"] = "🧠 Model Selector",
                ["Panel_ExternalModel"] = "External model",
                ["Panel_VoiceTitle"] = "🎙 Voice",

                // Piper External Panel
                ["Piper_PanelTitle"] = "📂 Piper Model (External)",
                ["Piper_OnnxTitle"] = ".onnx File",
                ["Piper_JsonTitle"] = ".json File",
                ["Piper_Browse"] = "Browse",
                ["Piper_NoFile"] = "No file selected",

                // VoiceStudio API External Panel
                ["VoiceStudio_PanelTitle"] = "🌐 VoiceStudio API (External)",
                ["VoiceStudio_UrlTitle"] = "API Endpoint",
                ["VoiceStudio_Connect"] = "Load",
                ["VoiceStudio_ProfileTitle"] = "Available Profile",
                ["VoiceStudio_Status_Disconnected"] = "Not connected",
                ["VoiceStudio_Status_Connecting"] = "Connecting to API...",
                ["VoiceStudio_Status_Connected"] = "Connected — {0} profile(s) available",
                ["VoiceStudio_Status_Error"] = "Failed to connect to VoiceStudio",
                ["VoiceStudio_Settings_Expander"] = "⚙ VoiceStudio Settings",
                ["VoiceStudio_GuidanceScale"] = "Guidance Scale",
                ["VoiceStudio_TShift"] = "T-Shift",
                ["VoiceStudio_PosTemp"] = "Position Temperature",
                ["VoiceStudio_ClassTemp"] = "Class Temperature",
                ["VoiceStudio_LayerPenalty"] = "Layer Penalty Factor",
                ["VoiceStudio_NumStep"] = "Voice Refinement Steps",
                ["VoiceStudio_BalanceVolume"] = "Balance volume",
                ["VoiceStudio_Denoise"] = "Denoise reference audio",
                ["VoiceStudio_Postprocess"] = "Postprocess generated audio",

                // Voice Sliders
                ["Slider_SpeedTitle"] = "⚡ Speed",
                ["Slider_PitchTitle"] = "🎵 Pitch",
                ["Slider_VolumeTitle"] = "🔊 Volume",

                // Emotion Analysis
                ["Emotion_PanelTitle"] = "🧠 Emotion Analysis",
                ["Emotion_UseDefaultModel"] = "Use Spanish model (default)",
                ["Emotion_CustomJsonTitle"] = "📂 Emotion JSON File:",
                ["Emotion_NoFile"] = "No file selected",
                ["Emotion_Load"] = "Load",

                // Bottom Buttons & Tooltips
                ["Btn_Reset_Tooltip"] = "Reset controls and system defaults",
                ["Btn_DownloadAudio_Tooltip"] = "Download full audio as WAV file",
                ["Btn_SegmentedDownload_Tooltip"] = "Segmented sentence download with JSON metadata for Virtual Narrator",

                // Text Editor
                ["Editor_CharCount"] = "{0} characters",
                ["Editor_WelcomeText"] = "Hello! Welcome to the text-to-speech conversion system! Here you can write any text and turn it into audio using different available voices and languages. You can also adjust speed, pitch, and volume to customize your experience.\n\nType something and give it a try!",

                // Dialogs and Alerts (MessageBox)
                ["Msg_InputEmpty"] = "Please enter some text to generate audio.",
                ["Msg_WarningTitle"] = "Notice",
                ["Msg_ErrorTitle"] = "Error",
                ["Msg_SuccessTitle"] = "Success",
                ["Msg_DownloadSuccess"] = "Audio exported successfully:\n{0}",
                ["Msg_DownloadError"] = "Error exporting audio: {0}",
                ["Msg_SegmentedExportSuccess"] = "Export completed:\n• {0} WAV files\n• 1 JSON file with emotions\n\nFolder: {1}",
                ["Msg_SegmentedExportTitle"] = "Segmented Download Succeeded",
                ["Msg_SaveAudioFilter"] = "WAV Audio File (*.wav)|*.wav",
                ["Msg_SelectFolderTitle"] = "Select the folder to save segments and the JSON file",

                // Find and Replace Window
                ["FindReplace_Title_Find"] = "Find",
                ["FindReplace_Title_Replace"] = "Replace",
                ["FindReplace_LblFind"] = "Find:",
                ["FindReplace_LblReplace"] = "Replace with:",
                ["FindReplace_ChkMatchCase"] = "Match case",
                ["FindReplace_ChkWholeWord"] = "Match whole word",
                ["FindReplace_LblDirection"] = "Direction:",
                ["FindReplace_RbDown"] = "Down",
                ["FindReplace_RbUp"] = "Up",
                ["FindReplace_BtnFindNext"] = "Find Next",
                ["FindReplace_BtnFindPrev"] = "Find Previous",
                ["FindReplace_BtnReplace"] = "Replace",
                ["FindReplace_BtnReplaceAll"] = "Replace All",
                ["FindReplace_BtnClose"] = "Close",
                ["FindReplace_Status_EnterText"] = "Enter text to search.",
                ["FindReplace_Status_NotFound"] = "Cannot find \"{0}\".",
                ["FindReplace_Status_Found"] = "Match found.",
                ["FindReplace_Status_Replaced"] = "Text replaced.",
                ["FindReplace_Status_ReplacedAll"] = "Replaced {0} occurrence(s).",
                ["FindReplace_Status_NoMatchesToReplace"] = "No occurrences found for \"{0}\".",

                // About Window
                ["About_Title"] = "About Vn Emote Voice",
                ["About_AppTitle"] = "Vn Emote Voice (TTS)",
                ["About_Version"] = "Version 1.0 — Multiplatform (Windows | Linux | macOS)",
                ["About_AuthorLabel"] = "Author:",
                ["About_DescTitle"] = "System Description:",
                ["About_DescParagraph1"] = "Vn Emote Voice is an advanced Text-to-Speech (TTS) and Emotional Analysis system designed for creating virtual narrators, interactive visual novels, and VTuber avatars (Live2D, VRoid, 3D).",
                ["About_DescParagraph2"] = "It integrates native OS voice engines (Windows WinRT, macOS say, Linux speech-dispatcher), next-gen ONNX local neural models (Kokoro v1.0 and Piper), VoiceStudio API, and a linguistic engine with 27 calibrated emotions and real-time dramatic twist detection.",
                ["About_License"] = "MIT License — Open Source",
                ["About_BtnClose"] = "OK"
            },

            // ==========================================
            // CHINO (zh)
            // ==========================================
            ["zh"] = new Dictionary<string, string>
            {
                // 主菜单 (Main Menu)
                ["Menu_Language"] = "语言(_L)",
                ["Menu_Language_Es"] = "🇪🇸 西班牙语 (Español)",
                ["Menu_Language_En"] = "🇬🇧 英语 (English)",
                ["Menu_Language_Zh"] = "🇨🇳 中文",
                ["Menu_Language_Ru"] = "🇷🇺 俄语 (Русский)",

                ["Menu_Edit"] = "编辑(_E)",
                ["Menu_Edit_Find"] = "查找(_F)...",
                ["Menu_Edit_FindNext"] = "查找下一个",
                ["Menu_Edit_FindPrev"] = "查找上一个",
                ["Menu_Edit_Replace"] = "替换(_R)...",

                ["Menu_Help"] = "帮助(_H)",
                ["Menu_Help_About"] = "关于 Vn Emote Voice(_A)...",

                // 顶部状态栏 (Header Bar)
                ["Header_AppTitle"] = "Vn Emote Voice (TTS 语音系统)",
                ["Header_AvatarTag"] = "角色状态:",
                ["Header_TransitionBadge"] = "情绪转折",
                ["Header_TransitionBadge_Tooltip"] = "在此段对话中检测到情绪转折",
                ["Header_Emotion_Neutral"] = "中立",
                ["Header_BtnPlay_Tooltip"] = "开始语音合成播放 (F5)",
                ["Header_BtnPause_Tooltip"] = "暂停播放",
                ["Header_BtnStop_Tooltip"] = "停止播放并重置位置",

                // 左侧面板：选择器
                ["Panel_LanguageTitle"] = "🌐 语音语言",
                ["Panel_ModelSelectorTitle"] = "🧠 模型选择器",
                ["Panel_ExternalModel"] = "外部模型",
                ["Panel_VoiceTitle"] = "🎙 发音人/音色",

                // Piper 外部面板
                ["Piper_PanelTitle"] = "📂 Piper 模型 (外部)",
                ["Piper_OnnxTitle"] = ".onnx 模型文件",
                ["Piper_JsonTitle"] = ".json 配置文件",
                ["Piper_Browse"] = "浏览",
                ["Piper_NoFile"] = "未选择任何文件",

                // VoiceStudio API 面板
                ["VoiceStudio_PanelTitle"] = "🌐 VoiceStudio API (外部服务)",
                ["VoiceStudio_UrlTitle"] = "API 接口地址",
                ["VoiceStudio_Connect"] = "加载",
                ["VoiceStudio_ProfileTitle"] = "可用音色配置",
                ["VoiceStudio_Status_Disconnected"] = "未连接",
                ["VoiceStudio_Status_Connecting"] = "正在连接 API 服务...",
                ["VoiceStudio_Status_Connected"] = "已成功连接 — 可用配置 {0} 个",
                ["VoiceStudio_Status_Error"] = "连接 VoiceStudio 服务失败",
                ["VoiceStudio_Settings_Expander"] = "⚙ VoiceStudio 高级参数",
                ["VoiceStudio_GuidanceScale"] = "引导系数 (Guidance Scale)",
                ["VoiceStudio_TShift"] = "细化时间 (T-Shift)",
                ["VoiceStudio_PosTemp"] = "位置采样温度",
                ["VoiceStudio_ClassTemp"] = "音色变化温度",
                ["VoiceStudio_LayerPenalty"] = "分层惩罚因子",
                ["VoiceStudio_NumStep"] = "降噪细化步数",
                ["VoiceStudio_BalanceVolume"] = "音量平衡",
                ["VoiceStudio_Denoise"] = "参考音频去噪",
                ["VoiceStudio_Postprocess"] = "音频后处理优化",

                // 语音滑块
                ["Slider_SpeedTitle"] = "⚡ 语速",
                ["Slider_PitchTitle"] = "🎵 声调",
                ["Slider_VolumeTitle"] = "🔊 音量",

                // 情绪分析
                ["Emotion_PanelTitle"] = "🧠 文本情绪分析",
                ["Emotion_UseDefaultModel"] = "使用默认情绪模型",
                ["Emotion_CustomJsonTitle"] = "📂 情绪 JSON 配置文件:",
                ["Emotion_NoFile"] = "未选择文件",
                ["Emotion_Load"] = "载入",

                // 底部按钮与提示
                ["Btn_Reset_Tooltip"] = "重置所有控制选项为系统初始默认值",
                ["Btn_DownloadAudio_Tooltip"] = "导出完整语音为 WAV 音频文件",
                ["Btn_SegmentedDownload_Tooltip"] = "按句子分段导出 WAV 音频及虚拟解说 JSON 元数据",

                // 文本编辑器
                ["Editor_CharCount"] = "{0} 个字符",
                ["Editor_WelcomeText"] = "您好！欢迎使用智能文本转语音（TTS）与情绪表达系统！您可以在此处键入任意文字，并使用多种发音人和语言进行语音合成。您还可以自由调节语速、音调和音量，定制属于您的虚拟角色解说。\n\n立即输入文字开始体验吧！",

                // 对话框与提示信息 (MessageBox)
                ["Msg_InputEmpty"] = "请输入需要合成语音的文本内容。",
                ["Msg_WarningTitle"] = "提示",
                ["Msg_ErrorTitle"] = "错误",
                ["Msg_SuccessTitle"] = "成功",
                ["Msg_DownloadSuccess"] = "音频导出成功：\n{0}",
                ["Msg_DownloadError"] = "音频导出失败：{0}",
                ["Msg_SegmentedExportSuccess"] = "分段导出完成：\n• {0} 个 WAV 音频文件\n• 1 个情绪元数据 JSON 文件\n\n保存路径：{1}",
                ["Msg_SegmentedExportTitle"] = "分段导出成功",
                ["Msg_SaveAudioFilter"] = "WAV 音频文件 (*.wav)|*.wav",
                ["Msg_SelectFolderTitle"] = "请选择保存分段音频和 JSON 文件的文件夹",

                // 查找与替换窗口
                ["FindReplace_Title_Find"] = "查找",
                ["FindReplace_Title_Replace"] = "替换",
                ["FindReplace_LblFind"] = "查找内容:",
                ["FindReplace_LblReplace"] = "替换为:",
                ["FindReplace_ChkMatchCase"] = "区分大小写",
                ["FindReplace_ChkWholeWord"] = "全字匹配",
                ["FindReplace_LblDirection"] = "方向:",
                ["FindReplace_RbDown"] = "向下",
                ["FindReplace_RbUp"] = "向上",
                ["FindReplace_BtnFindNext"] = "查找下一个",
                ["FindReplace_BtnFindPrev"] = "查找上一个",
                ["FindReplace_BtnReplace"] = "替换",
                ["FindReplace_BtnReplaceAll"] = "全部替换",
                ["FindReplace_BtnClose"] = "关闭",
                ["FindReplace_Status_EnterText"] = "请输入要查找的内容。",
                ["FindReplace_Status_NotFound"] = "找不到 \"{0}\"。",
                ["FindReplace_Status_Found"] = "已找到匹配项。",
                ["FindReplace_Status_Replaced"] = "已完成替换。",
                ["FindReplace_Status_ReplacedAll"] = "已完成 {0} 处替换。",
                ["FindReplace_Status_NoMatchesToReplace"] = "未找到关于 \"{0}\" 的任何匹配项。",

                // 关于窗口
                ["About_Title"] = "关于 Vn Emote Voice",
                ["About_AppTitle"] = "Vn Emote Voice (TTS 语音系统)",
                ["About_Version"] = "版本 1.0 — 跨平台支持 (Windows | Linux | macOS)",
                ["About_AuthorLabel"] = "作者:",
                ["About_DescTitle"] = "系统简介:",
                ["About_DescParagraph1"] = "Vn Emote Voice 是一套专为虚拟主播解说、视觉小说与 VTuber 虚拟角色（Live2D、VRoid、3D）打造的高级语音合成（TTS）与情绪感知分析系统。",
                ["About_DescParagraph2"] = "系统深度集成了操作系统原生语音引擎（Windows WinRT、macOS say、Linux speech-dispatcher）、新一代本地神经 ONNX 模型（Kokoro v1.0 与 Piper）、VoiceStudio API 服务，以及具备 27 种校准情绪与实时戏剧转折检测的语义分析引擎。",
                ["About_License"] = "MIT 开源协议",
                ["About_BtnClose"] = "确定"
            },

            // ==========================================
            // RUSO (ru)
            // ==========================================
            ["ru"] = new Dictionary<string, string>
            {
                // Главное меню
                ["Menu_Language"] = "_Язык",
                ["Menu_Language_Es"] = "🇪🇸 Испанский (Español)",
                ["Menu_Language_En"] = "🇬🇧 Английский (English)",
                ["Menu_Language_Zh"] = "🇨🇳 Китайский (中文)",
                ["Menu_Language_Ru"] = "🇷🇺 Русский",

                ["Menu_Edit"] = "_Правка",
                ["Menu_Edit_Find"] = "_Найти...",
                ["Menu_Edit_FindNext"] = "Найти _следующее",
                ["Menu_Edit_FindPrev"] = "Найти _предыдущее",
                ["Menu_Edit_Replace"] = "_Заменить...",

                ["Menu_Help"] = "_Справка",
                ["Menu_Help_About"] = "_О программе Vn Emote Voice...",

                // Верхняя панель (Header)
                ["Header_AppTitle"] = "Vn Emote Voice (TTS)",
                ["Header_AvatarTag"] = "АВАТАР:",
                ["Header_TransitionBadge"] = "ПЕРЕХОД",
                ["Header_TransitionBadge_Tooltip"] = "Обнаружен эмоциональный переход в этом фрагменте диалога",
                ["Header_Emotion_Neutral"] = "Нейтральный",
                ["Header_BtnPlay_Tooltip"] = "Воспроизвести синтез речи (F5)",
                ["Header_BtnPause_Tooltip"] = "Приостановить воспроизведение",
                ["Header_BtnStop_Tooltip"] = "Остановить и сбросить позицию",

                // Левая панель: Селекторы
                ["Panel_LanguageTitle"] = "🌐 Язык голоса",
                ["Panel_ModelSelectorTitle"] = "🧠 Выбор модели",
                ["Panel_ExternalModel"] = "Внешняя модель",
                ["Panel_VoiceTitle"] = "🎙 Голос",

                // Внешняя панель Piper
                ["Piper_PanelTitle"] = "📂 Модель Piper (внешняя)",
                ["Piper_OnnxTitle"] = "Файл .onnx",
                ["Piper_JsonTitle"] = "Файл .json",
                ["Piper_Browse"] = "Обзор",
                ["Piper_NoFile"] = "Файл не выбран",

                // Внешняя панель VoiceStudio API
                ["VoiceStudio_PanelTitle"] = "🌐 VoiceStudio API (внешний)",
                ["VoiceStudio_UrlTitle"] = "Адрес API",
                ["VoiceStudio_Connect"] = "Загрузить",
                ["VoiceStudio_ProfileTitle"] = "Доступный профиль",
                ["VoiceStudio_Status_Disconnected"] = "Не подключено",
                ["VoiceStudio_Status_Connecting"] = "Подключение к API...",
                ["VoiceStudio_Status_Connected"] = "Подключено — доступно профилей: {0}",
                ["VoiceStudio_Status_Error"] = "Ошибка подключения к VoiceStudio",
                ["VoiceStudio_Settings_Expander"] = "⚙ Настройки VoiceStudio",
                ["VoiceStudio_GuidanceScale"] = "Степень следования промпту",
                ["VoiceStudio_TShift"] = "Момент уточнения (t_shift)",
                ["VoiceStudio_PosTemp"] = "Температура позиций",
                ["VoiceStudio_ClassTemp"] = "Температура звучания",
                ["VoiceStudio_LayerPenalty"] = "Штраф слоев (layer_penalty)",
                ["VoiceStudio_NumStep"] = "Шагов синтеза речи",
                ["VoiceStudio_BalanceVolume"] = "Балансировать громкость",
                ["VoiceStudio_Denoise"] = "Шумоподавление референса",
                ["VoiceStudio_Postprocess"] = "Постобработка аудио",

                // Ползунки голоса
                ["Slider_SpeedTitle"] = "⚡ Скорость",
                ["Slider_PitchTitle"] = "🎵 Высота тона",
                ["Slider_VolumeTitle"] = "🔊 Громкость",

                // Анализ эмоций
                ["Emotion_PanelTitle"] = "🧠 Анализ эмоций",
                ["Emotion_UseDefaultModel"] = "Использовать стандартную модель",
                ["Emotion_CustomJsonTitle"] = "📂 Файл эмоций JSON:",
                ["Emotion_NoFile"] = "Файл не выбран",
                ["Emotion_Load"] = "Загрузить",

                // Нижние кнопки и подсказки
                ["Btn_Reset_Tooltip"] = "Сбросить параметры и восстановить настройки по умолчанию",
                ["Btn_DownloadAudio_Tooltip"] = "Скачать полную аудиозапись в файл WAV",
                ["Btn_SegmentedDownload_Tooltip"] = "Пофразовый экспорт WAV с метаданными JSON для виртуального рассказчика",

                // Текстовый редактор
                ["Editor_CharCount"] = "{0} символов",
                ["Editor_WelcomeText"] = "Здравствуйте! Добро пожаловать в систему синтеза речи (TTS) с передачей эмоций! Здесь вы можете ввести любой текст и озвучить его с помощью различных голосов и языков. Вы также можете настроить скорость, высоту тона и громкость для идеального звучания.\n\nНапишите что-нибудь и попробуйте!",

                // Диалоги и оповещения (MessageBox)
                ["Msg_InputEmpty"] = "Пожалуйста, введите текст для синтеза речи.",
                ["Msg_WarningTitle"] = "Предупреждение",
                ["Msg_ErrorTitle"] = "Ошибка",
                ["Msg_SuccessTitle"] = "Успешно",
                ["Msg_DownloadSuccess"] = "Аудио успешно экспортировано:\n{0}",
                ["Msg_DownloadError"] = "Ошибка при экспорте аудио: {0}",
                ["Msg_SegmentedExportSuccess"] = "Экспорт завершен:\n• {0} файлов WAV\n• 1 файл JSON с эмоциями\n\nПапка: {1}",
                ["Msg_SegmentedExportTitle"] = "Пофразовый экспорт завершен",
                ["Msg_SaveAudioFilter"] = "Файл аудио WAV (*.wav)|*.wav",
                ["Msg_SelectFolderTitle"] = "Выберите папку для сохранения сегментов и файла JSON",

                // Окно Поиска и Замены
                ["FindReplace_Title_Find"] = "Поиск",
                ["FindReplace_Title_Replace"] = "Замена",
                ["FindReplace_LblFind"] = "Найти:",
                ["FindReplace_LblReplace"] = "Заменить на:",
                ["FindReplace_ChkMatchCase"] = "С учетом регистра",
                ["FindReplace_ChkWholeWord"] = "Только слово целиком",
                ["FindReplace_LblDirection"] = "Направление:",
                ["FindReplace_RbDown"] = "Вниз",
                ["FindReplace_RbUp"] = "Вверх",
                ["FindReplace_BtnFindNext"] = "Найти далее",
                ["FindReplace_BtnFindPrev"] = "Найти назад",
                ["FindReplace_BtnReplace"] = "Заменить",
                ["FindReplace_BtnReplaceAll"] = "Заменить все",
                ["FindReplace_BtnClose"] = "Закрыть",
                ["FindReplace_Status_EnterText"] = "Введите текст для поиска.",
                ["FindReplace_Status_NotFound"] = "Не удалось найти \"{0}\".",
                ["FindReplace_Status_Found"] = "Совпадение найдено.",
                ["FindReplace_Status_Replaced"] = "Текст заменен.",
                ["FindReplace_Status_ReplacedAll"] = "Заменено совпадений: {0}.",
                ["FindReplace_Status_NoMatchesToReplace"] = "Совпадений для \"{0}\" не найдено.",

                // Окно О программе
                ["About_Title"] = "О программе Vn Emote Voice",
                ["About_AppTitle"] = "Vn Emote Voice (TTS)",
                ["About_Version"] = "Версия 1.0 — Кроссплатформенная (Windows | Linux | macOS)",
                ["About_AuthorLabel"] = "Автор:",
                ["About_DescTitle"] = "Описание системы:",
                ["About_DescParagraph1"] = "Vn Emote Voice — это передовая система синтеза речи (TTS) и эмоционального анализа, разработанная для создания виртуальных рассказчиков, визуальных новелл и VTuber-аватаров (Live2D, VRoid, 3D).",
                ["About_DescParagraph2"] = "Система интегрирует встроенные голосовые движки ОС (Windows WinRT, macOS say, Linux speech-dispatcher), современные локальные нейросетевые модели ONNX (Kokoro v1.0 и Piper), VoiceStudio API и лингвистический модуль с 27 откалиброванными эмоциями и распознаванием драматических поворотов в реальном времени.",
                ["About_License"] = "Лицензия MIT — Открытый исходный код",
                ["About_BtnClose"] = "ОК"
            }
        };

        /// <summary>
        /// Cambia el idioma de la interfaz del sistema ("es", "en", "zh" o "ru").
        /// </summary>
        public static void SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) return;

            string normalized = languageCode.ToLowerInvariant().Trim();
            if (normalized.StartsWith("es")) normalized = "es";
            else if (normalized.StartsWith("en")) normalized = "en";
            else if (normalized.StartsWith("zh")) normalized = "zh";
            else if (normalized.StartsWith("ru")) normalized = "ru";
            else normalized = "es"; // Fallback por defecto

            CurrentLanguage = normalized;
        }

        /// <summary>
        /// Detecta automáticamente el idioma de la interfaz según la cultura del sistema operativo.
        /// </summary>
        public static void InitializeFromSystem()
        {
            string sysCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            if (sysCulture == "zh")
            {
                SetLanguage("zh");
            }
            else if (sysCulture == "ru")
            {
                SetLanguage("ru");
            }
            else if (sysCulture == "en")
            {
                SetLanguage("en");
            }
            else
            {
                SetLanguage("es");
            }
        }

        /// <summary>
        /// Obtiene el texto traducido para la clave especificada en el idioma activo de la UI.
        /// </summary>
        public static string GetString(string key)
        {
            if (Translations.TryGetValue(CurrentLanguage, out var dict) && dict.TryGetValue(key, out var text))
            {
                return text;
            }

            // Fallback a español si no existe en el idioma actual
            if (Translations["es"].TryGetValue(key, out var fallbackText))
            {
                return fallbackText;
            }

            return key;
        }

        /// <summary>
        /// Obtiene el texto traducido y aplica formato con los argumentos suministrados.
        /// </summary>
        public static string Format(string key, params object[] args)
        {
            string template = GetString(key);
            try
            {
                return string.Format(template, args);
            }
            catch
            {
                return template;
            }
        }
    }
}