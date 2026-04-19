namespace NickScanCentralImagingPortal.Core.Configuration
{
    /// <summary>
    /// Feature flags and paths for assistive AI, training-data lineage, and optional export jobs.
    /// </summary>
    public class AiWorkflowOptions
    {
        public const string SectionName = "AiWorkflow";

        /// <summary>Master switch; when false, lineage/notifications no-op and assist endpoints return disabled.</summary>
        public bool Enabled { get; set; }

        public bool ImageAssistEnabled { get; set; }
        public bool OpsTriageEnabled { get; set; }
        public bool IcumsHintsEnabled { get; set; }
        public bool TrainingExportEnabled { get; set; }

        /// <summary>Recorded on suggestions until real models are wired.</summary>
        public string DefaultModelId { get; set; } = "stub-v1";

        public string DefaultModelVersion { get; set; } = "1";

        public string FeatureVersion { get; set; } = "phase0";

        /// <summary>Root folder for JSONL snapshot exports (server-local).</summary>
        public string ExportRootPath { get; set; } = "Data/AiTrainingExports";

        /// <summary>When true, autonomous paths only log shadow intent (T3 guardrail).</summary>
        public bool AutonomousShadowModeOnly { get; set; } = true;

        // --- AI Model Provider Settings ---

        /// <summary>Which provider to use: "claude-vision", "stub".</summary>
        public string ActiveProvider { get; set; } = "stub";

        /// <summary>Anthropic API key for Claude Vision. Leave empty to fall back to stub.</summary>
        public string? ClaudeApiKey { get; set; }

        /// <summary>Claude model ID (e.g., "claude-sonnet-4-20250514").</summary>
        public string? ClaudeModelId { get; set; }

        /// <summary>Max concurrent image analyses per group.</summary>
        public int MaxConcurrentInferences { get; set; } = 3;

        /// <summary>Timeout per inference call in seconds.</summary>
        public int InferenceTimeoutSeconds { get; set; } = 60;

        /// <summary>Base URL for internal API to fetch images (e.g., "http://localhost:5205").</summary>
        public string? InternalApiBaseUrl { get; set; }

        // --- Auto-Trigger ---
        /// <summary>When false, the background auto-trigger service is disabled. Manual trigger still works.</summary>
        public bool AutoTriggerEnabled { get; set; } = false;

        // --- Ollama Local Model Settings ---
        /// <summary>Base URL for Ollama API (e.g., "http://localhost:11434").</summary>
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

        /// <summary>Ollama model ID for vision analysis (e.g., "moondream", "llava:7b").</summary>
        public string OllamaModelId { get; set; } = "moondream";

        // --- Phase 1-3 AI Analysis Feature Flags ---
        public bool DensityHeatmapEnabled { get; set; } = true;
        public bool ImageQualityScoreEnabled { get; set; } = true;
        public bool OcrValidationEnabled { get; set; } = true;
        public bool YoloDetectionEnabled { get; set; } = false;
        public bool AnomalyDetectionEnabled { get; set; } = false;
        public string? YoloModelPath { get; set; }
        public double YoloConfidenceThreshold { get; set; } = 0.5;
        public double AnomalyScoreThreshold { get; set; } = 0.7;
        public string? AnomalyModelPath { get; set; }
    }
}
