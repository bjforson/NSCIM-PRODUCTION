import os
from urllib.parse import quote_plus
from dotenv import load_dotenv

load_dotenv()

DB_PASSWORD = os.getenv("NICKSCAN_DB_PASSWORD", "")
DB_PASSWORD_ENCODED = quote_plus(DB_PASSWORD)
DATABASE_URL = f"postgresql+asyncpg://postgres:{DB_PASSWORD_ENCODED}@localhost:5432/nickscan_production"
DATABASE_URL_SYNC = f"postgresql+psycopg2://postgres:{DB_PASSWORD_ENCODED}@localhost:5432/nickscan_production"

# Service config
SERVICE_PORT = int(os.getenv("SPLITTER_PORT", "5320"))
SERVICE_HOST = os.getenv("SPLITTER_HOST", "0.0.0.0")

# Image processing
MAX_IMAGE_SIZE_MB = 100
TESSERACT_CMD = os.getenv("TESSERACT_CMD", r"C:\Program Files\Tesseract-OCR\tesseract.exe")

# Main app callback (optional)
MAIN_APP_CALLBACK_URL = os.getenv("MAIN_APP_CALLBACK_URL", "")

# Consensus scoring
AGREEMENT_THRESHOLD_PX = 30  # strategies within 30px of each other "agree"
AGREEMENT_BONUS = 0.15       # bonus confidence for agreeing strategies
MIN_CONFIDENCE = 0.3         # below this, result is discarded
