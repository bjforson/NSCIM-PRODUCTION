import os
from urllib.parse import quote_plus
from dotenv import load_dotenv

load_dotenv()


def _env_bool(name: str, default: bool = False) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "on"}


def _env_int(name: str, default: int) -> int:
    raw = os.getenv(name)
    if raw is None or raw.strip() == "":
        return default
    try:
        return int(raw)
    except ValueError:
        return default


DB_PASSWORD = os.getenv("NICKSCAN_DB_PASSWORD", "")
DB_PASSWORD_ENCODED = quote_plus(DB_PASSWORD)
DATABASE_URL = f"postgresql+asyncpg://postgres:{DB_PASSWORD_ENCODED}@localhost:5432/nickscan_production"
DATABASE_URL_SYNC = f"postgresql+psycopg2://postgres:{DB_PASSWORD_ENCODED}@localhost:5432/nickscan_production"

# Service config
# SECURITY: default to loopback only. The splitter has no auth layer and is supervised
# by NSCIM_API on the same host, so a localhost bind is sufficient. Operators who need
# cross-host access must set SPLITTER_HOST=0.0.0.0 deliberately AND front the service
# with an auth proxy.
SERVICE_PORT = int(os.getenv("SPLITTER_PORT", "5320"))
SERVICE_HOST = os.getenv("SPLITTER_HOST", "127.0.0.1")

# Image processing
MAX_IMAGE_SIZE_MB = max(1, _env_int("MAX_IMAGE_SIZE_MB", 100))
TESSERACT_CMD = os.getenv("TESSERACT_CMD", r"C:\Program Files\Tesseract-OCR\tesseract.exe")

# Remote image fetching is disabled by default to avoid SSRF-style arbitrary URL
# fetches. Set SPLITTER_ALLOW_IMAGE_URL_FETCHES=true to enable, and optionally
# restrict hosts with SPLITTER_ALLOWED_IMAGE_URL_HOSTS=host1,host2.
ALLOW_IMAGE_URL_FETCHES = _env_bool("SPLITTER_ALLOW_IMAGE_URL_FETCHES", False)
ALLOWED_IMAGE_URL_HOSTS = {
    host.strip().lower().rstrip(".")
    for host in os.getenv("SPLITTER_ALLOWED_IMAGE_URL_HOSTS", "").split(",")
    if host.strip()
}

# Main app callback (optional)
MAIN_APP_CALLBACK_URL = os.getenv("MAIN_APP_CALLBACK_URL", "")

# OpenAI vision teacher/verifier (shadow/advisory only).
# Disabled by default and also requires OPENAI_API_KEY at call time. The
# orchestrator records advisory metadata on SplitResult objects but never lets
# OpenAI change deterministic splitting decisions.
OPENAI_VISION_ENABLED = _env_bool("OPENAI_VISION_ENABLED", False)
OPENAI_VISION_MODEL = os.getenv("OPENAI_VISION_MODEL", "gpt-4o-mini")
OPENAI_VISION_MAX_RES = max(256, _env_int("OPENAI_VISION_MAX_RES", 1568))
OPENAI_VISION_TIMEOUT_SECONDS = max(1, _env_int("OPENAI_VISION_TIMEOUT_SECONDS", 45))
OPENAI_VISION_REVIEW_DISAGREEMENT_PX = max(1, _env_int("OPENAI_VISION_REVIEW_DISAGREEMENT_PX", 35))

# Consensus scoring
AGREEMENT_THRESHOLD_PX = 30  # strategies within 30px of each other "agree"
AGREEMENT_BONUS = 0.15       # bonus confidence for agreeing strategies
MIN_CONFIDENCE = 0.3         # below this, result is discarded
